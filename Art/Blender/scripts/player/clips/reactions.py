"""반응·GK·감정·심판·흐름 클립 (specs/043 FR-31·47·63).

넘어짐 8방향(fall_n … fall_nw): hitDir = 넘어지는 방향(°, 0 = 정면, 90 = 오른쪽) — FR-47은 충돌 방향과 가장 가까운 것.
GK 동작은 011이 목록을 주기 전까지 최소 세트(준비·좌우 다이브·가슴 캐치·펀칭·킥).
"""
import math

from . import base as B
from . import poses as P
from .actions import _clip, kick

M = P.merged

FALL_DIRS = (("n", 0), ("ne", 45), ("e", 90), ("se", 135), ("s", 180), ("sw", 225), ("w", 270), ("nw", 315))


def fall(name, deg):
    frames = 40
    sx, sz = math.sin(math.radians(deg)), math.cos(math.radians(deg))
    # 넘어지는 방향으로 몸통을 기울이고(앞 = pitch +, 옆 = roll), 골반을 그쪽으로 옮기며 바닥에 눕는다
    pitch, roll = 80.0 * sz, 75.0 * sx
    stumble = M(P.READY, hips=(sx * 0.15, -0.12, sz * 0.15), spine=(25.0 * sz, 0.0, 20.0 * sx),
                ikL=(-0.12 + sx * 0.15, sz * 0.25, 0.0, -6.0), ikR=(0.12 + sx * 0.05, sz * 0.05, 0.08, 6.0),
                armL=(-30.0, 40.0 * sz, 30.0), armR=(-30.0, 40.0 * sz, 30.0))
    ground = M(P.READY, hips=(sx * 0.6, -0.86, sz * 0.6), hrot=(pitch * 1.05, 0.0, roll * 1.1),
               spine=(pitch * 0.08, 0.0, roll * 0.05), head=(-30.0 * sz, 0.0),
               legL=(10.0, 30.0, 20.0, 10.0, 0.0), legR=(25.0, 50.0, 20.0, 10.0, 0.0),
               armL=(-60.0, 60.0 * sz, 40.0), armR=(-60.0, 60.0 * sz, 40.0))
    keys = [(0, P.READY), (6, stumble), (18, ground), (frames, ground)]
    return _clip(name, "fall", frames, keys, hitDir=deg, root=(sx * 0.6, sz * 0.6))


def stumble():
    frames = 20
    hit = M(P.READY, hips=(0.0, -0.10, -0.05), spine=(20.0, 0.0, 8.0), ikR=(0.18, 0.25, 0.05, 15.0),
            armL=(-30.0, 30.0, 30.0), armR=(-40.0, 20.0, 30.0))
    keys = [(0, P.READY), (6, hit), (frames, P.READY)]
    return _clip("hit_stumble", "fall", frames, keys, hitDir=0, root=(0.0, 0.3))


def gk_ready():
    base = M(P.READY, hips=(0.0, -0.14, -0.03), spine=(18.0, 0.0, 0.0), armL=(10.0, 35.0, 45.0), armR=(10.0, 35.0, 45.0),
             ikL=(-0.22, 0.0, 0.0, -10.0), ikR=(0.22, 0.0, 0.0, 10.0))
    tl = P.timeline([(0, base), (15, M(base, hips=(0.0, -0.12, -0.03))), (30, base)])
    return B.Clip("gk_ready", "keeper", 30, lambda t: P.grounded(tl(t * 30.0)), loop=True)


def gk_dive(name, s):
    frames, c = 30, 14
    ready = M(P.READY, hips=(0.0, -0.14, -0.03), spine=(18.0, 0.0, 0.0), armL=(10.0, 35.0, 45.0), armR=(10.0, 35.0, 45.0))
    load = M(ready, hips=(s * 0.10, -0.20, 0.0), **{"ik" + ("L" if s < 0 else "R"): (s * 0.30, 0.0, 0.0, s * 20.0)})
    fly = M(P.READY, hips=(s * 1.0, -0.35, 0.0), hrot=(0.0, 0.0, s * 75.0), spine=(0.0, 0.0, s * 15.0), head=(0.0, 0.0),
            armL=(-80.0, 0.0, 10.0), armR=(-80.0, 0.0, 10.0), legL=(10.0, 20.0, 20.0, 10.0, 0.0), legR=(10.0, 20.0, 20.0, 10.0, 0.0))
    land = M(fly, hips=(s * 1.2, -0.80, 0.0), hrot=(0.0, 0.0, s * 85.0), armL=(-60.0, 30.0, 60.0), armR=(-60.0, 30.0, 60.0))
    keys = [(0, ready), (6, load), (c, fly), (22, land), (frames, land)]
    return _clip(name, "keeper", frames, keys, c, "Hand", B.HANDS, (-20, 20), (0.0, 2.3), (5.0, 35.0), root=(s * 1.6, 0.0))


def gk_catch_chest():
    frames, c = 20, 8
    reach = M(P.READY, hips=(0.0, -0.10, 0.0), spine=(5.0, 0.0, 0.0), armL=(0.0, 70.0, 60.0), armR=(0.0, 70.0, 60.0))
    hug = M(reach, spine=(25.0, 0.0, 0.0), armL=(0.0, 40.0, 120.0), armR=(0.0, 40.0, 120.0))
    keys = [(0, P.READY), (c, reach), (c + 5, hug), (frames, hug)]
    return _clip("gk_catch_chest", "keeper", frames, keys, c, "Hand", B.HANDS, (-30, 30), (0.8, 1.6), (5.0, 30.0))


