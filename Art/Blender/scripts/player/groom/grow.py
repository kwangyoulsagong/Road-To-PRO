"""그루밍 v2 공통 (specs/088 T09): 2026-11-22 사진 비교로 승인된 시안(groom_v2/groom2.py·groom_trial.py)의 가닥 생성 코드를 함수 그대로 옮겼다.
좌표: 시안과 같은 Blender 틀(Z 위, −Y 정면, +X = 선수 왼쪽). 선수 좌표(X 오른쪽·Y 위·Z 정면)와는 frame.to_blender / from_blender로 바꾼다.
bpy 없이 numpy + mathutils(BVH)만 쓴다. 같은 입력·시드 → 같은 가닥(결정론)."""
import math

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

def unit(a):
    return a / (np.linalg.norm(a, axis=-1, keepdims=True) + 1e-9)


RETURN_MARGIN = [False]


def hairline(rel, n, style):
    """rel = 점 − 눈 중심 (MPFB: 앞 = −Y, 위 = +Z). 스타일별 이마선·옆선·뒤 목선."""
    x, y, z = rel[:, 0], rel[:, 1], rel[:, 2]
    fwd = -(y - 0.085)                                 # 각도 기준 = 머리 중심(눈에서 8.5 cm 뒤) — 눈 기준이면 이마가 '뒤'로 판정됨
    t = np.arctan2(np.abs(x), -fwd) / math.pi        # 0 뒤 · 0.5 옆 · 1 앞
    front, side, nape = 0.066, 0.0, -0.075
    if style == "full":
        side = -0.012
    if style in ("fade_crop", "two_block", "pomade", "gail"):
        side = 0.035                                   # 옆·뒤 아래는 짧게 친 부분 → 가닥 없음(두피 텍스처 몫)
        nape = 0.005
    # 앞(t ≥ 0.78)은 높이 일정한 평평한 이마선 — 이마가 앞으로 둥글어 각도 보간만 쓰면 가운데가 V로 내려간다
    tf = np.clip((t - 0.5) / 0.28, 0, 1)
    th = np.where(t < 0.5, nape + (side - nape) * (t / 0.5) ** 1.5, side + (front - side) * (tf * tf * (3 - 2 * tf)))
    # 남자 이마선: 거의 수평, 관자놀이 모서리만 둥글게 살짝 올라감 (V자·흔들기 없음)
    ax = np.abs(x)
    th = th + np.where(fwd > 0.10, 0.007 * np.clip((ax - 0.025) / 0.03, 0, 1) ** 2, 0.0)
    ear = (np.abs(x) > 0.05) & (z < 0.05) & (fwd > 0.02) & (fwd < 0.135)
    if style == "full":   # v2 그루밍: 관자놀이까지 심고 귀 바로 위만 뺀다
        ear = (np.abs(x) > 0.05) & (z < 0.012) & (fwd > 0.05) & (fwd < 0.125)
    if RETURN_MARGIN[0]:
        return z - th, ear
    return (z > th) & ~ear & (n[:, 2] > -0.7)


def roots(V, T, eye, n, style, rng):
    C = V[T].mean(axis=1)
    keep = (C[:, 2] > eye[2] - 0.12) & (np.linalg.norm(C[:, :2] - eye[:2], axis=1) < 0.16)
    T = T[keep]
    a, b, c = V[T[:, 0]], V[T[:, 1]], V[T[:, 2]]
    cr = np.cross(b - a, c - a)
    area = np.linalg.norm(cr, axis=1) * 0.5
    N = cr / (np.linalg.norm(cr, axis=1, keepdims=True) + 1e-12)
    idx = rng.choice(len(T), size=n * 4, p=area / area.sum())
    u, v = rng.random(n * 4), rng.random(n * 4)
    f = u + v > 1
    u[f], v[f] = 1 - u[f], 1 - v[f]
    P = a[idx] + (b[idx] - a[idx]) * u[:, None] + (c[idx] - a[idx]) * v[:, None]
    Nn = N[idx]
    ok = hairline(P - eye, Nn, style)
    return P[ok][:n], Nn[ok][:n]


def margin_of(rel, style):
    RETURN_MARGIN[0] = True
    try:
        return hairline(rel, np.tile([0.0, 0.0, 1.0], (len(rel), 1)), style)
    finally:
        RETURN_MARGIN[0] = False


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


