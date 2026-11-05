"""달리기 스타일 원형 클립 (specs/055 FR-50·52).

원형 = 기본 보법에 대한 오프셋(Assets/_Project/Presentation/Players/Data/055-run-styles.json — Unity·테스트와 같은 파일).
클립 수를 억제하려고 정면 run·sprint만 원형별로 만든다(옆·뒤·walk·jog는 공유):
- sprint_f_<원형>_lo / _hi: 원형 표의 최고속도에서 보폭 × 빈도 중앙값. 30 fps 정수 주기라 빈도가 양자화된다 → 목표 주기 프레임의
  올림(lo: 빈도 낮고 보폭 김)·내림(hi)으로 두 극단을 만들고, 몸통 경사·어깨 가동도 −5%·+5%(흔들기, FR-51). Unity는 선수 id 시드
  가중치로 둘을 섞는다 → 평균이 원형 표 안.
- run_f_<원형>: 5.4 m/s에서 같은 보폭/빈도 비율 r(빈도 = √(v/r)).
- GK: 흔들기 없이 sprint_f_gk 하나 + run_f_gk.
"""
import json
import math
import os

from . import gait as GT
from . import poses as P
from .base import Clip

HERE = os.path.dirname(os.path.abspath(__file__))
STYLES_JSON = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Assets", "_Project", "Presentation", "Players",
                                            "Data", "055-run-styles.json"))


def load():
    with open(STYLES_JSON, encoding="utf-8") as f:
        return json.load(f)


def _mid(r):
    return 0.5 * (r[0] + r[1])


def _params(v, frames, st, base_gait, lean_scale=1.0, k=1.0):
    """원형 생성 파라미터 → 보법 dict. k = 흔들기 배율(0.95·1·1.05)."""
    gen = st["gen"]
    g = GT.base_at(v)
    sp = GT.GAITS[base_gait]
    g.update(v=v, frames=float(frames), lean=gen["lean"] * lean_scale * k, arm_rom=sp["arm_rom"] * gen["romScale"] * k,
             abd=sp["abd"] + gen["abdAdd"], drop=gen["drop"], sw_hip=g["sw_hip"] + gen["swHipAdd"])
    if "armC" in gen:
        g["arm_c"] = gen["armC"]
    if g["lean"] > 15.0:
        g["pelvis_tilt"] = g["lean"] * 0.35
    return g


def _clip(name, g, gait, sid, variant=None):
    G = GT._Gait(g, 0)
    frames = int(round(g["frames"]))
    stance = {"Left": [0.0, g["duty"]], "Right": [0.5, (0.5 + g["duty"]) % 1.0]}
    tags = dict(gait=gait, speed=round(g["v"], 4), dir=0, stance=stance, cycle=frames, style=sid)
    if variant:
        tags["variant"] = variant
    return Clip(name, "locomotion", frames, lambda t: P.to_pose(GT.gait_keypose(G, (t * 30.0 / frames) % 1.0)), loop=True, **tags)


def clips():
    data = load()
    jit = data["jitter"]
    out = []
    run_v = GT.GAITS["run"]["v"]
    for sid, st in data["styles"].items():
        gen = st["gen"]
        if "sprintStep" in st:
            step, cad = _mid(st["sprintStep"]), _mid(st["sprintCadence"])
            v = step * cad
        else:  # GK: 보폭·빈도 표 없음 — 기본 sprint 비율
            v = gen["speed"]
            sp = GT.GAITS["sprint"]
            cad = 60.0 / sp["frames"] * math.sqrt(v / sp["v"])
            step = v / cad
        ratio = step / cad
        # run: 같은 보폭/빈도 비율로 5.4 m/s
        rc = math.sqrt(run_v / ratio)
        rg = _params(run_v, max(1, round(60.0 / rc)), st, "run", lean_scale=GT.GAITS["run"]["lean"] / GT.GAITS["sprint"]["lean"])
        out.append(_clip(f"run_f_{sid}", rg, "run", sid))
        F = 60.0 / cad
        if "sprintStep" not in st:
            out.append(_clip(f"sprint_f_{sid}", _params(v, round(F), st, "sprint"), "sprint", sid))
            continue
        lo, hi = math.ceil(F), math.floor(F)
        if lo == hi:
            lo, hi = lo + 1, hi
        out.append(_clip(f"sprint_f_{sid}_lo", _params(v, lo, st, "sprint", k=1.0 - jit), "sprint", sid, "lo"))
        out.append(_clip(f"sprint_f_{sid}_hi", _params(v, hi, st, "sprint", k=1.0 + jit), "sprint", sid, "hi"))
    return out
