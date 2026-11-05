"""머리카락 30종 (specs/056 FR-40·41, specs/057 FR-10~14·30~33). 0~9는 056 순서: 0 버즈 · 1 짧은 · 2 긴(턱선) · 3 곱슬 · 4 번 ·
5 페이드 · 6 아프로 · 7 모히칸 · 8 드레드 · 9 삭발, 10~29는 057 추가(STYLE_TABLE).

057: 셸 위에 가장자리 **가닥 카드**(LOD0만, 클로즈업 머리는 LOD0 조각을 쓴다)를 붙이고, 묶음·포니테일은 머리 뼈에 고정한
부속 형상(타원체·관)으로 만든다. 카드 UV는 u + 2(parts 카드 칸) — 셰이더가 그 픽셀만 디더 알파 클립한다.

방식: **볼륨 셸**. 두피(머리 뼈 정점, 스타일별 헤어라인 위)를 기준 표면에서 떼어 법선 방향으로 스타일 두께만큼 띄운다.
가장자리는 두께가 헤어라인 쪽으로 줄어들고 두피 안쪽으로 턱 띠를 내려 끝이 얇은 판처럼 보이지 않게 한다.
가닥 방향은 parts 아틀라스 머리카락 칸(정수리에서 내려오는 줄무늬, UV v = 정수리→헤어라인)과 셰이더 이방성 하이라이트가 맡는다.

각 스타일은 별도 메시 조각(`Hair_<스타일>_LOD0..2`)으로 glb에 들어가고, Unity PlayerMeshBaker가 선수의 스타일 조각만 몸 메시에 합친다.
머리카락은 머리 뼈에 고정(가중치 Head 1) — 목·어깨는 헤어라인 아래라 겹치지 않는다(FR-41, checks.penetration).
델타: 두피 묶음점의 얼굴·머리 타깃·HideHead 델타를 그대로 받는다(머리 모양이 바뀌면 머리카락도 따라간다).
삭발(9)은 형상이 없다(피부 텍스처 그대로) → 메시 조각 없음.
"""
import math

import numpy as np

from . import mpfb_src as MS
from . import skeleton as S

# specs/057 FR-30: 30종 (이름, 계열). 0~9는 056 순서 그대로(기준 이미지·기존 시드 해석), 9 삭발은 형상 없음.
STYLE_TABLE = (
    ("Hair_Buzz", "short"), ("Hair_Short", "short"), ("Hair_Long", "long"), ("Hair_Curly", "medium"),
    ("Hair_Bun", "long"), ("Hair_Fade", "short"), ("Hair_Afro", "medium"), ("Hair_Mohawk", "medium"),
    ("Hair_Dreads", "long"), ("Hair_Bald", "short"),
    ("Hair_Crew", "short"), ("Hair_SidePart", "short"), ("Hair_Undercut", "short"), ("Hair_SlickBack", "medium"),
    ("Hair_Quiff", "short"), ("Hair_Curtains", "medium"), ("Hair_TexturedCrop", "short"), ("Hair_BuzzLine", "short"),
    ("Hair_HighTop", "medium"), ("Hair_Cornrows", "short"), ("Hair_Twists", "medium"), ("Hair_TopKnot", "long"),
    ("Hair_Ponytail", "long"), ("Hair_MediumWave", "medium"), ("Hair_FrenchCrop", "short"), ("Hair_IvyLeague", "short"),
    ("Hair_Spiky", "short"), ("Hair_CurlyTop", "short"), ("Hair_BoxBraids", "long"), ("Hair_HalfUp", "long"),
)
STYLES = tuple(n for n, _ in STYLE_TABLE)
FAMILY = dict(STYLE_TABLE)
MESH_STYLES = tuple(s for s in STYLES if s != "Hair_Bald")
LONG_LINE = {"Hair_Long", "Hair_Dreads", "Hair_BoxBraids", "Hair_HalfUp"}
MEDIUM_LINE = {"Hair_Curtains", "Hair_MediumWave"}
HAIR_TRIS = {0: 800, 1: 230, 2: 70}  # 셸 감량 목표 (057: LOD0 1,100 → 800 — 카드·부속 형상 몫, 헤어 LOD0 합계 ≤ 1,350)
HAIRLINE_SMOOTH = 12  # 헤어라인 고리 평활 반복
REGION = (0.25, 0.0, 1.0, 0.75)   # parts 아틀라스 머리카락 칸 (u0, v0, u1, v1)
CARD_REGION = (0.25, 0.75, 1.0, 1.0)  # parts 아틀라스 가닥 카드 칸 — 카드 UV는 u + 2 (셰이더가 알파 클립 대상으로 구분, 057 FR-10·12)
CARD_MAX = 400       # LOD0 카드 삼각형 상한 (057 plan T01)
HAIR_LOD0_MAX = 1320  # 헤어 LOD0 합계 목표 — 몸 LOD0 8,666 + 1,320 ≤ 10k (checks 상한 1,350)
CARD_TILE = 0.04     # 카드 텍스처 가로 한 칸 = 4 cm