SMALL_MAX_H = 0.008   # 작은 쪽 가닥이 피부에서 뜰 수 있는 최대 높이 (뜨는 판 방지)   # 가르마 기준 쪽 (+1 큰 쪽)


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


GAIL_G_PART = -0.029


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


GARMA_PART = 0.012    # 선수 왼쪽(+x)으로 살짝 — 6:4


def garma_base(x, f, z, C):
    """가르마 바탕(윗머리 밖): 옆·뒤를 길게 아래로 흘린다 (가이드가 안 닿는 귀 아래·목덜미 담당)."""
    F, H, L = pomade(x, f, z, C)
    ridge = smooth((0.078 - np.abs(x)) / 0.02) * smooth((z - (0.015 + 0.035 * np.clip(-f / 0.07, 0, 1))) / 0.02)
    Fd = C.vec(np.sign(x) * 0.22, -0.12 + 0 * x, -0.95 + 0 * x)
    nape = smooth((z + 0.075) / 0.03)                # 목선 맨 아래 뿌리는 짧게
    Lb = (0.024 + 0.026 * smooth((z + 0.03) / 0.06)) * (0.45 + 0.55 * nape)
    Hb = 0.004 + 0.004 * smooth((z + 0.02) / 0.05)
    w = (1 - ridge)
    F = F * (1 - w[:, None]) + Fd * w[:, None]
    L = L * (1 - w) + Lb * w
    H = H * (1 - w) + Hb * w
    return F, H, L


def _garma_guides():
    g, side = {}, {}
    p = GARMA_PART
    for s, tag in ((1, "L"), (-1, "R")):
        o = lambda a: p + s * a                       # 가르마 기준 바깥으로 a
        # 앞1: 가르마 옆 앞머리 → 솟음 → 바깥 아래로 갈라져 눈썹 바깥 위 (역V 가장자리)
        g["F1" + tag] = [(o(0.020), 0.06, 0.068, 0.0), (o(0.016), 0.088, 0.075, 0.016), (o(0.026), 0.10, 0.045, 0.018),
                         (o(0.030), 0.100, 0.018, 0.012), (o(0.036), 0.094, -0.002, 0.008)]
        # 앞0: 가르마 바로 옆 → 낮게 앞으로 넘어와 이마 위쪽을 덮고 가르마 쪽 눈썹 위에서 끝 (역V를 좁게)
        g["F0" + tag] = [(o(0.003), 0.065, 0.070, 0.0), (o(0.008), 0.092, 0.062, 0.010), (o(0.008), 0.103, 0.032, 0.010),
                         (o(0.010), 0.101, 0.010, 0.007)]
        # 앞2: 앞 바깥 → 관자놀이를 덮고 눈 높이에서 안으로 말림
        g["F2" + tag] = [(o(0.032), 0.06, 0.062, 0.0), (o(0.044), 0.075, 0.072, 0.024), (s * 0.066, 0.065, 0.035, 0.022),
                         (s * 0.077, 0.050, 0.000, 0.019), (s * 0.077, 0.045, -0.022, 0.011), (s * 0.073, 0.050, -0.032, 0.008)]
        # 정수리: 가르마에서 둥글게 넘어가 관자놀이 위
        g["D" + tag] = [(o(0.008), -0.03, 0.090, 0.0), (o(0.022), 0.02, 0.100, 0.018), (s * 0.045, 0.055, 0.080, 0.027),
                        (s * 0.064, 0.060, 0.042, 0.024), (s * 0.074, 0.055, 0.004, 0.019)]
        # 옆: 귀 위쪽을 덮고 귓불 높이에서 안으로
        g["S" + tag] = [(s * 0.050, -0.010, 0.070, 0.0), (s * 0.066, -0.010, 0.056, 0.021), (s * 0.079, 0.000, 0.020, 0.017),
                        (s * 0.081, 0.004, -0.015, 0.009), (s * 0.078, 0.008, -0.040, 0.005)]
        # 뒤 옆: 뒤통수 둥글게 → 목덜미
        g["B" + tag] = [(s * 0.030, -0.060, 0.070, 0.0), (s * 0.050, -0.080, 0.040, 0.020), (s * 0.060, -0.085, 0.000, 0.014),
                        (s * 0.055, -0.080, -0.040, 0.009), (s * 0.050, -0.072, -0.062, 0.007)]
        g["B0" + tag] = [(o(0.006), -0.070, 0.060, 0.0), (o(0.006), -0.090, 0.030, 0.020), (o(0.006), -0.095, -0.010, 0.014),
                         (o(0.006), -0.086, -0.050, 0.008)]
        # 앞0b: 가르마와 앞1 사이 → 이마 가운데 쪽으로 낮게 (역V 가장자리를 채움)
        g["F0b" + tag] = [(o(0.011), 0.062, 0.070, 0.0), (o(0.014), 0.090, 0.066, 0.012), (o(0.016), 0.102, 0.034, 0.011),
                          (o(0.019), 0.100, 0.006, 0.007)]
        for k in ("F0", "F0b", "F1", "F2", "D", "S", "B", "B0"):
            side[k + tag] = s
    return g, side


