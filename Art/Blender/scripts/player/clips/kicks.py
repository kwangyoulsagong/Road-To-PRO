"""킥·패스 클립 (specs/043 FR-31·32·61 → specs/055 FR-60·61).

055 공통 기준(FR-60, Data/anim/055-gait-standards.json kicks):
- 디딤발: 공 뒤 5~10 cm·옆 5~28 cm(가장자리 간). 접촉 키 포즈를 맞춘 뒤 공 위치(접촉점)를 보고 디딤발을 옮겨 다시 맞춘다(3회).
- 접촉 순간 무릎: 차는 다리 50°(40~60), 디딤다리 32°(25~40) — 디딤다리는 골반 높이로.
- 백스윙(엉덩이 최대 신전, 몸통선 기준): big 29°(Levanon & Dapena) / medium 18° / small 3°.
- 엉덩이 굴곡 각속도 피크: 백스윙 최대 → 접촉 구간의 27% 지점(Dörge 근육 타이밍을 운동학으로 옮긴 [제안] 정의) — 그 구간을
  속도가 베타 모양(시작 0, 27%에서 최대)인 이징으로, 무릎 신전은 접촉에서 가장 빠르게(x^2.2) 보간한다(근위 → 원위 채찍).
- 팔로스루(접촉 뒤 엉덩이 굴곡 증가): short 15° / medium 30° / long 55°.
원형 K1~K8(FR-61): 도움닫기 걸음 수·각도, 백스윙·팔로스루 등급(055-run-styles.json kicks)과 아래 KSTYLE 생성 값.
도움닫기 2걸음 이상은 보법 엔진(gait._Gait)의 반주기를 앞에 붙인다(제자리, 루트 이동은 태그 runup = {dir, speed} · root).
땅볼이 아닌 킥(발리, 펀트)은 디딤발·무릎 기준 대상이 아니다(공 높이 > 0.3 m).
"""
import math

from mathutils import Vector

from .. import skeleton as S
from ..pose import fk
from . import base as B
from . import gait as GT
from . import poses as P
from .base import Clip, smooth

M = P.merged

