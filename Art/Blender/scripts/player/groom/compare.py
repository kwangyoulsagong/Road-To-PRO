"""그루밍 시안 렌더·비교 (specs/088 FR-20 절차 ②, T09). 게임 기준 선수(MPFB) 머리에 스타일을 그루밍해 Cycles로 정면·3/4·옆·뒤를 찍는다.
참고 사진은 인자로만 받고(레포·증거에 저장 안 함) 나란히 붙인 비교 이미지를 출력 폴더에 만든다.
실행: Blender --background --python player/groom/compare.py -- <출력 폴더> <스타일[,스타일…]> [--density 0.5] [--ref <사진>]"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.normpath(os.path.join(HERE, "..", "..")))

from player.groom import frame, styles  # noqa: E402


def to_curves(name, S, radius_root):
    k, n, _ = S.shape
    cv = bpy.data.hair_curves.new(name)
    cv.add_curves([n] * k)
    cv.position_data.foreach_set("vector", S.reshape(-1).astype(np.float32))
    rad = np.tile(radius_root * (1 - 0.88 * np.linspace(0, 1, n) ** 1.6), k).astype(np.float32)   # 끝으로 갈수록 가늘게(테이퍼)
    att = cv.attributes.get("radius") or cv.attributes.new("radius", "FLOAT", "POINT")
    att.data.foreach_set("value", rad)
    ob = bpy.data.objects.new(name, cv)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def hair_mat(color):
    m = bpy.data.materials.new("hair")
    m.use_nodes = True
    nt = m.node_tree
    for nd in list(nt.nodes):
        nt.nodes.remove(nd)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    h = nt.nodes.new("ShaderNodeBsdfHairPrincipled")
    h.parametrization = "MELANIN"
    lum = 0.3 * color[0] + 0.59 * color[1] + 0.11 * color[2]
    h.inputs["Melanin"].default_value = float(np.clip(1.0 - lum * 3.2, 0.25, 0.97))
    h.inputs["Melanin Redness"].default_value = float(np.clip((color[0] - color[2]) * 4.0, 0.05, 0.9))
    if "Roughness" in h.inputs:
        h.inputs["Roughness"].default_value = 0.42
    for nm, v in (("Random Color", 0.18), ("Random Roughness", 0.2), ("Coat", 0.0)):
        if nm in h.inputs:
            h.inputs[nm].default_value = v
    nt.links.new(h.outputs[0], out.inputs[0])
    return m


def setup(sc):
    sc.world = bpy.data.worlds.new("w")
    sc.world.use_nodes = True
    sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.65, 0.85, 1)
    sc.world.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    for rot, en in (((math.radians(55), 0, math.radians(30)), 4.0), ((math.radians(75), 0, math.radians(195)), 3.0)):
        l = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        l.data.energy = en
        l.rotation_euler = rot
        sc.collection.objects.link(l)
    sc.render.engine = "CYCLES"     # 시험: 헤어 BSDF 형태 판단용 (게임은 카드 + 실시간 셰이더)
    sc.cycles.samples = 48
    sc.cycles.device = "CPU"
    sc.render.resolution_x, sc.render.resolution_y = 600, 700
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 85
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam



def head_object(H):
    me = bpy.data.meshes.new("head")
    me.from_pydata([tuple(p) for p in H.V], [], [tuple(int(i) for i in f) for f in H.T])
    uvl = me.uv_layers.new(name="UVMap")
    for poly, tu in zip(me.polygons, H.TUV):
        for li, uv in zip(poly.loop_indices, tu):
            uvl.data[li].uv = uv
    ob = bpy.data.objects.new("head", me)
    bpy.context.scene.collection.objects.link(ob)
    m = bpy.data.materials.new("skin")
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(H.skin_png)
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.6
    me.materials.append(m)
    for poly in me.polygons:
        poly.use_smooth = True
    return ob


def render_views(out, name, eye, prefix=""):
    sc = bpy.context.scene
    cam = sc.camera
    files = []
    for view, yaw in (("front", 0.0), ("three", 35.0), ("side", 90.0), ("back", 180.0)):   # 088 FR-55·57: 뒤 시점
        tgt = Vector((eye[0], eye[1] + 0.03, eye[2] + 0.03))
        a, dd = math.radians(yaw), 0.95
        cam.location = (tgt.x - dd * math.sin(a), tgt.y - dd * math.cos(a), tgt.z + 0.04)
        dv = tgt - cam.location
        cam.rotation_euler = (math.atan2(math.hypot(dv.x, dv.y), -dv.z), 0, math.atan2(dv.y, dv.x) - math.pi / 2)
        sc.render.filepath = os.path.join(out, f"{prefix}{name}_{view}.png")
        bpy.ops.render.render(write_still=True)
        files.append(sc.render.filepath)
    return files


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    out, names = args[0], args[1].split(",")
    density = float(args[args.index("--density") + 1]) if "--density" in args else 1.0
    os.makedirs(out, exist_ok=True)
    H = frame.head()
    for name in names:
        bpy.ops.wm.read_homefile(use_empty=True)
        cam = setup(bpy.context.scene)
        head_object(H)
        S, C = styles.groom(name, H.V, H.T, H.eye, density)
        hob = to_curves("hair", S, 0.00009)
        mat = hair_mat((0.03, 0.02, 0.014, 1))
        hb = [n for n in mat.node_tree.nodes if n.bl_idname == "ShaderNodeBsdfHairPrincipled"][0]
        if "Roughness" in hb.inputs:
            hb.inputs["Roughness"].default_value = 0.55
        if "Random Roughness" in hb.inputs:
            hb.inputs["Random Roughness"].default_value = 0.35
        hob.data.materials.append(mat)
        render_views(out, name, H.eye)
        print("[groom] rendered", name, len(S))


if __name__ == "__main__":
    main()