HEAD_AXIS_Z = [0.0]  # 머리 수직축 z (머리 정점 앞뒤 가운데) — build에서 정한다


def _phi(p, c):
    """머리 수직축 둘레 각: 0 = 정면, π = 뒤 (좌우 대칭이라 절댓값). 축은 눈 중심이 아니라 머리 앞뒤 가운데."""
    return abs(math.atan2(p[0] - c[0], p[2] - HEAD_AXIS_Z[0]))


def hairline(style, phi, d):
    """헤어라인 높이 (눈 중심 기준, d 단위). phi 0 정면 → π 뒤."""
    pts = [(0.0, 1.18), (0.55, 0.85), (1.05, 0.15), (1.35, -0.35), (1.9, -0.55), (math.pi, -1.30)]
    if style in LONG_LINE:
        pts = [(0.0, 1.10), (0.55, 0.80), (1.05, 0.0), (1.35, -0.75), (1.9, -1.15), (math.pi, -1.75)]
    elif style in MEDIUM_LINE:
        pts = [(0.0, 1.10), (0.55, 0.80), (1.05, 0.05), (1.35, -0.50), (1.9, -0.80), (math.pi, -1.50)]
    for (a0, y0), (a1, y1) in zip(pts, pts[1:]):
        if phi <= a1:
            t = (phi - a0) / (a1 - a0)
            return y0 + (y1 - y0) * t
    return pts[-1][1]


