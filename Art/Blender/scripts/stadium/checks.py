"""생성 검사·측정 (specs/044 AC-01·05·07·08·09, FR-13·62·63). 실패하면 gen_stadium이 종료 코드 3."""
import math

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import geometry, markings, uv2
from . import layout as L

TOL = 0.005
RUNOFF_EXEMPT_PREFIX = ("Pitch", "Ground", "GoalFrame_", "Net_", "CornerFlags")


def mesh_objects():
    return sorted((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: o.name)


def stadium_verts(obj):
    m = obj.matrix_world
    out = []
    for v in obj.data.vertices:
        w = m @ v.co
        out.append((-w.x, -w.y, w.z))
    return out


def bbox(pts):
    return ([min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)])


def _goal(obj, side):
    pts = stadium_verts(obj)
    ps = [p for p in pts if p[1] < 0]
    pn = [p for p in pts if p[1] > 0]
    bs, bn = bbox(ps), bbox(pn)
    bar_bottom = min(p[2] for p in pts if p[2] > 1.0)
    return {
        "innerWidth": bn[0][1] - bs[1][1],
        "innerHeight": bar_bottom,
        "postCenterX": (bs[0][0] + bs[1][0]) / 2,
        "postDiameter": bs[1][0] - bs[0][0],
        "postCentersZ": [(bs[0][1] + bs[1][1]) / 2, (bn[0][1] + bn[1][1]) / 2],
    }


def measure():
    objs = {o.name: o for o in mesh_objects()}
    lo, hi = bbox(stadium_verts(objs["Pitch"]))
    out = {"Pitch": {"min": L.unity(lo), "max": L.unity(hi)}}
    for side, nm in ((-1, "L"), (1, "R")):
        out[f"Goal_{nm}"] = _goal(objs[f"GoalFrame_{nm}"], side)
    flags = []
    pts = stadium_verts(objs["CornerFlags"])
    for sx in (-1, 1):
        for sy in (-1, 1):
            q = [p for p in pts if p[0] * sx > 0 and p[1] * sy > 0]
            flags.append({"x": sx * L.HALF_L, "z": sy * L.HALF_W, "height": max(p[2] for p in q)})
    out["CornerFlags"] = flags
    return out


def spec_check(meas):
    fails = []

    def near(v, want, what, tol=TOL):
        if abs(v - want) > tol:
            fails.append(f"{what}: 기대 {want}, 실제 {v:.4f}")

    p = meas["Pitch"]
    near(p["min"][0], -L.RUN_X, "피치 면 −X", 0.01)
    near(p["max"][0], L.RUN_X, "피치 면 +X", 0.01)
    near(p["min"][2], -L.RUN_Y, "피치 면 −Z", 0.01)
    near(p["max"][2], L.RUN_Y, "피치 면 +Z", 0.01)
    for nm, gx in (("Goal_L", -L.HALF_L), ("Goal_R", L.HALF_L)):
        g = meas[nm]
        near(g["innerWidth"], markings.GOAL_INNER_WIDTH, nm + " 안쪽 폭")
        near(g["innerHeight"], markings.GOAL_INNER_HEIGHT, nm + " 안쪽 높이")
        near(g["postCenterX"], gx, nm + " 포스트 중심 = 골라인 중심")
        near(g["postDiameter"], 2 * markings.POST_RADIUS, nm + " 포스트 지름")
    for f in meas["CornerFlags"]:
        if f["height"] < 1.5 - 1e-6:
            fails.append(f"코너 플래그 높이 {f['height']:.3f} < 1.5")
    m = markings.pitch_markings()
    if (m["length"], m["width"], m["lineWidth"]) != (105.0, 68.0, 0.12):
        fails.append("마킹 기본 치수")
    return {"ok": not fails, "failures": fails}


def runoff_intrusions():
    """지면 근처(z < 3 m) 면 중 런오프 사각형 안쪽(경계에서 1 mm 넘게)으로 들어온 것. 면 단위 — 고리·L자 물체의 경계 상자 오탐 방지."""
    bad = []
    e = 1e-3
    for o in mesh_objects():
        if o.name.startswith(RUNOFF_EXEMPT_PREFIX):
            continue
        pts = stadium_verts(o)
        for poly in o.data.polygons:
            lo, hi = bbox([pts[i] for i in poly.vertices])
            if lo[2] >= 3.0:
                continue
            if lo[0] < L.RUN_X - e and hi[0] > -L.RUN_X + e and lo[1] < L.RUN_Y - e and hi[1] > -L.RUN_Y + e:
                bad.append(o.name)
                break
    return {"intrusions": bad}


def visibility(mount):
    dg = bpy.context.evaluated_depsgraph_get()
    origin = Vector(geometry.to_blender(mount))
    corners, blocked = [], []
    for sx in (-1, 1):
        for sy in (-1, 1):
            target = Vector(geometry.to_blender((sx * L.HALF_L, sy * L.HALF_W, 0.05)))
            d = target - origin
            dist = d.length
            hit, loc, _, _, obj, _ = bpy.context.scene.ray_cast(dg, origin, d.normalized(), distance=dist)
            ok = (not hit) or (loc - origin).length >= dist - 0.5 or obj.name.startswith(("Pitch", "Ground", "CornerFlags"))
            name = f"{'+' if sx > 0 else '-'}X{'+' if sy > 0 else '-'}Z"
            corners.append({"corner": name, "visible": ok, "hit": None if ok else obj.name})
            if not ok:
                blocked.append(name)
    return {"corners": corners, "blockedCorners": blocked}


def seats_on_mesh(rows):
    stands = [o for o in mesh_objects() if o.name.startswith("Stand_")]
    dg = bpy.context.evaluated_depsgraph_get()
    trees = [BVHTree.FromObject(o, dg) for o in stands]
    worst, n = 0.0, 0
    for r in rows:
        sx, sy, sz = r["start"]
        dx, dy, dz = r["dir"]
        for k in range(r["count"]):
            u = (sx + dx * k * r["spacing"], sy + dy * k * r["spacing"], sz + dz * k * r["spacing"])
            p = Vector(geometry.to_blender((u[0], u[2], u[1])))
            best = min((t.find_nearest(p)[3] for t in trees), key=lambda v: v if v is not None else 1e9)
            worst = max(worst, best)
            n += 1
    return {"checked": n, "maxDistance": worst}


def uv2_check():
    missing, worst = [], 0.0
    for o in mesh_objects():
        if len(o.data.uv_layers) < 2:
            missing.append(o.name)
            continue
        worst = max(worst, uv2.overlap_ratio(o))
    return {"missing": missing, "overlapRatio": worst}


def summary_counts():
    tris, mats, total = {}, set(), 0
    for o in mesh_objects():
        t = geometry.triangle_count([o])
        part = o.name.split("_")[0]
        tris[part] = tris.get(part, 0) + t
        total += t
        for m in o.data.materials:
            if m is not None:
                mats.add(m.name)
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    for o in mesh_objects():
        a, b = bbox(stadium_verts(o))
        lo = [min(x, y) for x, y in zip(lo, a)]
        hi = [max(x, y) for x, y in zip(hi, b)]
    tris["total"] = total
    return {"triangles": tris, "materials": len(mats), "materialNames": sorted(mats),
            "objects": len(mesh_objects()), "bbox": {"min": L.unity(lo), "max": L.unity(hi)}}
