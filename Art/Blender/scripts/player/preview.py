"""미리보기 PNG (specs/043 T05·T11). Workbench(무작위 샘플링 없음), 고정 카메라. 여러 장면을 렌더해 numpy로 이어 붙인다.

- lineup: LOD0·LOD1·LOD2 + 극단 체형 4종(키·체형·헤어)을 나란히 정면에서.
- poses: 대표 클립의 접촉 프레임(또는 중간 프레임) 12장 격자.
"""
import os

import bpy
import numpy as np

from . import checks as CK
from . import textures as T
from .space import to_blender

W, H = 480, 640


def _setup():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.show_object_outline = True
    sc.display.render_aa = "8"
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("World")
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def _look(cam, eye, target):
    from mathutils import Vector
    e, t = Vector(to_blender(eye)), Vector(to_blender(target))
    cam.location = e
    cam.rotation_euler = (t - e).to_track_quat("-Z", "Y").to_euler()


def _render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)[::-1]
    bpy.data.images.remove(img)
    os.remove(path)
    return (np.clip(px, 0, 1) * 255 + 0.5).astype(np.uint8)


def _show_only(objs, rig_visible=False):
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in objs


def _use_action(rig, name):
    """NLA를 끄고 액션 하나만 적용(None이면 레스트 포즈)."""
    ad = rig.animation_data
    ad.use_nla = False
    if name is None:
        ad.action = None
        for pb in rig.pose.bones:
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0)
        return
    act = bpy.data.actions[name]
    ad.action = act
    try:
        ad.action_slot = act.slots[0]
    except (AttributeError, IndexError):
        pass


def lineup(rig, lod_objs, out_png, tmp):
    _use_action(rig, None)
    cam = _setup()
    cam.data.lens = 50
    _look(cam, (0.0, 1.0, 4.2), (0.0, 0.95, 0.0))
    tiles = []
    for lod in sorted(lod_objs):
        _show_only([lod_objs[lod]])
        tiles.append(_render(os.path.join(tmp, f"_l{lod}.png")))
    base = lod_objs[min(lod_objs)]
    _show_only([base])
    for name, (height, weights, _style) in CK.EXTREMES.items():
        for kb in base.data.shape_keys.key_blocks[1:]:
            kb.value = weights.get(kb.name, 0.0)
        s = height / 180.0
        rig.scale = (s, s, s)
        tiles.append(_render(os.path.join(tmp, f"_x{name}.png")))
    for kb in base.data.shape_keys.key_blocks[1:]:
        kb.value = 0.0
    rig.scale = (1, 1, 1)
    rig.animation_data.use_nla = True
    T.write_png(out_png, np.concatenate(tiles, axis=1)[:, :, :3])


def poses(rig, lod_obj, tags, ids, out_png, tmp):
    cam = _setup()
    cam.data.lens = 40
    _look(cam, (2.6, 1.4, 3.0), (0.0, 0.8, 0.3))
    _show_only([lod_obj])
    by = {t["id"]: t for t in tags}
    tiles = []
    for cid in ids:
        t = by[cid]
        _use_action(rig, cid)
        frame = t["contact"]["frame"] if t.get("contact") else t["frames"] // 2
        bpy.context.scene.frame_set(frame)
        tiles.append(_render(os.path.join(tmp, f"_p{cid}.png")))
    _use_action(rig, None)
    rig.animation_data.use_nla = True
    rows = [np.concatenate(tiles[i:i + 6], axis=1) for i in range(0, len(tiles), 6)]
    T.write_png(out_png, np.concatenate(rows, axis=0)[:, :, :3])


def heads(rig, lod_obj, hair_objs, out_png, tmp):
    """헤어 10종 × 서로 다른 얼굴(얼굴 축 무작위, 시드 고정) 머리 클로즈업, 3/4 정면 (056 FR-22·40)."""
    import random
    from . import face as F
    from . import hair as HR
    _use_action(rig, None)
    cam = _setup()
    bpy.context.scene.display.shading.color_type = "OBJECT"
    lod_obj.color = (0.78, 0.58, 0.47, 1.0)
    for ho in hair_objs.values():
        ho.color = (0.10, 0.07, 0.05, 1.0)
    cam.data.lens = 85
    _look(cam, (0.55, 1.74, 1.05), (0.0, 1.66, 0.0))
    keys = lod_obj.data.shape_keys.key_blocks
    rnd = random.Random(56)
    tiles = []
    for style in HR.STYLES:
        ho = hair_objs.get(style)
        _show_only([lod_obj] + ([ho] if ho else []))
        vals = {}
        for a, _, _, r in F.AXES:
            x = rnd.uniform(-r, r)
            vals[f"Face_{a}_{'pos' if x >= 0 else 'neg'}"] = abs(x)
        for ob in [lod_obj] + ([ho] if ho else []):
            for kb in ob.data.shape_keys.key_blocks[1:]:
                kb.value = vals.get(kb.name, 0.0)
        tiles.append(_render(os.path.join(tmp, f"_h{style}.png"))[60:540, 0:480])
    for ob in [lod_obj] + list(hair_objs.values()):
        for kb in ob.data.shape_keys.key_blocks[1:]:
            kb.value = 0.0
    bpy.context.scene.display.shading.color_type = "MATERIAL"
    rows = [np.concatenate(tiles[i:i + 5], axis=1) for i in range(0, len(tiles), 5)]
    T.write_png(out_png, np.concatenate(rows, axis=0)[:, :, :3])


POSE_IDS = ("idle", "walk_f", "sprint_f", "pass_inside_r", "shot_instep_r", "cross_r",
            "trap_chest", "header_jump", "tackle_slide_r", "fall_n", "gk_dive_r", "celebrate_arms")