def gk_punch():
    frames, c = 18, 8
    jump = M(P.READY, hips=(0.0, 0.25, 0.0), ikL=(-0.12, 0.0, 0.25, -6.0), ikR=(0.12, 0.0, 0.25, 6.0),
             armL=(-10.0, 60.0, 20.0), armR=(-20.0, 150.0, 30.0), spine=(-5.0, 0.0, 0.0))
    hit = M(jump, armR=(-20.0, 165.0, 0.0))
    keys = [(0, P.READY), (4, M(P.READY, hips=(0.0, -0.15, 0.0))), (c, hit), (14, jump), (frames, P.READY)]
    return _clip("gk_punch", "keeper", frames, keys, c, "Hand", B.hand(1), (-45, 45), (2.0, 2.8), (5.0, 30.0))


def celebrate_arms():
    frames = 40
    up = M(P.STAND, armL=(-60.0, 160.0, 20.0), armR=(-60.0, 160.0, 20.0), head=(-25.0, 0.0), spine=(-10.0, 0.0, 0.0))
    jump = M(up, hips=(0.0, 0.18, 0.0), ikL=(-0.12, 0.0, 0.18, -6.0), ikR=(0.12, 0.0, 0.18, 6.0))
    tl = P.timeline([(0, up), (10, jump), (20, up), (30, jump), (40, up)])
    return B.Clip("celebrate_arms", "emotion", frames, lambda t: P.grounded(tl(t * 30.0)), loop=True)


def celebrate_knee():
    frames = 45
    kneel = M(P.STAND, hips=(0.0, -0.45, -0.10), legL=(5.0, 95.0, 30.0, 8.0, 0.0), legR=(5.0, 95.0, 30.0, 8.0, 0.0),
              spine=(-20.0, 0.0, 0.0), head=(-30.0, 0.0), armL=(-50.0, 120.0, 10.0), armR=(-50.0, 120.0, 10.0))
    keys = [(0, P.READY), (15, kneel), (frames, kneel)]
    return _clip("celebrate_knee", "emotion", frames, keys, root=(0.0, 1.5))


def dejected():
    frames = 60
    d = M(P.STAND, head=(35.0, 0.0), spine=(12.0, 0.0, 0.0), armL=(20.0, 70.0, 140.0), armR=(20.0, 70.0, 140.0))
    tl = P.timeline([(0, d), (30, M(d, head=(40.0, 8.0))), (60, d)])
    return B.Clip("dejected", "emotion", frames, lambda t: P.grounded(tl(t * 30.0)), loop=True)


def ref(name, arm_r, arm_l, frames):
    pose = M(P.STAND, armR=arm_r, armL=arm_l)
    keys = [(0, P.STAND), (8, pose), (frames - 8, pose), (frames, P.STAND)]
    return _clip(name, "referee", frames, keys)


def flow(name, gait_like, head):
    from . import gait as G
    g = G.GAITS[gait_like]

    def sample(t):
        k = G.gait_keypose(g, 0, (t * 30.0 / g["frames"]) % 1.0)
        k["head"] = head
        return P.to_pose(k)
    return B.Clip(name, "flow", g["frames"], sample, loop=True, speed=round(g["v"], 4), dir=0)


def stop(name, frames, dist):
    jog = P.merged(P.READY, hips=(0.0, -0.06, 0.0), spine=(10.0, 0.0, 0.0), armL=(30.0, 20.0, 70.0), armR=(30.0, -10.0, 70.0),
                   ikL=(-0.12, 0.35, 0.0, -6.0), ikR=(0.12, -0.25, 0.05, 6.0))
    brake = P.merged(P.READY, hips=(0.0, -0.16, -0.12), spine=(-8.0, 0.0, 0.0), armL=(10.0, 30.0, 40.0), armR=(10.0, 30.0, 40.0),
                     ikL=(-0.14, 0.30, 0.0, -10.0), ikR=(0.14, -0.10, 0.0, 10.0), footL=(-15.0, 0.0))
    keys = [(0, jog), (frames // 2, brake), (frames, P.READY)]
    return _clip(name, "locomotion", frames, keys, root=(0.0, dist))


def turn(name, deg, frames):
    s = 1 if deg > 0 else -1
    step = P.merged(P.READY, hips=(0.0, -0.10, 0.0), spine=(12.0, s * 15.0, -s * 8.0), head=(-5.0, s * 30.0),
                    **{"ik" + ("R" if s > 0 else "L"): (s * 0.30, 0.10, 0.05, s * 50.0)})
    keys = [(0, P.READY), (frames // 2, step), (frames, P.READY)]
    return _clip(name, "locomotion", frames, keys, rootYaw=deg)


def clips():
    out = [stop("decel_stop", 20, 1.0), stop("quick_stop", 14, 0.6),
           turn("turn_l90", -90, 18), turn("turn_r90", 90, 18), turn("turn_180", 180, 24)]
    out += [fall(f"fall_{n}", d) for n, d in FALL_DIRS]
    out.append(stumble())
    out += [gk_ready(), gk_dive("gk_dive_l", -1), gk_dive("gk_dive_r", 1), gk_catch_chest(), gk_punch(),
            kick("gk_kick", 1, "lofted", "keeper")]
    out += [celebrate_arms(), celebrate_knee(), dejected()]
    out += [ref("ref_whistle", (-10.0, 110.0, 140.0), (33.0, 5.0, 15.0), 30),
            ref("ref_card", (-10.0, 175.0, 5.0), (33.0, 5.0, 15.0), 36),
            ref("ref_advantage", (0.0, 80.0, 10.0), (0.0, 80.0, 10.0), 30)]
    out += [flow("enter_jog_on", "jog", (-5.0, 0.0)), flow("exit_walk_off", "walk", (25.0, 0.0))]
    return out