def thickness(style, p, c, d, top):
    """두께 (m). p 위치, c 눈 중심, top 정수리 y."""
    phi = _phi(p, c)
    up = (p[1] - c[1]) / d                    # 눈 중심에서 위로 (d 단위)
    crown = np.clip((p[1] - (top - 0.06)) / 0.06, 0, 1)  # 정수리 근처 1
    side = abs(p[0] - c[0]) / d
    if style == "Hair_Buzz":
        return 0.0028
    if style == "Hair_Short":
        return 0.006 + 0.008 * crown + 0.002 * (phi < 0.6)
    if style == "Hair_Fade":
        fade = np.clip((up - 0.2) / 1.0, 0, 1)
        return 0.0015 + 0.016 * fade ** 1.5
    if style == "Hair_Long":
        return 0.014 + 0.008 * crown + 0.010 * np.clip((0.3 - up) / 1.2, 0, 1)
    if style == "Hair_Curly":
        n = math.sin(p[0] * 210) * math.sin(p[1] * 190) * math.sin(p[2] * 230)
        return 0.020 + 0.006 * crown + 0.004 * n
    if style == "Hair_Bun":
        b = np.array([c[0], top - 0.055, c[2] - 0.115])
        bun = max(0.0, 0.045 - float(np.linalg.norm(np.array(p) - b)))
        return 0.007 + 1.4 * bun
    if style == "Hair_Afro":
        return 0.038 + 0.020 * crown - 0.006 * np.clip(-up, 0, 1)
    if style == "Hair_Mohawk":
        strip = np.clip((0.42 - side) / 0.15, 0, 1)
        return 0.0012 + 0.040 * strip * np.clip((up - 0.4) / 0.8, 0, 1) + 0.006 * strip
    if style == "Hair_Dreads":
        n = math.sin(p[0] * 160 + p[2] * 140)
        return 0.017 + 0.006 * crown + 0.004 * n
    # ---- 057 추가 20종
    px = (p[0] - c[0]) / d                     # 좌우 (d 단위, 부호 있음)
    fade = float(np.clip((up - 0.2) / 1.0, 0, 1))
    top_mask = float(np.clip((up - 0.65) / 0.25, 0, 1)) * float(np.clip((0.80 - side) / 0.12, 0, 1))
    front = math.exp(-(phi / 0.55) ** 2)
    if style == "Hair_Crew":
        return 0.0045 + 0.005 * crown + 0.002 * (phi < 0.6)
    if style == "Hair_SidePart":
        g = 0.4 if (abs(px - 0.45) < 0.05 and up > 0.8) else 1.0
        return g * (0.006 + 0.007 * crown + 0.004 * float(np.clip((0.45 - px) / 0.8, 0, 1)))
    if style == "Hair_Undercut":
        return 0.0012 + top_mask * (0.016 + 0.004 * crown)
    if style == "Hair_SlickBack":
        return (0.010 + 0.008 * crown + 0.006 * float(np.clip((phi - 1.2) / 1.5, 0, 1))) * (0.6 + 0.4 * fade)
    if style == "Hair_Quiff":
        return 0.0015 + 0.012 * fade ** 1.5 + 0.026 * front * float(np.clip((up - 0.9) / 0.5, 0, 1))
    if style == "Hair_Curtains":
        g = 0.5 if (abs(px) < 0.05 and up > 0.9) else 1.0
        return g * (0.014 + 0.004 * crown)
    if style == "Hair_TexturedCrop":
        n = math.sin(p[0] * 230) * math.sin(p[2] * 210)
        return (0.008 + 0.004 * crown + 0.002 * n) * (0.35 + 0.65 * fade)
    if style == "Hair_BuzzLine":
        line = side > 0.55 and abs(up - (0.35 + 0.15 * math.cos(phi * 2.0))) < 0.04
        return 0.0006 if line else 0.0028
    if style == "Hair_HighTop":
        flat = float(np.clip((p[1] - (top - 0.05)) / 0.05, 0, 1))
        return 0.0015 + top_mask * (0.028 + 0.045 * flat)
    if style == "Hair_Cornrows":
        return 0.0022 + 0.0028 * abs(math.sin(p[0] * 95))
    if style == "Hair_Twists":
        return 0.013 + 0.004 * crown + 0.005 * math.sin(p[0] * 260) * math.sin(p[2] * 240)
    if style == "Hair_TopKnot":
        return 0.0012 + top_mask * (0.008 + 0.003 * crown)
    if style == "Hair_Ponytail":
        return 0.007 + 0.004 * crown
    if style == "Hair_MediumWave":
        return 0.017 + 0.004 * math.sin(p[1] * 120 + p[0] * 60) + 0.004 * crown
    if style == "Hair_FrenchCrop":
        return 0.0015 + 0.009 * fade ** 1.2 + 0.004 * front * float(np.clip((up - 0.9) / 0.3, 0, 1))
    if style == "Hair_IvyLeague":
        g = 0.45 if (abs(px - 0.45) < 0.045 and up > 0.8) else 1.0
        return g * (0.0035 + 0.007 * fade + 0.003 * crown)
    if style == "Hair_Spiky":
        sp = max(0.0, math.sin(p[0] * 170) * math.sin(p[2] * 170)) ** 2
        return 0.0015 + 0.006 * fade + top_mask * (0.006 + 0.014 * sp)
    if style == "Hair_CurlyTop":
        n = math.sin(p[0] * 210) * math.sin(p[1] * 190) * math.sin(p[2] * 230)
        return 0.0015 + 0.004 * fade + top_mask * (0.020 + 0.005 * n)
    if style == "Hair_BoxBraids":
        return 0.016 + 0.005 * abs(math.sin(p[0] * 140 + p[2] * 120))
    if style == "Hair_HalfUp":
        return 0.013 + 0.006 * crown
    raise ValueError(style)


