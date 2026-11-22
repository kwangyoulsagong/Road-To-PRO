"""헤어 그루밍 v2 (시안 생성기 — 포마드·가일컷(gail_g)·시스루(see_g) 승인 2026-11-22, 게임 생성기 반영 전): 스타일 = ① 두피 빗질 흐름장 F(위치) ② 머리 두께장 H(위치, 볼륨 껍질) ③ 길이장 L.
가닥은 뿌리에서 서서 두께 껍질 높이까지 올라간 뒤 흐름을 따라 그 높이를 유지하며 눕는다(껍질 위로 넘치면 눌림).
렌더 후 참고 사진과 같은 높이로 나란히 붙인 비교 이미지를 만든다. 인자: -- <출력 폴더> <스타일> [참고 사진]
필요: Blender MPFB 확장 + MakeHuman CC0 에셋 팩(레포 밖, Art/ThirdParty/MakeHumanPacks 압축 파일은 커밋 안 함).
실행 예: SHELL_ON=1 Blender --background --python groom2.py -- <출력> see_g   (DIAG=1: 렌더 없이 피부와의 거리만 출력)
참고 사진(실존 인물)은 모양 비교용으로만 쓰고 레포에 넣지 않는다 — compare2.py <출력.png> <참고> <렌더> ...로 나란히 붙인다."""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:]
OUT, STYLE = ARGS[0], ARGS[1]
REF = ARGS[2] if len(ARGS) > 2 else None
os.makedirs(OUT, exist_ok=True)

# 공통 도우미(사람·두피·셸·커브·재질·장면)는 v1에서 가져온다
_src = open(os.path.join(os.path.dirname(__file__), "groom_trial.py")).read().replace("\nmain()\n", "\n")
_src = _src.replace("ARGS = sys.argv[sys.argv.index(\"--\") + 1:]\nOUT = ARGS[0]\nos.makedirs(OUT, exist_ok=True)\n", "")
exec(compile(_src, "groom_trial.py", "exec"))


def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


class Coords:
    """머리 좌표: 원점 = 머리 중심(눈 중심에서 8.5 cm 뒤, 3 cm 위). x = 선수 왼쪽(+), f = 앞(+), z = 위(+)."""
    def __init__(self, eye):
        self.c = eye + np.array([0.0, 0.085, 0.03])

    def of(self, P):
        r = P - self.c
        return r[:, 0], -r[:, 1], r[:, 2]

    def vec(self, x, f, z):
        return np.stack([x, -f, z], axis=-1)


# ---------------------------------------------------------------- 스타일 정의 (흐름 F, 두께 H, 길이 L, 헤어라인 옆 높이)

def pomade(x, f, z, C):
    """옆가르마 포마드 (참고: 옆·뒤 아주 짧게, 윗머리 한 덩어리로 위·뒤·반대쪽으로 넘김, 앞 퀴프)."""
    part = -0.030                                     # 선수 오른쪽 가르마
    big = x > part
    # 윗머리 영역(앞 이마선 ~ 정수리 ~ 뒤통수 위): 옆(|x| 큼)·뒤 아래만 짧게 친 부분
    ridge = smooth((0.060 - np.abs(x)) / 0.02) * smooth((z - (0.015 + 0.035 * np.clip(-f / 0.07, 0, 1))) / 0.02)   # 부드러운 테이퍼
    front = smooth((f - 0.02) / 0.06)
    # 흐름: 큰 쪽 = 뒤 + 반대쪽(+x) + 앞에서는 위로, 작은 쪽 = 아래·뒤로 붙임
    Fx = np.where(big, 0.55, -0.3)
    Ff = np.where(big, -0.65 + 0.25 * front, -0.85)
    Fz = np.where(big, 0.35 + 0.5 * front, -0.12)                 # 작은 쪽: 뒤로 붙여 넘김(아래로 늘어지지 않게)
    lowz = smooth((z + 0.05) / 0.06)                 # 목선 쪽은 더 짧게(테이퍼)
    H = 0.004 * lowz + 0.0015 + ridge * (0.008 + 0.011 * front + 0.004 * smooth((x - part) / 0.05))
    H = np.where(big, H, 0.004 * lowz + 0.0015 + ridge * 0.006)
    L = 0.004 + 0.012 * lowz + ridge * np.where(big, 0.05 + 0.03 * front, 0.04)   # 옆 1.6 cm 가위컷 → 위 8 cm
    # 귀 위 띠(귀 상단 ~ 2 cm 위): 귀를 덮지 않게 짧게 깎는다
    over_ear = smooth((np.abs(x) - 0.052) / 0.008) * smooth((0.028 - z) / 0.015) * smooth((f + 0.045) / 0.02) * smooth((0.06 - f) / 0.02)
    L = L * (1 - 0.75 * over_ear)
    H = H * (1 - 0.6 * over_ear)
    side_w = 1 - ridge
    Fx = Fx * (1 - side_w) + np.sign(x) * 0.15 * side_w
    Ff = Ff * (1 - side_w) - 0.75 * side_w
    Fz = Fz * (1 - side_w) - 0.55 * side_w
    return C.vec(Fx, Ff, Fz), H, L


GAIL_PART = -0.035   # 화면 왼쪽 가르마 (포마드와 같은 쪽 부호)


