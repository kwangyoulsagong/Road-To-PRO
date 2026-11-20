"""GK 동작 클립 (specs/080 FR-01·02, 011 FR-90 목록).

다이빙 12개 = 좌/우 × 높이(낮음·중간·높음, 011 FR-17) × 도달(손 옆 거리 짧음 0.5 m · 긺 1.0 m).
시뮬이 다이빙 동안 GK 루트를 이미 옮기므로(011 GoalkeeperSystem BeginExternal) 클립의 몸은 루트 위에 두고(옆 이동 0)
몸을 눕히고 팔을 뻗어 닿는다 (080 FR-01 개정, T01 측정: 선방 접촉 공은 시뮬 루트 옆 중앙 0.44 m · 최대 1.07 m).
접촉 키 포즈의 팔은 fit_arms가 FK 격자 탐색으로 맞춘다 — 접촉점(공 중심)이 목표에 오도록.
"""
from . import base as B
from . import poses as P
from .actions import _clip

M = P.merged

GK_READY = M(P.READY, hips=(0.0, -0.14, -0.03), spine=(18.0, 0.0, 0.0), armL=(10.0, 35.0, 45.0), armR=(10.0, 35.0, 45.0))
# 맞춤 팔(4원소: 경첩 팔꿈치·내림 상한 180)과 보간할 키는 팔도 4원소여야 한다 — poses._lerp는 zip이라 3원소와 섞이면 4번째 값이 잘린다
GK_READY4 = M(GK_READY, armL=(10.0, 35.0, 30.0, 180.0), armR=(10.0, 35.0, 30.0, 180.0))

# 다이빙 목표 접촉점 (루트 기준, 공 중심): 높이 구간 대표값과 도달
DIVE_HEIGHT = {"low": 0.22, "mid": 0.95, "high": 1.75}
DIVE_BALL_RANGE = {"low": (0.0, 0.6), "mid": (0.6, 1.5), "high": (1.5, 2.4)}
DIVE_REACH = {"short": 0.5, "long": 1.0}
DIVE_FWD = 0.15
FIT_TOL = 0.60  # 맞춤 실패(포즈가 엉뚱함) 감지용. 목표는 공간을 고르게 덮기 위한 값이고, 실제 접촉점은 굽기 태그 — 높이 구간 안인지는 checks.clip_list, 손–공은 080 T12 측정이 판정
_FIT = {}  # (높이, 도달) → (맞춤 팔 키 포즈, 오차) — 오른쪽만 맞추고 왼쪽은 거울


HAND_GAP = 0.25  # 두 손 잡기 손목 간격 상한 (m, 공 지름 0.22 + 손 두께)
GAP_WEIGHT = 3.0  # 간격 초과 비용 — 낮으면 맞춤이 한 팔만 공 쪽으로 뻗고 다른 팔을 벌린다 (T06 미리보기)


# 관통 방지 (056 AC-03 penetration 검사는 허용 0): 팔이 몸통·허벅지(유니폼)·반대 위팔(소매)·머리(머리카락)에 닿지 않게
# 극단 체형(무거운 체형·아프로 헤어)까지 덮는 보수적 반지름 (m, 기준 키). 팔 점은 아래팔·손 5점 + 위팔 바깥 절반 3점.
CLEAR_TORSO, CLEAR_THIGH, CLEAR_ARM, CLEAR_HEAD = 0.22, 0.14, 0.11, 0.28
CLEAR_WEIGHT = 20.0


def _seg_dist(p, a, b):
    ab = b - a
    t = 0.0 if ab.length_squared < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def clearance_deficit(head, tail):
    """팔 표본점이 몸 부위 캡슐 안으로 들어간 깊이 합 (m)."""
    torso = (head["Hips"], head["Neck"])
    hc = head["Head"].lerp(tail["Head"], 0.5)
    total = 0.0
    for n, o in (("Left", "Right"), ("Right", "Left")):
        pts = [head[n + "UpperArm"].lerp(head[n + "LowerArm"], t) for t in (0.5, 0.75, 1.0)]
        pts += [head[n + "LowerArm"].lerp(head[n + "Hand"], t) for t in (0.25, 0.5, 0.75, 1.0)]
        pts.append(tail[n + "Hand"])
        for p in pts:
            total += max(0.0, CLEAR_TORSO - _seg_dist(p, *torso))
            total += max(0.0, CLEAR_HEAD - (p - hc).length)
            for leg in ("Left", "Right"):
                total += max(0.0, CLEAR_THIGH - _seg_dist(p, head[leg + "UpperLeg"], head[leg + "LowerLeg"]))
        for p in pts[3:]:
            total += max(0.0, CLEAR_ARM - _seg_dist(p, head[o + "UpperArm"], head[o + "LowerArm"]))
    return total