# 부속 형상 (머리 뼈 고정): (종류, 머리 중심에서 두피를 찾는 방향, 크기). 057 FR-30·33.
#   knot: 타원체 (반지름 x, y, z) — 두피 표면점 + 법선 × 0.55 r 에 중심
#   tail: 관 (길이, 시작 반지름, 끝 반지름, 늘어지는 방향) — 두피 표면점에서 시작, 칼라 위에서 끝난다
ATTACH = {
    "Hair_TopKnot": [("knot", (0.0, 0.85, -0.53), (0.034, 0.030, 0.034))],
    "Hair_HalfUp": [("knot", (0.0, 0.55, -0.83), (0.026, 0.024, 0.024))],
    # 포니테일: 뒤통수 묶음에서 머리를 따라 아래로 늘어진다(뿌리 굵게) — 057 smoke 캡처에서 짧은 막대처럼 보여 고침
    "Hair_Ponytail": [("knot", (0.0, 0.25, -0.97), (0.020, 0.018, 0.016)),
                      ("tail", (0.0, 0.25, -0.97), (0.095, 0.022, 0.009, (0.0, -0.88, -0.47)))],
}
ATTACH_SEG = {0: (6, 8), 1: (4, 6), 2: (3, 4)}  # (고리 수, 둘레 분할)

# 가닥 카드 길이 (앞·옆·뒤, m). 057 FR-10: 삭발 제외 전부 카드가 있다.
CARD_LEN = {
    "tiny": (0.005, 0.005, 0.005), "short": (0.008, 0.009, 0.008), "fringe": (0.014, 0.009, 0.008),
    "long": (0.030, 0.024, 0.016), "curly": (0.012, 0.012, 0.010), "tight": (0.008, 0.007, 0.006),
}
CARD_KIND = {
    "Hair_Buzz": "tiny", "Hair_Fade": "tiny", "Hair_BuzzLine": "tiny", "Hair_Cornrows": "tiny", "Hair_Undercut": "tiny",
    "Hair_TopKnot": "tiny", "Hair_HighTop": "tiny", "Hair_Mohawk": "tiny", "Hair_CurlyTop": "tiny", "Hair_Spiky": "tiny",
    "Hair_Short": "short", "Hair_Crew": "short", "Hair_SidePart": "short", "Hair_IvyLeague": "short",
    "Hair_Quiff": "fringe", "Hair_TexturedCrop": "fringe", "Hair_FrenchCrop": "fringe",
    "Hair_Long": "long", "Hair_Curtains": "long", "Hair_MediumWave": "long", "Hair_Dreads": "long", "Hair_BoxBraids": "long",
    "Hair_HalfUp": "long",
    "Hair_Curly": "curly", "Hair_Afro": "curly", "Hair_Twists": "curly",
    "Hair_SlickBack": "tight", "Hair_Ponytail": "tight", "Hair_Bun": "tight",
}