def gail_comma(x, f, z):
    """쉼표 다발: 가르마 바로 큰 쪽(+x) 앞 이마선 근처 작은 영역."""
    return (x > GAIL_PART + 0.004) & (x < 0.072) & (f > 0.015) & (z > -0.005)   # 큰 쪽 앞머리 전체


def gail(x, f, z, C):
    """가일컷 (사진 확대 비교로 다시 짬): 옆·뒤는 포마드처럼 짧게. 가르마 = 가운데 약간 왼쪽, 선이 드러나지 않음.
    왼쪽 윗머리: 왼쪽 아래로 둥글게 흘러 관자놀이를 덮음(볼륨). 오른쪽 윗머리: 정수리 가운데에서 앞으로 넘어와
    두꺼운 다발이 두 눈 사이~오른쪽 눈 안쪽 위(눈 높이)에서 끝나고, 바깥 가닥은 관자놀이 쪽으로 올라가며 끝(C자)."""
    F, H, L = pomade(x, f, z, C)
    part = -0.038                                      # 가르마 = 왼쪽 눈썹 바깥 위 (다발이 가운데를 덮으며 넘어오게)
    ridge = smooth((0.062 - np.abs(x)) / 0.025) * smooth((z - (0.012 + 0.035 * np.clip(-f / 0.07, 0, 1))) / 0.025)
    fr = smooth((f + 0.05) / 0.08)
    left = x <= part
    wl = smooth((part - x) / 0.06)                     # 왼쪽: 가르마에서 멀수록 바깥
    wr = smooth((x - part) / 0.07)                     # 오른쪽: 가르마에서 멀수록 바깥
    Fl = C.vec(-0.85, -0.25, -0.35)                    # 왼쪽(작은 쪽): 바깥·뒤로 둥글게 — 눈 위로 내려오지 않고 관자놀이를 덮음
    # 오른쪽(큰 쪽): 가르마 근처 가닥은 거의 수직으로 떨어지며 살짝 오른쪽 → 두 눈 사이 눈 높이, 바깥 가닥은 오른쪽으로 쓸림
    Fr = C.vec(0.38 + 0.55 * wr, 0.45 * fr - 0.3 * (1 - fr) - 0.2 * wr, -0.50 * (1 - wr) - 0.12)
    Fn = np.where(left[:, None], Fl, Fr)
    m = ridge[:, None]
    F = F * (1 - m) + Fn * m
    Ll = 0.045 + 0.015 * wl + 0.025 * smooth(-f / 0.05)
    Lr = 0.06 + 0.085 * (1 - wr) ** 1.3 * fr + 0.05 * smooth(-f / 0.05)
    L = L * (1 - ridge) + np.where(left, Ll, Lr) * ridge
    Hd = 0.007 + 0.014 * smooth((z - 0.03) / 0.035)  # 돔: 정수리 2.1 cm, 옆 아래 7 mm
    H = H * (1 - ridge) + Hd * ridge
    return F, H, L


TREE = [None]
EYE = [None]


def gail_post(S, P, C, rng):
    """쉼표 다발을 가이드 베지어로 다시 그린다: 뿌리 → 앞·위로 솟음(볼륨) → 관자놀이 쪽으로 내려옴 → 눈썹 꼬리에서 안·위로 말림.
    다발 폭 안에서 뿌리 위치만큼 곡선을 옆으로 벌리고 가닥마다 조금 흔든 뒤 피부 밖으로 민다."""
    x, f, z = C.of(P)
    cm = gail_comma(x, f, z)
    idx = np.where(cm)[0]
    thin = rng.random(len(idx)) < 0.95                       # 다발을 얇게: 45 %는 쉼표에서 빼서 일반 윗머리로 둔다
    idx = idx[thin]
    n = S.shape[1] - 1
    eye = EYE[0]
    t = np.linspace(0, 1, n + 1)[:, None]
    brow = eye + np.array([0.0, 0.0, 0.0])
    tt = np.linspace(0, 1, 64)[:, None]
    for j in idx:
        p0 = P[j]
        w = np.clip((x[j] - GAIL_PART) / (0.072 - GAIL_PART), 0, 1)   # 0 가르마 쪽 ~ 1 관자놀이 쪽
        back = np.clip(-f[j] / 0.06, 0, 1)                            # 뿌리가 뒤쪽일수록 위로 더 넘어오며 두께
        sp = rng.normal(0, 1, 3)
        lift = 0.010 + 0.012 * back + 0.004 * rng.random()            # 층 두께(바깥으로 띄움)
        p1 = p0 + np.array([0.004 * w, -0.030, 0.012 + lift])         # 앞·위로 둥글게 넘어옴
        # 끝: 가르마 쪽 가닥 → 이마 가운데·오른쪽 눈 안쪽 위(가장 낮음), 바깥 가닥 → 관자놀이(높음) → C자 가장자리
        p3 = np.array([eye[0] + 0.004 + 0.062 * w ** 0.8 + 0.003 * sp[0],
                       eye[1] - 0.020 + 0.016 * w,
                       eye[2] - 0.004 + 0.030 * w ** 1.5 + 0.003 * sp[1]])
        p2 = p3 + np.array([-0.006 - 0.006 * w, -0.014, 0.034 + lift * 0.6])   # 위에서 수직에 가깝게 떨어짐
        B = ((1 - tt) ** 3) * p0 + 3 * ((1 - tt) ** 2) * tt * p1 + 3 * (1 - tt) * tt ** 2 * p2 + tt ** 3 * p3
        B += np.outer(np.sin(tt[:, 0] * np.pi), rng.normal(0, 0.0022, 3))
        # 테이퍼: 가닥마다 길이를 다르게 잘라 끝이 엇갈리게 (짧은 가닥이 많을수록 끝이 가늘게 흩어짐)
        u = 0.68 + 0.32 * rng.random() ** 0.5
        keep = int(max(8, round(u * 63)))
        Bk = B[:keep + 1]
        # 같은 점 수(n + 1)로 다시 뽑기 (호 길이 기준)
        seglen = np.linalg.norm(np.diff(Bk, axis=0), axis=1)
        cum = np.concatenate([[0], np.cumsum(seglen)])
        q = np.linspace(0, cum[-1], n + 1)
        R = np.stack([np.interp(q, cum, Bk[:, c]) for c in range(3)], axis=1)
        for i in range(1, n + 1):
            loc, nrm, _, _ = TREE[0].find_nearest(Vector(R[i]), 0.2)
            if loc is not None and (R[i] - np.array(loc)) @ np.array(nrm) < 0.003:
                R[i] = np.array(loc) + np.array(nrm) * 0.003
        S[j] = R
    return S


