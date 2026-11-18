#!/usr/bin/env python3
"""specs/007 AC-01 참조 계산 — Spearman 피치 컨트롤 지점 질의(원 논문 모드)를 C# 구현과 독립적으로 계산한다.

같은 수식(FR-01~04)을 double 정밀도·가지치기 없이 적분한다. 표준 라이브러리만 쓴다.
  python3 -I Tools/pitchcontrol/reference.py           # Data/tests/pitchcontrol-reference.json 생성
  python3 -I Tools/pitchcontrol/reference.py --check   # 다시 계산해 파일과 같은지 확인 (exit 1 = 다름)

모델 (원 논문 모드, 능력치 연결 끔):
  τ_j = t_r + |r_target − (r_j + v_j·t_r)| / v_max
  P_j(T) = 1 / (1 + exp(−π/√3 · (T − τ_j)/σ))
  λ_j = λ · (수비 팀이면 κ_def) · (GK면 gkMul)
  T_k = T_ball + k·dt (k = 1, 2, …), T_ball = |target − ball| / 공 속도
  dPC_j = (1 − ΣPC)·P_j(T_k)·λ_j·dt  (ΣPC는 이전 스텝 값), ΣPC ≥ 1 − tol 이거나 T_k > T_ball + maxT면 끝.
  마지막 스텝은 ΣPC가 1 − tol을 넘지 않도록 그 스텝의 dPC를 비례로 줄인다 (FR-04 개정 2026-11-18 T06 — 조기 종료 불연속 제거).
"""
import json
import math
import os
import sys

PARAMS = dict(reaction=0.7, vmax=5.0, sigma=0.45, lam=4.3, kappa=1.0, gk_mul=3.0, dt=0.04, max_t=10.0, tol=0.01, ball_speed=15.0)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Data", "tests", "pitchcontrol-reference.json")


def point_control(players, attacking, ball, target, p=PARAMS):
    tb = math.hypot(target[0] - ball[0], target[1] - ball[1]) / p["ball_speed"]
    tau, lam = [], []
    for pl in players:
        rx = pl["x"] + pl["vx"] * p["reaction"]
        ry = pl["y"] + pl["vy"] * p["reaction"]
        tau.append(p["reaction"] + math.hypot(target[0] - rx, target[1] - ry) / p["vmax"])
        l = p["lam"]
        if pl["team"] != attacking:
            l *= p["kappa"]
        if pl["gk"]:
            l *= p["gk_mul"]
        lam.append(l)
    pc = [0.0] * len(players)
    total = 0.0
    k = 0
    c = math.pi / math.sqrt(3.0) / p["sigma"]
    while total < 1.0 - p["tol"]:
        k += 1
        t = tb + k * p["dt"]
        if t > tb + p["max_t"]:
            break
        d = [0.0] * len(players)
        for j in range(len(players)):
            x = c * (t - tau[j])
            pj = 1.0 / (1.0 + math.exp(-x)) if x > -700 else 0.0
            d[j] = (1.0 - total) * pj * lam[j] * p["dt"]
        s = sum(d)
        if s > 0.0 and total + s > 1.0 - p["tol"]:
            f = (1.0 - p["tol"] - total) / s  # 마지막 스텝은 ΣPC가 정확히 1 − tol에 닿도록 비례로 자른다 (FR-04 개정, T06)
            d = [v * f for v in d]
        for j in range(len(players)):
            pc[j] += d[j]
        total = sum(pc)
        if total > 1.0:
            pc = [v / total for v in pc]
            total = 1.0
    att = sum(v for v, pl in zip(pc, players) if pl["team"] == attacking)
    dfn = sum(v for v, pl in zip(pc, players) if pl["team"] != attacking)
    return att, dfn


def pl(team, x, y, vx=0.0, vy=0.0, gk=False):
    return dict(team=team, x=x, y=y, vx=vx, vy=vy, gk=gk)


