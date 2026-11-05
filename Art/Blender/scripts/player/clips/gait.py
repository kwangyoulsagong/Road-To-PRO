"""로코모션 클립 (specs/043 FR-31·41, specs/055 FR-01~44·50).

제자리 클립(루트 고정). 선수가 속도 v로 방향 d(정면 기준 각도)로 움직일 때, 접지 중인 발은 몸 기준으로 −v·d 속도로
정확히 뒤로 간다 → 시뮬이 같은 속도로 몸을 옮기면 접지 발은 월드에서 멈춰 있다(미끄러짐 0, 구성상).
Unity는 재생률 = 시뮬 속도 / 클립 speed로 맞춘다(LocomotionBlend).

055 재작성 — 형태 기준은 Data/anim/055-gait-standards.json, 지표 정의는 player/gait_metrics.py:
- 시간 구조: 주기 프레임 = 걸음 빈도 표(FR-01). 30 fps 정수라 빈도가 양자화된다 → 보폭 = 속도 / 빈도.
- 골반 높이(FR-03): 달리기는 스프링-질량 — 접지 중 반사인으로 내려가 접지 중간 최저, 공중 구간 반사인으로 올라가 공중 중간 최고.
  두 구간 경계에서 수직 속도가 이어지게 진폭을 시간 비례로 나눈다(D/ts = R/tf). 걷기는 역진자(접지 중간 최고).
  접지 순간 무릎 굴곡(knee_td)이 되도록 기준 높이를 계산한다.
- 접지 발: 볼(발가락 뿌리)이 접지 내내 한 점(미끄러짐 0). 앞꿈치 착지 기울기(p_td) → 중간(p_mid) → 발끝 떼기(toe_off)를 볼 축으로.
- 스윙 발(정면·대각 앞): 엉덩이 관절 기준 허벅지 각 θ·무릎 굴곡 κ의 시간 곡선(FK) — 뒤꿈치 접기(κ 피크 r≈0.4) → 무릎 앞으로(θ 피크 r≈0.67)
  → 정강이 펴기. 양 끝 각속도는 접지 기하에서 이어 받는다(Hermite) → 발 속도 연속. 옆·뒤 방향은 043 발 경로(Hermite) 유지.
- 몸통 경사 표, 머리는 몸통과 한 줄(목 보상 없음, 시선만 약간 아래), 골반 요·기울기와 흉곽 반대 회전, 팔은 반대 다리 엉덩이 굴곡 피크에 맞춘
  어깨 스윙 + 위상 팔꿈치(앞에서 더 굽고 뒤에서 편다, 실제 굴곡 각 — poses 4값 팔 키).
보법(속도 m/s 정면 기준): walk 1.4, jog 3.4, run 5.4, sprint 7.6. 방향별 속도 배율: 정면 1, 대각 앞 0.85, 옆 0.6, 대각 뒤 0.5, 뒤 0.45.
태그: speed(m/s), dir(°, 0 정면·90 오른쪽), stance[발] = [시작 위상, 끝 위상] (0~1), cycle 프레임, style·variant(055 원형).
"""
import math

from mathutils import Vector

from .. import skeleton as S
from ..pose import X, Y, Z, q
from . import poses as P
from .base import Clip, smoother

