"""광고판·벤치·터널 (specs/044 FR-04·10·50·63). 광고판은 런오프 바깥, 가상 브랜드(무늬 없음)."""
from . import geometry
from . import layout as L


def build(p, mats):
    if p.boards:
        b = geometry.MeshBuilder()
        m = mats["Board"]
        x0, y0 = L.RUN_X + L.BOARD_GAP, L.RUN_Y + L.BOARD_GAP
        x1, y1 = x0 + L.BOARD_T, y0 + L.BOARD_T
        b.box((-x1, -y1, 0), (x1, -y0, L.BOARD_H), m)
        b.box((-x1, y0, 0), (x1, y1, L.BOARD_H), m)
        b.box((-x1, -y0, 0), (-x0, y0, L.BOARD_H), m)
        b.box((x0, -y0, 0), (x1, y0, L.BOARD_H), m)
        b.build("Boards")
    y_front = L.RUN_Y + L.BOARD_GAP + L.BOARD_T + 0.4
    y_back = L.STAND_Y - 0.4
    for name, xa, xb in (("Bench_Home", -16.0, -6.0), ("Bench_Away", 6.0, 16.0)):
        b = geometry.MeshBuilder()
        b.box((xa, -y_back, 0.0), (xb, -y_front, 1.6), mats["Bench"])
        b.build(name)
    t = geometry.MeshBuilder()
    t.box((-2.0, -(L.STAND_Y + 2.5), 0.0), (2.0, -(L.STAND_Y - 0.02), 2.5), mats["Tunnel"])
    t.build("Tunnel")