def build(R, style, lod):
    """→ body.Mesh (머리카락 조각). 두피를 띄운 셸 + 가장자리 턱."""
    from . import body as B
    from . import face as F
    c, d = F.eye_frame(R)
    hi = MS.OUR_NAMES.index("Head")
    headw = R.weights[:, hi]
    ears = set(R.groups.get("ears", []))
    bodyset = set(R.groups["body"])
    top = float(R.verts[R.groups["body"], 1].max())
    N = R.normals
    hz = R.verts[[v for v in R.groups["body"] if headw[v] >= 0.5], 2]
    HEAD_AXIS_Z[0] = float((hz.min() + hz.max()) / 2)
    # 두피 정점: 머리 뼈 정점, 헤어라인 위, 귀 제외
    scalp = np.zeros(R.n, dtype=bool)
    for v in R.groups["body"]:
        p = R.verts[v]
        if v in ears or headw[v] < 0.5:
            continue
        if (p[1] - c[1]) / d > hairline(style, _phi(p, c), d) and N[v][1] > -0.35:
            scalp[v] = True
    faces = [f for f in R.faces if all(scalp[v] for v in f) and all(v in bodyset for v in f)]
    ids = sorted({v for f in faces for v in f})
    remap = {v: i for i, v in enumerate(ids)}
    fl = [tuple(remap[v] for v in f) for f in faces]
    base = R.verts[ids]
    # 가장자리까지 거리(고리 수)로 두께를 줄인다
    nb = B._neighbors(len(ids), fl)
    edge_cnt = {}
    for f in fl:
        for k in range(len(f)):
            e = (min(f[k], f[(k + 1) % len(f)]), max(f[k], f[(k + 1) % len(f)]))
            edge_cnt[e] = edge_cnt.get(e, 0) + 1
    boundary = sorted({v for e, n in edge_cnt.items() if n == 1 for v in e})
    ring = np.full(len(ids), 99)
    frontier = list(boundary)
    for v in frontier:
        ring[v] = 0
    k = 0
    while frontier:
        k += 1
        nxt = []
        for v in frontier:
            for u in nb[v]:
                if ring[u] > k:
                    ring[u] = k
                    nxt.append(u)
        frontier = nxt
    # 헤어라인 계단 펴기: 가장자리 고리 정점을 고리 이웃 평균으로 옮긴 뒤 머리 표면에 다시 붙인다(면 단위 선택의 계단 제거)
    bset = set(boundary)
    bnb = {v: [u for u in nb[v] if u in bset and (min(u, v), max(u, v)) in edge_cnt and edge_cnt[(min(u, v), max(u, v))] == 1]
           for v in boundary}
    base = base.copy()
    head_tree = B._subset_tree(R, [f for f in R.faces if all(headw[v] >= 0.3 and v in bodyset for v in f)])
    for _ in range(HAIRLINE_SMOOTH):
        nxt = base.copy()
        for v in boundary:
            if len(bnb[v]) == 2:
                nxt[v] = 0.5 * base[v] + 0.25 * (base[bnb[v][0]] + base[bnb[v][1]])
        for v in boundary:
            loc = head_tree[0].find_nearest(tuple(nxt[v]))[0]
            if loc is not None:
                nxt[v] = np.array(loc)
        base = nxt
    taper = np.clip(ring / 4.0, 0.25, 1.0)
    T = np.array([thickness(style, base[i], c, d, top) for i in range(len(ids))]) * taper
    shell = base + N[ids] * T[:, None]
    # 감량 (셸 위치에서)
    co, f2 = B._decimate_faces(shell, fl, min(HAIR_TRIS[lod], sum(len(f) - 2 for f in fl)))
    # 묶음: 셸 점 → 두피 최근접(셸을 두께만큼 되돌린 점 기준)
    tree = B._subset_tree(R, faces)
    anchor_tri = np.zeros((len(co), 3), dtype=np.int64)
    anchor_bc = np.zeros((len(co), 3))
    off = np.zeros((len(co), 3))
    for i, p in enumerate(co):
        (t, bc), _ = B._bind_point(R, tree, p)
        anchor_tri[i], anchor_bc[i] = t, bc
        a = (R.verts[t] * bc[:, None]).sum(axis=0)
        off[i] = p - a
    bind = B.Binding.__new__(B.Binding)
    bind.tri, bind.bc = anchor_tri, anchor_bc
    P = bind.apply(R.verts) + off
    W = np.zeros((len(co), len(MS.OUR_NAMES)))
    W[:, hi] = 1.0
    alld = B._all_deltas(R)
    D = {k2: bind.apply(v) for k2, v in sorted(alld.items())}
    m = B.Mesh()
    m.verts, m.W, m.deltas, m.vpart = P, W, D, ["hair"] * len(P)
    for f in f2:
        m.faces.append((tuple(f), B.BODY, tuple(hair_uv(P[i], c, d, top) for i in f), "hair", None))
    _rim(m, f2, off, c, d, top)
    for kind, dirn, size in ATTACH.get(style, []):
        _attach(m, kind, dirn, size, lod, c, d, top, head_tree, tree, R)
    if lod == 0:  # 카드는 셸·턱·부속 형상 뒤에 남은 몫만큼 (헤어 LOD0 합계 ≤ HAIR_LOD0_MAX)
        noear = B._subset_tree(R, [f for f in R.faces if all(headw[v] >= 0.3 and v in bodyset and v not in ears for v in f)])
        _cards(m, f2, off, style, c, d, top, noear, tree, R, min(CARD_MAX, HAIR_LOD0_MAX - m.triangles))
    return m