# knee_pk·hip_pk = 스윙 진행에서 무릎·허벅지 피크 위치(없으면 SW_KNEE_PEAK·SW_HIP_PEAK) — 걷기는 무릎이 스윙 초반에 가장 굽는다.
# 각도는 기하 각(°). sw_hip = 스윙 엉덩이 최대 굴곡(몸통선 기준), lean = 몸통 경사(골반→목 선), elbow = 팔꿈치 실제 굴곡 평균.
GAITS = {
    "walk": dict(v=1.4, frames=32, duty=0.60, bob=0.045, land=0.30, knee_td=4.0, knee_mid=16.0, knee_to=8.0, p_td=0.0, p_mid=0.0, toe_off=45.0,
                 sw_knee=72.0, sw_hip=32.0, lean=2.0, arm_rom=32.0, arm_c=-4.0, elbow=20.0, elbow_rng=14.0, abd=10.0,
                 yaw=5.0, thx=10.0, obl=4.0, foot_x=0.105, clear=0.07, gaze=4.0, knee_pk=0.30, hip_pk=0.75, dorsi=20.0, blend_td=0.25, heel_off_u=0.45),
    "jog": dict(v=3.4, frames=22, duty=0.35, bob=0.060, land=0.29, knee_td=16.0, knee_mid=40.0, knee_to=14.0, p_td=12.0, p_mid=0.0, toe_off=65.0,
                sw_knee=98.0, sw_hip=42.0, lean=5.5, arm_rom=60.0, arm_c=-10.0, elbow=90.0, elbow_rng=30.0, abd=10.0,
                yaw=8.0, thx=11.0, obl=4.5, foot_x=0.095, clear=0.18, gaze=5.0, ball_lift=0.28),
    "run": dict(v=5.4, frames=19, duty=0.27, bob=0.061, land=0.29, knee_td=14.0, knee_mid=40.0, knee_to=15.0, p_td=8.0, p_mid=0.0, toe_off=80.0,
                sw_knee=112.0, sw_hip=52.0, lean=7.0, arm_rom=80.0, arm_c=-10.0, elbow=90.0, elbow_rng=36.0, abd=10.0,
                yaw=9.0, thx=12.0, obl=4.5, foot_x=0.090, clear=0.25, gaze=6.0),
    "sprint": dict(v=7.6, frames=15, duty=0.225, bob=0.052, land=0.32, knee_td=13.0, knee_mid=36.0, knee_to=16.0, p_td=18.0, p_mid=0.0, toe_off=55.0,
                   sw_knee=120.0, sw_hip=62.0, lean=8.5, arm_rom=110.0, arm_c=-6.0, elbow=90.0, elbow_rng=40.0, abd=10.0,
                   yaw=10.0, thx=12.0, obl=4.5, foot_x=0.080, clear=0.35, gaze=6.0),
}
DIRS = (("f", 0), ("fr", 45), ("r", 90), ("br", 135), ("b", 180), ("bl", 225), ("l", 270), ("fl", 315))
DIR_SCALE = {0: 1.0, 45: 0.85, 315: 0.85, 90: 0.6, 270: 0.6, 135: 0.5, 225: 0.5, 180: 0.45}
UP = Vector((0.0, 1.0, 0.0))
HEEL = Vector((0.0, -0.075, -0.05))  # 발목 → 뒤꿈치 바닥 점 (뒤꿈치 착지 축)
SW_KNEE_PEAK = 0.50   # 스윙 진행 r에서 무릎 굴곡 피크(뒤꿈치 접기)
KNEE_MIN = 12.0        # 접지 중 무릎 최소 굴곡 (FR-11 이지 ≥ 10°) — 골반 안전 제한
SW_BLEND_TO = 0.10     # 스윙 시작 이 구간을 발 경로와 섞는다
SW_BALL_LIFT = 0.40    # 스윙 시작 이 구간은 볼이 들림 경로를 따른다
SW_BLEND_TD = 0.30     # 스윙 끝 이 구간을 발 경로와 섞는다 (수평만 — 높이는 FK)
TO_TANGENT = 0.3       # 이지 직후 각속도 이어받기 비율
TD_TANGENT = 0.5       # 착지 직전 각속도 이어받기 비율 — 1이면 착지 전 무릎이 과신전된다(측정: 3.6°)
SW_HIP_PEAK = 0.58    # 허벅지 앞 피크(무릎 들기)


def _bump(r, peak):
    """[0,1] → [0,1], 양 끝 0·기울기 0, peak에서 1."""
    if r <= 0.0 or r >= 1.0:
        return 0.0
    e = math.log(0.5) / math.log(peak)
    return math.sin(math.pi * r ** e) ** 2


def _peak_curve(a, peak, b, rp, r):
    """a → peak(r = rp) → b. 양 끝·피크에서 기울기 0 (경계 속도 연속은 스윙 양 끝 발 경로 섞기가 맡는다)."""
    if r < rp:
        return a + (peak - a) * smoother(r / rp)
    return peak + (b - peak) * smoother((r - rp) / (1.0 - rp))


def _herm(p0, p1, m0, m1, r):
    r2, r3 = r * r, r * r * r
    return (2 * r3 - 3 * r2 + 1) * p0 + (r3 - 2 * r2 + r) * m0 + (-2 * r3 + 3 * r2) * p1 + (r3 - r2) * m1


