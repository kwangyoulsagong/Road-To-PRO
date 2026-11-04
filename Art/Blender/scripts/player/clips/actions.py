"""공 액션 클립 (specs/043 FR-31·32·48·61).

키 포즈 타임라인(poses.timeline)으로 만든다. 클립마다 접촉 태그: frame, part(006 BodyPart 이름), rule(base.py),
approach(접근 각도 범위 °, 0 = 정면에서 공으로), ballHeight(m), ballSpeed(결과 공 속도 m/s), root(시뮬 예상 이동 [dx, dz] m).
접촉점(contact.point)은 굽는 단계(bake.clip_tags)가 FK로 계산한다 — 공 중심이 그 순간 있어야 할 위치(루트 기준).
"""
from . import base as B
from . import poses as P
from .base import Clip

M = P.merged


def _clip(id_, group, frames, keys, contact=None, part=None, rule=None, approach=(-30, 30), height=(0.0, 0.3),
          speed=(3.0, 20.0), root=(0.0, 0.0), **extra):
    tl = P.timeline(keys)
    tags = dict(approach=list(approach), ballHeight=list(height), ballSpeed=list(speed), root=list(root))
    tags.update(extra)
    if contact is not None:
        tags["contact"] = {"frame": contact, "part": part, "rule": rule}
    return Clip(id_, group, frames, lambda t: P.grounded(tl(t * 30.0)), **tags)


def fit_leg(kp, leg, rule, target_y):
    """접촉 키 포즈의 FK 다리 (굴곡, 무릎)를 2° 격자로 찾아 접촉점 높이를 target_y에 맞춘다.
    조건: 발목 높이 ≥ 0.06, 볼(발가락 뿌리) 높이 ≥ 0.02 (지면 관통 없음, FR-45). 원래 각도에 가까운 해를 고른다."""
    from ..pose import fk
    flex0, knee0, ankle, abd, rot = kp[leg]
    n = "Left" if leg.endswith("L") else "Right"
    best = None
    for knee in range(0, 122, 2):
        for flex in range(-20, 122, 2):
            k = dict(kp)
            k[leg] = (float(flex), float(knee), ankle, abd, rot)
            _, head, tail = fk(P.to_pose(k))
            if head[n + "Foot"].y < 0.06 or head[n + "Toes"].y < 0.02 or tail[n + "Toes"].y < P.TIP_MIN + 0.005:
                continue
            y = B.contact_point(rule, head).y
            cost = (y - target_y) ** 2 * 1e4 + ((flex - flex0) ** 2 + (knee - knee0) ** 2) * 1e-4
            if best is None or cost < best[0]:
                best = (cost, flex, knee, y)
    if best is None or abs(best[3] - target_y) > 0.01:
        raise ValueError(f"접촉 높이 맞춤 실패: {leg} 목표 {target_y}")
    out = dict(kp)
    out[leg] = (float(best[1]), float(best[2]), ankle, abd, rot)
    return out


def _side(s):
    return ("R", "L") if s > 0 else ("L", "R")


