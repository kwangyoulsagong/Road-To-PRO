"""스킨·셰이프키·애니메이션 임포트 검증 에셋 (specs/043 T01).

만드는 것: 2본 뼈대(`Root` → `Tip`)에 스킨된 1×1×2 m 기둥 `Column`(셰이프키 `Wide`),
액션 2개 `Bend`(Tip이 X축으로 0→90°, 1 s)와 `Twist`(Tip이 Z축으로 0→90°, 1 s), 30 fps.
Unity 임포트 후 Mecanim 클립 2개·블렌드셰이프 1개·본 2개가 있어야 한다.

인자: --out <glb 경로>
"""
import argparse
import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30


def build():
    arm_data = bpy.data.armatures.new("SkinTestRig")
    arm = bpy.data.objects.new("SkinTestRig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    root = arm_data.edit_bones.new("Root")
    root.head, root.tail = (0, 0, 0), (0, 0, 1)
    tip = arm_data.edit_bones.new("Tip")
    tip.head, tip.tail = (0, 0, 1), (0, 0, 2)
    tip.parent = root
    tip.use_connect = True
    bpy.ops.object.mode_set(mode="OBJECT")

    # 기둥: 높이 0~2 m, 8단 링 × 4각
    verts, faces = [], []
    rings, sides = 9, 4
    for r in range(rings):
        z = 2.0 * r / (rings - 1)
        for s in range(sides):
            a = math.pi / 4 + 2 * math.pi * s / sides
            verts.append((0.5 * math.sqrt(2) * math.cos(a), 0.5 * math.sqrt(2) * math.sin(a), z))
    for r in range(rings - 1):
        for s in range(sides):
            a0, a1 = r * sides + s, r * sides + (s + 1) % sides
            faces.append((a0, a1, a1 + sides, a0 + sides))
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple((rings - 1) * sides + s for s in range(sides)))
    me = bpy.data.meshes.new("Column")
    me.from_pydata(verts, [], faces)
    me.update()
    col = bpy.data.objects.new("Column", me)
    bpy.context.scene.collection.objects.link(col)
    col.parent = arm
    gr, gt = col.vertex_groups.new(name="Root"), col.vertex_groups.new(name="Tip")
    for i, v in enumerate(verts):
        w = min(1.0, max(0.0, v[2] - 0.5))  # 0.5~1.5 m 구간에서 선형 전환
        if w < 1.0:
            gr.add([i], 1.0 - w, "REPLACE")
        if w > 0.0:
            gt.add([i], w, "REPLACE")
    mod = col.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    col.shape_key_add(name="Basis")
    wide = col.shape_key_add(name="Wide")
    for i, v in enumerate(verts):
        wide.data[i].co = (v[0] * 1.5, v[1] * 1.5, v[2])

    def action(name, axis):
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        arm.animation_data_create()
        arm.animation_data.action = act
        pb = arm.pose.bones["Tip"]
        pb.rotation_mode = "XYZ"
        for f, deg in ((1, 0.0), (31, 90.0)):
            pb.rotation_euler = (0, 0, 0)
            pb.rotation_euler[axis] = math.radians(deg)
            pb.keyframe_insert("rotation_euler", frame=f)
        track = arm.animation_data.nla_tracks.new()
        track.name = name
        track.strips.new(name, 1, act)
        arm.animation_data.action = None

    action("Bend", 0)
    action("Twist", 2)


def write_glb(path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, export_apply=False,
        export_texcoords=True, export_normals=True, export_tangents=False,
        export_materials="NONE", export_cameras=False, export_lights=False, export_extras=False,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_frame_range=False,
        export_force_sampling=True, export_skins=True, export_all_influences=False,
        export_morph=True, export_morph_normal=False,
        use_selection=False, use_visible=False, use_renderable=False, export_copyright="",
    )


def main(argv):
    ap = argparse.ArgumentParser(prog="gen_skin_test")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    reset()
    build()
    write_glb(a.out)
    print(f"[skin-test] → {a.out}")


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
