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


def mesh_object(name, m, rig, mats, keys=None):
    """body.Mesh → 스킨 메시 오브젝트. 셰이프키는 이 메시에서 0이 아닌 델타만 (keys로 강제 포함 가능), 이름 정렬."""
    bm = bmesh.new()
    vs = [bm.verts.new(to_blender(p)) for p in m.verts]
    bm.verts.ensure_lookup_table()
    uv = bm.loops.layers.uv.new("UVMap")
    for idx, mat, uvs, part, ref in m.faces:
        try:
            f = bm.faces.new([vs[i] for i in idx])
        except ValueError:
            continue  # 같은 정점 집합 면(감량 잔재)은 건너뛴다
        f.material_index = mat
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
    names = []
    for key in sorted(m.deltas):
        d = m.deltas[key]
        if (keys is None or key not in keys) and float(abs(d).max()) < 1e-5:
            continue
        sk = obj.shape_key_add(name=key, from_mix=False)
        sk.value = 0.0  # Blender 5 기본값은 1.0 — glb 기본 가중치에도 들어간다
        co = m.verts + d
        for i, p in enumerate(co):
            sk.data[i].co = to_blender(p)
        names.append(key)
    return obj, names


REQUIRED_KEYS = ("Thin", "Muscular", "Heavy", "HideHead")


def build(lods):
    """→ (뼈대, {lod: (obj, mesh)}, {이름: (obj, mesh)} 헤어·클로즈업 머리). 056: MPFB2 기준 선수에서."""
    from . import hair as H
    from . import headlod as HL
    reset_scene()
    R = B.reference()  # skeleton.configure 포함 — 뼈대보다 먼저
    mats = materials()
    rig = armature()
    meshes, parts = {}, {}
    for lod in lods:
        m = B.build(lod)
        obj, _ = mesh_object(f"Player_LOD{lod}", m, rig, mats, keys=REQUIRED_KEYS)
        meshes[lod] = (obj, m)
    for style in H.MESH_STYLES:
        for lod in lods:
            m = H.build(R, style, lod)
            obj, _ = mesh_object(f"{style}_LOD{lod}", m, rig, mats)
            parts[f"{style}_LOD{lod}"] = (obj, m)
    m = HL.build(R)
    obj, _ = mesh_object(HL.NAME, m, rig, mats)
    parts[HL.NAME] = (obj, m)
    return rig, meshes, parts