def kick(id_, s, kind, group):
    """s = +1 오른발, −1 왼발. kind: inside, outside, instep, lofted, curl, volley, cross, onetouch_pass, onetouch_shot."""
    k, pl = _side(s)  # 차는 발, 디딤발
    sp = {"inside": (22, 12), "outside": (22, 12), "instep": (26, 13), "lofted": (26, 13), "curl": (26, 13),
          "volley": (24, 12), "cross": (28, 14), "onetouch_pass": (14, 6), "onetouch_shot": (16, 7)}[kind]
    frames, c = sp
    rot = {"inside": 65, "outside": -25, "curl": 25, "cross": 30}.get(kind, 0)
    back = {"inside": (-25, 60, 10), "outside": (-25, 70, 30), "onetouch_pass": (-12, 40, 10), "onetouch_shot": (-25, 80, 35),
            "volley": (-10, 90, 35), "lofted": (-40, 95, 30), "cross": (-35, 95, 25)}.get(kind, (-40, 100, 35))
    hit = {"inside": (8, 15, 5), "outside": (12, 15, 35), "onetouch_pass": (8, 15, 5), "onetouch_shot": (18, 20, 40),
           "volley": (62, 25, 40), "lofted": (22, 18, 35), "cross": (20, 18, 30)}.get(kind, (18, 20, 40))
    follow = {"inside": (30, 10, 5), "outside": (30, 10, 30), "onetouch_pass": (25, 10, 5), "volley": (85, 20, 35),
              "lofted": (70, 10, 30), "cross": (60, 10, 25)}.get(kind, (65, 10, 40))
    lean = {"lofted": -10, "cross": -8, "volley": -14}.get(kind, 4)
    abd = {"volley": 10, "cross": 5}.get(kind, 0)
    plant_x = -s * (0.30 if kind != "volley" else 0.40)
    arm_k, arm_p = "arm" + k, "arm" + pl
    start = M(P.READY, **{"ik" + pl: (s * -0.12 + 0.0 * s, -0.25, 0.0, -s * 8.0), "ik" + k: (s * 0.12, -0.35, 0.0, s * 8.0)},
              hips=(0.0, -0.06, -0.30), spine=(10.0, 0.0, 0.0))
    plant = {"ik" + pl: (plant_x, 0.0, 0.0, -s * 10.0)}
    windup = M(P.READY, **plant, **{"leg" + k: back + (abd, rot * 0.6)},
               hips=(s * -0.05, -0.08, -0.08), hrot=(0.0, -s * 15.0, 0.0), spine=(12.0 + lean * 0.3, -s * 20.0, -s * 6.0),
               **{arm_p: (0.0, 35.0, 30.0), arm_k: (15.0, -20.0, 30.0)})
    contact = M(P.READY, **plant, **{"leg" + k: hit + (abd, rot)},
                hips=(s * -0.08, -0.07, 0.0), hrot=(0.0, s * 5.0, 0.0), spine=(6.0 + lean, s * 10.0, -s * 10.0),
                head=(15.0, 0.0), **{arm_p: (-10.0, 25.0, 30.0), arm_k: (20.0, -30.0, 30.0)})
    end = M(P.READY, **plant, **{"leg" + k: follow + (abd, rot * 0.5)},
            hips=(s * -0.06, -0.05, 0.10), hrot=(0.0, s * 15.0, 0.0), spine=(4.0 + lean, s * 20.0, -s * 6.0),
            **{arm_p: (5.0, 10.0, 30.0), arm_k: (25.0, -10.0, 30.0)})
    settle = M(P.READY, ikL=(-0.14, 0.15 if k == "L" else 0.0, 0.0, -8.0), ikR=(0.14, 0.15 if k == "R" else 0.0, 0.0, 8.0),
               hips=(0.0, -0.06, 0.08))
    rule = {"inside": B.inside(s), "outside": B.outside(s), "onetouch_pass": B.inside(s)}.get(kind, B.instep(s))
    target = {"volley": 0.60}.get(kind, B.BALL_R)
    contact = fit_leg(contact, "leg" + k, rule, target)
    keys = [(0, start), (max(2, c - 6), windup), (c, contact), (min(frames - 4, c + 5), end), (frames, settle)]
    part = "RightFoot" if s > 0 else "LeftFoot"
    height = {"volley": (0.35, 1.0), "onetouch_shot": (0.0, 0.4)}.get(kind, (0.0, 0.3))
    speed = {"inside": (4, 16), "outside": (4, 16), "onetouch_pass": (4, 15), "lofted": (12, 25), "cross": (14, 27)}.get(kind, (15, 34))
    approach = {"cross": (-60, 10), "inside": (-40, 40), "curl": (-40, 20), "outside": (-20, 40)}.get(kind, (-30, 30))
    if s < 0:
        approach = (-approach[1], -approach[0])
    return _clip(id_, group, frames, keys, c, part, rule, approach, height, speed, root=(0.0, 0.35), kind=kind)