def layouts():
    """고정 배치 5개 (피치 105 × 68, 팀 0이 +x로 공격). 슬롯 순서 = 팀 0 → 팀 1, GK가 각 팀 첫 번째."""
    out = []
    # 1) 역습 3v2: 하프라인 근처 공격 3, 수비 2 + GK
    out.append(dict(name="counter-3v2", attacking=0, ball=[55, 34], players=[
        pl(0, 10, 34, gk=True), pl(0, 55, 34, 6, 0), pl(0, 58, 20, 7, 0), pl(0, 58, 48, 7, 0),
        pl(1, 102, 34, gk=True), pl(1, 75, 28, -2, 0), pl(1, 75, 42, -2, 0)]))
    # 2) 높은 라인 뒷공간: 수비 4명이 x = 60에 일렬, 공격 침투
    out.append(dict(name="high-line", attacking=0, ball=[45, 34], players=[
        pl(0, 15, 34, gk=True), pl(0, 45, 34), pl(0, 58, 25, 6, 0), pl(0, 58, 43, 6, 0), pl(0, 50, 10),
        pl(1, 95, 34, gk=True), pl(1, 60, 15, -1, 0), pl(1, 60, 28, -1, 0), pl(1, 60, 40, -1, 0), pl(1, 60, 53, -1, 0), pl(1, 52, 34)]))
    # 3) 박스 혼전: 페널티 박스 안 다수
    out.append(dict(name="box-scramble", attacking=0, ball=[88, 30], players=[
        pl(0, 30, 34, gk=True), pl(0, 88, 30), pl(0, 95, 36, 1, 1), pl(0, 92, 28, 2, -1), pl(0, 84, 40),
        pl(1, 103, 34, gk=True), pl(1, 96, 33, -1, 0), pl(1, 94, 38), pl(1, 90, 26, 0, 1), pl(1, 86, 35, 2, 0)]))
    # 4) 대칭 배치: x = 52.5 기준 거울
    out.append(dict(name="symmetric", attacking=0, ball=[52.5, 34], players=[
        pl(0, 40, 20), pl(0, 40, 48), pl(0, 45, 34),
        pl(1, 65, 20), pl(1, 65, 48), pl(1, 60, 34)]))
    # 5) 11 vs 11 일반 배치 (4-4-2 대 4-4-2, 속도 섞음)
    t0 = [pl(0, 5, 34, gk=True)] + [pl(0, 30, y, 1, 0) for y in (10, 26, 42, 58)] + [pl(0, 50, y, 2, 0.5) for y in (12, 28, 40, 56)] + [pl(0, 65, 28, 3, 0), pl(0, 65, 40, 3, 0)]
    t1 = [pl(1, 100, 34, gk=True)] + [pl(1, 75, y, -1, 0) for y in (10, 26, 42, 58)] + [pl(1, 60, y, -2, -0.5) for y in (14, 30, 38, 54)] + [pl(1, 48, 30, -1, 0), pl(1, 48, 38, -1, 0)]
    out.append(dict(name="full-442", attacking=0, ball=[50, 28], players=t0 + t1))
    return out


TARGETS = [[x, y] for x in (20, 45, 60, 75, 90, 100) for y in (8, 34, 60)]


def build():
    doc = dict(note="specs/007 AC-01 참조값 — Tools/pitchcontrol/reference.py가 생성. 손으로 고치지 않는다.", params=PARAMS, layouts=[])
    for lay in layouts():
        pts = []
        for t in TARGETS:
            att, dfn = point_control(lay["players"], lay["attacking"], lay["ball"], t)
            pts.append(dict(x=t[0], y=t[1], att=round(att, 6), defn=round(dfn, 6)))
        lay = dict(lay)
        lay["points"] = pts
        doc["layouts"].append(lay)
    return doc


def main():
    doc = build()
    text = json.dumps(doc, ensure_ascii=False, indent=1) + "\n"
    if "--check" in sys.argv:
        with open(OUT, encoding="utf-8") as f:
            same = f.read() == text
        print("같음" if same else "다름")
        sys.exit(0 if same else 1)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(text)
    print(f"썼음: {os.path.normpath(OUT)} ({len(doc['layouts'])}배치 × {len(TARGETS)}지점)")


if __name__ == "__main__":
    main()
