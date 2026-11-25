"""매개변수형 그루밍 스타일 (specs/088 T12~T17). 승인 4종과 같은 그루밍 엔진(grow2: 껍질 높이까지 서고 흐름을 따라 눕는다)에
바탕장(흐름 F · 두께 H · 길이 L)을 매개변수로 준다. 머리 좌표: x = 선수 왼쪽(+), f = 앞(+), z = 위(+), 원점 = 머리 중심.

영역: 윗머리 띠(ridge, 포마드와 같은 정의) · 옆(1 − ridge) · 뒤(f < −4 cm) · 앞머리(윗머리 중 f > fringe_f).
흐름 종류: back(뒤로 넘김) · forward(앞으로 빗음) · up(세움) · side(가르마에서 옆으로) · down(아래로 내림) · natural(정수리에서 방사).
"""
import numpy as np

from . import grow as G

smooth = G.smooth


def ridge_of(x, f, z, w=0.060):
    return smooth((w - np.abs(x)) / 0.02) * smooth((z - (0.015 + 0.035 * np.clip(-f / 0.07, 0, 1))) / 0.02)


def _flow(kind, x, f, z, part):
    one = np.ones_like(x)
    if kind == "back":
        return np.stack([G.side_x(x, f, 0.05), -one, 0.15 * one], -1)
    if kind == "backup":                                   # 뒤로 넘기며 살짝 세움 (언더컷·퀴프 뒤)
        return np.stack([0.0 * x, -0.85 * one, 0.5 * one], -1)
    if kind == "forward":
        return np.stack([G.side_x(x, f, 0.1), one, -0.35 * one], -1)
    if kind == "up":                                       # 088: 가운데서 좌우로 갈라지지 않게 x 성분은 연속(뿔 방지)
        return np.stack([0.15 * np.clip(x / 0.05, -1, 1), 0.15 * one, one], -1)
    if kind == "down":
        return np.stack([G.side_x(x, f, 0.25), 0.1 * one, -one], -1)
    if kind == "side":
        s = np.where(x > part, 1.0, -1.0)
        return np.stack([0.8 * s, -0.25 * one, -0.2 * one], -1)
    # natural: 정수리 소용돌이에서 방사 (앞은 앞으로, 옆은 아래로)
    r = np.stack([x, f + 0.02, z * 0.2], -1)
    r = r / (np.linalg.norm(r, axis=-1, keepdims=True) + 1e-9)
    return r + np.stack([0 * x, 0 * x, -0.35 * one], -1)


def make(top_len=0.03, side_len=0.012, back_len=None, top_flow="back", side_flow="down", part=0.0,
         vol_top=0.008, vol_side=0.004, fade=None, fringe_len=None, fringe_flow="forward", fringe_f=0.035,
         ridge_w=0.060, nape_len=None, over_ear=True):
    """→ fn(x, f, z, C) = (F, H, L). fade = (아래 길이, 시작 z, 끝 z): 옆·뒤 길이를 z에 따라 아래→위로 키운다(페이드·투블록)."""
    back_len = side_len if back_len is None else back_len
    nape_len = back_len * 0.6 if nape_len is None else nape_len

    def fn(x, f, z, C):
        rd = ridge_of(x, f, z, ridge_w)
        side = 1 - rd
        Ft = _flow(top_flow, x, f, z, part)
        Fs = _flow(side_flow, x, f, z, part)
        Fv = Ft * rd[:, None] + Fs * side[:, None]
        back = smooth((-f - 0.04) / 0.03)
        Ls = side_len * (1 - back) + back_len * back
        low = smooth((z + 0.06) / 0.05)
        Ls = nape_len + (Ls - nape_len) * low
        if fade is not None:
            lo, z0, z1 = fade
            Ls = lo + (Ls - lo) * smooth((z - z0) / max(z1 - z0, 1e-4))
        L = top_len * rd + Ls * side
        H = vol_top * rd + vol_side * side + 0.0015
        if over_ear:                                        # 귀 위 띠는 짧게 (귀를 덮지 않음)
            oe = smooth((np.abs(x) - 0.052) / 0.008) * smooth((0.028 - z) / 0.015) * smooth((f + 0.045) / 0.02) * smooth((0.06 - f) / 0.02)
            L = L * (1 - 0.7 * oe)
            H = H * (1 - 0.5 * oe)
        if fringe_len is not None:
            fr = smooth((f - fringe_f) / 0.02) * rd
            Ff = _flow(fringe_flow, x, f, z, part)
            Fv = Fv * (1 - fr[:, None]) + Ff * fr[:, None]
            L = L * (1 - fr) + fringe_len * fr
        return C.vec(Fv[:, 0], Fv[:, 1], Fv[:, 2]), H, L
    return fn