def trap_foot(id_, s, fail=False):
    k, pl = _side(s)
    frames, c = (20, 8)
    plant = {"ik" + pl: (-s * 0.14, 0.0, 0.0, -s * 8.0)}
    reach = M(P.READY, **plant, **{"leg" + k: (25.0, 25.0, 0.0, 5.0, 60.0)}, hips=(0.0, -0.07, 0.0), spine=(14.0, 0.0, 0.0),
              head=(25.0, 0.0), armL=(10.0, 20.0, 30.0), armR=(10.0, 20.0, 30.0))
    reach = fit_leg(reach, "leg" + k, B.inside(s), 0.16)
    cushion = M(reach, **{"leg" + k: (reach["leg" + k][0] - 15.0,) + reach["leg" + k][1:]})
    if fail:
        after = M(P.READY, **plant, **{"leg" + k: (35.0, 30.0, 20.0, 15.0, 30.0)}, hips=(0.0, -0.09, 0.0),
                  spine=(20.0, s * 15.0, 0.0), head=(30.0, s * 30.0), armL=(-10.0, 35.0, 30.0), armR=(-10.0, 35.0, 30.0))
    else:
        after = M(P.READY, **plant, **{"ik" + k: (s * 0.14, 0.05, 0.0, s * 8.0)}, hips=(0.0, -0.07, 0.0), head=(25.0, 0.0))
    keys = [(0, P.READY), (c - 4, reach), (c, reach), (c + 4, cushion), (frames, after)]
    return _clip(id_, "control", frames, keys, c, "RightFoot" if s > 0 else "LeftFoot", B.inside(s),
                 (-60, 60), (0.0, 0.35), (2.0, 25.0), outcome="fail" if fail else "success")


def trap_chest():
    frames, c = 22, 9
    lean = M(P.READY, spine=(-18.0, 0.0, 0.0), head=(20.0, 0.0), armL=(-20.0, 15.0, 35.0), armR=(-20.0, 15.0, 35.0),
             hips=(0.0, -0.06, 0.05))
    give = M(lean, spine=(-8.0, 0.0, 0.0), hips=(0.0, -0.10, 0.0))
    keys = [(0, P.READY), (c - 4, lean), (c, lean), (c + 5, give), (frames, P.READY)]
    return _clip("trap_chest", "control", frames, keys, c, "Chest", B.CHEST, (-45, 45), (1.0, 1.5), (3.0, 22.0), outcome="success")


def trap_thigh(s):
    k, pl = _side(s)
    frames, c = 22, 9
    plant = {"ik" + pl: (-s * 0.12, 0.0, 0.0, -s * 8.0)}
    lift = M(P.READY, **plant, **{"leg" + k: (70.0, 85.0, 20.0, 5.0, 10.0)}, hips=(0.0, -0.04, 0.0),
             spine=(10.0, 0.0, 0.0), head=(30.0, 0.0), armL=(0.0, 20.0, 30.0), armR=(0.0, 20.0, 30.0))
    drop = M(lift, **{"leg" + k: (45.0, 70.0, 20.0, 5.0, 10.0)})
    keys = [(0, P.READY), (c - 4, lift), (c, lift), (c + 4, drop), (frames, P.READY)]
    return _clip(f"trap_thigh_{k.lower()}", "control", frames, keys, c, "Thigh", B.thigh(s), (-45, 45), (0.7, 1.1),
                 (2.0, 18.0), outcome="success")


def dribble_touch(s):
    k, pl = _side(s)
    frames, c = 12, 5
    plant = {"ik" + pl: (-s * 0.12, 0.0, 0.0, -s * 6.0)}
    pre = M(P.READY, **plant, **{"leg" + k: (-15.0, 45.0, 30.0, 0.0, 10.0)}, hips=(0.0, -0.07, -0.05), spine=(16.0, 0.0, 0.0),
            head=(20.0, 0.0), armL=(20.0, 15.0, 70.0), armR=(20.0, 15.0, 70.0))
    tap = fit_leg(M(pre, **{"leg" + k: (22.0, 25.0, 35.0, 0.0, 10.0)}, hips=(0.0, -0.07, 0.05)), "leg" + k, B.instep(s), B.BALL_R)
    keys = [(0, pre), (c, tap), (frames, M(pre, hips=(0.0, -0.07, 0.10)))]
    return _clip(f"dribble_touch_{k.lower()}", "dribble", frames, keys, c, "RightFoot" if s > 0 else "LeftFoot", B.instep(s),
                 (-20, 20), (0.0, 0.25), (2.0, 9.0), root=(0.0, 1.2))


