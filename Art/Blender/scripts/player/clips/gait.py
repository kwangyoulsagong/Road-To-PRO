"""로코모션 클립 (specs/043 FR-31·41, AC-09).

제자리 클립(루트 고정). 선수가 속도 v로 방향 d(정면 기준 각도)로 움직일 때, 접지 중인 발은 몸 기준으로 −v·d 속도로
정확히 뒤로 간다 → 시뮬이 같은 속도로 몸을 옮기면 접지 발은 월드에서 멈춰 있다(미끄러짐 0, 구성상).
Unity는 재생률 = 시뮬 속도 / 클립 speed로 맞춘다(LocomotionBlend).

보법(속도 m/s 정면 기준, 주기 프레임, 접지 비율, 발 들기 m):
  walk 1.4 / 30 / 0.60 / 0.07, jog 3.4 / 23 / 0.30 / 0.16, sprint 7.6 / 19 / 0.16 / 0.32, run(정면만) 5.4 / 21 / 0.22 / 0.24
방향별 속도 배율: 정면 1, 대각 앞 0.85, 옆 0.6, 대각 뒤 0.5, 뒤 0.45.
태그: speed(m/s), dir(°, 0 정면·90 오른쪽), stance[발] = [시작 위상, 끝 위상] (0~1), cycle 프레임.
"""
import math

from mathutils import Vector

from .. import skeleton as S
from ..pose import X, Y, q
from . import poses as P
from .base import Clip, smoother

GAITS = {
    "walk": dict(v=1.4, frames=30, duty=0.60, lift=0.07, crouch=0.035, bob=0.020, lean=4.0, arm=18.0, elbow=15.0, hrot=5.0),
    "jog": dict(v=3.4, frames=23, duty=0.30, lift=0.16, crouch=0.060, bob=0.030, lean=9.0, arm=32.0, elbow=70.0, hrot=7.0),
    "run": dict(v=5.4, frames=21, duty=0.22, lift=0.24, crouch=0.070, bob=0.035, lean=13.0, arm=45.0, elbow=85.0, hrot=8.0),
    "sprint": dict(v=7.6, frames=19, duty=0.16, lift=0.32, crouch=0.075, bob=0.035, lean=17.0, arm=60.0, elbow=90.0, hrot=9.0),
}
DIRS = (("f", 0), ("fr", 45), ("r", 90), ("br", 135), ("b", 180), ("bl", 225), ("l", 270), ("fl", 315))
DIR_SCALE = {0: 1.0, 45: 0.85, 315: 0.85, 90: 0.6, 270: 0.6, 135: 0.5, 225: 0.5, 180: 0.45}
FOOT_X = 0.115
TOE_OFF = 35.0
STANCE_BACK = 0.14  # 접지 구간 중심을 몸 뒤로 (보폭 비율): 발은 엉덩이 조금 앞에 딛고 한참 뒤에서 뗀다
FOOT_BALL = Vector(S.HEAD["LeftToes"]) - Vector(S.HEAD["LeftFoot"])  # 발목 → 볼 (좌우 대칭이라 x = 0)
REACH = (S.L_THIGH + S.L_SHIN) * 0.995


def _foot(phase, start, duty, ls, lift):
    """발 하나: (진행 방향 변위 s, 들기 높이, 접지 여부, 접지면 접지 진행 u / 스윙이면 스윙 진행 r)."""
    qph = (phase - start) % 1.0
    if qph < duty:
        return ls * (0.5 - qph / duty), 0.0, True, qph / duty
    r = (qph - duty) / (1.0 - duty)
    # Hermite: 양 끝 속도 = 접지 중 몸 기준 속도(−ls/duty per 위상) → 발 떼는·딛는 순간 월드 속도 0 (끌림 없음)
    m = -ls * (1.0 - duty) / duty
    h00, h10, h01, h11 = 2 * r ** 3 - 3 * r ** 2 + 1, r ** 3 - 2 * r ** 2 + r, -2 * r ** 3 + 3 * r ** 2, r ** 3 - r ** 2
    sd = h00 * (-0.5 * ls) + h10 * m + h01 * (0.5 * ls) + h11 * m
    # 들기: 양 끝이 가파른 곡선(sin^0.5) — 발이 낮게 깔린 채 수평으로 움직이는 구간을 없앤다
    return sd, lift * math.sqrt(max(0.0, math.sin(math.pi * r))), False, r