def cap(top=0.012, side=0.006):
    def c(x, f, z):
        t = smooth((z - 0.02) / 0.06) * smooth((0.068 - np.abs(x)) / 0.025)
        return side + (top - side) * t
    return c


def wave(S, C, tree, rng, amp=0.0025, lam=0.032, min_len=0.02):
    """곱슬·웨이브 (가르마 펌 웨이브와 같은 식, 진폭·파장만 매개변수)."""
    old = G.GARMA_WAVE
    G.GARMA_WAVE = (amp, lam)
    try:
        return G.garma_wave(S, C, tree, rng)
    finally:
        G.GARMA_WAVE = old


def clump(S, C, rng, n_cent=300, strength=0.6, min_len=0.01, root=False):
    """끝을 다발 중심으로 모은다 (스파이크·드레드·트위스트)."""
    from mathutils import Vector
    from mathutils.kdtree import KDTree
    seg = np.linalg.norm(np.diff(S, axis=1), axis=2).sum(axis=1)
    idx = np.where(seg > min_len)[0]
    if len(idx) < n_cent * 2:
        return S
    cen = idx[np.linspace(0, len(idx) - 1, n_cent).astype(int)]
    kd = KDTree(len(cen))
    for i, c in enumerate(cen):
        kd.insert(Vector(tuple(S[c, 0])), i)
    kd.balance()
    own = np.array([kd.find(Vector(tuple(S[j, 0])))[1] for j in idx])
    t = (np.linspace(0, 1, S.shape[1]) ** 1.2)[None, :, None]
    if root:                                                # 브레이드·드레드: 뿌리 근처에서 바로 한 가닥(로프)으로
        t = np.clip(np.linspace(0, 1, S.shape[1]) * 8, 0, 1)[None, :, None]
    S[idx] = S[idx] * (1 - strength * t) + (S[cen[own]] + (S[idx, :1] - S[cen[own], :1]) * 0.15) * (strength * t)
    return S


def helix(S, C, rng, radius=0.003, turns=4.0, min_len=0.03):
    """나선 꼬기 (코일·트위스트·브레이드 로프): 가닥 축을 따라 원을 그린다."""
    k, n, _ = S.shape
    seg = np.linalg.norm(np.diff(S, axis=1), axis=2)
    L = seg.sum(axis=1)
    idx = np.where(L > min_len)[0]
    t = np.linspace(0, 1, n)
    for j in idx:
        tan = np.gradient(S[j], axis=0)
        tan /= np.linalg.norm(tan, axis=1, keepdims=True) + 1e-9
        a = np.cross(tan, [0.0, 0.0, 1.0])
        a /= np.linalg.norm(a, axis=1, keepdims=True) + 1e-9
        b = np.cross(tan, a)
        ph = rng.uniform(0, 2 * np.pi)
        ang = 2 * np.pi * turns * t * (L[j] / 0.05) + ph
        r = radius * np.clip(t * 4, 0, 1)
        S[j] = S[j] + (a * np.cos(ang)[:, None] + b * np.sin(ang)[:, None]) * r[:, None]
    return S


def push_out(S, tree, gap=0.0015):
    from mathutils import Vector
    for j in range(len(S)):
        for i in range(1, S.shape[1]):
            loc, nrm, _, _ = tree.find_nearest(Vector(tuple(S[j, i])), 0.3)
            if loc is not None and (S[j, i] - np.array(loc)) @ np.array(nrm) < gap:
                S[j, i] = np.array(loc) + np.array(nrm) * gap
    return S


