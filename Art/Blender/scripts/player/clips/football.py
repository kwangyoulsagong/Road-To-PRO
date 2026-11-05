"""축구 특화 보법 (specs/055 FR-40~42): 출발 가속 start_f, 감속·정지 stop_f, 드리블 런 dribble_run_30/50/65.

- 드리블 런: 루프 로코모션 클립. 같은 속도 공 없는 보법(gait.base_at) 대비 보폭 −15%(빈도는 그만큼 높게), 무게중심 −4.5 cm,
  무릎 들기 −8°, 터치하는 발(오른발) 쪽 팔 외전 +8°. 터치 시점은 054 FR-37 — 여기서는 형태만.
- 출발·감속: 비루프. 걸음마다 속도·보폭·접지 시간·몸통 경사를 정한 반주기(보법 엔진 gait._Gait)를 이어 붙이고, 걸음 경계 앞뒤 3프레임을 키 포즈 보간으로 섞는다.
  루트는 제자리 — 시뮬 이동 속도는 태그 speedCurve(걸음별 평균 속도, m/s)와 stepFrames(걸음 시작 프레임)를 054가 따른다.
태그: football = "start" | "stop" | "dribble", speedCurve, stepFrames, stepLengths(m).
"""
from . import gait as GT
from . import poses as P
from .base import Clip, smooth

# 출발 가속 [제안] (FR-40): 첫 걸음 보폭 = 최고속도 보폭(sprint 1.90 m)의 53%, 경사 38° → 11°(걸음마다 감소), 접지 시간 0.20 → 0.13 s
START_STEPS = (  # (보폭 m, 걸음 빈도 Hz, 접지 시간 s, 몸통 경사 °)
    (1.00, 3.6, 0.200, 38.0),
    (1.25, 3.9, 0.170, 30.0),
    (1.45, 4.0, 0.150, 23.0),
    (1.60, 4.1, 0.140, 16.0),
    (1.75, 4.1, 0.130, 11.0),
)
# 감속·정지 [제안] (FR-41): 짧고 넓은 제동 걸음 3, 몸통 뒤로 −8~−12°, 골반 하강 누적, 뒤꿈치 먼저
STOP_STEPS = (  # (보폭, 빈도, 접지 시간, 경사, 골반 하강 m)
    (1.20, 4.0, 0.180, -8.0, 0.04),
    (0.90, 3.8, 0.220, -12.0, 0.08),
    (0.55, 3.4, 0.260, -10.0, 0.11),
)
BLEND = 3  # 걸음 경계 섞기 프레임 (앞뒤)


def _step_gait(L, f, ct, lean, drop=0.0, heel=False, arm_rom=None):
    v = L * f
    g = GT.base_at(v)
    g.update(v=v, frames=60.0 / f, duty=min(0.9, ct * f / 2.0), lean=lean, drop=drop)
    if lean > 15.0:
        g["pelvis_tilt"] = lean * 0.35
    if heel:
        g.update(p_td=-15.0, foot_x=g["foot_x"] + 0.04)
    if arm_rom is not None:
        g["arm_rom"] = arm_rom
    return g