def gait_keypose(g, dir_deg, phase):
    v = g["v"] * DIR_SCALE[dir_deg]
    f = 30.0 / g["frames"]
    ls = v * g["duty"] / f
    d = Vector((math.sin(math.radians(dir_deg)), 0.0, math.cos(math.radians(dir_deg))))
    fwd = max(0.0, d.z)
    k = dict(P.STAND)
    feet = {}
    for s, side, start in (("L", -1, 0.0), ("R", 1, 0.5)):
        sd, lift, stance, r = _foot(phase, start, g["duty"], ls, g["lift"])
        base = Vector((side * FOOT_X, 0.0, 0.0))
        # 옆걸음: 두 발이 겹치지 않게 바깥으로 벌린다
        p = base + d * (sd - STANCE_BACK * ls)
        if abs(d.x) > 0.3:
            p.x += side * 0.04
            # 056: 굵어진 MPFB 다리가 교차하지 않게 — 옆걸음은 발이 서로 지나가지 않는 사이드 셔플(기본 폭 + 반 보폭).
            # 발마다 일정한 오프셋이라 접지 중 미끄러짐은 그대로 0
            if abs(d.x) > 0.9:  # 순수 옆걸음(r·l)만 — 대각선(fr 등)까지 넓히면 이웃 방향 블렌드에서 발 위치가 크게 달라 팝이 난다
                p.x += side * (0.5 * ls * abs(d.x) + 0.03)
        # 발끝 떼기: 접지 후반에 볼(발가락 뿌리)을 축으로 뒤꿈치를 들고, 스윙 초반에 풀린다.
        # 볼 위치는 접지 동안 그대로라 미끄러짐 없음. 접지 끝 = 스윙 시작에서 연속.
        if stance:
            toe = TOE_OFF * smoother((r - 0.6) / 0.4) if r > 0.6 else 0.0
            extra, heel = 0.0, 0.0
        else:
            toe = TOE_OFF * (1.0 - smoother(r / 0.35))
            extra = -25.0 * math.sin(math.pi * r) * smoother(r / 0.35)
            # 뒤꿈치 차기: 스윙 중 뒤로 접히는 양(앞으로 갈 때만)
            heel = g["lift"] * 0.6 * math.sin(math.pi * min(1.0, r * 1.6)) * fwd
        if toe > 0:
            yaw = -6.0 if s == "L" else 6.0
            off = q(Y, yaw) @ FOOT_BALL - q(Y, yaw) @ q(X, toe) @ FOOT_BALL  # 발목이 움직여야 하는 양
            p = p + Vector((off.x, 0.0, off.z))
            lift = lift + off.y
        pitch = toe + extra
        feet[s] = (p, lift, stance, heel, r, pitch)
    swing = math.cos(2 * math.pi * phase)  # 왼발이 앞일 때 +1 (위상 0)
    hyaw = -g["hrot"] * swing * fwd
    # 골반 높이: 기본 − 웅크림 + 상하 진동(한 주기에 2번), 접지 발이 닿도록 제한(회전된 엉덩이 관절 기준)
    hy = -g["crouch"] + g["bob"] * math.cos(4 * math.pi * phase)
    rot = q(Y, hyaw)
    hips_rest = Vector(S.HEAD["Hips"])
    for s, (p, lift, stance, heel, r, pitch) in feet.items():
        # 접지 발 + 스윙 초반(발끝 떼기 풀리는 중) 발이 닿아야 한다. 그 뒤 스윙 발은 무릎을 굽혀 따라온다.
        if not stance and r >= 0.35:
            continue
        n = "Left" if s == "L" else "Right"
        hip = rot @ (Vector(S.HEAD[n + "UpperLeg"]) - hips_rest) + hips_rest
        dx = math.hypot(p.x - hip.x, (p.z - heel) - hip.z)
        max_y = math.sqrt(max(0.0, REACH ** 2 - dx ** 2)) + S.ANKLE_Y + lift + heel * 0.5
        hy = min(hy, max_y - hip.y)
    k["hips"] = (0.0, hy, 0.0)
    k["hrot"] = (0.0, hyaw, 0.0)
    lean_axis_pitch = g["lean"] * d.z * (1.0 if d.z > 0 else 0.4)
    lean_roll = g["lean"] * 0.5 * d.x
    k["spine"] = (3.0 + lean_axis_pitch, g["hrot"] * 1.4 * swing * fwd, lean_roll)
    k["head"] = (-2.0 - lean_axis_pitch * 0.8, -g["hrot"] * 0.5 * swing * fwd)
    amp = g["arm"] * (0.4 + 0.6 * fwd)
    k["armL"] = (30.0, -amp * swing + 4.0, g["elbow"])
    k["armR"] = (30.0, amp * swing + 4.0, g["elbow"])
    for s, (p, lift, stance, heel, r, pitch) in feet.items():
        k["ik" + s] = (p.x, p.z - heel, lift + heel * 0.5, (-6.0 if s == "L" else 6.0))
        k["foot" + s] = (pitch, 0.0)
    return k


def _gait_clip(name, gk, dir_name, dir_deg):
    g = GAITS[gk]
    frames = g["frames"]
    v = g["v"] * DIR_SCALE[dir_deg]
    stance = {"Left": [0.0, g["duty"]], "Right": [0.5, (0.5 + g["duty"]) % 1.0]}

    def sample(t):
        return P.to_pose(gait_keypose(g, dir_deg, (t * 30.0 / frames) % 1.0))
    return Clip(name, "locomotion", frames, sample, loop=True, gait=gk, speed=round(v, 4), dir=dir_deg,
                stance=stance, cycle=frames)


def _idle(name, base, frames, breath):
    def sample(t):
        ph = 2 * math.pi * t * 30.0 / frames
        k = dict(base)
        hx, hy, hz = base["hips"]
        k["hips"] = (hx, hy + breath * math.sin(ph), hz)
        sp = base["spine"]
        k["spine"] = (sp[0] + 1.5 * math.sin(ph), sp[1], sp[2])
        return P.to_pose(k)
    return Clip(name, "locomotion", frames, sample, loop=True, gait="idle", speed=0.0, dir=0,
                stance={"Left": [0.0, 1.0], "Right": [0.0, 1.0]}, cycle=frames)


def clips():
    out = [_idle("idle", P.STAND, 60, 0.004), _idle("idle_ready", P.READY, 40, 0.006)]
    for gk in ("walk", "jog", "sprint"):
        for dn, dd in DIRS:
            out.append(_gait_clip(f"{gk}_{dn}", gk, dn, dd))
    out.append(_gait_clip("run_f", "run", "f", 0))
    return out
