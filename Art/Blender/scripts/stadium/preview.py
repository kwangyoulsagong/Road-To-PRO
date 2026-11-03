"""미리보기 렌더 3장 (specs/044 FR-14). Workbench(무작위 샘플 없음), 1280×720, 메타데이터 도장 끔.

라인은 미리보기 전용 얇은 메시(내보내기 뒤에 만든다 — glb에 들어가지 않는다).
"""
import math

import bpy
from mathutils import Vector

from . import geometry, markings
from . import layout as L

W, H = 1280, 720
ARC_SEG = 96


def _line_mesh():
    m = markings.pitch_markings()
    hw = m["lineWidth"] / 2
    mat = geometry.material("PreviewLine", (0.95, 0.95, 0.95, 1.0))
    b = geometry.MeshBuilder()
    z = 0.01
    for l in m["lines"]:
        if l["kind"] == "segment":
            (ax, ay), (bx, by) = l["a"], l["b"]
            dx, dy = bx - ax, by - ay
            n = math.hypot(dx, dy)
            px, py = -dy / n * hw, dx / n * hw
            b.quad((ax - px, ay - py, z), (bx - px, by - py, z), (bx + px, by + py, z), (ax + px, ay + py, z), mat, (0, 0, 1))
        else:
            (cx, cy), r = l["c"], l["r"]
            seg = max(8, int(ARC_SEG * (l["to"] - l["from"]) / 360))
            for k in range(seg):
                a0 = math.radians(l["from"] + (l["to"] - l["from"]) * k / seg)
                a1 = math.radians(l["from"] + (l["to"] - l["from"]) * (k + 1) / seg)
                pts = [(cx + (r + s) * math.cos(a), cy + (r + s) * math.sin(a), z) for a, s in ((a0, -hw), (a1, -hw), (a1, hw), (a0, hw))]
                b.quad(*pts, mat, (0, 0, 1))
    for s in m["spots"]:
        (cx, cy), r = s["c"], s["r"]
        idx = [b.v((cx + r * math.cos(2 * math.pi * k / 16), cy + r * math.sin(2 * math.pi * k / 16), z)) for k in range(16)]
        b.face(idx, mat, (0, 0, 1))
    b.build("PreviewLines")


def _camera(name, loc, target, ortho=None, lens=35.0):
    cam = bpy.data.cameras.new(name)
    cam.lens = lens
    cam.clip_end = 2000.0
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    o = bpy.data.objects.new(name, cam)
    bpy.context.scene.collection.objects.link(o)
    o.location = Vector(geometry.to_blender(loc))
    d = Vector(geometry.to_blender(target)) - o.location
    o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return o


def render(prefix, mount):
    _line_mesh()
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.display.render_aa = "FXAA"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "MATERIAL"
    sh.show_shadows = False
    sh.show_cavity = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new("World")
    sc.world.color = (0.55, 0.68, 0.85)
    for attr in dir(sc.render):
        if attr.startswith("use_stamp"):
            try:
                setattr(sc.render, attr, False)
            except (AttributeError, TypeError):
                pass
    img = sc.render.image_settings
    img.file_format = "PNG"
    img.color_mode = "RGB"
    img.color_depth = "8"
    img.compression = 15
    views = {
        "main": _camera("PreviewMain", mount, (0.0, 0.0, 0.0), lens=24.0),
        "goal": _camera("PreviewGoal", (-L.HALF_L - 18.0, -10.0, 38.0), (-L.HALF_L + 18.0, 0.0, 0.0), lens=24.0),
        "top": _camera("PreviewTop", (0.0, 0.0, 300.0), (0.0, 0.0001, 0.0), ortho=L.STAND_X * 2 + 60.0),
    }
    out = {}
    for name, cam in views.items():
        sc.camera = cam
        sc.render.filepath = f"{prefix}.preview-{name}.png"
        bpy.ops.render.render(write_still=True)
        out[name] = sc.render.filepath
    return out