def header(id_, jump):
    frames, c = (30, 15) if jump else (20, 10)
    up = 0.42 if jump else 0.0
    crouch = M(P.READY, hips=(0.0, -0.16, 0.0), spine=(20.0, 0.0, 0.0), armL=(0.0, 30.0, 60.0), armR=(0.0, 30.0, 60.0))
    rise = M(P.READY, hips=(0.0, up - 0.02, 0.0), ikL=(-0.12, -0.05, up, -6.0), ikR=(0.12, -0.05, up, 6.0),
             spine=(-15.0, 0.0, 0.0), head=(-20.0, 0.0), armL=(-30.0, 60.0, 50.0), armR=(-30.0, 60.0, 50.0),
             footL=(30.0, 0.0), footR=(30.0, 0.0))
    hit = M(rise, spine=(12.0, 0.0, 0.0), head=(20.0, 0.0), hips=(0.0, up, 0.0))
    land = M(P.READY, hips=(0.0, -0.12, 0.0), spine=(12.0, 0.0, 0.0))
    if jump:
        keys = [(0, P.READY), (6, crouch), (c - 3, rise), (c, hit), (24, M(hit, hips=(0.0, 0.05, 0.0), ikL=(-0.12, 0.0, 0.05, -6.0), ikR=(0.12, 0.0, 0.05, 6.0))), (frames, land)]
        height = (2.0, 2.7)
    else:
        keys = [(0, P.READY), (c - 4, M(P.READY, spine=(-15.0, 0.0, 0.0), head=(-20.0, 0.0))), (c, M(P.READY, spine=(12.0, 0.0, 0.0), head=(20.0, 0.0))), (frames, P.READY)]
        height = (1.55, 1.95)
    return _clip(id_, "header", frames, keys, c, "Head", B.HEAD, (-60, 60), height, (3.0, 18.0))


def tackle_stand(id_, s, win):
    k, pl = _side(s)
    frames, c = 24, 11
    plant = {"ik" + pl: (-s * 0.10, -0.10, 0.0, -s * 8.0)}
    lunge = M(P.READY, **plant, **{"leg" + k: (55.0, 10.0, -10.0, 10.0, 50.0)}, hips=(0.0, -0.22, 0.10),
              spine=(25.0, 0.0, s * -10.0), armL=(-10.0, 30.0, 40.0), armR=(-10.0, 30.0, 40.0))
    lunge = fit_leg(lunge, "leg" + k, B.inside(s), B.BALL_R)
    after = (M(P.READY, **plant, **{"ik" + k: (s * 0.16, 0.20, 0.0, s * 20.0)}, hips=(0.0, -0.10, 0.08)) if win else
             M(P.READY, **plant, **{"ik" + k: (s * 0.20, 0.30, 0.0, s * 30.0)}, hips=(0.0, -0.18, 0.12), spine=(30.0, 0.0, 0.0)))
    keys = [(0, P.READY), (c - 5, M(P.READY, hips=(0.0, -0.14, 0.0), spine=(18.0, 0.0, 0.0))), (c, lunge), (c + 5, lunge), (frames, after)]
    return _clip(id_, "tackle", frames, keys, c, "RightFoot" if s > 0 else "LeftFoot", B.inside(s), (-45, 45), (0.0, 0.25),
                 (0.0, 12.0), root=(0.0, 0.6), outcome="win" if win else "attempt")


def tackle_slide(s):
    k, pl = _side(s)
    frames, c = 36, 12
    # 디딤발은 몸 아래로 접어 바닥에 두고(IK), 차는 발을 앞으로 뻗는다. 팔은 뒤로 짚는다.
    down = M(P.READY, **{"leg" + k: (75.0, 5.0, -20.0, 0.0, 30.0), "ik" + pl: (-s * 0.20, -0.20, 0.0, -s * 35.0)},
             hips=(-s * 0.05, -0.62, -0.05), hrot=(-12.0, 0.0, -s * 15.0), spine=(-5.0, 0.0, -s * 10.0), head=(20.0, 0.0),
             **{"arm" + k: (-20.0, -15.0, 20.0), "arm" + pl: (-35.0, -25.0, 10.0)})
    down = fit_leg(down, "leg" + k, B.sole(s), B.BALL_R)
    lean = M(P.READY, hips=(0.0, -0.25, 0.1), spine=(20.0, 0.0, 0.0))
    keys = [(0, P.READY), (6, lean), (c, down), (24, down), (frames, M(P.READY, hips=(0.0, -0.30, 0.0), spine=(25.0, 0.0, 0.0)))]
    return _clip(f"tackle_slide_{k.lower()}", "tackle", frames, keys, c, "RightFoot" if s > 0 else "LeftFoot", B.sole(s),
                 (-30, 30), (0.0, 0.25), (0.0, 15.0), root=(0.0, 1.8), outcome="attempt")