# ---------------------------------------------------------------- 가이드 가닥 그루밍 (사진 추적)
# 머리 좌표 (x 왼쪽+ = 화면 오른쪽, f 앞+, z 위+), 원점 = 머리 중심(눈 8.5 cm 뒤·3 cm 위). 눈 높이 z ≈ −0.03, 눈썹 z ≈ −0.005.
# 웨이포인트 (x, f, z, h): (x, f, z) 방향으로 머리 중심에서 쏜 광선이 피부와 만나는 점 + 법선 × h (h = 머리 표면 위 높이).
GAIL_GUIDES = {
    # 가르마 = 왼쪽 눈썹 가운데 위(x ≈ −0.029, 3:7). 큰 쪽(+x)은 이마를 덮고 내려오고, 작은 쪽(−x)은 볼륨 있게 왼쪽 관자놀이로 둥글게.
    # A: 가르마 쪽 앞머리 → 위로 볼륨 → 이마 앞으로 넘어와 두 눈 사이 눈 높이
    "A": [(-0.025, 0.07, 0.055, 0.0), (-0.018, 0.085, 0.072, 0.017), (-0.004, 0.10, 0.045, 0.013), (0.002, 0.10, 0.008, 0.009), (0.006, 0.10, -0.030, 0.007)],
    # B: 가운데 앞머리 → 오른쪽 눈 위
    "B": [(0.016, 0.075, 0.055, 0.0), (0.022, 0.09, 0.068, 0.017), (0.032, 0.095, 0.038, 0.013), (0.038, 0.09, 0.004, 0.009), (0.042, 0.085, -0.016, 0.007)],
    # C: 오른쪽 앞 → 관자놀이(눈썹 높이)
    "C": [(0.045, 0.06, 0.05, 0.0), (0.055, 0.07, 0.052, 0.015), (0.066, 0.06, 0.026, 0.011), (0.071, 0.05, 0.006, 0.007)],
    # D: 정수리 → 앞으로 넘어와 이마 위(앞머리 위를 덮는 층)
    "D": [(0.008, -0.03, 0.09, 0.0), (0.012, 0.02, 0.10, 0.021), (0.020, 0.07, 0.082, 0.019), (0.028, 0.095, 0.045, 0.014), (0.032, 0.098, 0.012, 0.009)],
    # B2: 오른쪽 중간 → 오른쪽 눈 바깥 위(눈썹 높이) — 끝선이 눈 → 관자놀이로 올라가는 곡선
    "B2": [(0.030, 0.07, 0.055, 0.0), (0.038, 0.085, 0.062, 0.016), (0.050, 0.085, 0.032, 0.012), (0.056, 0.078, 0.004, 0.008), (0.058, 0.072, -0.006, 0.007)],
    # F: 가르마 바로 옆 큰 쪽 정수리 → 가르마 홈을 덮으며 앞으로 (가르마 선이 안 보이게)
    "F": [(-0.025, -0.01, 0.085, 0.0), (-0.032, 0.03, 0.095, 0.018), (-0.020, 0.075, 0.078, 0.016), (-0.004, 0.098, 0.035, 0.012), (0.000, 0.10, 0.0, 0.008)],
    # 작은 쪽: 가르마에서 위로 솟았다가 바깥으로 둥글게 넘어가 왼쪽 관자놀이를 덮음(눈썹 높이 위에서 끝, 판처럼 늘어지지 않게 뒤로 조금)
    # E: 정수리 쪽 → 옆머리 위로 넘어감
    "E": [(-0.036, 0.02, 0.075, 0.0), (-0.050, 0.01, 0.078, 0.006), (-0.064, -0.01, 0.060, 0.004), (-0.074, -0.035, 0.040, 0.003)],
    # E2: 이마선 가르마 옆 → 위로 솟아 바깥으로 둥글게 → 관자놀이 눈썹 꼬리 위
    "E2": [(-0.034, 0.07, 0.05, 0.0), (-0.044, 0.065, 0.060, 0.005), (-0.058, 0.05, 0.050, 0.004), (-0.070, 0.025, 0.032, 0.003), (-0.075, 0.005, 0.020, 0.002)],
    # E3: 중간 → 관자놀이 뒤쪽 (E·E2 사이를 채워 둥근 덩어리)
    "E3": [(-0.040, 0.045, 0.065, 0.0), (-0.053, 0.035, 0.070, 0.006), (-0.066, 0.015, 0.052, 0.004), (-0.075, -0.01, 0.030, 0.003)],
}
GAIL_GUIDES["E4"] = [(-0.040, -0.05, 0.065, 0.0), (-0.055, -0.058, 0.060, 0.005), (-0.068, -0.066, 0.045, 0.004), (-0.074, -0.072, 0.030, 0.003)]   # 작은 쪽 뒤: 뒤·아래로 붙여 넘김
GAIL_SIDE = {"A": 1, "B": 1, "C": 1, "D": 1, "E": -1, "F": 1, "E2": -1, "E3": -1, "E4": -1, "B2": 1}
SMALL_MAX_H = 0.008   # 작은 쪽 가닥이 피부에서 뜰 수 있는 최대 높이 (뜨는 판 방지)   # 가르마 기준 쪽 (+1 큰 쪽)
GAIL_G_PART = -0.029


