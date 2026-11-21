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


STANCE_TOE = 0.03      # 발끝 또는
STANCE_ANKLE = 0.075   # 발목이 이 높이 이하면 땅에 있다 (specs/054 FR-10, 043 GROUND_MIN 0.035 + 뒤꿈치 여유)
STANCE_MIN_FRAMES = 3
STANCE_PIECE = 6


def _foot_xz(pose, foot):
    _, head, _ = PO.fk(pose)
    p = head[foot + "Toes"]
    return p.x, p.z


def stance_paths(c):
    """발끝 경로 (specs/054 발 고정 입력): 발마다 [[프레임0, 프레임1, x0, z0, x1, z1], ...] — 선수 좌표. 루프는 stance 태그, 나머지는 높이로 찾는다."""
    out = {"Left": [], "Right": []}
    if c.loop and c.tags.get("stance"):
        for n in out:
            s0, s1 = c.tags["stance"][n]
            span = (s1 - s0) % 1.0
            if span <= 0.0 and s1 > s0:
                span = 1.0  # 서 있는 클립: 주기 내내 접지
            f0, f1 = s0 * c.frames, (s0 + span) * c.frames
            a, b = _foot_xz(c.sample(f0 / CB.FPS), n), _foot_xz(c.sample((f1 % c.frames) / CB.FPS), n)
            out[n].append([round(f0, 3), round(f1, 3), round(a[0], 4), round(a[1], 4), round(b[0], 4), round(b[1], 4)])
        return out
    low = {n: [] for n in out}
    xz = {n: [] for n in out}
    for f in range(c.frames + 1):
        _, head, _ = PO.fk(c.sample(f / CB.FPS))
        for n in out:
            low[n].append(head[n + "Toes"].y <= STANCE_TOE or head[n + "Foot"].y <= STANCE_ANKLE)
            xz[n].append((head[n + "Toes"].x, head[n + "Toes"].z))
    for n in out:
        f = 0
        while f <= c.frames:
            if not low[n][f]:
                f += 1
                continue
            g = f
            while g + 1 <= c.frames and low[n][g + 1]:
                g += 1
            if g - f + 1 >= STANCE_MIN_FRAMES:
                # 비루프 발끝 경로는 직선이 아니다 (디딤 → 준비 자세): 6프레임 이하 조각으로 나눠 선형 보간 오차를 줄인다
                h = f
                while h < g:
                    m = min(g, h + STANCE_PIECE)
                    a, b = xz[n][h], xz[n][m]
                    out[n].append([h, m, round(a[0], 4), round(a[1], 4), round(b[0], 4), round(b[1], 4)])
                    h = m
            f = g + 1
    return out


def arm_length(head):
    """080: 위팔 + 아래팔 길이 (좌우 평균, m). 본 길이는 포즈와 무관하다."""
    def one(n):
        return (head[n + "LowerArm"] - head[n + "UpperArm"]).length + (head[n + "Hand"] - head[n + "LowerArm"]).length
    return 0.5 * (one("Left") + one("Right"))


def pelvis_accel(c):
    """062 FR-21: 프레임별(30 Hz, 0..frames) 골반 가속도 [x, y, z] m/s² (선수 좌표, 루트 기준 제자리 움직임).
    2차 중앙 차분. 루프 클립은 주기로 감싸고(마지막 = 처음), 비루프는 양 끝에서 이웃 값을 쓴다. 런타임 커널 합성의 입력."""
    n = c.frames
    pos = []
    for f in range(n + 1):
        t = (0.0 if (c.loop and f == n) else f / CB.FPS)
        _, head, _ = PO.fk(c.sample(t))
        pos.append(head["Hips"])
    out = []
    for f in range(n + 1):
        if c.loop:
            a, b, d = pos[(f - 1) % n], pos[f % n], pos[(f + 1) % n]
        else:
            g = min(max(f, 1), n - 1) if n >= 2 else 0
            a, b, d = pos[max(g - 1, 0)], pos[g], pos[min(g + 1, n)]
        acc = (d - 2 * b + a) * (CB.FPS * CB.FPS)
        out.append([round(float(acc.x), 3), round(float(acc.y), 3), round(float(acc.z), 3)])
    return out


def clip_tags(c):
    from .hands import hand_pose_for
    t = {"id": c.id, "group": c.group, "frames": c.frames, "loop": c.loop, "stancePaths": stance_paths(c),
         "handPose": hand_pose_for(c.id, c.group), "pelvisAccel": pelvis_accel(c)}  # 062 FR-11·21
    for k, v in sorted(c.tags.items()):
        if k == "contact" and v is not None:
            frame = v["frame"]
            _, head, _ = PO.fk(c.sample(frame / CB.FPS))
            p = CB.contact_point(v["rule"], head)
            t["contact"] = {"frame": frame, "part": v["part"], "rule": v["rule"], "point": _fmt(p)}
            if v["part"] == "Hand":
                # 080 FR-04: 손 IK 도달 한계 — 접촉 프레임 어깨(위팔 머리) 2점 + 팔 길이(위팔 + 아래팔, 기준 키)
                t["contact"]["shoulders"] = [_fmt(head["LeftUpperArm"]), _fmt(head["RightUpperArm"])]
                t["contact"]["armLength"] = round(arm_length(head), 4)
        else:
            t[k] = v
    return t