def _sequence(name, steps, pre, post, kind):
    """steps = [gait dict] → 비루프 클립. pre/post = 앞뒤 정지 키 포즈와 프레임 수 ((키포즈, 프레임) 또는 None)."""
    Gs = [GT._Gait(g, 0) for g in steps]
    lens = [30.0 * g["frames"] / 60.0 for g in steps]  # 반주기(한 걸음) 프레임 = 30 / 빈도
    starts, t = [], float(pre[1] if pre else 0)
    for n in lens:
        starts.append(t)
        t += n
    total = int(round(t + (post[1] if post else 0)))

    def step_pose(i, frame):
        """걸음 i의 반주기를 frame(클립 프레임)에서. 짝수 걸음 = 왼발 접지(위상 0~0.5), 홀수 = 오른발(0.5~1)."""
        w = (frame - starts[i]) / lens[i]
        ph = (0.5 * (i % 2) + 0.5 * w) % 1.0
        return GT.gait_keypose(Gs[i], ph)

    def keypose(frame):
        if pre and frame < starts[0]:
            return P.blend(pre[0], step_pose(0, starts[0]), smooth(frame / starts[0]))
        last = len(steps) - 1
        end = starts[last] + lens[last]
        if frame >= end:
            if not post:
                return step_pose(last, end)
            return P.blend(step_pose(last, end), post[0], smooth((frame - end) / post[1]))
        i = max(k for k in range(len(steps)) if starts[k] <= frame)
        k = step_pose(i, frame)
        # 경계 섞기: 다음 걸음 시작 BLEND 프레임 앞부터 다음 걸음 포즈(그 시작 위상 앞쪽으로 연장)와 섞는다
        if i < last and frame > starts[i + 1] - BLEND:
            a = (frame - (starts[i + 1] - BLEND)) / (2 * BLEND)
            return P.blend(k, step_pose(i + 1, frame), smooth(a))
        if i > 0 and frame < starts[i] + BLEND:
            a = (frame - (starts[i] - BLEND)) / (2 * BLEND)
            return P.blend(step_pose(i - 1, frame), k, smooth(a))
        return k

    tags = dict(football=kind, speedCurve=[round(g["v"], 3) for g in steps],
                stepFrames=[round(s, 2) for s in starts],
                stepLengths=[round(g["v"] / (60.0 / g["frames"]), 3) for g in steps])
    return Clip(name, "locomotion", total, lambda tt: P.to_pose(keypose(tt * 30.0)), loop=False, gait="start" if kind == "start" else "stop",
                speed=round(steps[-1]["v"] if kind == "start" else 0.0, 4), dir=0, **tags)


def start_clip():
    steps = [_step_gait(L, f, ct, lean, arm_rom=110.0) for L, f, ct, lean in START_STEPS]
    ready = dict(P.READY, armL=P.READY["armL"] + (P.ARM_DOWN_MAX,), armR=P.READY["armR"] + (P.ARM_DOWN_MAX,))
    return _sequence("start_f", steps, (ready, 6), None, "start")


def stop_clip():
    steps = [_step_gait(L, f, ct, lean, drop=dr, heel=True) for L, f, ct, lean, dr in STOP_STEPS]
    ready = dict(P.READY, armL=P.READY["armL"] + (P.ARM_DOWN_MAX,), armR=P.READY["armR"] + (P.ARM_DOWN_MAX,))
    return _sequence("stop_f", steps, None, (ready, 8), "stop")


DRIBBLE_SPEEDS = (3.0, 5.0, 6.5)
DRIBBLE_SHORTER = 0.15   # 보폭 −15% [제안] (FR-42 10~20%)
DRIBBLE_DROP = 0.045     # 무게중심 −4.5 cm [제안] (FR-42 3~6 cm)


def dribble_clip(v):
    base = GT.base_at(v)
    step0 = v / (60.0 / base["frames"])
    step = step0 * (1.0 - DRIBBLE_SHORTER)
    frames = int(round(60.0 * step / v))  # 정수 프레임 → 실제 보폭 = v × frames / 60
    g = dict(base, frames=frames, drop=DRIBBLE_DROP, sw_hip=base["sw_hip"] - 8.0)
    g["abd_r"] = 8.0  # 터치하는 오른발 쪽 팔 외전 +
    G = GT._Gait(g, 0)
    stance = {"Left": [0.0, g["duty"]], "Right": [0.5, (0.5 + g["duty"]) % 1.0]}
    name = f"dribble_run_{int(round(v * 10))}"
    return Clip(name, "locomotion", frames, lambda t: P.to_pose(GT.gait_keypose(G, (t * 30.0 / frames) % 1.0)), loop=True,
                gait="dribble", speed=round(v, 4), dir=0, stance=stance, cycle=frames, football="dribble")


def clips():
    return [start_clip(), stop_clip()] + [dribble_clip(v) for v in DRIBBLE_SPEEDS]