def fit_arms(kp, rule, target, downs=range(-175, 125, 10), flexes=range(-60, 185, 10), elbows=(0, 20, 45), rounds=3, arms=("R", "L")):
    """접촉 키 포즈 kp의 양팔(down·flex·elbow, 055 경첩)을 FK 격자로 찾아 접촉점(공 중심)을 target에 맞춘다.
    몸이 옆으로 누우면 몸 기준 대칭 팔은 월드에서 반대쪽을 향하므로 팔을 번갈아 따로 맞춘다(왼팔 고정 → 오른팔 → …).
    arms = 맞출 팔(한 손 동작은 ("R",) — 나머지 팔은 kp 그대로, 손목 간격 비용 없음).
    비용 = 접촉점 오차 + (양손이면) 손목 간격이 HAND_GAP에서 벗어난 만큼의 절반. 거친 격자(10°) 뒤 ±9° 범위 1° 다듬기. 반환 (새 키 포즈, 접촉점 오차 m)."""
    from mathutils import Vector
    from ..pose import fk
    target = Vector(target)

    def measure(k):
        _, head, tail = fk(P.grounded(k))  # 굽기와 같은 지면 보정 포즈로 잰다 (bake.clip_tags)
        e = (B.contact_point(rule, head) - target).length
        gap = (head["LeftHand"] - head["RightHand"]).length
        return e + (GAP_WEIGHT * max(0.0, gap - HAND_GAP) if len(arms) == 2 else 0.0) + CLEAR_WEIGHT * clearance_deficit(head, tail), e

    def with_arm(k, s, d, f, e):
        k = dict(k)
        k["arm" + s] = (float(d), float(f), float(e), 180.0)
        return k

    def best_arm(k, s, ds, fs, es):
        best = None
        for e in es:
            for d in ds:
                for f in fs:
                    c, _ = measure(with_arm(k, s, d, f, e))
                    if best is None or c < best[0] - 1e-9:
                        best = (c, d, f, e)
        return best

    out = None
    for start in FIT_STARTS:
        k = dict(kp)
        for s in arms:
            k = with_arm(k, s, *start)
        for _ in range(rounds):
            for s in arms:
                _, d, f, e = best_arm(k, s, downs, flexes, elbows)
                _, d, f, e = best_arm(k, s, range(d - 9, d + 10), range(f - 9, f + 10), (max(0, e - 10), e, e + 10))
                k = with_arm(k, s, d, f, e)
        c, e = measure(k)
        if out is None or c < out[0] - 1e-9:
            out = (c, k, e)
    return out[1], out[2]


FIT_STARTS = ((-90.0, 0.0, 0.0), (-150.0, 30.0, 0.0), (0.0, 90.0, 0.0), (-45.0, 150.0, 0.0), (0.0, 60.0, 45.0))


def mirror_arms(kp):
    """오른쪽 다이빙 키 포즈의 팔을 왼쪽 다이빙용으로 (팔 키는 몸 기준이라 좌우 교환이 곧 거울)."""
    out = dict(kp)
    out["armL"], out["armR"] = kp["armR"], kp["armL"]
    return out