class _Gait:
    """보법 하나 × 방향 하나의 고정 값(뼈대 의존 — 클립 생성 때 만든다)."""

    def __init__(self, g, dir_deg):
        self.g = g
        self.dir = dir_deg
        self.v = g["v"] * g.get("dir_scale", DIR_SCALE.get(dir_deg % 360, 1.0))  # dir_scale: 킥 도움닫기처럼 임의 각도
        self.T = g["frames"] / 30.0
        self.duty = g["duty"]
        r = math.radians(dir_deg)
        self.d = Vector((math.sin(r), 0.0, math.cos(r)))
        self.fwd = max(0.0, self.d.z)
        self.fk_swing = self.d.z >= 0.7
        self.ls = self.v * self.duty * self.T          # 접지 동안 발이 몸 기준으로 뒤로 가는 거리
        self.land = g["land"] * (0.4 + 0.6 * self.fwd)  # 착지 거리(발목, 골반 앞)
        self.a, self.b = S.L_THIGH, S.L_SHIN
        self.fb = Vector(S.HEAD["LeftToes"]) - Vector(S.HEAD["LeftFoot"])  # 발목 → 볼 (좌우 대칭이라 x = 0)
        self.hips_rest = Vector(S.HEAD["Hips"])
        ts, tf = self.duty * self.T, max(0.0, 0.5 - self.duty) * self.T
        if self.duty < 0.5:  # 접지·공중 경계에서 수직 속도가 이어지게: D/ts = R/tf
            self.D = g["bob"] * ts / (ts + tf)
            self.R = g["bob"] * tf / (ts + tf)
        else:
            self.D = self.R = 0.0
        self.reach = self._leg_len(KNEE_MIN)
        self.left_peak = 0.0
        self.hy0 = 0.0
        self._hpk = None
        self.left_peak = (self._thigh_peak() + 0.5) % 1.0
        self.hy0 = self._base_height()
        if self.duty >= 0.5:
            self.hy0 -= g["bob"] * 0.5  # 걷기: 이 높이가 이중 지지(최저) 근처
        self.hy0 = min(self.hy0, self._reach_height()) - g.get("drop", 0.0)  # drop: 드리블·감속처럼 무게중심을 낮춘 보법
        self._hpk = None

    # ---- 접지 발

    def foot_pitch_stance(self, u):
        g = self.g
        p = g["p_td"] + (g["p_mid"] - g["p_td"]) * smoother(u / 0.4)
        hu = g.get("heel_off_u", 0.55)  # 뒤꿈치 들기 시작 (걷기는 이르다)
        if u > hu:
            p += (g["toe_off"] - g["p_mid"]) * smoother((u - hu) / (1.0 - hu))
        return p

    def _ankle_from_ball(self, ball, pitch, yaw):
        """발 기울기 pitch(+ = 뒤꿈치 듦: 볼 축, − = 발끝 듦: 뒤꿈치 축) → 발목 위치. 축 점은 접지 동안 몸 기준 속도 −v로만 움직인다(미끄러짐 0)."""
        R = q(Y, yaw)
        if pitch >= 0.0:
            return ball - R @ q(X, pitch) @ self.fb
        flat = ball - R @ self.fb
        heel = flat + R @ HEEL
        return heel - R @ q(X, pitch) @ HEEL

    def ball_td(self):
        """접지 시작 순간 볼의 진행 방향 위치(골반 기준): 발목이 land에 오도록."""
        fbp = q(X, self.g["p_td"]) @ self.fb
        return self.land + fbp.z

    def _ankle_ball(self, side, u):
        """접지 중 볼 위치(고정점)."""
        yaw = -6.0 if side < 0 else 6.0
        s = self.ball_td() - self.ls * u
        return Vector((side * self._foot_x() + (q(Y, yaw) @ self.fb).x, S.ANKLE_Y + self.fb.y, self.hips_rest.z)) + self.d * s

    def stance_ankle(self, side, u):
        yaw = -6.0 if side < 0 else 6.0
        s = self.ball_td() - self.ls * u
        ball = Vector((side * self._foot_x() + (q(Y, yaw) @ self.fb).x, S.ANKLE_Y + self.fb.y, self.hips_rest.z)) + self.d * s
        return self._ankle_from_ball(ball, self.foot_pitch_stance(u), yaw), yaw

    def _foot_x(self):
        x = self.g["foot_x"]
        if abs(self.d.x) > 0.3:
            x += 0.11  # 대각·옆: 다리끼리 닿지 않게 (056 관통 검사: jog_fr 허벅지·정강이 × 정강이)
            # 056: 굵어진 MPFB 다리가 교차하지 않게 — 옆걸음은 발이 서로 지나가지 않는 사이드 셔플(기본 폭 + 반 보폭).
            if abs(self.d.x) > 0.9:  # 순수 옆걸음(r·l)만 — 대각선까지 넓히면 이웃 방향 블렌드에서 발 위치가 크게 달라 팝이 난다
                x += 0.5 * self.ls * abs(self.d.x) + 0.03
        return x

    def _perp(self, v):
        """진행 방향에 수직인 수평 성분."""
        h = Vector((v.x, 0.0, v.z))
        return h - self.d * h.dot(self.d)

    # ---- 골반: 명시 곡선(FR-03) + 접지 발 도달 보장

    def _leg_len(self, k):
        return math.sqrt(self.a ** 2 + self.b ** 2 + 2 * self.a * self.b * math.cos(math.radians(k)))

    def _yaw_roll(self, ph):
        g = self.g
        # 요: 골반은 다리를 따라 돈다 — 왼 허벅지가 가장 앞일 때 왼 엉덩이가 가장 앞(+요 = 왼 엉덩이 앞으로)
        yaw = g["yaw"] * math.cos(2 * math.pi * (ph - self.left_peak)) * self.fwd
        roll = g["obl"] * math.cos(2 * math.pi * (ph - self.duty / 2))
        return yaw, roll

    def _base_height(self):
        """접지 시작 순간 무릎 굴곡 = knee_td가 되는 골반 기준 높이 (Hips 머리 y 변화량)."""
        ank, _ = self.stance_ankle(-1, 0.0)
        yaw, roll = self._yaw_roll(0.0)
        hip0 = self.hip_joint("Left", 0.0, yaw, roll)
        dx = math.hypot(ank.x - hip0.x, ank.z - hip0.z)
        L = self._leg_len(self.g["knee_td"])
        return ank.y + math.sqrt(max(1e-6, L * L - dx * dx)) - hip0.y

    def _curve(self, ph):
        """명시 골반 곡선 − hy0 (기준 높이 대비)."""
        if self.duty < 0.5:
            ql = ph % 1.0
            qr = (ph - 0.5) % 1.0
            if ql < self.duty:
                return -self.D * math.sin(math.pi * ql / self.duty)
            if qr < self.duty:
                return -self.D * math.sin(math.pi * qr / self.duty)
            w = ((ph - self.duty) % 0.5) / (0.5 - self.duty)
            return self.R * math.sin(math.pi * w)
        return 0.5 * self.g["bob"] * (1.0 + math.cos(4 * math.pi * (ph - self.duty / 2)))

    def _reach_height(self):
        """두 발 모든 접지 순간에서 무릎 ≥ KNEE_MIN이 되는 기준 높이 상한 — 곡선 전체를 한 번에 낮춰 안전 제한이 걸리지 않게
        (제한이 순간적으로 걸리면 골반이 튀고 스윙 발 IK가 끌려간다: 옆걸음 측정 발목 가속 2,000 m/s²)."""
        best = 1e9
        for side, start, n in ((-1, 0.0, "Left"), (1, 0.5, "Right")):
            for i in range(41):
                u = i / 40.0
                ph = (start + u * self.duty) % 1.0
                yaw, roll = self._yaw_roll(ph)
                ank, _ = self.stance_ankle(side, u)
                hip0 = self.hip_joint(n, 0.0, yaw, roll)
                dx = math.hypot(ank.x - hip0.x, ank.z - hip0.z)
                allowed = math.sqrt(max(0.0, self.reach ** 2 - dx ** 2)) + ank.y - hip0.y
                best = min(best, allowed - self._curve(ph))
        return best

    def pelvis(self, ph):
        g = self.g
        yaw, roll = self._yaw_roll(ph)
        if self.duty < 0.5:
            ql = ph % 1.0
            qr = (ph - 0.5) % 1.0
            if ql < self.duty:
                hy = self.hy0 - self.D * math.sin(math.pi * ql / self.duty)
            elif qr < self.duty:
                hy = self.hy0 - self.D * math.sin(math.pi * qr / self.duty)
            else:
                w = ((ph - self.duty) % 0.5) / (0.5 - self.duty)
                hy = self.hy0 + self.R * math.sin(math.pi * w)
        else:
            hy = self.hy0 + 0.5 * g["bob"] * (1.0 + math.cos(4 * math.pi * (ph - self.duty / 2)))
        # 안전: 접지 발 무릎 굴곡 ≥ KNEE_MIN (스윙 발은 제외 — 043은 스윙 초반 발까지 넣어 골반이 크게 꺼졌다)
        for side, start, n in ((-1, 0.0, "Left"), (1, 0.5, "Right")):
            qq = (ph - start) % 1.0
            if qq > self.duty + 1e-9:  # 이지 순간(qq = duty)도 포함 — 스윙 시작 FK가 같은 골반을 쓴다
                continue
            ank, _ = self.stance_ankle(side, min(1.0, qq / self.duty))
            hip = self.hip_joint(n, hy, yaw, roll)
            dx = math.hypot(ank.x - hip.x, ank.z - hip.z)
            max_y = math.sqrt(max(0.0, self.reach ** 2 - dx ** 2)) + ank.y
            hy = min(hy, hy + (max_y - hip.y))
        return hy, yaw, roll

    def hip_joint(self, n, hy, yaw, roll):
        rot = q(Y, yaw) @ q(X, self.g.get("pelvis_tilt", 0.0)) @ q(Z, -roll)
        hh = self.hips_rest + Vector((0.0, hy, 0.0))
        return hh + rot @ (Vector(S.HEAD[n + "UpperLeg"]) - self.hips_rest)

    # ---- 스윙 (FK)

    def _angles(self, hip, ank):
        """엉덩이 관절 → 발목: (허벅지 각 θ, 무릎 굴곡 κ) 시상면(위·d). θ = 아래에서 d 쪽으로 + (°)."""
        dv = ank - hip
        f, y = dv.dot(self.d), dv.y
        L = min(math.hypot(f, y), self.a + self.b - 1e-6)
        ang = math.atan2(f, -y)
        c = max(-1.0, min(1.0, (self.a ** 2 + L * L - self.b ** 2) / (2 * self.a * L)))
        beta = math.acos(c)
        ck = max(-1.0, min(1.0, (self.a ** 2 + self.b ** 2 - L * L) / (2 * self.a * self.b)))
        return math.degrees(ang + beta), 180.0 - math.degrees(math.acos(ck))

    def _stance_angles(self, side, n, start, u):
        ph = (start + u * self.duty) % 1.0
        hy, yaw, roll = self.pelvis(ph)
        hip = self.hip_joint(n, hy, yaw, roll)
        ank, _ = self.stance_ankle(side, u)
        return self._angles(hip, ank)

    def swing_ankle(self, side, n, start, r, hip):
        """스윙 진행 r(0~1) → 발목 위치. 양 끝은 접지 기하와 위치·각속도가 이어진다."""
        t0, k0 = self._stance_angles(side, n, start, 1.0)
        t1, k1 = self._stance_angles(side, n, start, 0.0)
        g = self.g
        lean = g["lean"] * self.fwd
        # 허벅지: 피크 뒤 착지까지는 착지 순간 허벅지 각속도(뒤로 돎)에 맞춘 Hermite — 발이 몸 기준 −v로 들어와 착지 수평 속도가 이어진다
        hpk = g.get("hip_pk", SW_HIP_PEAK)
        eps = 1e-3
        t1b, _ = self._stance_angles(side, n, start, eps)
        th_pk = g["sw_hip"] - lean
        m1 = (t1b - t1) / eps * (1.0 - self.duty) / self.duty * (1.0 - hpk)  # 접지 u 단위 → 피크 뒤 구간 단위
        m1 = max(-2.5 * abs(th_pk - t1), min(0.0, m1))
        th = _peak_curve(t0, th_pk, t1, hpk, r) if r < hpk else _herm(th_pk, t1, 0.0, m1, (r - hpk) / (1.0 - hpk))
        kn = _peak_curve(k0, g["sw_knee"], k1, g.get("knee_pk", SW_KNEE_PEAK), r)
        tr, kr = math.radians(th), math.radians(th - kn)
        knee = hip + (self.d * math.sin(tr) - UP * math.cos(tr)) * self.a
        ank = knee + (self.d * math.sin(kr) - UP * math.cos(kr)) * self.b
        # 진행 방향에 수직인 성분은 이지 → 착지 발 위치 사이를 부드럽게 (FK 평면은 엉덩이 관절을 지나므로 그대로 두면 경계에서 튄다)
        a_to, _ = self.stance_ankle(side, 1.0)
        a_td, _ = self.stance_ankle(side, 0.0)
        want = self._perp(a_to).lerp(self._perp(a_td), smoother(r))
        ank = ank - self._perp(ank) + want
        # 양 끝: 접지 속도와 정확히 맞는 발 경로(Hermite)로 섞어 수평 발 속도를 잇는다 (착지 직전 발이 월드에서 앞으로 미끄러지지 않게)
        btd = self.g.get("blend_td", SW_BLEND_TD)
        w = max(1.0 - smoother(r / SW_BLEND_TO), smoother((r - (1.0 - btd)) / btd))
        if w > 0.0:
            path, _ = self.swing_ankle_path(side, start, r)
            h = ank.lerp(path, w)
            # 착지 쪽은 높이도 섞되 늦게(경로의 가파른 내림) — FK 높이만 쓰면 볼이 착지 전에 바닥 3 cm 안으로 내려와 끌린다
            ank = Vector((h.x, ank.y + (path.y - ank.y) * smoother((r - (1.0 - btd)) / btd), h.z)) if r > 0.5 else h
        # 다리 길이 상한: 발 경로가 몸 뒤·앞으로 더 나가도 무릎이 KNEE_MIN보다 펴지지 않게 엉덩이 쪽으로 당긴다
        dv = ank - hip
        if dv.length > self.reach:
            ank = hip + dv * (self.reach / dv.length)
        return ank

    def _ik_knee(self, hip, ank):
        """poses.leg_ik와 같은 2본 IK의 무릎 위치 (극 = 정면 Z)."""
        dv = ank - hip
        L = min(dv.length, (self.a + self.b) * 0.9995)
        dn = dv.normalized()
        c = max(-1.0, min(1.0, (self.a ** 2 + L * L - self.b ** 2) / (2 * self.a * L)))
        pole = Z - dn * Z.dot(dn)
        pole = pole.normalized() if pole.length > 1e-6 else Z
        return hip + (dn * c + pole * math.sqrt(max(0.0, 1 - c * c))) * self.a

    def _thigh_peak(self):
        """요 없이 본 오른 다리 허벅지 피크 위상(골반 요 위상 기준). hip_peak_phase와 같지만 요·기준 높이 전이라 근사."""
        save = self.g["yaw"]
        self.g = dict(self.g, yaw=0.0)
        self._hpk = None
        ph = self.hip_peak_phase()
        self.g = dict(self.g, yaw=save)
        self._hpk = None
        return ph

    def hip_peak_phase(self, n="Right"):
        """오른 다리 엉덩이 굴곡 피크 위상 — 팔 반대 위상 기준(FR-30). gait_metrics와 같은 정의(전체 FK, 몸통선 기준·몸 정면 시상면)로
        구해 옆·뒤 방향 클립에서도 맞는다. 한 번 계산해 둔다."""
        cache = getattr(self, "_hpk", None)
        if cache is not None and n in cache:
            return cache[n]
        from ..pose import fk
        best, bph = -1e9, 0.0
        for i in range(120):
            ph = i / 120.0
            _, h, _ = fk(P.to_pose(gait_keypose(self, ph, arms=False)))
            tv = h[n + "LowerLeg"] - h[n + "UpperLeg"]
            tr = h["Neck"] - h["Hips"]
            th = math.degrees(math.atan2(tv.z, -tv.y) - math.atan2(-tr.z, tr.y))
            if th > best:
                best, bph = th, ph
        self._hpk = dict(cache or {}, **{n: bph})
        return bph

    def swing_pitch(self, r):
        g = self.g
        # 발끝 떼기 기울기는 발목이 충분히 오른 뒤에 푼다(빨리 풀면 볼이 바닥 근처에서 앞으로 끌린다 — 043 lowToeSpeed 기준)
        rel = smoother((r - 0.08) / 0.45)
        # 스윙 중 발끝 들기(배측굴곡): 착지 직전까지 유지해 볼이 바닥 3 cm 안에서 끌리지 않게, 마지막 12%에서 착지 기울기로
        up = g.get("dorsi", 12.0) * smoother(r / 0.4) * (1.0 - smoother((r - 0.88) / 0.12))
        p = g["toe_off"] * (1.0 - rel) - up
        return p + g["p_td"] * smoother((r - 0.65) / 0.35)

    # ---- 옆·뒤 스윙 (043 Hermite 발 경로)

    def swing_ankle_path(self, side, start, r):
        yaw = -6.0 if side < 0 else 6.0
        a0, _ = self.stance_ankle(side, 1.0)
        a1, _ = self.stance_ankle(side, 0.0)
        m = -self.ls * (1.0 - self.duty) / self.duty
        h00, h10, h01, h11 = 2 * r ** 3 - 3 * r ** 2 + 1, r ** 3 - 2 * r ** 2 + r, -2 * r ** 3 + 3 * r ** 2, r ** 3 - r ** 2
        s0, s1 = a0.dot(self.d), a1.dot(self.d)
        sd = h00 * s0 + h10 * m + h01 * s1 + h11 * m
        p = self._perp(a0).lerp(self._perp(a1), smoother(r)) + self.d * sd
        # 들기: 양 끝이 가파른 곡선(sin^0.5, 043과 같음) — 발이 낮게 깔린 채 수평으로 움직이는 구간을 없앤다
        lift = self.g["clear"] * math.sqrt(max(0.0, math.sin(math.pi * r)))
        p.y = a0.y + (a1.y - a0.y) * smoother(r) + lift
        return p, yaw


