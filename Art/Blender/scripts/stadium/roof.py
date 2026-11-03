"""지붕 (specs/044 FR-10). none: 없음, partial: 메인(S)·반대(N) 스탠드, full: 4면."""
from . import geometry
from . import layout as L


def roofed_sections(roof):
    return {"none": (), "partial": ("S", "N"), "full": L.SECTIONS}[roof]


def roof_height(pl):
    return pl["top"] + L.ROOF_CLEAR


def build(p, mats, pl):
    h = roof_height(pl)
    d = pl["depth"]
    out = []
    for sec in roofed_sections(p.roof):
        n, t, front, half = L.section_frame(sec)
        b = geometry.MeshBuilder()
        # 전체 지붕: S·N이 모서리까지 덮고, W·E는 S·N 지붕과 겹치지 않게 짧게 (같은 높이 면 겹침 방지)
        if p.roof == "full":
            ext = half + d if sec in ("S", "N") else L.STAND_Y - L.ROOF_OVERHANG
        else:
            ext = half
        u_a, u_b = -L.ROOF_OVERHANG, d
        pts = []
        for u, s in ((u_a, -ext), (u_b, -ext), (u_b, ext), (u_a, ext)):
            dd = front + u
            pts.append((n[0] * dd + t[0] * s, n[1] * dd + t[1] * s))
        xs = [q[0] for q in pts]
        ys = [q[1] for q in pts]
        b.box((min(xs), min(ys), h), (max(xs), max(ys), h + L.ROOF_T), mats["Roof"], skip_bottom=False)
        # 뒤 기둥 벽 (지붕을 받친다)
        bd = front + d
        bx = [n[0] * bd + t[0] * s for s in (-ext, ext)] + [n[0] * (bd + 0.6) + t[0] * s for s in (-ext, ext)]
        by = [n[1] * bd + t[1] * s for s in (-ext, ext)] + [n[1] * (bd + 0.6) + t[1] * s for s in (-ext, ext)]
        b.box((min(bx), min(by), 0.0), (max(bx), max(by), h), mats["Concrete"])
        b.build(f"Roof_{sec}")
        front_line = [(n[0] * (front + u_a) + t[0] * s, n[1] * (front + u_a) + t[1] * s, h) for s in (-ext, ext)]
        out.append({"section": sec, "front": front_line, "height": h})
    return out
