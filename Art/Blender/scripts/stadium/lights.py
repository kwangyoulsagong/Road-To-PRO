"""조명 (specs/044 FR-40~42·61). 발광 머티리얼만(빛줄기 메시 없음). towers: 모서리 4기, roofline: 지붕 앞선 띠."""
from . import geometry
from . import layout as L

HEAD_W, HEAD_H, HEAD_T = 8.0, 5.0, 1.0


def build(p, mats, pl, roofs):
    emitters = []
    d = pl["depth"]
    if p.lights == "towers":
        h = max(L.TOWER_H, pl["top"] + 15.0)
        parent = geometry.empty("Lights")
        for sx, sy, nm in ((-1, -1, "SW"), (1, -1, "SE"), (-1, 1, "NW"), (1, 1, "NE")):
            x = sx * (L.STAND_X + d * 0.7 + L.TOWER_OFFSET)
            y = sy * (L.STAND_Y + d * 0.7 + L.TOWER_OFFSET)
            b = geometry.MeshBuilder()
            b.cylinder((x, y, 0.0), (x, y, h), 0.8, 12, mats["Metal"])
            b.box((x - HEAD_W / 2, y - HEAD_T / 2, h), (x + HEAD_W / 2, y + HEAD_T / 2, h + HEAD_H), mats["LightEmit"], skip_bottom=False)
            b.build(f"LightTower_{nm}", parent=parent)
            emitters.append(L.unity((x, y, h + HEAD_H / 2)))
    else:
        parent = geometry.empty("Lights")
        for r in roofs:
            a, b2 = r["front"]
            lb = geometry.MeshBuilder()
            lo = (min(a[0], b2[0]) - 0.3, min(a[1], b2[1]) - 0.3, r["height"] - 0.5)
            hi = (max(a[0], b2[0]) + 0.3, max(a[1], b2[1]) + 0.3, r["height"])
            lb.box(lo, hi, mats["LightEmit"], skip_bottom=False)
            lb.build(f"LightStrip_{r['section']}", parent=parent)
        corners = []
        for r in roofs:
            corners.extend(r["front"])
        # 모서리 4점: 지붕 앞선 끝점 중 대각 4개 (x·y 부호별 가장 바깥)
        for sx in (-1, 1):
            for sy in (-1, 1):
                best = max(corners, key=lambda q: (sx * q[0] + sy * q[1], q[0], q[1]))
                emitters.append(L.unity(best))
    return emitters