KIND = {  # frames(킥 부분), contact, rot(차는 발 바깥 회전), lean(몸통), abd, backswing, follow, part rule, 공 높이, 공 속도, 접근각
    "inside": dict(frames=22, c=12, rot=65, lean=4, abd=0, back="small", follow="short", rule="inside", height=(0.0, 0.3), speed=(4, 16), approach=(-40, 40)),
    "outside": dict(frames=22, c=12, rot=-25, lean=4, abd=0, back="small", follow="short", rule="outside", height=(0.0, 0.3), speed=(4, 16), approach=(-20, 40)),
    "instep": dict(frames=26, c=13, rot=0, lean=4, abd=0, back="big", follow="medium", rule="instep", height=(0.0, 0.3), speed=(15, 34), approach=(-30, 30)),
    "lofted": dict(frames=26, c=13, rot=0, lean=-10, abd=0, back="big", follow="long", rule="instep", height=(0.0, 0.3), speed=(12, 25), approach=(-30, 30)),
    "curl": dict(frames=26, c=13, rot=25, lean=4, abd=0, back="medium", follow="long", rule="instep", height=(0.0, 0.3), speed=(15, 34), approach=(-40, 20)),
    "volley": dict(frames=24, c=12, rot=0, lean=-14, abd=10, back="big", follow="medium", rule="instep", height=(0.35, 1.0), speed=(15, 34), approach=(-30, 30)),
    "cross": dict(frames=28, c=14, rot=30, lean=-8, abd=5, back="big", follow="long", rule="instep", height=(0.0, 0.3), speed=(14, 27), approach=(-60, 10)),
    "onetouch_pass": dict(frames=14, c=6, rot=65, lean=4, abd=0, back="small", follow="short", rule="inside", height=(0.0, 0.3), speed=(4, 15), approach=(-30, 30)),
    "onetouch_shot": dict(frames=16, c=7, rot=0, lean=4, abd=0, back="small", follow="short", rule="instep", height=(0.0, 0.4), speed=(15, 34), approach=(-30, 30)),
    "power": dict(frames=26, c=13, rot=0, lean=2, abd=0, back="big", follow="short", rule="instep", height=(0.0, 0.3), speed=(20, 34), approach=(-15, 15)),
    "finish": dict(frames=18, c=9, rot=65, lean=8, abd=0, back="small", follow="short", rule="inside", height=(0.0, 0.3), speed=(8, 22), approach=(-30, 30)),
    "punt": dict(frames=24, c=12, rot=0, lean=-10, abd=0, back="big", follow="long", rule="instep", height=(0.4, 0.8), speed=(18, 30), approach=(-20, 20)),
}
BACK_EXT = {"big": 29.0, "medium": 18.0, "small": 3.0}
FOLLOW = {"short": 15.0, "medium": 30.0, "long": 55.0}
KNEE_KICK, KNEE_PLANT = 56.0, 30.0
PLANT_DZ = 0.30         # 골반 높이를 정할 때 가정하는 디딤발 앞 거리(m)
FLEX_PEAK = 0.265       # 엉덩이 굴곡 각속도 피크(백스윙 최대 → 접촉 구간 비율)
SWING_FRAMES = 3        # 백스윙 최대 → 접촉 (0.1 s): 접촉 발 속도 10~20 m/s (4프레임이면 8~10, 6이면 3~4; 실측 프로 18~24 m/s, Shinkai 2008)
KNEE_BACK = 108.0       # 큰·중간 백스윙의 무릎 굴곡 (무릎 채찍)
FLEX_CONTACT = 33.0     # 접촉 순간 차는 허벅지 굴곡 선호(골반 기준) — 몸이 디딤발 위, 무릎이 공 위
PLANT_BEHIND = 0.075    # 공 뒤 (m, 발 중심)
FOOT_HALF_W = 0.045
# 원형별 생성 값 [추정] (FR-61 표): 도움닫기 걸음 n, 각도 a(°), 속도 v(m/s), 디딤발 옆 간격(가장자리 m)·뒤 거리
KSTYLE = {
    "k1": dict(n=4, a=8.0, v=3.5, side=0.17, behind=0.08),
    "k2": dict(n=1, a=0.0, v=0.0, side=0.12, behind=0.07),
    "k3": dict(n=3, a=50.0, v=3.0, side=0.10, behind=0.075),
    "k4": dict(n=1, a=0.0, v=0.0, side=0.13, behind=0.06),
    "k5": dict(n=2, a=38.0, v=3.0, side=0.24, behind=0.085),
    "k6": dict(n=1, a=0.0, v=0.0, side=0.14, behind=0.07),
    "k7": dict(n=2, a=38.0, v=3.0, side=0.18, behind=0.055),
    "k8": dict(n=4, a=10.0, v=3.4, side=0.18, behind=0.08),
    "k8punt": dict(n=1, a=0.0, v=0.0, side=0.18, behind=0.08),
}
DEFAULT = dict(n=1, a=0.0, v=0.0, side=0.15, behind=PLANT_BEHIND)


def _side(s):
    return ("R", "L") if s > 0 else ("L", "R")


def _leg_len(knee):
    a, b = S.L_THIGH, S.L_SHIN
    return math.sqrt(a * a + b * b + 2 * a * b * math.cos(math.radians(knee)))


