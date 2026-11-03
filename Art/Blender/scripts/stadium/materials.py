"""경기장 머티리얼 (≤ 12, specs/044 AC-05). Principled BSDF만 — Unity에서 URP Lit로 매핑, 피치는 임포트 때 Pitch.mat로 교체."""
from . import geometry

DEFS = {
    "Pitch": ((0.16, 0.42, 0.14, 1.0), None),
    "Concrete": ((0.55, 0.55, 0.56, 1.0), None),
    "Metal": ((0.62, 0.64, 0.68, 1.0), None),
    "Roof": ((0.85, 0.86, 0.88, 1.0), None),
    "Net": ((0.95, 0.95, 0.95, 0.45), None),
    "Board": ((0.10, 0.12, 0.16, 1.0), None),
    "LightEmit": ((1.0, 0.98, 0.92, 1.0), (1.0, 0.98, 0.92, 4.0)),
    "Ground": ((0.30, 0.31, 0.30, 1.0), None),
    "Bench": ((0.20, 0.22, 0.26, 1.0), None),
    "Tunnel": ((0.05, 0.05, 0.06, 1.0), None),
}


def srgb_to_linear(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def make_all(seat_colors):
    m = {k: geometry.material(k, rgba, emission) for k, (rgba, emission) in DEFS.items()}
    for name, col in (("Seat_A", seat_colors[0]), ("Seat_B", seat_colors[1])):
        m[name] = geometry.material(name, tuple(srgb_to_linear(x) for x in col) + (1.0,))
    net = m["Net"]
    net.surface_render_method = "BLENDED"
    return m