_ORDER = ("walk", "jog", "run", "sprint")
_WALK_ONLY = ("knee_pk", "hip_pk", "dorsi", "blend_td", "heel_off_u")


def base_at(v):
    """속도 v(m/s, 정면)의 보법 파라미터 — 이웃 보법 사이 선형 보간 (055 축구 보법·원형의 출발점). 걷기 전용 키는 jog 이상이면 뺀다."""
    pts = [(GAITS[k]["v"], k) for k in _ORDER]
    if v <= pts[0][0]:
        return dict(GAITS["walk"], v=v)
    for (v0, a), (v1, b) in zip(pts, pts[1:]):
        if v <= v1:
            t = (v - v0) / (v1 - v0)
            A, B = GAITS[a], GAITS[b]
            out = {k: A[k] + (B[k] - A[k]) * t for k in B if k in A and isinstance(B[k], (int, float))}
            if t < 0.5 and a == "walk":
                out.update({k: A[k] for k in _WALK_ONLY if k in A})
            out["v"] = v
            return out
    return dict(GAITS["sprint"], v=v)


def _elbow(g, ph, peak):
    return g["elbow"] + 0.5 * g["elbow_rng"] * math.cos(2 * math.pi * (ph - peak))


def gait_keypose(G, phase, arms=True):
    g = G.g
    k = dict(P.STAND)
    hy, hyaw, roll = G.pelvis(phase)
    feet = {}
    for s, side, start, n in (("L", -1, 0.0, "Left"), ("R", 1, 0.5, "Right")):
        qq = (phase - start) % 1.0
        yaw = -6.0 if side < 0 else 6.0
        if qq < G.duty:
            u = qq / G.duty
            ank, yaw = G.stance_ankle(side, u)
            pitch = G.foot_pitch_stance(u)
            stance = True
        else:
            r = (qq - G.duty) / (1.0 - G.duty)
            if G.fk_swing:
                hip = G.hip_joint(n, hy, hyaw, roll)
                ank = G.swing_ankle(side, n, start, r, hip)
            else:
                ank, yaw = G.swing_ankle_path(side, start, r)
            pitch = G.swing_pitch(r)
            if r < SW_BALL_LIFT:
                # 스윙 초반: 볼(발가락 뿌리)이 들림 경로를 따라 바로 올라가게 발목을 볼에서 역산 (043처럼). 발끝 떼기 기울기를 유지한 채
                # 발목만 경로를 따르면 볼이 바닥 3 cm 안에 오래 남아, 다른 보법과 섞일 때 끌린다(043 블렌드 미끄러짐 검사).
                a0, _ = G.stance_ankle(side, 1.0)
                ball0 = G._ankle_ball(side, 1.0)
                path, _ = G.swing_ankle_path(side, start, r)
                ball = path + (ball0 - a0)
                # 볼 들림 높이는 따로(ball_lift): 043 블렌드 검사 — walk 접지와 jog 스윙 초반이 겹칠 때 섞인 볼이 3 cm 위로 빨리 올라가야 한다
                ball.y = ball0.y + G.g.get("ball_lift", G.g["clear"]) * math.sqrt(max(0.0, math.sin(math.pi * r)))
                ab = G._ankle_from_ball(ball, pitch, yaw)
                ank = ank.lerp(ab, 1.0 - smoother(r / SW_BALL_LIFT))
            stance = False
        feet[s] = (ank, yaw, pitch, stance, n)
    k["hips"] = (0.0, hy, 0.0)
    tilt = g.get("pelvis_tilt", 0.0)  # 출발 가속: 몸통 숙임 일부를 골반 앞 기울기로 (허리만 꺾이지 않게)
    k["hrot"] = (tilt, hyaw, roll)
    c = math.cos(2 * math.pi * phase)
    fwd = G.fwd
    # 몸통: 경사(키는 Spine·Chest·UpperChest에 나뉘고 Hips→Spine 분절은 그대로라 기하 경사보다 크게 준다)
    lean = g["lean"] * G.d.z * (1.0 if G.d.z > 0 else 0.4)
    side_lean = g["lean"] * 0.5 * G.d.x
    thorax_abs = -(g["thx"] - g["yaw"]) * math.cos(2 * math.pi * (phase - G.left_peak)) * fwd  # 흉곽 절대 요(골반과 반대 위상)
    sy = thorax_abs - hyaw
    k["spine"] = ((lean - tilt) * 1.18, sy, side_lean - roll * 0.95)
    # 머리: 몸통과 한 줄, 시선만 약간 아래(gaze) — 043의 목 보상(−경사 × 0.8) 없음. 요는 흉곽 회전을 되돌려 정면 유지
    k["head"] = (-(max(0.0, lean - g["gaze"])) * 0.6, -thorax_abs * 0.8)
    # 팔: 반대 다리 엉덩이 굴곡 피크에 어깨 굴곡 피크를 맞춘다
    if not arms:
        for s, (ank, yaw, pitch, stance, n) in feet.items():
            k["ik" + s] = (ank.x, ank.z, ank.y - S.ANKLE_Y, yaw)
            k["foot" + s] = (pitch, 0.0)
        return k
    # 오른 다리 엉덩이 굴곡 피크 → 왼팔, 왼 다리 → 오른팔 (옆걸음은 두 다리가 대칭이 아니라 따로 구한다)
    amp = 0.5 * g["arm_rom"] * (0.5 + 0.5 * fwd) if G.d.z >= 0 else 0.5 * g["arm_rom"] * 0.5
    # 옆·뒤 방향은 팔 스윙이 작아 손이 허벅지(반바지) 옆에 머문다 → 벌림을 키운다 (056 관통 검사: 옆걸음 손 × 반바지)
    down = 45.0 - g["abd"] - 15.0 * (1.0 - fwd)
    for s, pk in (("L", G.hip_peak_phase("Right")), ("R", G.hip_peak_phase("Left"))):
        flex = g["arm_c"] * max(0.0, G.d.z) + amp * math.cos(2 * math.pi * (phase - pk))
        # 옆·뒤 방향 걷기: 팔꿈치를 더 굽혀 손을 허벅지에서 띄운다
        el = _elbow(g, phase, pk) + max(0.0, 70.0 - g["elbow"]) * (1.0 - fwd)
        dn = down - (g.get("abd_r", 0.0) if s == "R" else 0.0)  # 드리블 런: 터치 쪽 팔 외전 +
        k["arm" + s] = (dn, flex, el, dn)
    for s, (ank, yaw, pitch, stance, n) in feet.items():
        k["ik" + s] = (ank.x, ank.z, ank.y - S.ANKLE_Y, yaw)
        k["foot" + s] = (pitch, 0.0)
    return k


def _gait_clip(name, g, dir_name, dir_deg, gait_name, **tags):
    frames = g["frames"]
    G = _Gait(g, dir_deg)
    stance = {"Left": [0.0, g["duty"]], "Right": [0.5, (0.5 + g["duty"]) % 1.0]}

    def sample(t):
        return P.to_pose(gait_keypose(G, (t * 30.0 / frames) % 1.0))
    return Clip(name, "locomotion", frames, sample, loop=True, gait=gait_name, speed=round(G.v, 4), dir=dir_deg,
                stance=stance, cycle=frames, **tags)


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
            out.append(_gait_clip(f"{gk}_{dn}", GAITS[gk], dn, dd, gk))
    out.append(_gait_clip("run_f", GAITS["run"], "f", 0, "run"))
    return out