def apply_cap(S, C, tree, capf, rng):
    """위치별 최대 높이 (가닥 점의 피부 위 높이가 cap(x,f,z) × (0.6~1)을 넘으면 그 높이로 누른다 — 헬멧 방지)."""
    from mathutils import Vector
    x, f, z = C.of(S[:, 0])
    hc = capf(x, f, z) * (0.6 + 0.4 * rng.random(len(S)))
    for j in range(len(S)):
        for i in range(1, S.shape[1]):
            loc, nrm, _, _ = tree.find_nearest(Vector(tuple(S[j, i])), 0.3)
            if loc is None:
                continue
            d = (S[j, i] - np.array(loc)) @ np.array(nrm)
            if d > hc[j]:
                S[j, i] = np.array(loc) + np.array(nrm) * hc[j]
    return S


def post(capf=None, wave_amp=0.0, wave_lam=0.03, clump_n=0, clump_s=0.6, clump_min=0.01, clump_root=False, helix_r=0.0, helix_turns=4.0, drop=None, extra=None):
    """매개변수형 후처리 묶음 → styles.STYLES의 post 함수. drop(x,f,z) → True면 그 뿌리 가닥을 뺀다(라인·모히칸 옆)."""
    def fn(S, P, C, tree, rng):
        x0, f0, z0 = C.of(P)
        keep = ~((np.abs(x0) > 0.05) & (z0 < -0.012) & (f0 > -0.03) & (f0 < 0.035))
        P2 = P[keep]
        if drop is not None:
            x, f, z = C.of(P2)
            k = ~drop(x, f, z)
            S, P2 = S[k], P2[k]
        if extra is not None:
            S, P2 = extra(S, P2, C, tree, rng)
        if capf is not None:
            S = apply_cap(S, C, tree, capf, rng)
        if clump_n:
            S = clump(S, C, rng, clump_n, clump_s, clump_min, clump_root)
        if helix_r:
            S = helix(S, C, rng, helix_r, helix_turns)
        if wave_amp:
            S = wave(S, C, tree, rng, wave_amp, wave_lam)
        return S, P2
    return fn



def shape_to(target, coil_r=0.0035, coil_turns=7.0, top_only=None):
    """가닥을 목표 외곽으로 곧게 뻗게 한다 (아프로 = 타원체, 하이톱 = 납작한 원통). target(C, root, normal) → 끝점.
    뿌리 → 끝 직선 + 바깥 부풀림(가운데 0.25 × 길이) + 나선(코일) + 끝 엇갈림. 피부 밀어내기는 호출자가 apply_cap/push_out로."""
    def extra(S, P2, C, tree, rng):
        from mathutils import Vector
        k, n, _ = S.shape
        t = np.linspace(0, 1, n)
        for j in range(k):
            r0 = S[j, 0]
            loc, nrm, _, _ = tree.find_nearest(Vector(tuple(r0)), 0.3)
            nr = np.array(nrm) if loc is not None else (r0 - C.c) / (np.linalg.norm(r0 - C.c) + 1e-9)
            tip = target(C, r0, nr, rng)
            if tip is None:
                continue
            L = np.linalg.norm(tip - r0)
            line = r0[None] + (tip - r0)[None] * t[:, None] + nr[None] * (np.sin(np.pi * t) * 0.18 * L)[:, None]
            u = 0.8 + 0.2 * rng.random()
            line = r0[None] + (line - r0[None]) * u
            tan = np.gradient(line, axis=0)
            tan /= np.linalg.norm(tan, axis=1, keepdims=True) + 1e-9
            a = np.cross(tan, nr)
            a /= np.linalg.norm(a, axis=1, keepdims=True) + 1e-9
            b = np.cross(tan, a)
            ph = rng.uniform(0, 2 * np.pi)
            ang = 2 * np.pi * coil_turns * rng.uniform(0.6, 1.4) * t * (L / 0.05) + ph
            rr = coil_r * rng.uniform(0.6, 1.3) * np.clip(t * 5, 0, 1)
            S[j] = line + (a * np.cos(ang)[:, None] + b * np.sin(ang)[:, None]) * rr[:, None]
        return push_out(S, tree), P2
    return extra