def _append(m, P, faces_uv, part, R, tree):
    """새 정점 P (k,3)를 두피 최근접 + 오프셋으로 묶어 m에 붙인다(머리 모양 델타를 따라간다). faces_uv: [(정점 지역 인덱스, UV)]."""
    from . import body as B
    hi = MS.OUR_NAMES.index("Head")
    base = len(m.verts)
    tri = np.zeros((len(P), 3), dtype=np.int64)
    bc = np.zeros((len(P), 3))
    off = np.zeros((len(P), 3))
    for i, p in enumerate(P):
        (t3, w3), _ = B._bind_point(R, tree, p)
        tri[i], bc[i] = t3, w3
        off[i] = p - (R.verts[t3] * w3[:, None]).sum(axis=0)
    bind = B.Binding.__new__(B.Binding)
    bind.tri, bind.bc = tri, bc
    Pn = bind.apply(R.verts) + off
    W = np.zeros((len(P), len(MS.OUR_NAMES)))
    W[:, hi] = 1.0
    alld = B._all_deltas(R)
    m.verts = np.vstack([m.verts, Pn])
    m.W = np.vstack([m.W, W])
    for k in m.deltas:
        m.deltas[k] = np.vstack([m.deltas[k], bind.apply(alld[k])])
    m.vpart += [part] * len(P)
    for f, uv in faces_uv:
        m.faces.append((tuple(base + i for i in f), B.BODY, tuple(uv), part, None))


