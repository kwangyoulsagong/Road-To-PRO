"""피치 면·외곽 바닥·코너 플래그 (specs/044 FR-01·04·05). 라인은 메시가 아니다(셰이더, FR-32)."""
from . import geometry
from .layout import HALF_L, HALF_W, RUN_X, RUN_Y

CORNER_FLAG_H = 1.5
OUTER = 140.0  # 외곽 바닥 반폭 (관중석 바깥까지 덮는다)


def build(mats):
    b = geometry.MeshBuilder()
    b.quad((-RUN_X, -RUN_Y, 0), (RUN_X, -RUN_Y, 0), (RUN_X, RUN_Y, 0), (-RUN_X, RUN_Y, 0), mats["Pitch"], (0, 0, 1))
    b.build("Pitch")

    g = geometry.MeshBuilder()
    m, z = mats["Ground"], 0.0
    g.quad((-OUTER, -OUTER, z), (OUTER, -OUTER, z), (OUTER, -RUN_Y, z), (-OUTER, -RUN_Y, z), m, (0, 0, 1))
    g.quad((-OUTER, RUN_Y, z), (OUTER, RUN_Y, z), (OUTER, OUTER, z), (-OUTER, OUTER, z), m, (0, 0, 1))
    g.quad((-OUTER, -RUN_Y, z), (-RUN_X, -RUN_Y, z), (-RUN_X, RUN_Y, z), (-OUTER, RUN_Y, z), m, (0, 0, 1))
    g.quad((RUN_X, -RUN_Y, z), (OUTER, -RUN_Y, z), (OUTER, RUN_Y, z), (RUN_X, RUN_Y, z), m, (0, 0, 1))
    g.build("Ground")

    f = geometry.MeshBuilder()
    flags = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * HALF_L, sy * HALF_W
            f.cylinder((x, y, 0.0), (x, y, CORNER_FLAG_H), 0.02, 6, mats["Metal"])
            flags.append((x, y))
    f.build("CornerFlags")
    return flags