def fit_contact(K, ks, s, k, pl, rule, ground, target, lean, abd, rot, arm_k, arm_p):
    """접촉 키 포즈를 해석적으로: 차는 허벅지 각 후보마다 ① 발끝이 바닥 위인 가장 큰 발목 각 ② 접촉점 높이 = target이 되는 골반 높이(이분법)
    ③ 공 기준 디딤발(뒤 behind·옆 side) ④ 디딤 무릎(엉덩이 관절 ↔ 디딤 발목 거리) → 디딤 무릎이 KNEE_PLANT에 가장 가까운 해.
    반복 맞춤(디딤발 → 골반 → 차는 다리 → 공 → 디딤발)은 되먹임으로 흔들려서 쓰지 않는다."""
    nk = "Left" if k == "L" else "Right"
    npl = "Left" if pl == "L" else "Right"
    knee = KNEE_KICK if ground else 30.0
    if K["rule"] == "instep":
        ankles = list(range(45, 4, -5)) if ground else list(range(40, -11, -5))
    else:
        ankles = list(range(15, -6, -5))
    thetas = range(10, 62, 2) if ground else range(50, 112, 3)
    base = M(P.READY, hips=(s * -0.08, -0.07, 0.0), hrot=(0.0, s * 5.0, 0.0), spine=(6.0 + lean, s * 10.0, -s * 10.0),
             head=(15.0, 0.0), **{arm_p: (-10.0, 25.0, 30.0), arm_k: (20.0, -30.0, 30.0)})
    base.pop("ik" + k, None)
    best = None
    for th in thetas:
        for an in ankles:
            kp = dict(base)
            kp["leg" + k] = (float(th), knee, float(an), abd, rot)
            lo, hi = -0.40, 0.10  # 골반 높이 이분법
            for _ in range(22):
                mid = 0.5 * (lo + hi)
                kp["hips"] = (base["hips"][0], mid, 0.0)
                _, h, _ = fk(P.to_pose(kp))
                if B.contact_point(rule, h).y > target:
                    hi = mid
                else:
                    lo = mid
            kp["hips"] = (base["hips"][0], 0.5 * (lo + hi), 0.0)
            _, h, t = fk(P.to_pose(kp))
            if h[nk + "Foot"].y < 0.06 or h[nk + "Toes"].y < 0.02 or t[nk + "Toes"].y < P.TIP_MIN + 0.005:
                continue
            ball = B.contact_point(rule, h)
            f = B.facing(h)
            side = Vector((0, 1, 0)).cross(f)
            # 디딤발 중심 목표 → 발목 위치 (발 중심 = 발목 + 발 앞 방향 반 길이, 발 yaw −s·10°)
            yaw = math.radians(-s * 10.0)
            fdir = Vector((math.sin(yaw), 0.0, math.cos(yaw)))
            half = (Vector(S.TAIL[npl + "Toes"]) - Vector(S.HEAD[npl + "Foot"])).length * 0.5
            pc = ball - f * ks["behind"] - side * (s * (B.BALL_R + FOOT_HALF_W + ks["side"])) if ground else \
                h[npl + "UpperLeg"] + f * 0.10 - side * (s * 0.05)
            ank = Vector((pc.x, S.ANKLE_Y, pc.z)) - fdir * half
            hip = h[npl + "UpperLeg"]
            dist = (ank - hip).length
            a_, b_ = S.L_THIGH, S.L_SHIN
            ck = (a_ * a_ + b_ * b_ - dist * dist) / (2 * a_ * b_)
            if ck < -1.0:  # 닿지 않음
                continue
            pk = 180.0 - math.degrees(math.acos(max(-1.0, min(1.0, ck))))
            cost = abs(pk - KNEE_PLANT) + 0.05 * abs(th - FLEX_CONTACT) + 0.02 * abs(an - 30)
            if best is None or cost < best[0]:
                best = (cost, dict(kp), ank.x, ank.z)
            break  # 이 허벅지 각에서 발끝이 뜨는 가장 큰 발목 각만
    if best is None:
        raise ValueError(f"접촉 자세 맞춤 실패 (목표 높이 {target})")
    _, kp, ax, az = best
    kp["ik" + pl] = (ax, az, 0.0, -s * 10.0)
    kp.pop("leg" + pl, None)
    return kp, ax, az


def _rule(name, s):
    return {"inside": B.inside(s), "outside": B.outside(s), "instep": B.instep(s)}[name]


