"""골대·골망과 충돌 형상 (specs/044 FR-03·27·64, 059 FR-01~07). 포스트 중심 = 골라인 중심 = 피치 경계 (004 GoalFrame).

골망(059): 면 4개(뒤·위·옆 2) 변형 격자 — 런타임 NetRestShape(C#)와 같은 식 `net_point`, 칸 수 = ceil(면 길이 / gridCell).
UV는 면 위 m 단위(셰이더가 육각 망을 그린다, FR-01). 지지대(뒤 기둥·위 뒤 막대·바닥 막대)는 GoalSupport_ 메시(Metal) —
044 골대 규격 측정(GoalFrame_)과 섞이지 않게 따로 둔다(드로우콜 골대당 그물 1 + 지지대 1).
"""
import math

from . import geometry, markings
from .layout import HALF_L, unity, unity_box

SEGMENTS = 16
NET_TOP_EXTRA = 0.05
SUPPORT_R = 0.025
SUPPORT_SEG = 8
SUPPORT_GAP = 0.04     # 그물 면 → 지지대 중심 (그물 바깥)
FACES = ("back", "top", "sideNeg", "sidePos")   # C# NetFace 순서


class NetShape:
    """그물 인자. 로컬 좌표 (d 깊이, w 폭, h 높이) — C# NetShapeParams."""

    def __init__(self, post_r, depth, net):
        self.half_mouth = markings.GOAL_INNER_WIDTH / 2
        self.height = markings.GOAL_INNER_HEIGHT
        self.hw = self.half_mouth + post_r
        self.top = self.height + post_r + NET_TOP_EXTRA
        self.depth = depth
        self.sag = net["sagTop"]
        self.bulge = net["bulgeBack"]
        self.cell = net["gridCell"]

    def face_size(self, f):
        if f == "back":
            return 2 * self.hw, self.top
        if f == "top":
            return 2 * self.hw, self.depth
        return self.depth, self.top

    def cells(self, length):
        return max(2, int(math.ceil(length / self.cell - 1e-4)))

    def grid(self, f):
        lu, lv = self.face_size(f)
        return self.cells(lu), self.cells(lv)

    def point(self, f, u, v):
        """면 매개변수 (u, v) → 로컬 (d, w, h). C# NetRestShape.Point와 같은 식."""
        hw, top, D = self.hw, self.top, self.depth
        if f == "back":
            w, h = -hw + 2 * hw * u, top * v
            bulge = self.bulge * math.sin(math.pi * u) * (27 / 4) * v * (1 - v) * (1 - v)
            return D + bulge, w, h
        if f == "top":
            w, d = -hw + 2 * hw * u, D * v
            return d, w, top - self.sag * math.sin(math.pi * u) * math.sin(math.pi * v)
        d, h = D * u, top * v
        return d, (-hw if f == "sideNeg" else hw), h


def _net_mesh(name, parent, shape, side, x, mat):
    """그물 한 골대: 면별 격자, UV = 면 위 m 좌표. 반환 (오브젝트, 로컬 정점 목록 [(face, d, w, h)], 삼각형 수)."""
    import bpy

    verts, faces, uvs, local = [], [], [], []
    for f in FACES:
        U, V = shape.grid(f)
        lu, lv = shape.face_size(f)
        base = len(verts)
        for j in range(V + 1):
            for i in range(U + 1):
                d, w, h = shape.point(f, i / U, j / V)
                verts.append((x + side * d, w, h))
                uvs.append((lu * i / U, lv * j / V))
                local.append((f, d, w, h))
        for j in range(V):
            for i in range(U):
                a = base + j * (U + 1) + i
                faces.append([a, a + 1, a + U + 2, a + U + 1])
    obj = geometry.from_pydata(name, verts, faces, mat, parent=parent)
    me = obj.data
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            uv.data[li].uv = uvs[me.loops[li].vertex_index]
    me.update()
    return obj, local, 2 * len(faces)


def _supports(fr, shape, side, x, mat):
    """지지대 (FR-04): 뒤 모서리 기둥 2, 위 뒤 막대 1, 바닥 막대(뒤 1·옆 2). 전부 그물 면 바깥 SUPPORT_GAP."""
    D, hw, top = shape.depth, shape.hw, shape.top
    db = x + side * (D + SUPPORT_GAP)
    wb = hw + SUPPORT_GAP
    zb = SUPPORT_R
    segs = []
    for s in (-1, 1):
        segs.append(((db, s * wb, 0.0), (db, s * wb, top + SUPPORT_GAP)))          # 뒤 기둥
        segs.append(((x + side * 0.2, s * wb, zb), (db, s * wb, zb)))               # 옆 바닥 막대 (포스트 뒤 0.2 m부터)
    segs.append(((db, -wb, top + SUPPORT_GAP), (db, wb, top + SUPPORT_GAP)))       # 위 뒤 막대
    segs.append(((db, -wb, zb), (db, wb, zb)))                                       # 뒤 바닥 막대
    for a, b in segs:
        fr.cylinder(a, b, SUPPORT_R, SUPPORT_SEG, mat)
    return segs


