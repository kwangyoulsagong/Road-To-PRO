"""클립 → NLA 트랙 + 태그 (specs/043 FR-31·32).

각 클립은 액션 하나(이름 = 클립 id)로 굽고 뼈대의 NLA 트랙으로 둔다(T01: glTF NLA_TRACKS 내보내기 → Unity 클립).
키는 매 프레임(0..frames) — 마지막 키 = 길이. 루프 클립은 마지막 프레임 포즈가 첫 프레임과 같다(위상 1 = 0).
태그 계산은 선수 좌표 FK(pose.fk)로 한다(접촉점 contactPoint, 루트 기준).
"""
import bpy

from . import pose as PO
from .clips import base as CB


def _fmt(v):
    return [round(float(x), 6) for x in v]


def bake(rig, clips):
    keyer = PO.Keyer(rig)
    rig.animation_data_create()
    tags = []
    for c in clips:
        act = bpy.data.actions.new(c.id)
        act.use_fake_user = True
        rig.animation_data.action = act
        keyer.reset()
        for f in range(c.frames + 1):
            t = f / CB.FPS
            if c.loop and f == c.frames:
                t = 0.0
            keyer.key(c.sample(t), f)  # 프레임 0부터: glTF 내보내기는 0부터 샘플링 → 1부터 넣으면 앞에 정지 프레임이 붙는다
        for fc in _fcurves(act):
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR"
        tr = rig.animation_data.nla_tracks.new()
        tr.name = c.id
        tr.strips.new(c.id, 0, act)
        rig.animation_data.action = None
        tags.append(clip_tags(c))
    return tags


def _fcurves(act):
    # Blender 5: 슬롯 액션 — 레이어·스트립·채널백
    out = []
    try:
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    out.extend(bag.fcurves)
    except AttributeError:
        out = list(act.fcurves)
    return out


def clip_tags(c):
    t = {"id": c.id, "group": c.group, "frames": c.frames, "loop": c.loop}
    for k, v in sorted(c.tags.items()):
        if k == "contact" and v is not None:
            frame = v["frame"]
            _, head, _ = PO.fk(c.sample(frame / CB.FPS))
            p = CB.contact_point(v["rule"], head)
            t["contact"] = {"frame": frame, "part": v["part"], "rule": v["rule"], "point": _fmt(p)}
        else:
            t[k] = v
    return t
