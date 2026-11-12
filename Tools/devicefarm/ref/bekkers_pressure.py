#!/usr/bin/env python3
"""specs/008 AC-01 독립 참조 구현: Bekkers 압박 강도 (리서치 02 §4.1, 스펙 FR-02 수식 그대로).

엔진 C# 구현(PressureModel)과 별개로 double 정밀도로 계산해 고정 배치 20개의 기대값 JSON을 만든다.
사용: python3 Tools/devicefarm/ref/bekkers_pressure.py > Tools/devicefarm/ref/pressure-ref.json
"""
import json
import math
import random

SIGMA = 0.45
T = 1.5
TAU_R = 0.7
VMAX = 7.0
MIN_SPEED = 2.0
TOUCH_BONUS = 0.15
TOUCH_BAND = 3.0
PITCH_W = 68.0


def pair(ri, vi, rj, vj):
    """수비수 i가 목표 j를 압박할 확률 p_ij."""
    speed = math.hypot(*vi)
    if speed < MIN_SPEED:
        return 0.0
    dj = (rj[0] + vj[0], rj[1] + vj[1])              # 목표 1초 뒤
    di = (ri[0] + vi[0] * TAU_R, ri[1] + vi[1] * TAU_R)  # 반응 시간 뒤 수비수
    to = (dj[0] - ri[0], dj[1] - ri[1])
    tl = math.hypot(*to)
    if tl < 1e-9:
        beta = 0.0
    else:
        c = (vi[0] * to[0] + vi[1] * to[1]) / (speed * tl)
        beta = math.acos(max(-1.0, min(1.0, c)))
    tau_beta = speed * beta / math.pi
    tij = TAU_R + math.hypot(dj[0] - di[0], dj[1] - di[1]) / VMAX + tau_beta
    k = math.pi / (math.sqrt(3.0) * SIGMA)
    return 1.0 / (1.0 + math.exp(-k * (T - tij)))


def team(rj, vj, defenders):
    prod = 1.0
    for ri, vi in defenders:
        prod *= 1.0 - pair(ri, vi, rj, vj)
    p = 1.0 - prod
    if rj[1] < TOUCH_BAND or rj[1] > PITCH_W - TOUCH_BAND:
        p += TOUCH_BONUS
    return min(1.0, p)


def main():
    rng = random.Random(8008)
    cases = []
    for n in range(20):
        tx, ty = rng.uniform(10, 95), rng.uniform(0.5, 67.5)
        if n % 5 == 0:
            ty = rng.uniform(0.2, 2.8)  # 터치라인 보정 배치
        tv = (rng.uniform(-4, 4), rng.uniform(-4, 4))
        defs = []
        for _ in range(rng.randint(0, 6)):
            dx, dy = tx + rng.uniform(-9, 9), min(68, max(0, ty + rng.uniform(-9, 9)))
            sp = rng.uniform(0, 7.5)
            ang = math.atan2(ty - dy, tx - dx) + rng.uniform(-1.6, 1.6)  # 대체로 목표 쪽으로
            defs.append(((dx, dy), (sp * math.cos(ang), sp * math.sin(ang))))
        cases.append({
            "target": [tx, ty], "targetVel": list(tv),
            "defenders": [[d[0][0], d[0][1], d[1][0], d[1][1]] for d in defs],
            "expected": team((tx, ty), tv, defs),
        })
    print(json.dumps({"pitchWidth": PITCH_W, "cases": cases}, indent=1))


if __name__ == "__main__":
    main()
