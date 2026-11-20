"""구조물 차폐(AO) 굽기 (specs/061 FR-30). 면 모서리(corner)마다 반구 레이를 쏴 정점 색 속성 `ao`에 넣는다.

- 텍스처 없음: 경기장이 단순한 메시(≈ 6천 삼각형)라 UV2 아틀라스를 물체마다 따로 쓰는 044 구조에서는 한 장으로 굽기 어렵다.
  모서리 단위 값은 관중석 계단(디딤판–챌판 안쪽)·지붕 아래 벽 쪽처럼 구조물 AO가 생기는 곳을 담는다.
- 결정론: 레이 방향은 고정 피보나치 반구, 물체·면·모서리 순서 고정, 난수 없음 → 같은 입력 → 같은 값(바이트 색).
- Unity 쪽 `StadiumSurface` 셰이더가 정점 색 R을 차폐로 곱한다.
"""
import math

ATTR = "ao"
TARGET_PREFIXES = ("Stand_", "Roof_", "Tunnel", "Bench_", "LightTower_", "Boards")
RAYS = 48
MAX_DIST = 10.0      # m — 이보다 먼 차폐물은 무시 (경기장 반대편 스탠드가 하늘을 막는 정도는 넣지 않음)
STRENGTH = 0.85
MIN_AO = 0.25


def _directions(n):
    """z+ 반구 피보나치 방향 (코사인 가중 근사: 고도 분포 sqrt)."""
    out = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for i in range(n):
        u = (i + 0.5) / n
        r = math.sqrt(u)
        z = math.sqrt(max(0.0, 1.0 - u))
        a = golden * i
        out.append((r * math.cos(a), r * math.sin(a), z))
    return out


def _frame(n):
    from mathutils import Vector
    up = Vector((0.0, 0.0, 1.0)) if abs(n.z) < 0.9 else Vector((1.0, 0.0, 0.0))
    t = up.cross(n).normalized()
    b = n.cross(t)
    return t, b


def _bvh(objs):
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in objs:
        mw = o.matrix_world
        base = len(verts)
        verts.extend(mw @ v.co for v in o.data.vertices)
        polys.extend([base + i for i in p.vertices] for p in o.data.polygons)
    return BVHTree.FromPolygons(verts, polys, epsilon=0.0)


def bake(mesh_objects):
    """대상 물체에 `ao` 모서리 색을 만들고 통계를 돌려준다."""
    from mathutils import Vector
    objs = list(mesh_objects)
    tree = _bvh(objs)
    dirs = [Vector(d) for d in _directions(RAYS)]
    stats = {"objects": 0, "corners": 0, "min": 1.0, "max": 0.0, "down": [], "up": []}
    for o in objs:
        if not o.name.startswith(TARGET_PREFIXES):
            continue
        me = o.data
        if ATTR in me.color_attributes:
            me.color_attributes.remove(me.color_attributes[ATTR])
        attr = me.color_attributes.new(ATTR, "BYTE_COLOR", "CORNER")
        mw = o.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        for poly in me.polygons:
            n = (nm @ poly.normal).normalized()
            c = mw @ poly.center
            t, b = _frame(n)
            face_ao = []
            for li in poly.loop_indices:
                v = mw @ me.vertices[me.loops[li].vertex_index].co
                p = v + (c - v) * 0.03 + n * 0.02
                occl = 0.0
                for d in dirs:
                    w = t * d.x + b * d.y + n * d.z
                    hit, _, _, dist = tree.ray_cast(p, w, MAX_DIST)
                    if hit is not None:
                        occl += 1.0 - dist / MAX_DIST
                ao = max(MIN_AO, 1.0 - STRENGTH * occl / len(dirs))
                attr.data[li].color = (ao, ao, ao, 1.0)
                face_ao.append(ao)
                stats["corners"] += 1
                stats["min"] = min(stats["min"], ao)
                stats["max"] = max(stats["max"], ao)
            if o.name.startswith("Roof_"):
                (stats["down"] if n.z < -0.5 else stats["up"] if n.z > 0.5 else []).append(sum(face_ao) / len(face_ao))
        me.color_attributes.active_color = attr
        stats["objects"] += 1
    down = sum(stats["down"]) / len(stats["down"]) if stats["down"] else None
    up = sum(stats["up"]) / len(stats["up"]) if stats["up"] else None
    return {"objects": stats["objects"], "corners": stats["corners"], "min": round(stats["min"], 4), "max": round(stats["max"], 4),
            "roofUnder": None if down is None else round(down, 4), "roofTop": None if up is None else round(up, 4)}


def check(s):
    """AO 0~1, 지붕 아래가 위보다 어둡다(지붕이 있을 때)."""
    ok = s["objects"] > 0 and 0.0 <= s["min"] <= s["max"] <= 1.0 and s["min"] < 0.95
    if s["roofUnder"] is not None and s["roofTop"] is not None:
        ok = ok and s["roofUnder"] < s["roofTop"]
    return ok