def guide_world(C, tree, wp, n=24):
    """웨이포인트 → 월드 폴리라인 (피부 위 높이 h 반영, 캣멀롬 보간 n점)."""
    pts = []
    for x, f, z, h in wp:
        d = unit(C.vec(np.array(x), np.array(f), np.array(z)))
        hit = tree.ray_cast(Vector(C.c), Vector(d))
        if hit[0] is None:
            q = C.c + d * 0.09
            nrm = d
        else:
            q, nrm = np.array(hit[0]), np.array(hit[1])
        pts.append(q + nrm * h)
    pts = np.array(pts)
    seglen = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    cum = np.concatenate([[0], np.cumsum(seglen)])
    q = np.linspace(0, cum[-1], n)
    # 단조 3차(PCHIP 대신 간단히 선형 + 라플라시안 3회)
    R = np.stack([np.interp(q, cum, pts[:, c]) for c in range(3)], axis=1)
    for _ in range(3):
        R[1:-1] = R[1:-1] * 0.5 + (R[:-2] + R[2:]) * 0.25
    return R


def apply_guides(S, P, C, tree, rng, guides, side, part, noise=0.0012, clump=0.45, cdiv=250, ridge_x=0.062, fmin=-0.06, cap_fn=None):
    """윗머리 가닥을 가이드 혼합으로 다시 그린다: 경로 = 뿌리 + Σ w_g (가이드_g(t) − 가이드뿌리_g), w = 같은 쪽 가이드 IDW."""
    x, f, z = C.of(P)
    steps = S.shape[1] - 1
    G = {k: guide_world(C, tree, v, steps + 1) for k, v in guides.items()}
    roots = {k: g[0] for k, g in G.items()}
    ridge = smooth((ridge_x - np.abs(x)) / 0.02) * smooth((z - (0.015 + 0.035 * np.clip(-f / 0.07, 0, 1))) / 0.02)
    guided = (ridge > 0.5) & (f > fmin)
    sgn = np.where(x > part, 1, -1)
    for j in np.where(guided)[0]:
        ks = [k for k in G if side[k] == sgn[j]]
        dist = np.array([np.linalg.norm(P[j] - roots[k]) for k in ks]) + 0.004
        w = 1.0 / dist ** 2
        w /= w.sum()
        off = sum(wi * (G[k] - roots[k]) for wi, k in zip(w, ks))
        path = P[j] + off + rng.normal(0, noise, 3) * np.linspace(0, 1, steps + 1)[:, None]
        u = 0.72 + 0.28 * rng.random() ** 0.6               # 끝 엇갈림(가늘게 흩어지는 끝)
        if abs(x[j]) > 0.05 and cap_fn is not None:
            u = 0.55 + 0.45 * rng.random() ** 0.7
        seg = np.linalg.norm(np.diff(path, axis=0), axis=1)
        cum = np.concatenate([[0], np.cumsum(seg)])
        qq = np.linspace(0, cum[-1] * u, steps + 1)
        S[j] = np.stack([np.interp(qq, cum, path[:, c]) for c in range(3)], axis=1)
    # 다발: 가이드 가닥끼리 끝을 모아 결이 보이게
    gi = np.where(guided)[0]
    if len(gi) > 300:
        from mathutils.kdtree import KDTree
        m = max(60, len(gi) // cdiv)
        cen = rng.choice(gi, m, replace=False)
        kd = KDTree(m)
        for ii, c in enumerate(cen):
            kd.insert(Vector(S[c, steps // 2]), ii)
        kd.balance()
        own = np.array([kd.find(Vector(S[q, steps // 2]))[1] for q in gi])
        t = (np.linspace(0, 1, steps + 1) ** 1.3)[None, :, None]
        S[gi] = S[gi] * (1 - clump * t) + S[cen[own]] * (clump * t)
    # 피부 밖으로 + 작은 쪽은 최대 높이까지만(뜨지 않게). 높이는 가닥마다 0.45~1배로 흩어 층이 생기게
    hcap = SMALL_MAX_H * (0.45 + 0.55 * rng.random(len(P)))
    if cap_fn is not None:                           # 위치별 최대 높이(둥근 돔): 모든 가닥에 적용
        hcap = cap_fn(x, f, z) * (0.6 + 0.4 * rng.random(len(P)))
        sgn = -np.ones(len(P), int)
    for j in np.where(guided)[0]:
        for i in range(1, steps + 1):
            loc, nrm, _, _ = tree.find_nearest(Vector(S[j, i]), 0.3)
            if loc is None:
                continue
            d = (S[j, i] - np.array(loc)) @ np.array(nrm)
            if d < 0.0015:
                S[j, i] = np.array(loc) + np.array(nrm) * 0.0015
            elif sgn[j] < 0 and d > hcap[j]:
                S[j, i] = np.array(loc) + np.array(nrm) * hcap[j]
    return S, G


def seethrough_base(x, f, z, C):
    """시스루 바탕: 포마드 바탕에서 옆머리만 길게(관자놀이 덮고 귀 위까지) 아래로 흘림."""
    F, H, L = pomade(x, f, z, C)
    # 옆 바탕: 귀 위·관자놀이 전체를 채움 (윗머리 끝 아래 맨살 띠가 없게) — 아래로 흘러 귀 위에서 끝
    side = smooth((np.abs(x) - 0.045) / 0.015) * smooth((z + 0.045) / 0.03) * smooth((f + 0.07) / 0.03)
    L = L * (1 - side) + 0.035 * side * (0.75 + 0.25 * np.sin(x * 900 + f * 700) ** 2)
    H = H * (1 - side) + 0.004 * side
    Fs = C.vec(np.sign(x) * 0.25, 0.15 + 0 * x, -0.95 + 0 * x)
    F = F * (1 - side[:, None]) + Fs * side[:, None]
    return F, H, L


# 시스루: 가르마 없음(가운데). 앞머리는 이마가 비치게 듬성한 다발로 눈썹~눈 높이, 윗머리는 정수리에서 앞으로 볼륨 있게 넘어옴.
SEE_GUIDES = {
    # 앞머리: 가운데는 짧고 듬성(이마 비침), 양옆 다발이 눈썹~눈 높이로 길게. 정수리에서 볼륨 있게 솟았다가 내려옴.
    "M":  [(0.000, 0.07, 0.055, 0.0), (0.000, 0.09, 0.075, 0.022), (0.002, 0.10, 0.035, 0.016), (0.004, 0.10, 0.010, 0.008)],
    "L1": [(-0.020, 0.07, 0.055, 0.0), (-0.024, 0.09, 0.075, 0.022), (-0.030, 0.10, 0.035, 0.015), (-0.034, 0.095, 0.000, 0.008), (-0.036, 0.09, -0.026, 0.005)],
    "R1": [(0.020, 0.07, 0.055, 0.0), (0.024, 0.09, 0.075, 0.022), (0.030, 0.10, 0.035, 0.015), (0.034, 0.095, 0.000, 0.008), (0.038, 0.09, -0.020, 0.005)],
    "L2": [(-0.045, 0.06, 0.050, 0.0), (-0.056, 0.07, 0.068, 0.020), (-0.070, 0.06, 0.035, 0.015), (-0.078, 0.045, 0.000, 0.010), (-0.080, 0.03, -0.020, 0.007)],
    "R2": [(0.045, 0.06, 0.050, 0.0), (0.056, 0.07, 0.068, 0.020), (0.070, 0.06, 0.035, 0.015), (0.078, 0.045, 0.000, 0.010), (0.080, 0.03, -0.020, 0.007)],
    "D":  [(0.0, -0.03, 0.09, 0.0), (0.0, 0.02, 0.105, 0.022), (0.0, 0.07, 0.088, 0.021), (0.004, 0.095, 0.048, 0.018), (0.008, 0.10, 0.015, 0.010)],
    "DL": [(-0.035, -0.03, 0.085, 0.0), (-0.048, 0.02, 0.09, 0.022), (-0.062, 0.05, 0.065, 0.020), (-0.074, 0.05, 0.020, 0.012), (-0.078, 0.045, -0.020, 0.007)],
    "DR": [(0.035, -0.03, 0.085, 0.0), (0.048, 0.02, 0.09, 0.022), (0.062, 0.05, 0.065, 0.020), (0.074, 0.05, 0.020, 0.012), (0.078, 0.045, -0.020, 0.007)],
    "B":  [(0.0, -0.07, 0.06, 0.0), (0.0, -0.088, 0.04, 0.018), (0.0, -0.095, 0.0, 0.012), (0.0, -0.09, -0.03, 0.008)],
    "BL": [(-0.04, -0.06, 0.06, 0.0), (-0.062, -0.07, 0.035, 0.016), (-0.074, -0.06, 0.0, 0.011), (-0.074, -0.05, -0.025, 0.007)],
    "BR": [(0.04, -0.06, 0.06, 0.0), (0.062, -0.07, 0.035, 0.016), (0.074, -0.06, 0.0, 0.011), (0.074, -0.05, -0.025, 0.007)],
}
SEE_GUIDES["DB"] = [(0.0, -0.075, 0.075, 0.0), (0.0, -0.06, 0.095, 0.022), (0.0, -0.01, 0.105, 0.026), (0.0, 0.04, 0.10, 0.024)]   # 정수리 뒤 → 앞 (서 있는 잔털 덮기)
SEE_SIDE = {k: 1 for k in SEE_GUIDES}
SEE_LOCKS = 26


def see_cap(x, f, z):
    """시스루 최대 높이: 옆(|x| > 6 cm)·목선 6 mm → 정수리 18 mm (둥근 돔, 옆이 뜨지 않게)."""
    top = smooth((z - 0.02) / 0.06) * smooth((0.068 - np.abs(x)) / 0.025)
    return 0.006 + 0.012 * top


def see_bang_locks(S, P, C, tree, rng, steps):
    """시스루 앞머리: 이마선 뿌리를 다발 22개로 나누고, 다발마다 솟았다가 휘며 내려와 끝이 뾰족하게 모이는 경로를 준다.
    다발 끝은 부채꼴로 퍼지고(가운데는 화면 왼쪽으로 쓸림), 길이는 눈썹~눈 높이에서 다발마다 다르다."""
    x, f, z = C.of(P)
    # 정수리 앞쪽(f > 0)부터 이마선까지 = 앞머리 층 하나 (윗머리와 끊기지 않게)
    bang = np.where((f > 0.0) & (np.abs(x) < 0.05) & (z > 0.03))[0]
    if len(bang) < SEE_LOCKS:
        return S, bang
    # 다발 중심: 가로로 고르게 놓지 않고 흩어서 굵기가 제각각
    xs = np.sort(rng.uniform(-0.046, 0.046, SEE_LOCKS))
    cen = np.array([bang[np.argmin(np.abs(x[bang] - xx) + 0.2 * np.abs(f[bang] - 0.05))] for xx in xs])
    d2 = (x[bang][:, None] - x[cen][None]) ** 2 + 0.05 * (f[bang][:, None] - f[cen][None]) ** 2
    own = np.argmin(d2, axis=1)
    t = np.linspace(0, 1, steps + 1)[:, None]
    for li, c in enumerate(cen):
        xr = x[c]
        xt = xr * 1.2 - 0.008 + rng.normal(0, 0.010)        # 부채꼴 + 가운데는 화면 왼쪽으로 쓸림, 다발마다 방향 다름
        zt = rng.uniform(-0.030, 0.000)
        lift = rng.uniform(0.011, 0.016)                     # 윗머리 돔 높이와 같게 (혹 없음)
        bend = rng.normal(0, 0.006)                          # 중간에서 옆으로 휘는 정도
        wp = [(xr, 0.03, 0.10, lift), (xr * 1.05 + 0.3 * bend, 0.075, 0.085, lift),
              (0.5 * (xr + xt) + bend, 0.10, 0.040, lift * 0.75),
              (xt, 0.097, zt + 0.022, 0.007), (xt + rng.normal(0, 0.003), 0.092, zt, 0.004)]
        G = guide_world(C, tree, wp, steps + 1)
        G[:, :] += np.outer(np.sin(t[:, 0] * np.pi), rng.normal(0, 0.0015, 3))
        for j in bang[own == li]:
            # 뿌리에서 가이드 첫 점으로 이어 붙이고, 끝으로 갈수록 모이되 35 %는 남겨 바늘처럼 뾰족하지 않게
            r0 = P[j]
            Gj = np.concatenate([r0[None] + (G[0] - r0)[None] * np.linspace(0, 1, 4)[:, None], G[1:]], axis=0)
            seg0 = np.linalg.norm(np.diff(Gj, axis=0), axis=1)
            c0 = np.concatenate([[0], np.cumsum(seg0)])
            q0 = np.linspace(0, c0[-1], steps + 1)
            Gj = np.stack([np.interp(q0, c0, Gj[:, k]) for k in range(3)], axis=1)
            spread = (r0 - P[c])[None] * (0.35 + 0.65 * (1 - t) ** 1.3)
            spread[0] = 0
            off = spread * (t > 0) + rng.normal(0, 0.0008, 3)[None] * np.sin(t * np.pi)
            G_ = Gj
            path = G_ + off
            # 시스루: 60 %는 이마선 위에서 끝(윗머리 숱), 40 %만 이마로 내려옴(듬성한 앞머리)
            u = rng.uniform(0.42, 0.62) if rng.random() < 0.6 else 0.8 + 0.2 * rng.random() ** 0.5
            seg = np.linalg.norm(np.diff(path, axis=0), axis=1)
            cum = np.concatenate([[0], np.cumsum(seg)])
            qq = np.linspace(0, cum[-1] * u, steps + 1)
            R = np.stack([np.interp(qq, cum, path[:, k]) for k in range(3)], axis=1)
            for i in range(1, steps + 1):
                loc, nrm, _, _ = tree.find_nearest(Vector(R[i]), 0.3)
                if loc is not None and (R[i] - np.array(loc)) @ np.array(nrm) < 0.0015:
                    R[i] = np.array(loc) + np.array(nrm) * 0.0015
            S[j] = R
    return S, bang
SEE_THIN = 0.0        # 앞머리 뿌리 가닥 중 빼는 비율 (이마가 비치게)


STYLES2 = {"pomade": (pomade, "young_caucasian_male", (0.03, 0.02, 0.014, 1), 110000),
           "gail": (gail, "young_caucasian_male", (0.03, 0.02, 0.014, 1), 150000),
           "see_g": (seethrough_base, "young_caucasian_male", (0.03, 0.02, 0.014, 1), 150000),
           "gail_g": (pomade, "young_caucasian_male", (0.03, 0.02, 0.014, 1), 150000)}
POST = {}


def grow2(P, N, C, fn, tree, rng, steps=16):
    x0, f0, z0 = C.of(P)
    keep = ~((np.abs(x0) > 0.05) & (z0 < -0.012) & (f0 > -0.03) & (f0 < 0.035))   # 귀 바로 둘레만 제외
    P, N = P[keep], N[keep]
    k = len(P)
    x, f, z = C.of(P)
    F, H, L = fn(x, f, z, C)
    # 끝 엇갈림: 긴 가닥은 길이를 0.6~1.05배로 흩어 끝이 한 높이에 모여 턱이 지지 않게
    stag = 0.9 + 0.2 * rng.random(k)
    L = L * stag
    S = np.zeros((k, steps + 1, 3))
    S[:, 0] = P
    d = unit(N * 0.85 + unit(F) * 0.15)              # 뿌리에서는 선다 → 이마선 띠가 가려진다
    seg = L / steps
    for i in range(1, steps + 1):
        p = S[:, i - 1] + d * seg[:, None]
        nn = np.zeros((k, 3))
        dist = np.zeros(k)
        for j in range(k):
            loc, nrm, _, _ = tree.find_nearest(Vector(p[j]), 0.3)
            if loc is None:
                continue
            nn[j] = nrm
            off = p[j] - np.array(loc)
            dist[j] = off @ np.array(nrm)
            if dist[j] < 0.0012:                     # 피부 안으로 못 들어감
                p[j] = np.array(loc) + np.array(nrm) * 0.0012
                dist[j] = 0.0012
        # 다음 방향: 껍질 높이까지는 바깥, 그 위로는 흐름 접선 + 살짝 안쪽(눌림)
        xi, fi, zi = C.of(p)
        Fi, Hi, _ = fn(xi, fi, zi, C)
        tang = unit(Fi - nn * (Fi * nn).sum(axis=1, keepdims=True))
        below = np.clip((Hi - dist) / np.maximum(Hi, 1e-4), -1, 1)
        d = unit(tang * 0.8 + nn * (below * 0.9)[:, None] + d * 0.35)
        S[:, i] = p
    # 생머리: 충돌로 생긴 꺾임을 가닥 방향으로 펴기(뿌리 고정, 라플라시안 8회) → 다시 피부 밖으로
    for _ in range(8):
        S[:, 1:-1] = S[:, 1:-1] * 0.5 + (S[:, :-2] + S[:, 2:]) * 0.25
        S[:, -1] = S[:, -1] * 0.5 + S[:, -2] * 0.5 + (S[:, -2] - S[:, -3]) * 0.5
    for i in range(1, steps + 1):
        for j in range(k):
            loc, nrm, _, _ = tree.find_nearest(Vector(S[j, i]), 0.3)
            if loc is not None and (S[j, i] - np.array(loc)) @ np.array(nrm) < 0.0012:
                S[j, i] = np.array(loc) + np.array(nrm) * 0.0012
    # 결: 긴 가닥(≥ 2 cm)끼리만 다발로 모으고 다발마다 방향을 조금 흐트러뜨림 — 짧게 친 옆·뒤는 건드리지 않는다
    from mathutils.kdtree import KDTree
    long_ = np.where(L >= 0.02)[0]
    if len(long_) > 200:
        m = max(80, len(long_) // 300)
        cen = rng.choice(long_, m, replace=False)
        mid = S[:, steps // 2]
        kd = KDTree(m)
        for ii, c in enumerate(cen):
            kd.insert(Vector(mid[c]), ii)
        kd.balance()
        owner = np.array([kd.find(Vector(mid[q]))[1] for q in long_])
        jitter = rng.normal(0, 0.0005, (m, 3))
        for i in range(1, steps + 1):
            t = (i / steps) ** 1.4
            S[long_, i] = S[long_, i] * (1 - 0.7 * t) + S[cen[owner], i] * (0.7 * t) + jitter[owner] * t
    return S, H


def main():
    fn, skin, col, n = STYLES2[STYLE]
    bpy.ops.wm.read_homefile(use_empty=True)
    sc = bpy.context.scene
    cam = setup(sc)
    rng = np.random.default_rng(7)
    bm = human(skin)
    bpy.context.view_layer.update()
    V, T = body_geo(bm)
    eye = eye_center(bm)
    C = Coords(eye)
    tree = BVHTree.FromPolygons([tuple(p) for p in V], [tuple(t) for t in T.tolist()])
    TREE[0], EYE[0] = tree, eye
    P, N = roots(V, T, eye, n, "full", rng)          # 두피 전체(옆·뒤 포함) — 짧은 곳은 두께·길이장이 정한다
    S, H = grow2(P, N, C, fn, tree, rng)
    if STYLE == "gail_g":
        x0, f0, z0 = C.of(P)
        keep0 = ~((np.abs(x0) > 0.05) & (z0 < -0.012) & (f0 > -0.03) & (f0 < 0.035))
        S, GUIDES = apply_guides(S, P[keep0], C, tree, rng, GAIL_GUIDES, GAIL_SIDE, GAIL_G_PART)
    if STYLE == "see_g":
        x0, f0, z0 = C.of(P)
        keep0 = ~((np.abs(x0) > 0.05) & (z0 < -0.012) & (f0 > -0.03) & (f0 < 0.035))
        S, GUIDES = apply_guides(S, P[keep0], C, tree, rng, SEE_GUIDES, SEE_SIDE, -1.0, noise=0.003, clump=0.55, cdiv=600, ridge_x=0.078, fmin=-0.10, cap_fn=see_cap)
        S, bidx = see_bang_locks(S, P[keep0], C, tree, rng, S.shape[1] - 1)
        bang = np.zeros(len(S), bool); bang[bidx] = True
        drop = bang & (rng.random(len(S)) < SEE_THIN)
        S = S[~drop]
        P = P[keep0][~drop]
    if STYLE in ("gail_g", "see_g"):
        if os.environ.get("SHOW_GUIDES"):
            for k, g in GUIDES.items():
                gob = to_curves("guide_" + k, np.repeat(g[None], 1, 0), 0.0012)
                gm = bpy.data.materials.new("gm")
                gm.use_nodes = True
                gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0.1, 0.1, 1)
                gob.data.materials.append(gm)
    if os.environ.get("DIAG") and STYLE == "see_g":
        xs, fs, zs = C.of(S[:, 0])
        for name, msk in (("side |x|>0.05", np.abs(xs) > 0.05), ("top", (np.abs(xs) <= 0.05) & (zs > 0.05)), ("back", fs < -0.05)):
            idx = np.where(msk)[0][::max(1, msk.sum() // 2000)]
            st = []
            for j in idx:
                best = 0
                for i in range(S.shape[1]):
                    loc, nrm, _, _ = tree.find_nearest(Vector(S[j, i]), 0.3)
                    if loc is not None:
                        best = max(best, (S[j, i] - np.array(loc)) @ np.array(nrm))
                st.append(best)
            st = np.array(st) * 1000
            print(f"[diag] {name} n={len(st)} p50={np.percentile(st,50):.1f}mm p90={np.percentile(st,90):.1f} max={st.max():.1f}")
        sys.exit(0)
    if os.environ.get("DIAG") and STYLE == "gail_g":
        xr, fr_, zr = C.of(P[keep0])
        ridge = smooth((0.062 - np.abs(xr)) / 0.02) * smooth((zr - (0.015 + 0.035 * np.clip(-fr_ / 0.07, 0, 1))) / 0.02)
        gd = (ridge > 0.5) & (fr_ > -0.06)
        sel = np.where((xr < -0.029))[0]
        sel = sel[::max(1, len(sel) // 4000)]
        rows = []
        for j in sel:
            best = 0; bi = 0
            for i in range(S.shape[1]):
                loc, nrm, _, _ = tree.find_nearest(Vector(S[j, i]), 0.3)
                d = (S[j, i] - np.array(loc)) @ np.array(nrm) if loc is not None else 0
                if d > best: best, bi = d, i
            px, pf, pz = C.of(S[j, bi][None])
            rows.append((best, gd[j], xr[j], fr_[j], zr[j], px[0], pf[0], pz[0]))
        rows = np.array(rows, dtype=float)
        for g in (1, 0):
            r = rows[rows[:, 1] == g]
            print(f"[diag] guided={g} n={len(r)} standoff p50={np.percentile(r[:,0],50)*1000:.1f}mm p90={np.percentile(r[:,0],90)*1000:.1f} p99={np.percentile(r[:,0],99)*1000:.1f}")
            top = r[np.argsort(-r[:, 0])[:8]]
            for t in top:
                print("   ", np.round(t * [1000, 1, 1000, 1000, 1000, 1000, 1000, 1000], 1).tolist())
        sys.exit(0)
    tipr = np.linalg.norm(S[:, -1] - C.c, axis=1)
    midr = np.linalg.norm(S[:, S.shape[1] // 2] - C.c, axis=1)
    good = (tipr < np.percentile(tipr, 99.7)) & (midr < np.percentile(midr, 99.7))
    print("[groom2] drop strays", int((~good).sum()))
    if STYLE in POST:
        x0, f0, z0 = C.of(P)
        keep = ~((np.abs(x0) > 0.05) & (z0 < -0.012) & (f0 > -0.03) & (f0 < 0.035))
        S = POST[STYLE](S, P[keep], C, rng)
    S = S[good]
    hob = to_curves("hair", S, 0.00009)
    mat = hair_mat(col)
    if STYLE in ("gail_g", "see_g"):                 # 판처럼 번쩍이는 반사를 줄임 (거칠기↑, 가닥별 흩어짐↑)
        hb = [n for n in mat.node_tree.nodes if n.bl_idname == "ShaderNodeBsdfHairPrincipled"][0]
        if "Roughness" in hb.inputs:
            hb.inputs["Roughness"].default_value = 0.55
        if "Random Roughness" in hb.inputs:
            hb.inputs["Random Roughness"].default_value = 0.35
    hob.data.materials.append(mat)
    shell = scalp_shell("scalp", V, T, eye, "full", col) if os.environ.get("SHELL_ON") else None
    for view, yaw in (("front", 0.0), ("three", 35.0), ("side", 90.0)):
        tgt = Vector((eye[0], eye[1] + 0.03, eye[2] + 0.03))
        a, dd = math.radians(yaw), 0.95
        cam.location = (tgt.x - dd * math.sin(a), tgt.y - dd * math.cos(a), tgt.z + 0.04)
        dv = tgt - cam.location
        cam.rotation_euler = (math.atan2(math.hypot(dv.x, dv.y), -dv.z), 0, math.atan2(dv.y, dv.x) - math.pi / 2)
        sc.render.filepath = os.path.join(OUT, f"{STYLE}_{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[groom2] done", len(P))


main()