def _cards(m, faces, off, style, c, d, top, head_tree, tree, R, budget=CARD_MAX):
    """057 FR-10~13: 셸 가장자리 변마다 가닥 카드(두피 접선 방향으로 헤어라인 밖까지, 끝은 피부 위 2 mm). LOD0만.
    카드마다 정점을 따로 둔다(UV 칸 안에서 반복). 삼각형 ≤ CARD_MAX."""
    from . import body as B
    P = m.verts
    edge, opp = {}, {}
    for f in faces:
        k = len(f)
        for i in range(k):
            a, b = f[i], f[(i + 1) % k]
            key = (min(a, b), max(a, b))
            edge.setdefault(key, []).append((a, b))
            opp[key] = P[list(f)].mean(axis=0)
    bnd = sorted(key for key, lst in edge.items() if len(lst) == 1)
    if not bnd:
        return
    segs = 2 if len(bnd) * 4 <= budget else 1
    if len(bnd) * 2 * segs > budget:
        keep = max(budget // (2 * segs), 0)
        step = len(bnd) / max(keep, 1)
        bnd = [bnd[int(i * step)] for i in range(keep)]  # 고르게 솎는다
    lf, ls, lb = CARD_LEN[CARD_KIND[style]]
    u0, v0, u1, v1 = CARD_REGION
    newP, fuv = [], []
    for key in bnd:
        a, b = edge[key][0]
        pa, pb = P[a], P[b]
        mid = 0.5 * (pa + pb)
        nrm = off[a] + off[b]
        nrm = nrm / max(np.linalg.norm(nrm), 1e-9)
        g = mid - opp[key]
        g = g - (g @ nrm) * nrm
        g = g / max(np.linalg.norm(g), 1e-9)
        phi = _phi(mid, c)
        L = lf + (ls - lf) * min(phi / 1.2, 1.0) if phi < 1.2 else ls + (lb - ls) * min((phi - 1.2) / 1.6, 1.0)
        rows = []
        bent = False
        for s in range(segs + 1):
            tt = s / segs
            row = []
            for q in (pa, pb):
                x = q + g * L * tt
                if s > 0:
                    loc, sn, _, _ = head_tree[0].find_nearest(tuple(x))
                    if loc is not None:
                        lift = 0.002 + 0.001 * tt
                        x = np.array(loc) + np.array(sn) * lift
                        x = x if (x - q) @ nrm > -0.002 else q + g * L * tt + nrm * lift
                    if (x - q) @ g < 0.5 * L * tt:  # 표면에 붙이며 뒤로 꺾였다(귀 뒤·오목한 곳) → 이 카드는 버린다
                        bent = True
                row.append(x)
            rows.append(row)
        if bent or any((rb - ra) @ (pb - pa) < 0.3 * float((pb - pa) @ (pb - pa)) for ra, rb in rows[1:]):
            continue  # 꺾이거나 끝이 엇갈린(X자) 카드는 버린다
        h = (key[0] * 2654435761 + key[1] * 40503) % 997 / 997.0
        w = min(np.linalg.norm(pb - pa) / CARD_TILE, 0.5)
        ua, ub = h * (1.0 - w), h * (1.0 - w) + w
        base = len(newP)
        for s, (qa, qb) in enumerate(rows):
            newP += [qa, qb]
        for s in range(segs):
            va, vb = 1.0 - s / segs, 1.0 - (s + 1) / segs
            uv = lambda uu, vv: (2.0 + u0 + (u1 - u0) * uu, v0 + (v1 - v0) * vv)
            i0, i1, i2, i3 = base + 2 * s, base + 2 * s + 1, base + 2 * s + 3, base + 2 * s + 2
            # 면 방향: 셸 바깥(법선 쪽)을 향하게 a→b 반대로 감는다(셸 면의 변 a→b와 같은 감기)
            fuv.append(((i1, i0, i3, i2), (uv(ub, va), uv(ua, va), uv(ua, vb), uv(ub, vb))))
    if newP:
        _append(m, np.array(newP), fuv, "hair_card", R, tree)


def _attach(m, kind, dirn, size, lod, c, d, top, head_tree, tree, R):
    """057: 묶음(타원체)·포니테일(관). 머리 중심에서 dirn으로 두피를 찾아 붙인다. UV는 머리카락 칸(가닥 = v)."""
    rings, segs = ATTACH_SEG[lod]
    hc = np.array([c[0], c[1] + 0.6 * d, HEAD_AXIS_Z[0]])
    dv = np.array(dirn, dtype=float)
    dv /= np.linalg.norm(dv)
    hit = head_tree[0].ray_cast(tuple(hc), tuple(dv))
    if hit[0] is None:
        raise RuntimeError(f"부속 형상 두피를 못 찾음: {dirn}")
    s, sn = np.array(hit[0]), np.array(hit[1])
    if sn @ dv < 0:
        sn = -sn  # 감기 방향과 상관없이 바깥 법선
    pts, fuv = [], []
    u0, v0, u1, v1 = REGION

    def uv(uu, vv):
        return (1.0 + u0 + (u1 - u0) * uu, v0 + (v1 - v0) * vv)

    if kind == "knot":
        rx, ry, rz = size
        cen = s + sn * 0.55 * max(rx, ry, rz)
        ax_up = sn
        ax_x = np.cross(ax_up, [0.0, 0.0, 1.0]) if abs(ax_up[2]) < 0.9 else np.cross(ax_up, [1.0, 0.0, 0.0])
        ax_x /= np.linalg.norm(ax_x)
        ax_z = np.cross(ax_x, ax_up)
        pts.append(cen + ax_up * ry)
        for r in range(1, rings):
            th = math.pi * r / rings
            for k in range(segs):
                ph = 2 * math.pi * k / segs
                pts.append(cen + ax_up * ry * math.cos(th) + ax_x * rx * math.sin(th) * math.cos(ph)
                           + ax_z * rz * math.sin(th) * math.sin(ph))
        pts.append(cen - ax_up * ry)
        for k in range(segs):
            k2 = (k + 1) % segs
            fuv.append(((0, 1 + k2, 1 + k), (uv(0.5, 1.0), uv(k2 / segs, 0.8), uv(k / segs, 0.8))))
        for r in range(rings - 2):
            for k in range(segs):
                k2 = (k + 1) % segs
                a, b = 1 + r * segs + k, 1 + r * segs + k2
                cc, dd = 1 + (r + 1) * segs + k2, 1 + (r + 1) * segs + k
                vv = 0.8 - 0.6 * r / max(rings - 2, 1)
                fuv.append(((a, b, cc, dd), (uv(k / segs, vv), uv(k2 / segs, vv), uv(k2 / segs, vv - 0.1), uv(k / segs, vv - 0.1))))
        last = len(pts) - 1
        lr = 1 + (rings - 2) * segs
        for k in range(segs):
            k2 = (k + 1) % segs
            fuv.append(((last, lr + k, lr + k2), (uv(0.5, 0.0), uv(k / segs, 0.1), uv(k2 / segs, 0.1))))
    else:  # tail
        length, r0, r1, dn = size
        dn = np.array(dn, dtype=float)
        dn /= np.linalg.norm(dn)
        start = s + sn * 0.004
        ax_x = np.array([1.0, 0.0, 0.0])
        ax_z = np.cross(dn, ax_x)
        ax_z /= np.linalg.norm(ax_z)
        n_r = max(rings - 2, 1)
        for r in range(n_r + 1):
            tt = r / n_r
            cen = start + dn * length * tt
            rad = r0 + (r1 - r0) * tt
            for k in range(segs):
                ph = 2 * math.pi * k / segs
                pts.append(cen + (ax_x * math.cos(ph) + ax_z * math.sin(ph)) * rad)
        pts.append(start + dn * (length + 0.4 * r1))
        for r in range(n_r):
            for k in range(segs):
                k2 = (k + 1) % segs
                a, b = r * segs + k, r * segs + k2
                cc, dd = (r + 1) * segs + k2, (r + 1) * segs + k
                fuv.append(((a, dd, cc, b), (uv(k / segs, 1 - r / n_r), uv(k / segs, 1 - (r + 1) / n_r),
                                             uv(k2 / segs, 1 - (r + 1) / n_r), uv(k2 / segs, 1 - r / n_r))))
        tip = len(pts) - 1
        lr = n_r * segs
        for k in range(segs):
            k2 = (k + 1) % segs
            fuv.append(((lr + k, tip, lr + k2), (uv(k / segs, 0.05), uv(0.5, 0.0), uv(k2 / segs, 0.05))))
    _append(m, np.array(pts), fuv, "hair", R, tree)


def _rim(m, faces, off, c, d, top):
    """셸 가장자리 → 두피 안쪽(두께 + 1 mm 아래)으로 띠."""
    from . import body as B
    edge = {}
    for f in faces:
        for k in range(len(f)):
            a, b = f[k], f[(k + 1) % len(f)]
            edge.setdefault((min(a, b), max(a, b)), []).append((a, b))
    new = {}
    P = m.verts
    extra = []
    for key in sorted(edge):
        if len(edge[key]) != 1:
            continue
        a, b = edge[key][0]
        for v in (a, b):
            if v not in new:
                n = off[v] / max(np.linalg.norm(off[v]), 1e-9)
                new[v] = len(P) + len(extra)
                extra.append((v, P[v] - off[v] - n * 0.001))
        m.faces.append(((b, a, new[a], new[b]), B.BODY,
                        tuple(hair_uv(x, c, d, top) for x in (P[b], P[a], P[a], P[b])), "hair", None))
    if extra:
        src = [v for v, _ in extra]
        m.verts = np.vstack([P, np.array([p for _, p in extra])])
        m.W = np.vstack([m.W, m.W[src]])
        for k in m.deltas:
            m.deltas[k] = np.vstack([m.deltas[k], m.deltas[k][src]])
        m.vpart += ["hair"] * len(extra)


def hair_uv(p, c, d, top):
    """u = 머리 둘레 각(정면 0 → 뒤 1, 좌우 대칭), v = 정수리 1 → 헤어라인 0. parts 칸 + u 1 오프셋."""
    phi = _phi(p, c) / math.pi
    v = np.clip((p[1] - (c[1] - 1.8 * d)) / (top + 0.06 - (c[1] - 1.8 * d)), 0, 1)
    u0, v0, u1, v1 = REGION
    return (1.0 + u0 + (u1 - u0) * phi, v0 + (v1 - v0) * float(v))