def afro_target(axes=(0.135, 0.15, 0.14), lift=0.025):
    """머리 중심(조금 위) 타원체 위 점 — 뿌리에서 바깥 방향(법선·중심 방향 섞음)으로 쏜 광선이 만나는 곳. 얼굴 앞은 짧게."""
    ax = np.array(axes)

    def target(C, r0, nr, rng):
        c0 = C.c + np.array([0.0, 0.0, lift])
        x, f, z = C.of(r0[None])
        d = 0.6 * nr + 0.4 * (r0 - c0) / (np.linalg.norm(r0 - c0) + 1e-9)
        d /= np.linalg.norm(d)
        q = (r0 - c0) / ax
        v = d / ax
        A, B, Cc = v @ v, 2 * q @ v, q @ q - 1
        disc = B * B - 4 * A * Cc
        if disc <= 0:
            return None
        s = (-B + np.sqrt(disc)) / (2 * A)
        if s <= 0.004:
            return None
        s *= 0.9 + 0.1 * rng.random()
        s *= 1.0 - 0.5 * float(smooth(np.array((f[0] - 0.03) / 0.05))) * float(smooth(np.array((0.08 - z[0]) / 0.05)))   # 이마 쪽은 완만하게 짧게
        return r0 + d * s
    return target


def hightop_target(top_z=0.135, radius=0.072):
    """하이톱: 윗머리는 위로 곧게 → 평평한 윗면(머리 좌표 z = top_z), 원통 반경 밖으로는 안 나감. 옆머리(반경 밖 뿌리)는 None(그대로)."""
    def target(C, r0, nr, rng):
        x, f, z = C.of(r0[None])
        rad = np.hypot(x[0], f[0] + 0.01)
        if rad > radius or z[0] < 0.02:
            return None
        up = C.vec(np.array([0.0]), np.array([0.0]), np.array([1.0]))[0]
        h = top_z - z[0]
        out = C.vec(np.array([x[0]]), np.array([f[0] + 0.01]), np.array([0.0]))[0]
        out = out / (np.linalg.norm(out) + 1e-9)
        tip = r0 + up * h * (0.97 + 0.03 * rng.random()) + out * max(0.0, radius - rad) * 0.25
        return tip
    return target



def rows(spacing=0.016, curve=0.25, zig=0.0, strength=0.9, bump=0.0016, turns=14.0, h=0.02):
    """콘로우: 가닥을 앞 → 뒤 줄 중심선으로 모아 두피에 붙인다. 줄 = 관상면 방위각 φ = atan2(x, z + h) 칸(간격 spacing / 반지름) —
    정수리에서 옆까지 줄이 고르게 갈라진다. 뒤로 갈수록 φ를 (1 + curve × 뒤쪽 정도)배, 지그재그는 φ에 삼각파.
    줄마다 땋은 마디 = 중심선 둘레 작은 나선(bump). 가닥은 grow2(흐름 뒤로, 두께 낮게)로 이미 두피를 따라 누워 있다."""
    def extra(S, P2, C, tree, rng):
        k, n, _ = S.shape
        x0, f0, z0 = C.of(S[:, 0])
        r0 = np.hypot(x0, z0 + h)
        ph0 = np.arctan2(x0, z0 + h)
        dphi = spacing / np.maximum(np.median(r0), 0.05)
        row = np.round(ph0 / dphi)
        t = np.linspace(0, 1, n)
        for j in range(k):
            x, f, z = C.of(S[j])
            r = np.hypot(x, z + h)
            phi = np.arctan2(x, z + h)
            back = np.clip(-(f - f0[j]) / 0.12, 0, 1)
            pc = row[j] * dphi * (1 + curve * back)
            if zig:
                tri = 2 * np.abs((f / 0.03) % 1 - 0.5) - 0.5          # 주기 3 cm
                pc = pc + zig * tri / np.maximum(r, 0.05)
            w = strength * np.clip(t * 4, 0, 1)
            ang = 2 * np.pi * turns * t + rng.uniform(0, 2 * np.pi)
            phn = phi + (pc - phi) * w + (bump / np.maximum(r, 0.05)) * np.cos(ang) * w
            rn = r + bump * 0.6 * np.sin(ang) * w
            S[j] = C.c + C.vec(rn * np.sin(phn), f, rn * np.cos(phn) - h)
        return push_out(S, tree, 0.0012), P2
    return extra