def shoulder_duel():
    frames = 24
    push = M(P.READY, hips=(0.08, -0.12, 0.0), hrot=(0.0, 0.0, 6.0), spine=(14.0, -10.0, 18.0), armL=(10.0, 10.0, 70.0),
             armR=(5.0, 20.0, 80.0), ikL=(-0.22, 0.0, 0.0, -15.0), ikR=(0.16, 0.05, 0.0, 10.0))
    keys = [(0, P.READY), (8, push), (16, push), (frames, P.READY)]
    return _clip("shoulder_duel", "duel", frames, keys, None, None, None, (-90, 90), (0.0, 0.3), (0.0, 0.0), root=(0.0, 0.5))


def throw_in():
    frames, c = 36, 20
    feet = dict(ikL=(-0.14, 0.0, 0.0, -6.0), ikR=(0.14, 0.0, 0.0, 6.0))
    over = M(P.STAND, **feet, spine=(-18.0, 0.0, 0.0), head=(-10.0, 0.0), armL=(-5.0, 175.0, 95.0), armR=(-5.0, 175.0, 95.0),
             hips=(0.0, -0.04, -0.05))
    rel = M(over, spine=(14.0, 0.0, 0.0), armL=(-5.0, 120.0, 10.0), armR=(-5.0, 120.0, 10.0), hips=(0.0, -0.06, 0.05))
    keys = [(0, M(P.STAND, **feet, armL=(-5.0, 120.0, 60.0), armR=(-5.0, 120.0, 60.0))), (12, over), (c, rel), (frames, M(P.STAND, **feet))]
    rule = {"bone": "LeftHand", "toward": "RightHand", "t": 0.5, "up": 0.05, "fwd": 0.0, "side": 0.0, "basis": "body"}
    return _clip("throw_in", "restart", frames, keys, c, "Hand", rule, (-30, 30), (1.3, 2.3), (6.0, 16.0))


def clips():
    out = []
    for s, n in ((1, "r"), (-1, "l")):
        out.append(kick(f"pass_inside_{n}", s, "inside", "pass"))
    out.append(kick("pass_outside_r", 1, "outside", "pass"))
    out.append(kick("pass_lofted_r", 1, "lofted", "pass"))
    out.append(kick("onetouch_pass_r", 1, "onetouch_pass", "pass"))
    out.append(kick("cross_r", 1, "cross", "cross"))
    out.append(kick("cross_l", -1, "cross", "cross"))
    out.append(kick("shot_instep_r", 1, "instep", "shot"))
    out.append(kick("shot_instep_l", -1, "instep", "shot"))
    out.append(kick("shot_curl_r", 1, "curl", "shot"))
    out.append(kick("shot_volley_r", 1, "volley", "shot"))
    out.append(kick("onetouch_shot_r", 1, "onetouch_shot", "shot"))
    out.append(kick("clearance_r", 1, "lofted", "clearance"))
    out.append(trap_foot("trap_foot_r", 1))
    out.append(trap_foot("trap_foot_l", -1))
    out.append(trap_foot("trap_fail_r", 1, fail=True))
    out.append(trap_chest())
    out.append(trap_thigh(1))
    out.append(dribble_touch(1))
    out.append(dribble_touch(-1))
    out.append(header("header_jump", True))
    out.append(header("header_stand", False))
    out.append(tackle_stand("tackle_stand_r", 1, False))
    out.append(tackle_stand("tackle_win_r", 1, True))
    out.append(tackle_slide(1))
    out.append(shoulder_duel())
    out.append(throw_in())
    return out