def _dive_body(s, height, reach):
    """접촉 순간 몸통 (팔 제외): 높이·도달에 따라 몸을 s 쪽으로 눕힌다. 엉덩이 옆 이동은 0 근처(시뮬 루트 위)."""
    legs = dict(legL=(15.0, 25.0, 20.0, 10.0, 0.0), legR=(15.0, 25.0, 20.0, 10.0, 0.0))
    if height == "low":
        # 짧은 도달: 옆으로 무너지며 눕는다 — 엉덩이가 공 반대쪽에 남고 상체·손이 공 쪽 (몸 중심 ≈ 시뮬 루트)
        roll = 85.0
        body = M(P.READY, hips=(s * 0.05 if reach == "long" else -s * 0.25, -0.72, 0.05), hrot=(0.0, 0.0, s * roll),
                 spine=(5.0, 0.0, s * 10.0), head=(0.0, 0.0), **legs)
    elif height == "mid":
        if reach == "long":
            body = M(P.READY, hips=(s * 0.05, -0.30, 0.05), hrot=(0.0, 0.0, s * 80.0), spine=(0.0, 0.0, s * 15.0), head=(0.0, 0.0), **legs)
        else:  # 옆으로 한 걸음 내디디며 허리 높이 공 — 몸은 조금만 기울인다
            body = M(P.READY, hips=(-s * 0.10, -0.30, 0.0), hrot=(0.0, 0.0, s * 35.0), spine=(15.0, 0.0, s * 25.0), head=(-5.0, 0.0),
                     **{"ik" + ("R" if s > 0 else "L"): (s * 0.45, 0.05, 0.0, s * 30.0), "ik" + ("L" if s > 0 else "R"): (-s * 0.15, 0.0, 0.0, -s * 10.0)})
    else:
        roll = 55.0 if reach == "long" else 25.0
        body = M(P.READY, hips=(s * 0.05, 0.25 if reach == "long" else 0.10, 0.0), hrot=(0.0, 0.0, s * roll),
                 spine=(-5.0, 0.0, s * 10.0), head=(-15.0, 0.0), legL=(20.0, 45.0, 20.0, 5.0, 0.0), legR=(5.0, 20.0, 20.0, 5.0, 0.0))
    return body


def _sided(body, fitted, s):
    arms = fitted if s > 0 else mirror_arms(fitted)
    return dict(body, armL=arms["armL"], armR=arms["armR"])


def landing(s):
    key = ("low", "long")
    if key not in _FIT:
        _FIT[key] = fit_arms(_dive_body(1, *key), B.HANDS, (DIVE_REACH["long"], DIVE_HEIGHT["low"], DIVE_FWD))
    return _sided(M(_dive_body(s, *key), hips=(s * 0.10, -0.72, 0.05)), _FIT[key][0], s)


def gk_dive(height, reach, s, report=None):
    side = "l" if s < 0 else "r"
    name = f"gk_dive_{height}_{reach}_{side}"
    frames, c = 27, 12  # 27: 착지(20) 뒤 7프레임 멈춤 — 클립 메모리(055 예산 +80%)와 착지 뒤 발 미끄러짐(054 FR-10) 사이 (080 T13a)
    # 오른쪽(s = +1)만 맞추고 왼쪽은 거울 — 좌우가 정확히 대칭이고 탐색이 한 번이다
    key = (height, reach)
    if key not in _FIT:
        _FIT[key] = fit_arms(_dive_body(1, height, reach), B.HANDS, (DIVE_REACH[reach], DIVE_HEIGHT[height], DIVE_FWD))
    fitted, e = _FIT[key]
    fly = _sided(_dive_body(s, height, reach), fitted, s)
    if report is not None:
        report[name] = e
    if e > FIT_TOL:
        raise ValueError(f"{name}: 접촉점 맞춤 실패 {e:.3f} m")
    load = M(GK_READY4, hips=(s * 0.05, -0.22, 0.0), **{"ik" + ("L" if s < 0 else "R"): (s * 0.32, 0.0, 0.0, s * 20.0)})
    # 착지: 모든 다이빙이 낮은 긴 다이빙의 누운 자세로 끝난다 (관통 검사를 통과한 포즈 하나로 통일, 일어나기는 GetUp 클립)
    land = landing(s)
    keys = [(0, GK_READY4), (5, load), (c, fly), (c + 3, fly), (20, land), (frames, land)]
    return _clip(name, "keeper", frames, keys, c, "Hand", B.HANDS, (-180, 180), DIVE_BALL_RANGE[height], (0.0, 40.0),
                 root=(0.0, 0.0))


def gk_ready():
    base = M(GK_READY, ikL=(-0.22, 0.0, 0.0, -10.0), ikR=(0.22, 0.0, 0.0, 10.0))
    tl = P.timeline([(0, base), (15, M(base, hips=(0.0, -0.12, -0.03))), (30, base)])
    return B.Clip("gk_ready", "keeper", 30, lambda t: P.grounded(tl(t * 30.0)), loop=True)


