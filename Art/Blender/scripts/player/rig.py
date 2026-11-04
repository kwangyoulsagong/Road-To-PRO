"""bpy 연결: 뼈대·LOD 메시·셰이프키·가중치 (specs/043 FR-01·10·11·30).

오브젝트 이름: 뼈대 `PlayerRig`, 메시 `Player_LOD0..2`. 머티리얼 `PlayerBody`, `PlayerKit`(2개, FR-11 ≤ 3).
가중치는 body.py가 계산한 값(뼈 선분 거리·관절 블렌드, 정점당 최대 4본)을 그대로 넣는다 — 자동 가중치를 쓰지 않는다.
"""
import bmesh
import bpy
from mathutils import Vector

from . import body as B
from . import skeleton as S
from .space import to_blender

MATERIALS = ("PlayerBody", "PlayerKit")


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = 30
    sc.render.fps_base = 1.0


def materials():
    out = []
    for name, rgba in zip(MATERIALS, ((0.8, 0.62, 0.5, 1.0), (0.85, 0.85, 0.85, 1.0))):
        m = bpy.data.materials.new(name)
        m.diffuse_color = rgba
        out.append(m)
    return out


def armature():
    data = bpy.data.armatures.new("PlayerRig")
    obj = bpy.data.objects.new("PlayerRig", data)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for name, parent, head, tail in S.BONES:
        b = data.edit_bones.new(name)
        b.head, b.tail = to_blender(head), to_blender(tail)
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
        eb[name] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def _top4(w):
    items = sorted(w.items(), key=lambda kv: (-kv[1], kv[0]))[:4]
    tot = sum(v for _, v in items)
    return [(k, v / tot) for k, v in items]


def lod_object(lod, rig, mats):
    m = B.build(lod)
    name = f"Player_LOD{lod}"
    bm = bmesh.new()
    vs = [bm.verts.new(to_blender(p)) for p in m.verts]
    bm.verts.ensure_lookup_table()
    uv = bm.loops.layers.uv.new("UVMap")
    for idx, mat, uvs, part, ref in m.faces:
        f = bm.faces.new([vs[i] for i in idx])
        f.material_index = mat
        f.normal_update()
        c = f.calc_center_median()
        if f.normal.dot(c - Vector(to_blender(ref))) < 0:
            f.normal_flip()  # UV는 아래에서 정점 기준으로 넣으므로 순서가 바뀌어도 맞다
        corner = {vs[i]: u for i, u in zip(idx, uvs)}
        for loop in f.loops:
            loop[uv].uv = corner[loop.vert]
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for mt in mats:
        me.materials.append(mt)
    for p in me.polygons:
        p.use_smooth = True
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = rig
    groups = {n: obj.vertex_groups.new(name=n) for n in S.NAMES}
    for i, w in enumerate(m.weights):
        for bone, val in _top4(w):
            groups[bone].add([i], val, "REPLACE")
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    obj.shape_key_add(name="Basis")
    for key, verts in B.shape_keys(lod).items():
        sk = obj.shape_key_add(name=key, from_mix=False)
        sk.value = 0.0  # Blender 5 기본값은 1.0 — glb 기본 가중치에도 들어간다
        for i, p in enumerate(verts):
            sk.data[i].co = to_blender(p)
    return obj, m


def build(lods):
    reset_scene()
    mats = materials()
    rig = armature()
    meshes = {}
    for lod in lods:
        obj, m = lod_object(lod, rig, mats)
        meshes[lod] = (obj, m)
    return rig, meshes