def _check(shape, local, segs, side, x, post_r, ball_r, tris, net):
    """AC-01 검사 (bpy 없음): 시뮬 그물 상자 침범, 바닥선, 지지대, 프레임 틈."""
    half, H, D = shape.half_mouth, shape.height, shape.depth
    inside_max = 0.0
    bottom = []
    gap = 0.0
    for f, d, w, h in local:
        # 시뮬 공 중심 영역 + 반지름 = 상자 [0, D] × [−half, half] × [0, H] (059 FR-02)
        pen = min(d, D - d, half - abs(w), H - h, h)
        if pen > 0:
            inside_max = max(inside_max, pen)
        if f != "top" and h == 0.0:
            bottom.append(h)
        # 골라인 쪽 테두리(d = 0)는 포스트·크로스바 표면과의 틈 (중심선 위라 안에 묻히면 0)
        if d == 0.0:
            if f in ("sideNeg", "sidePos"):
                dist = max(0.0, abs(abs(w) - (half + post_r)) - post_r) if h <= H + 2 * post_r else 0.0
            else:
                dist = max(0.0, math.hypot(h - (H + post_r), 0.0) - post_r)
            gap = max(gap, dist)
    sup_clear = 1e9
    blocks = False
    for a, b in segs:
        for t in range(11):
            px = a[0] + (b[0] - a[0]) * t / 10
            py = a[1] + (b[1] - a[1]) * t / 10
            pz = a[2] + (b[2] - a[2]) * t / 10
            d = side * (px - x)
            # 상자 밖 거리 (양수 = 밖)
            out = max(-d, d - D, abs(py) - half, pz - H)
            sup_clear = min(sup_clear, out - SUPPORT_R)
            if d < 0.05 and abs(py) < half and pz < H:
                blocks = True
    ok = inside_max <= 0.001 and sup_clear > 0 and not blocks and gap <= 0.02
    return {
        "ok": ok,
        "insideBoxMax": round(inside_max, 6),
        "boxBallRadius": ball_r,
        "bottomZ": [min(bottom), max(bottom)] if bottom else [0.0, 0.0],
        "supportClearance": round(sup_clear, 6),
        "supportBlocksMouth": blocks,
        "frameGap": round(gap, 6),
        "cellSize": net["cellSize"],
        "strand": net["strand"],
        "sagTop": shape.sag,
        "bulgeBack": shape.bulge,
        "triangles": tris,
    }


def build(mats, tuning, net):
    r = tuning["postRadius"]
    depth = tuning["netDepth"]
    w, h = markings.GOAL_INNER_WIDTH, markings.GOAL_INNER_HEIGHT
    zc = w / 2 + r                   # 포스트 중심 (폭 방향)
    top = h + 2 * r                  # 크로스바 윗면
    shape = NetShape(r, depth, net)
    data = []
    for side, name in ((-1, "Goal_L"), (1, "Goal_R")):
        x = side * HALF_L
        parent = geometry.empty(name)
        fr = geometry.MeshBuilder()
        posts = []
        for sy in (-1, 1):
            a, b = (x, sy * zc, 0.0), (x, sy * zc, top)
            fr.cylinder(a, b, r, SEGMENTS, mats["Metal"])
            posts.append({"a": unity(a), "b": unity(b), "r": r})
        ca, cb = (x, -zc - r, h + r), (x, zc + r, h + r)
        fr.cylinder(ca, cb, r, SEGMENTS, mats["Metal"])
        fr.build(f"GoalFrame_{name[-1]}", parent=parent)
        sup = geometry.MeshBuilder()
        segs = _supports(sup, shape, side, x, mats["Metal"])
        support_tris = len(segs) * SUPPORT_SEG * 2 + len(segs) * 2 * (SUPPORT_SEG - 2)
        sup.build(f"GoalSupport_{name[-1]}", parent=parent)

        _, local, net_tris = _net_mesh(f"Net_{name[-1]}", parent, shape, side, x, mats["Net"])
        nx = x + side * depth
        lo = (min(x, nx), -shape.hw, 0.0)
        hi = (max(x, nx), shape.hw, shape.top)
        check = _check(shape, local, segs, side, x, r, tuning["ballRadius"], net_tris + support_tris, net)
        data.append({"side": side, "name": name, "posts": posts,
                     "crossbar": {"a": unity(ca), "b": unity(cb), "r": r},
                     "net": unity_box(lo, hi),
                     "netGrid": {"cell": shape.cell, "depth": depth, "halfWidth": shape.hw, "top": shape.top,
                                 "faces": [list(shape.grid(f)) for f in FACES]},
                     "netCheck": check})
    return data