# ---------------------------------------------------------------- 잡기·쳐내기·넘기기·펀칭·클레임 (080 T05)
# 목표 접촉점은 T01 진단(evidence/baseline.md)의 GK 기준 공 위치 분포에서: 땅볼 잡기 높이 0.11·옆 ±0.5, 가슴 잡기 높이 중앙 1.08,
# 쳐내기 정면 앞 낮은 공, 펀칭 두 손 1.9 m 근처. 측면 쳐내기는 대부분 낮은 공이라 다이빙 클립이 후보를 같이 받는다(043 맵).

STEP = dict(ikL=(-0.17, 0.02, 0.0, -10.0), ikR=(0.17, 0.02, 0.0, 10.0))


def _air(lift):
    return dict(ikL=(-0.14, 0.0, lift, -6.0), ikR=(0.14, 0.0, lift, 6.0))


def _fit_once(key, body, rule, target, arms=("R", "L")):
    if key not in _FIT:
        _FIT[key] = fit_arms(body, rule, target, arms=arms)
    return _FIT[key][0]


def fitted(name, body, rule, target, frames, c, height, prep=None, arms=("R", "L"), s=1, part="Hand", hold=3, key=None, air=None):
    """접촉 키 포즈를 맞춰 클립 하나를 만든다. s = −1이면 오른쪽(s = +1) 맞춤을 거울로 쓴다 (몸 키는 호출자가 s로 만든다)."""
    key = key or (name[:-2] if name.endswith(("_l", "_r")) else name)
    right_body = body(1)
    fk_ = _fit_once(key, right_body, rule(1), target(1), arms)
    contact = _sided(body(s), fk_, s)
    start = GK_READY4
    keys = [(0, start)]
    if prep is not None:
        keys.append((max(1, c // 2), prep(s)))
    # 끝: 접촉 자세 유지(다음 동작이 이어 받는다). 점프 동작(air)은 같은 자세로 착지 — 엉덩이·발을 lift만큼 내린다
    end = contact if air is None else M(contact, hips=(contact["hips"][0], contact["hips"][1] - air, contact["hips"][2]), **_air(0.0))
    keys += [(c, contact), (min(frames - 1, c + hold), contact), (frames, end)]
    return _clip(name, "keeper", frames, keys, c, part, rule(s), (-180, 180), height, (0.0, 40.0))


def _legs(k, near, far):
    """비대칭 다리 키: k = +1이면 왼다리 = near, 오른다리 = far. 거울(k = −1)은 좌우 교환 (팔 거울과 짝)."""
    return dict(legL=near, legR=far) if k > 0 else dict(legL=far, legR=near)


def _crouch(s, depth, lean):
    return M(GK_READY4, hips=(0.0, depth, 0.05), spine=(lean, 0.0, 0.0), head=(-25.0, 0.0), **STEP)


def gk_catch_ground(side):
    """몸 가까운 땅볼: 무릎 꿇고 품기 (080 Q1). side = 0 정면, ±1 옆 0.35 m."""
    name = {0: "gk_catch_ground", -1: "gk_catch_ground_l", 1: "gk_catch_ground_r"}[side]
    s = side if side else 1
    # 무릎을 벌리고 꿇어 손이 다리 사이 바닥으로 내려간다 (허벅지 관통 없이)
    kneel = lambda k: M(GK_READY4, hips=(k * 0.08 * abs(side), -0.55, -0.08), hrot=(20.0, k * 15.0 * abs(side), 0.0),
                        spine=(60.0, 0.0, k * 10.0 * abs(side)), head=(-45.0, 0.0),
                        **_legs(k, (60.0, 110.0, 30.0, 28.0, 10.0), (15.0, 125.0, 10.0, 28.0, 10.0)))
    return fitted(name, kneel, lambda k: B.HANDS, lambda k: (0.35 * abs(side) * k, 0.12, 0.40), 22, 9, (0.0, 0.45),
                  prep=lambda k: _crouch(k, -0.28, 25.0), s=s, key="gk_catch_ground" + ("_side" if side else ""))


def gk_catch_chest():
    body = lambda k: M(GK_READY4, hips=(0.0, -0.12, 0.0), spine=(8.0, 0.0, 0.0), head=(-8.0, 0.0), **STEP)
    return fitted("gk_catch_chest", body, lambda k: B.HANDS, lambda k: (0.0, 1.10, 0.45), 20, 8, (0.6, 1.6))  # 손이 공 뒤(HANDS 규칙) — 가슴 앞 공 중심


def gk_catch_high():
    body = lambda k: M(GK_READY4, hips=(0.0, 0.15, 0.0), spine=(-5.0, 0.0, 0.0), head=(-25.0, 0.0), **_air(0.15))
    return fitted("gk_catch_high", body, lambda k: B.HANDS, lambda k: (0.0, 2.05, 0.30), 20, 10, (1.6, 2.5),
                  prep=lambda k: _crouch(k, -0.22, 10.0), air=0.15)


def gk_claim_jump():
    """073 크로스 클레임: 한 무릎을 들고 높이 뛰어 잡기."""
    body = lambda k: M(GK_READY4, hips=(0.0, 0.38, 0.0), spine=(-5.0, 0.0, 0.0), head=(-30.0, 0.0),
                       legL=(5.0, 15.0, 20.0, 5.0, 0.0), legR=(75.0, 95.0, 20.0, 5.0, 0.0))
    return fitted("gk_claim_jump", body, lambda k: B.HANDS, lambda k: (0.0, 2.45, 0.35), 22, 11, (2.0, 3.0),
                  prep=lambda k: _crouch(k, -0.25, 10.0), air=0.38)


def gk_parry_front():
    body = lambda k: M(GK_READY4, hips=(0.0, -0.50, -0.05), hrot=(15.0, 0.0, 0.0), spine=(60.0, 0.0, 0.0), head=(-45.0, 0.0),
                       ikL=(-0.30, -0.05, 0.0, -25.0), ikR=(0.30, -0.05, 0.0, 25.0))
    return fitted("gk_parry_front", body, lambda k: B.HANDS, lambda k: (0.0, 0.18, 0.55), 20, 8, (0.0, 0.7))


def gk_parry_side(s):
    """허리~가슴 높이 옆 공을 한 손으로 쳐낸다 (낮은 측면 쳐내기는 다이빙 클립)."""
    body = lambda k: M(GK_READY4, hips=(0.0, -0.18, 0.0), hrot=(0.0, 0.0, k * 30.0), spine=(5.0, 0.0, k * 15.0), head=(-5.0, 0.0),
                       **{"ik" + ("R" if k > 0 else "L"): (k * 0.45, 0.05, 0.0, k * 30.0), "ik" + ("L" if k > 0 else "R"): (-k * 0.15, 0.0, 0.0, -k * 10.0)})
    return fitted("gk_parry_side_" + ("l" if s < 0 else "r"), body, lambda k: B.hand(k), lambda k: (k * 0.80, 1.20, 0.15), 20, 8,
                  (0.6, 1.6), arms=("R",), s=s)


def gk_tip_over(s):
    """크로스바 위로 넘기기: 뛰어올라 한 손끝."""
    body = lambda k: M(GK_READY4, hips=(k * 0.05, 0.35, 0.0), hrot=(0.0, 0.0, k * 30.0), spine=(-10.0, 0.0, k * 15.0), head=(-30.0, 0.0),
                       **_air(0.35))
    return fitted("gk_tip_over_" + ("l" if s < 0 else "r"), body, lambda k: B.hand(k), lambda k: (k * 0.70, 2.20, 0.10), 22, 10,
                  (2.0, 2.8), prep=lambda k: _crouch(k, -0.22, 10.0), arms=("R",), s=s, air=0.35)


def gk_punch_two():
    body = lambda k: M(GK_READY4, hips=(0.0, 0.25, 0.0), spine=(-8.0, 0.0, 0.0), head=(-25.0, 0.0), **_air(0.25))
    return fitted("gk_punch_two", body, lambda k: B.HANDS, lambda k: (0.0, 2.20, 0.40), 22, 9, (1.8, 2.8),
                  prep=lambda k: _crouch(k, -0.22, 10.0), air=0.25)


def gk_punch():
    frames, c = 18, 8
    jump = M(P.READY, hips=(0.0, 0.25, 0.0), ikL=(-0.12, 0.0, 0.25, -6.0), ikR=(0.12, 0.0, 0.25, 6.0),
             armL=(-10.0, 60.0, 20.0), armR=(-20.0, 150.0, 30.0), spine=(-5.0, 0.0, 0.0))
    hit = M(jump, armR=(-20.0, 165.0, 0.0))
    keys = [(0, P.READY), (4, M(P.READY, hips=(0.0, -0.15, 0.0))), (c, hit), (14, jump), (frames, P.READY)]
    return _clip("gk_punch", "keeper", frames, keys, c, "Hand", B.hand(1), (-45, 45), (2.0, 2.8), (5.0, 30.0))


# ---------------------------------------------------------------- 스마더·스프레드·이동·일어나기·배급 (080 T06)

def gk_smother(s):
    """073 1v1 덮치기: 옆으로 쓰러지며 공격수 발밑 공을 덮는다 (080 Q1 '옆으로 쓰러지며 덮기')."""
    body = lambda k: M(_dive_body(k, "low", "short"), hips=(-k * 0.20, -0.72, -0.10), spine=(20.0, 0.0, k * 10.0))
    return fitted("gk_smother_" + ("l" if s < 0 else "r"), body, lambda k: B.HANDS, lambda k: (k * 0.30, 0.12, 0.45), 22, 10,
                  (0.0, 0.6), prep=lambda k: _crouch(k, -0.35, 30.0), s=s)


def _plain(name, frames, keys, **tags):
    tl = P.timeline(keys)
    return B.Clip(name, "keeper", frames, lambda t: P.grounded(tl(t * 30.0)), **tags)


def gk_spread():
    """073 1v1 스프레드: 몸을 크게 펼쳐 슈팅 각을 막는다 (접촉 없음 — 선방 판정은 시뮬)."""
    wide = M(GK_READY4, hips=(0.0, -0.40, 0.0), spine=(25.0, 0.0, 0.0), head=(-20.0, 0.0),
             armL=(-40.0, 20.0, 10.0, 180.0), armR=(-40.0, 20.0, 10.0, 180.0),
             legL=(30.0, 80.0, 20.0, 35.0, 10.0), legR=(5.0, 30.0, 20.0, 35.0, 10.0))
    return _plain("gk_spread", 16, [(0, GK_READY4), (8, wide), (16, wide)], root=[0.0, 0.0])


def gk_shuffle(s):
    """옆걸음 (011 FR-90 좌우 셔플). 시뮬이 루트를 옮기므로 제자리 동작."""
    lead, trail = ("R", "L") if s > 0 else ("L", "R")
    step = M(GK_READY4, hips=(s * 0.04, -0.16, -0.03), **{"ik" + lead: (s * 0.36, 0.0, 0.04, s * 12.0), "ik" + trail: (-s * 0.10, 0.0, 0.0, -s * 8.0)})
    close = M(GK_READY4, hips=(0.0, -0.14, -0.03), ikL=(-0.20, 0.0, 0.0, -10.0), ikR=(0.20, 0.0, 0.0, 10.0))
    return _plain("gk_shuffle_" + ("l" if s < 0 else "r"), 16, [(0, close), (6, step), (16, close)], root=[0.0, 0.0])


def gk_pk_move(s):
    """074 PK 조기 이동: 킥 직전 한쪽으로 체중을 싣고 반 걸음."""
    lead, trail = ("R", "L") if s > 0 else ("L", "R")
    lean = M(GK_READY4, hips=(s * 0.10, -0.22, 0.0), hrot=(0.0, 0.0, s * 8.0), spine=(15.0, 0.0, s * 8.0),
             **{"ik" + lead: (s * 0.42, 0.02, 0.0, s * 25.0), "ik" + trail: (-s * 0.12, 0.0, 0.0, -s * 8.0)})
    return _plain("gk_pk_move_" + ("l" if s < 0 else "r"), 12, [(0, GK_READY4), (8, lean), (12, lean)], root=[0.0, 0.0])


def gk_getup():
    """다이빙 착지(누운 자세) → 준비 자세 (011 FR-17 일어나기, GetUp 이벤트)."""
    lying = landing(1)
    sit = M(GK_READY4, hips=(0.10, -0.62, 0.0), hrot=(0.0, 0.0, 30.0), spine=(30.0, 0.0, 10.0),
            armL=(10.0, 35.0, 30.0, 180.0), armR=(-20.0, 20.0, 20.0, 180.0),
            legL=(70.0, 120.0, 20.0, 10.0, 0.0), legR=(40.0, 110.0, 20.0, 10.0, 0.0))
    crouch = M(GK_READY4, hips=(0.05, -0.40, 0.0), spine=(35.0, 0.0, 0.0), **STEP)
    return _plain("gk_getup", 24, [(0, lying), (8, sit), (16, crouch), (24, GK_READY4)], root=[0.0, 0.0])


def gk_roll():
    """074 배급 굴리기: 웅크려 오른손으로 공을 앞으로 굴린다 (공이 손을 떠나는 순간 = 접촉)."""
    body = lambda k: M(GK_READY4, hips=(0.0, -0.45, -0.05), hrot=(15.0, 0.0, 0.0), spine=(55.0, 0.0, 0.0), head=(-40.0, 0.0),
                       **_legs(-1, (55.0, 95.0, 20.0, 10.0, 0.0), (10.0, 60.0, 20.0, 10.0, 0.0)))
    return fitted("gk_roll_r", body, lambda k: B.hand(1), lambda k: (0.10, 0.12, 0.32), 20, 12, (0.0, 0.4),  # 시뮬 공은 배급 순간 GK 발밑
                  prep=lambda k: _crouch(k, -0.30, 30.0), arms=("R",))


def gk_throw():
    """074 배급 오버암 던지기: 오른팔을 머리 위로 돌려 던진다. 시뮬 공이 발밑이라 손–공은 기록만 (080 FR-06, D31)."""
    body = lambda k: M(GK_READY4, hips=(0.0, -0.10, 0.05), spine=(10.0, -15.0, -10.0), head=(-10.0, 0.0),
                       ikL=(-0.15, 0.35, 0.0, -10.0), ikR=(0.18, -0.20, 0.0, 20.0))
    return fitted("gk_throw_r", body, lambda k: B.hand(1), lambda k: (0.30, 2.00, 0.25), 20, 12, (1.5, 2.4),
                  prep=lambda k: M(body(k), spine=(0.0, 25.0, -15.0)), arms=("R",))


def gk_lunge_low():
    """앞으로 몸을 던져 땅볼 덮치기 (080 T13a: 시뮬 GK가 1.5~1.8 m 앞 느슨한 공을 잡는다). 엎드려 팔을 앞으로, 고개는 들어 머리카락이 팔에 닿지 않게."""
    body = lambda k: M(GK_READY4, hips=(0.0, -0.70, 0.75), hrot=(80.0, 0.0, 0.0), spine=(5.0, 0.0, 0.0), head=(-55.0, 0.0),
                       legL=(-10.0, 20.0, 30.0, 8.0, 0.0), legR=(-5.0, 35.0, 30.0, 8.0, 0.0))
    return fitted("gk_lunge_low", body, lambda k: B.HANDS, lambda k: (0.0, 0.12, 1.65), 22, 10, (0.0, 0.6),
                  prep=lambda k: _crouch(k, -0.35, 35.0))


def gk_lunge_mid():
    """앞으로 크게 내디디며 가슴~머리 높이 공 잡기 (080 T13a)."""
    body = lambda k: M(GK_READY4, hips=(0.0, -0.25, 0.30), spine=(30.0, 0.0, 0.0), head=(-25.0, 0.0),
                       ikL=(-0.15, 0.75, 0.0, -5.0), ikR=(0.17, -0.35, 0.0, 15.0))
    return fitted("gk_lunge_mid", body, lambda k: B.HANDS, lambda k: (0.0, 1.30, 1.00), 22, 9, (0.6, 1.8),
                  prep=lambda k: _crouch(k, -0.20, 15.0))


def moves():
    return [gk_smother(-1), gk_smother(1), gk_spread(), gk_shuffle(-1), gk_shuffle(1), gk_pk_move(-1), gk_pk_move(1),
            gk_getup(), gk_roll(), gk_throw(), gk_lunge_low(), gk_lunge_mid()]


def dives(report=None):
    return [gk_dive(h, r, s, report) for h in ("low", "mid", "high") for r in ("short", "long") for s in (-1, 1)]


def saves():
    return [gk_catch_ground(0), gk_catch_ground(-1), gk_catch_ground(1), gk_catch_chest(), gk_catch_high(), gk_claim_jump(),
            gk_parry_front(), gk_parry_side(-1), gk_parry_side(1), gk_tip_over(-1), gk_tip_over(1), gk_punch(), gk_punch_two()]


def clips():
    return [gk_ready()] + dives() + saves() + moves()