def tied(tie=(0.0, -0.075, 0.065), mode="bun", bun_r=0.03, tail_len=0.15, split_z=None, standoff=0.0025, tail_clump=40):
    """묶음 (번·톱 노트·포니테일·하프 업). tie = 묶음점 방향(머리 좌표) — 그 방향 두피 위 점에 묶는다.
    가닥: 뿌리 → 묶음점 (두피를 따라 누움, standoff) → mode: bun = 묶음점 위 구(반지름 bun_r) 겉을 감는 나선,
    tail = 묶음점에서 아래·뒤로 늘어진 꼬리(tail_len, 끝으로 갈수록 다발). split_z가 있으면 뿌리 z < split_z 가닥은 묶지 않는다(하프 업)."""
    def extra(S, P2, C, tree, rng):
        from mathutils import Vector
        k, n, _ = S.shape
        d = C.vec(np.array([tie[0]]), np.array([tie[1]]), np.array([tie[2]]))[0]
        d = d / np.linalg.norm(d)
        hit = tree.ray_cast(Vector(tuple(C.c)), Vector(tuple(d)), 0.4)
        tp = (np.array(hit[0]) + np.array(hit[1]) * standoff) if hit[0] is not None else C.c + d * 0.1
        nrm_t = np.array(hit[1]) if hit[0] is not None else d
        x0, f0, z0 = C.of(S[:, 0])
        down = C.vec(np.array([0.0]), np.array([-0.08]), np.array([-1.0]))[0]
        down /= np.linalg.norm(down)
        for j in range(k):
            if split_z is not None and z0[j] < split_z + rng.normal(0, 0.01):   # 경계를 흩어 맨살 띠가 생기지 않게
                continue
            r0 = S[j, 0]
            m1 = 10
            seg1 = np.array([r0 + (tp - r0) * a for a in np.linspace(0, 1, m1)])
            # 두피를 따라: 각 점을 머리 표면 + standoff로 (머리 중심에서 바깥으로 투영)
            for i in range(1, m1 - 1):
                dd = seg1[i] - C.c
                dd /= np.linalg.norm(dd) + 1e-9
                h2 = tree.ray_cast(Vector(tuple(C.c)), Vector(tuple(dd)), 0.4)
                if h2[0] is not None:
                    seg1[i] = np.array(h2[0]) + np.array(h2[1]) * standoff * (1.0 + 0.5 * rng.random())
            if mode == "bun":
                c0 = tp + nrm_t * bun_r * 0.9
                u = rng.random() * 2 * np.pi
                turns = 1.5 + rng.random()
                th = np.linspace(0, turns * 2 * np.pi, 14) + u
                el = np.linspace(-0.8, 0.8, 14) * rng.uniform(0.6, 1.0) + rng.normal(0, 0.15)
                a1 = np.cross(nrm_t, [1.0, 0.0, 0.0])
                a1 /= np.linalg.norm(a1) + 1e-9
                a2 = np.cross(nrm_t, a1)
                rr = bun_r * rng.uniform(0.85, 1.0)
                seg2 = c0 + rr * (np.cos(el)[:, None] * (np.cos(th)[:, None] * a1 + np.sin(th)[:, None] * a2) + np.sin(el)[:, None] * nrm_t)
            else:
                L = tail_len * rng.uniform(0.8, 1.0)
                tt = np.linspace(0, 1, 14)
                seg2 = tp + down[None] * (tt * L)[:, None] + nrm_t[None] * (0.02 * np.sin(np.pi * tt * 0.5))[:, None] \
                    + rng.normal(0, 0.004, 3)[None] * tt[:, None]
            path = np.vstack([seg1, seg2[1:]])
            sl = np.linalg.norm(np.diff(path, axis=0), axis=1)
            cum = np.concatenate([[0], np.cumsum(sl)])
            q = np.linspace(0, cum[-1], n)
            S[j] = np.stack([np.interp(q, cum, path[:, c]) for c in range(3)], axis=1)
        if mode != "bun" and tail_clump:
            S = clump(S, C, rng, tail_clump, 0.6, 0.05)
        return push_out(S, tree), P2
    return extra