def _hip_flex_metric(kp, k):
    """gait_metrics와 같은 정의의 엉덩이 굴곡(몸통선 기준, 몸 정면 시상면)."""
    _, h, _ = fk(P.to_pose(kp))
    n = "Left" if k == "L" else "Right"
    th, tr = h[n + "LowerLeg"] - h[n + "UpperLeg"], h["Neck"] - h["Hips"]
    return math.degrees(math.atan2(th.z, -th.y) - math.atan2(-tr.z, tr.y))


def _solve_ext(kp, k, ext):
    """백스윙 키 포즈의 FK 허벅지 굴곡을 이분법으로 — 측정 엉덩이 신전 = ext."""
    lo, hi = -80.0, 40.0
    leg = kp["leg" + k]
    for _ in range(30):
        mid = 0.5 * (lo + hi)
        if -_hip_flex_metric(dict(kp, **{"leg" + k: (mid,) + leg[1:]}), k) > ext:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def _trunk_lean(kp):
    _, h, _ = fk(P.to_pose(kp))
    tr = h["Neck"] - h["Hips"]
    return math.degrees(math.atan2(tr.z, tr.y))


def _plant_height(kp, pl, virtual_dz=None):
    """디딤다리 무릎 = KNEE_PLANT가 되는 골반 높이(hips dy)로 바꾼다. virtual_dz가 있으면 디딤발이 엉덩이 관절보다 그만큼 앞에
    있다고 보고 정한다 — 실제 디딤발 위치로 정하면 디딤발 → 골반 높이 → 차는 다리 → 공 → 디딤발 되먹임으로 발산한다."""
    a, b = S.L_THIGH, S.L_SHIN
    L = math.sqrt(a * a + b * b + 2 * a * b * math.cos(math.radians(KNEE_PLANT)))
    for _ in range(4):
        _, h, _ = fk(P.to_pose(kp))
        n = "Left" if pl == "L" else "Right"
        hip, ank = h[n + "UpperLeg"], h[n + "Foot"]
        dx = math.hypot(ank.x - hip.x, ank.z - hip.z) if virtual_dz is None else virtual_dz
        want = ank.y + math.sqrt(max(1e-6, L * L - dx * dx))
        hx, hy, hz = kp["hips"]
        kp = dict(kp, hips=(hx, hy + (want - hip.y), hz))
    return kp


def _timeline(keys):
    """keys = [(프레임, 키포즈, 이 키로 들어오는 이징)] → f(프레임) → 키포즈."""
    def at(f):
        if f <= keys[0][0]:
            return keys[0][1]
        for (f0, a, _), (f1, b, ease) in zip(keys, keys[1:]):
            if f <= f1:
                return P.blend(a, b, ease((f - f0) / (f1 - f0)))
        return keys[-1][1]
    return at


def _fast_start(x_peak, a=1.6):
    """속도 곡선 v(x) ∝ x^a (1−x)^b (베타 모양): x = 0·1에서 속도 0, x_peak에서 최대. 누적 적분표로 이징 f(x) (0 → 1)."""
    bexp = a * (1.0 / x_peak - 1.0)
    n = 400
    acc = [0.0]
    for i in range(1, n + 1):
        x = (i - 0.5) / n
        acc.append(acc[-1] + x ** a * (1.0 - x) ** bexp)
    tot = acc[-1]

    def f(x):
        x = min(1.0, max(0.0, x))
        j = x * n
        i = int(j)
        if i >= n:
            return 1.0
        return (acc[i] + (acc[i + 1] - acc[i]) * (j - i)) / tot
    return f


