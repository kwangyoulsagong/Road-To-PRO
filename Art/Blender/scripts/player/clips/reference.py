"""연구 기준 스틱 피겨 (specs/055 FR-71). 생성기 포즈와 독립 — 기준 파일(Data/anim/055-gait-standards.json) 범위 중앙값과
표준 파형(Novacheck 1998의 달리기 위상 구분: 접지 → 공중 → 스윙)으로 보법별 8위상 관절각을 만든다. Unity E2E가 필름스트립 옆에 선으로 그린다.

시상면(측면) 각(°): hip = 허벅지의 몸통선 대비 앞 굴곡, knee = 무릎 굴곡, trunk = 몸통 앞 경사, shoulder = 위팔의 몸통선 대비 앞 굴곡,
elbow = 팔꿈치 굴곡, pelvisY = 골반 높이 변화(m, 평균 0). 정면 각: obliquity = 골반 기울기(+ = 오른쪽 내려감), abduction = 팔 벌림,
footWidth = 두 발 중심 사이 옆 거리(m). 왼다리 접지 시작 = 위상 0, 오른다리 = 0.5.
"""
import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
STD_JSON = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Data", "anim", "055-gait-standards.json"))
PHASES = [i / 8.0 for i in range(8)]
GAITS = ("walk", "jog", "run", "sprint")
# 기준 파일에 없는 값의 [제안] 기본 (걷기 무릎 등 — 기준 범위가 null인 항목)
DEFAULTS = {
    "walk": dict(kneeTouchdown=5.0, kneeStanceMax=18.0, kneeToeOff=35.0, kneeSwingMax=62.0, hipSwingMax=30.0, hipToeOff=-12.0),
    "jog": dict(hipToeOff=-15.0),
    "run": dict(hipToeOff=-18.0),
    "sprint": dict(hipToeOff=-20.0),
}


def _mid(r):
    return 0.5 * (r[0] + r[1])


def _std():
    with open(STD_JSON, encoding="utf-8") as f:
        return json.load(f)


def _v(g, key, gk):
    r = g.get(key)
    if isinstance(r, list):
        return _mid(r)
    return DEFAULTS[gk][key]


def _smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def _leg(gk, g, q):
    """한 다리의 위상 q(0 = 접지 시작) → (엉덩이, 무릎)."""
    duty = _mid(g["duty"])
    hip_td = _v(g, "hipSwingMax", gk) * 0.55   # 착지 순간 허벅지 굴곡 ≈ 스윙 최대의 55% (Novacheck 파형)
    hip_to = DEFAULTS[gk]["hipToeOff"]
    k_td, k_mx, k_to = _v(g, "kneeTouchdown", gk), _v(g, "kneeStanceMax", gk), _v(g, "kneeToeOff", gk) if g.get("kneeToeOff") else 18.0
    if gk == "walk":
        k_to = DEFAULTS["walk"]["kneeToeOff"]
    k_sw, h_sw = _v(g, "kneeSwingMax", gk), _v(g, "hipSwingMax", gk)
    if q < duty:
        u = q / duty
        hip = hip_td + (hip_to - hip_td) * u
        knee = k_td + (k_mx - k_td) * math.sin(math.pi * min(1.0, u / 0.8)) ** 1 if u < 0.4 else \
            k_mx + (k_to - k_mx) * _smooth((u - 0.4) / 0.6)
        return hip, knee
    r = (q - duty) / (1.0 - duty)
    hip = hip_to + (h_sw - hip_to) * _smooth(r / 0.7) if r < 0.7 else h_sw + (hip_td - h_sw) * _smooth((r - 0.7) / 0.3)
    knee = k_to + (k_sw - k_to) * _smooth(r / 0.4) if r < 0.4 else k_sw + (k_td - k_sw) * _smooth((r - 0.4) / 0.6)
    return hip, knee


def gait_reference(gk, g):
    duty = _mid(g["duty"])
    rom = _mid(g["shoulderRom"])
    el, er = _mid(g["elbowMean"]), (_mid(g["elbowRange"]) if g.get("elbowRange") else 10.0)
    bob = _mid(g["pelvisBob"])
    trunk = _mid(g["trunkLean"])
    obl = _mid(g["pelvisObliquity"])
    abd = _mid(g["armAbduction"])
    side, front = [], []
    # 팔 위상: 반대 다리 엉덩이 굴곡 최대(스윙 70% 지점) 순간에 어깨 앞 최대
    pk_r = (0.5 + duty + 0.7 * (1.0 - duty)) % 1.0  # 오른 다리 → 왼팔
    for ph in PHASES:
        hl, kl = _leg(gk, g, ph % 1.0)
        hr, kr = _leg(gk, g, (ph - 0.5) % 1.0)
        shl = 0.5 * rom * math.cos(2 * math.pi * (ph - pk_r))
        shr = 0.5 * rom * math.cos(2 * math.pi * (ph - pk_r - 0.5))
        if duty < 0.5:  # 스프링-질량: 접지 중간 최저, 공중 중간 최고
            ql, qr = ph % 1.0, (ph - 0.5) % 1.0
            if ql < duty:
                y = -0.5 * bob * math.sin(math.pi * ql / duty)
            elif qr < duty:
                y = -0.5 * bob * math.sin(math.pi * qr / duty)
            else:
                y = 0.5 * bob * math.sin(math.pi * ((ph - duty) % 0.5) / (0.5 - duty))
        else:  # 걷기: 역진자(접지 중간 최고)
            y = 0.5 * bob * math.cos(4 * math.pi * (ph - duty / 2))
        side.append({"hipL": round(hl, 2), "kneeL": round(kl, 2), "hipR": round(hr, 2), "kneeR": round(kr, 2),
                     "trunk": round(trunk, 2), "shoulderL": round(shl, 2), "shoulderR": round(shr, 2),
                     "elbowL": round(el + 0.5 * er * math.cos(2 * math.pi * (ph - pk_r)), 2),
                     "elbowR": round(el + 0.5 * er * math.cos(2 * math.pi * (ph - pk_r - 0.5)), 2), "pelvisY": round(y, 4)})
        front.append({"obliquity": round(obl * math.cos(2 * math.pi * (ph - duty / 2)), 2), "abduction": round(abd, 2),
                      "footWidth": 0.2})
    return {"phases": PHASES, "side": side, "front": front}


def build():
    std = _std()["gaits"]
    return {"schemaVersion": 1, "spec": "specs/055-running-form FR-71",
            "note": "연구 기준 스틱 피겨 관절각 — 기준 파일 범위 중앙값 + 표준 파형. 생성기 포즈와 독립(자기 비교 방지).",
            "segments": {"thigh": 0.43, "shin": 0.415, "trunk": 0.55, "neck": 0.10, "head": 0.22, "upperArm": 0.29, "foreArm": 0.26,
                         "foot": 0.20, "pelvisHeight": 0.93},
            "gaits": {gk: gait_reference(gk, std[gk]) for gk in GAITS}}