GARMA_GUIDES, GARMA_SIDE = _garma_guides()


def garma_trim(S, C, rng):
    """뒷머리 끝 다듬기: 뒤쪽 뿌리(f < −3 cm) 가닥을 0.6~1배로 무작위로 잘라 끝 단면이 일자로 안 보이게 (숱 치기)."""
    x, f, z = C.of(S[:, 0])
    idx = np.where(f < -0.03)[0]
    steps = S.shape[1] - 1
    for j in idx:
        u = 0.6 + 0.4 * rng.random() ** 0.7
        seg = np.linalg.norm(np.diff(S[j], axis=0), axis=1)
        cum = np.concatenate([[0], np.cumsum(seg)])
        q = np.linspace(0, cum[-1] * u, steps + 1)
        S[j] = np.stack([np.interp(q, cum, S[j][:, c]) for c in range(3)], axis=1)
    return S


GARMA_WAVE = (0.0025, 0.032)   # 펌 웨이브 진폭·파장 (m)


def garma_wave(S, C, tree, rng):
    """펌 웨이브: 2 cm 넘는 가닥에 가닥 방향과 두피 법선에 수직인 방향으로 사인 굴곡. 위상은 위치에 따라 천천히 바뀌어
    이웃 가닥이 같이 굽이친다(다발 결). 뿌리 쪽은 0 → 끝으로 갈수록 커짐. 끝나면 피부 밖으로 다시 민다."""
    amp, lam = GARMA_WAVE
    seg = np.linalg.norm(np.diff(S, axis=1), axis=2)
    cum = np.concatenate([np.zeros((len(S), 1)), np.cumsum(seg, axis=1)], axis=1)
    long_ = np.where(cum[:, -1] > 0.02)[0]
    x, f, z = C.of(S[:, 0])
    phase = 2.0 * np.sin(x * 70 + 1.3) + 2.0 * np.cos(f * 60 + z * 40) + rng.normal(0, 0.35, len(S))
    steps = S.shape[1] - 1
    for j in long_:
        tan = np.gradient(S[j], axis=0)
        tan /= np.linalg.norm(tan, axis=1, keepdims=True) + 1e-9
        out = unit((S[j] - C.c))
        side = np.cross(tan, out)
        side /= np.linalg.norm(side, axis=1, keepdims=True) + 1e-9
        k = np.clip(cum[j] / 0.025, 0, 1)
        a = amp * (0.8 + 0.4 * rng.random())
        w = np.sin(2 * np.pi * cum[j] / lam + phase[j])
        S[j] = S[j] + side * (a * k * w)[:, None] + out * (0.4 * a * k * np.cos(2 * np.pi * cum[j] / lam + phase[j]))[:, None]
        for i in range(1, steps + 1):
            loc, nrm, _, _ = tree.find_nearest(Vector(S[j, i]), 0.3)
            if loc is not None and (S[j, i] - np.array(loc)) @ np.array(nrm) < 0.0015:
                S[j, i] = np.array(loc) + np.array(nrm) * 0.0015
    return S


def garma_cap(x, f, z):
    """가르마 최대 높이: 정수리 20 mm, 관자놀이(눈썹~이마 높이) 16 mm, 귀 높이 아래·뒤 8 mm (귀 옆 혹 방지)."""
    top = smooth((z - 0.02) / 0.06) * smooth((0.068 - np.abs(x)) / 0.025)
    top = top * (0.55 + 0.45 * smooth((np.abs(x - GARMA_PART) - 0.004) / 0.03))   # 가르마 선 옆은 낮게 (뿔 방지)
    temple = smooth((z + 0.02) / 0.03) * smooth((f + 0.01) / 0.03)
    return 0.011 + 0.013 * temple * (1 - top) + 0.019 * top