def ik_to_fk(kp, leg):
    """키 포즈의 IK 다리(ik L/R)를 같은 자세의 FK 각 (flex, knee, ankle, abd, rot)으로 바꾼다 — 도움닫기(IK) → 킥(FK) 이음새용.
    to_pose의 FK 정의를 거꾸로: 골반 좌표에서 허벅지 굴곡·벌림, 무릎 = 허벅지·정강이 사이각, 발목 = 발 회전 + (굴곡 − 무릎)."""
    if "ik" + leg not in kp:
        return kp
    rw, h, _ = fk(P.to_pose(kp))
    n = "Left" if leg == "L" else "Right"
    side = -1.0 if leg == "L" else 1.0
    inv = rw["Hips"].inverted()
    th = inv @ (h[n + "LowerLeg"] - h[n + "UpperLeg"])
    sh = inv @ (h[n + "Foot"] - h[n + "LowerLeg"])
    ft = inv @ (h[n + "Toes"] - h[n + "Foot"])
    flex = math.degrees(math.atan2(th.z, -th.y))
    abd = math.degrees(math.atan2(side * th.x, math.hypot(th.y, th.z)))
    knee = math.degrees(math.acos(max(-1.0, min(1.0, th.normalized().dot(sh.normalized())))))
    rest = Vector(S.HEAD[n + "Toes"]) - Vector(S.HEAD[n + "Foot"])
    rot_f = math.degrees(math.atan2(-ft.y, ft.z) - math.atan2(-rest.y, rest.z))  # 발이 레스트보다 앞으로 숙은 각
    ankle = rot_f + (flex - knee)
    out = dict(kp)
    out.pop("ik" + leg, None)
    out.pop("foot" + leg, None)
    out["leg" + leg] = (flex, knee, ankle, abd, 0.0)
    return out


def _ease_in(x, p=3.0):
    x = min(1.0, max(0.0, x))
    return x ** p


def kick(id_, s, kind, group, style=None, **extra):
    """s = +1 오른발, −1 왼발."""
    K = KIND[kind]
    ks = KSTYLE.get(style, DEFAULT) if style else DEFAULT
    k, pl = _side(s)
    frames, c = K["frames"], K["c"]
    rot, lean, abd = K["rot"], K["lean"], K["abd"]
    rule = _rule(K["rule"], s)
    arm_k, arm_p = "arm" + k, "arm" + pl
    ground = K["height"][0] < 0.3  # 공이 땅에 있을 수 있는 킥(원터치 슈팅 0~0.4 포함) — 발리·펀트는 공중
    target = B.BALL_R if ground else 0.60
    contact, plant_x, plant_z = fit_contact(K, ks, s, k, pl, rule, ground, target, lean, abd, rot, arm_k, arm_p)
    plant = {"ik" + pl: (plant_x, plant_z, 0.0, -s * 10.0)}
    hips_c = contact["hips"]
    flex_c, knee_c, ankle_c = contact["leg" + k][0], contact["leg" + k][1], contact["leg" + k][2]
    # 백스윙 무릎: 접촉 무릎보다 충분히 굽혀 무릎 신전으로 발이 빠르게 들어가게(작은 백스윙도 — 같으면 접촉 순간 발이 멈춘다)
    windup = M(P.READY, **plant, **{"leg" + k: (0.0, KNEE_BACK if K["back"] != "small" else knee_c + 45.0, 30.0, abd, rot * 0.6)},
               hips=(hips_c[0] * 0.6, hips_c[1] - 0.01, hips_c[2] - 0.03), hrot=(0.0, -s * 15.0, 0.0),  # 디딤발이 닿는 거리 (−0.08이면 다리가 다 펴져 착지가 두 번 잡힌다)
               spine=(12.0 + lean * 0.3, -s * 20.0, -s * 6.0), **{arm_p: (0.0, 35.0, 30.0), arm_k: (15.0, -20.0, 30.0)})
    flex_w = _solve_ext(windup, k, BACK_EXT[K["back"]])  # 백스윙: 엉덩이 신전(몸통 기준, gait_metrics 정의) = BACK_EXT
    knee_w = windup["leg" + k][1]
    windup["leg" + k] = (flex_w,) + windup["leg" + k][1:]
    flex_e = flex_c + FOLLOW[K["follow"]]
    end = M(P.READY, **plant, **{"leg" + k: (flex_e, 15.0, 35.0, abd, rot * 0.5)},
            hips=(hips_c[0] * 0.75, hips_c[1], hips_c[2] + 0.10), hrot=(0.0, s * 15.0, 0.0), spine=(4.0 + lean, s * 20.0, -s * 6.0),
            **{arm_p: (5.0, 10.0, 30.0), arm_k: (25.0, -10.0, 30.0)})
    settle = ik_to_fk(M(P.READY, ikL=(-0.14, 0.15 if k == "L" else 0.0, 0.0, -8.0), ikR=(0.14, 0.15 if k == "R" else 0.0, 0.0, 8.0),
                        hips=(0.0, -0.06, 0.08)), k)
    # 시작: 차는 발이 몸 뒤에 딛고(이지 직전), 디딤발 착지 직전
    # 디딤발은 공중(마지막 보폭)에서 백스윙 때 한 번 착지한다 — 바닥을 끌며 옮기면 착지가 여러 번 잡힌다
    start = ik_to_fk(M(P.READY, **{"ik" + pl: (plant_x, plant_z - 0.25, 0.08, -s * 8.0), "ik" + k: (s * 0.12, plant_z - 0.40, 0.0, s * 8.0)},
                       hips=(0.0, -0.06, plant_z - 0.30), spine=(10.0, 0.0, 0.0)), k)
    wf = max(2, c - SWING_FRAMES)
    fe = min(frames - 4, c + 5)
    keys = [(0, start), (wf, windup), (c, contact), (fe, end), (frames, settle)]
    pre, runup, root = 0, None, (0.0, 0.35)
    if ks["n"] >= 2:
        pre, run_kp, runup, root, start = _runup(ks, s, k, pl, plant_x, plant_z)
        keys[0] = (0, start)
    else:
        run_kp = None
    base_tl = P.timeline(keys)
    leg0 = start["leg" + k]
    # 엉덩이 굴곡 각속도 피크를 백스윙 최대 → 접촉 구간의 FLEX_PEAK 지점에
    x_peak = FLEX_PEAK
    hip_ease = _fast_start(x_peak)
    vk = (knee_c - knee_w) * 3.0 / (c - wf)  # 접촉 순간 무릎 각속도(°/프레임) — 무릎 신전이 접촉에서 가장 빠르다

    def leg(f):
        """차는 다리 해석 곡선: 이지 → 백스윙(smooth) → 접촉(엉덩이 앞쪽 피크·무릎 신전 가속) → 팔로스루(무릎 속도 이어받기)."""
        a0 = leg0
        if f <= wf:
            x = smooth(f / wf)
            return (a0[0] + (flex_w - a0[0]) * x, a0[1] + (knee_w - a0[1]) * x, a0[2] + (30.0 - a0[2]) * x,
                    a0[3] + (abd - a0[3]) * x, a0[4] + (rot * 0.6 - a0[4]) * x)
        if f <= c:
            x = (f - wf) / (c - wf)
            return (flex_w + (flex_c - flex_w) * hip_ease(x), knee_w + (knee_c - knee_w) * _ease_in(x),
                    30.0 + (ankle_c - 30.0) * smooth(x), abd, rot * 0.6 + rot * 0.4 * smooth(x))
        x = (f - c) / (fe - c)
        x = min(1.0, x)
        kn = GT._herm(knee_c, 15.0, vk * (fe - c), 0.0, x)
        return (flex_c + (flex_e - flex_c) * smooth(x), max(5.0, kn), ankle_c + (35.0 - ankle_c) * smooth(x), abd,
                rot - rot * 0.5 * smooth(x))

    def keypose(f):
        if run_kp is not None and f < pre:
            return run_kp(f)
        g = f - pre
        kp = base_tl(g)
        if g <= fe:
            kp = dict(kp)
            kp["leg" + k] = leg(g)
        return kp

    part = "RightFoot" if s > 0 else "LeftFoot"
    approach = K["approach"] if s > 0 else (-K["approach"][1], -K["approach"][0])
    tags = dict(approach=list(approach), ballHeight=list(K["height"]), ballSpeed=list(K["speed"]), root=list(root), kind=kind,
                backswing=K["back"], contact={"frame": c + pre, "part": part, "rule": rule}, runupFrames=pre)
    if style:
        tags["kickStyle"] = style if style != "k8punt" else "k8"
    if runup:
        tags["runup"] = runup
    tags.update(extra)
    return Clip(id_, group, frames + pre, lambda tt: P.grounded(keypose(tt * 30.0)), **tags)


def _runup(ks, s, k, pl, plant_x, plant_z):
    """도움닫기 n걸음(디딤발 착지 포함)을 보법 반주기로 앞에 붙인다. 마지막 반주기는 차는 발 접지 → 디딤발 착지로 끝난다.
    → (프레임 수, f → 키포즈, runup 태그, root 태그, 킥 시작 키포즈(차는 다리 FK))."""
    n, a, v = ks["n"], ks["a"], ks["v"]
    g = GT.base_at(v)
    g["dir_scale"] = 1.0
    G = GT._Gait(g, s * a)
    half = 30.0 * g["frames"] / 60.0
    kick_left = k == "L"
    phases = []
    for i in range(n):
        last_k = (n - 1 - i) % 2 == 0  # 마지막 반주기 = 차는 발 접지
        phases.append(0.0 if (last_k == kick_left) else 0.5)
    pre = int(round(n * half))
    end_ph = (phases[-1] + 0.5) % 1.0
    kp_end = GT.gait_keypose(G, end_ph)
    dx, dz = plant_x - kp_end["ik" + pl][0], plant_z - kp_end["ik" + pl][1]

    def shift(kp):
        out = dict(kp)
        hx, hy, hz = kp["hips"]
        out["hips"] = (hx + dx, hy, hz + dz)
        for sd in ("L", "R"):
            if "ik" + sd in kp:
                x, z, lift, yaw = kp["ik" + sd]
                out["ik" + sd] = (x + dx, z + dz, lift, yaw)
        return out

    def run_kp(f):
        i = min(n - 1, int(f / half))
        w = (f - i * half) / half
        return shift(GT.gait_keypose(G, (phases[i] + 0.5 * w) % 1.0))
    start = ik_to_fk(shift(kp_end), k)
    rad = math.radians(s * a)
    dist = v * pre / 30.0
    root = (round(math.sin(rad) * dist, 3), round(math.cos(rad) * dist + 0.35, 3))
    return pre, run_kp, {"dir": round(s * a, 1), "speed": v, "steps": n}, root, start


def clips():
    out = []
    for s, n in ((1, "r"), (-1, "l")):
        out.append(kick(f"pass_inside_{n}", s, "inside", "pass"))
    out.append(kick("pass_outside_r", 1, "outside", "pass", "k4"))
    out.append(kick("pass_lofted_r", 1, "lofted", "pass", "k5"))
    out.append(kick("onetouch_pass_r", 1, "onetouch_pass", "pass", "k6"))
    out.append(kick("cross_r", 1, "cross", "cross"))
    out.append(kick("cross_l", -1, "cross", "cross"))
    out.append(kick("shot_instep_r", 1, "instep", "shot"))
    out.append(kick("shot_instep_l", -1, "instep", "shot"))
    out.append(kick("shot_curl_r", 1, "curl", "shot", "k3"))
    out.append(kick("shot_volley_r", 1, "volley", "shot"))
    out.append(kick("onetouch_shot_r", 1, "onetouch_shot", "shot", "k6"))
    out.append(kick("shot_power_r", 1, "power", "shot", "k1"))
    out.append(kick("shot_inside_r", 1, "finish", "shot", "k2"))
    out.append(kick("clearance_r", 1, "lofted", "clearance", "k7"))
    out.append(kick("gk_goalkick_r", 1, "lofted", "keeper", "k8"))
    out.append(kick("gk_punt_r", 1, "punt", "keeper", "k8punt"))
    return out
