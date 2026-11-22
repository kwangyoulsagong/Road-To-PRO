"""공유 결 아틀라스 (specs/088 FR-10, T10): parts.png 머리카락 칸(셸 타일 6)·카드 칸(카드 타일 6)을 그루밍 가닥 모양으로 굽는다.

가닥 모양 = 그 결 계열 스타일의 표본 가닥을 현(뿌리 → 끝) 기준 2D 옆 편차 곡선으로 편 것. 아직 그루밍 스타일이 없는 계열은
같은 방식의 해석 곡선(웨이브 = 사인, 곱슬·코일 = 나선 투영, 브레이드 = 마디, 드레드 = 굵은 로프)으로 만든다.
채널 (062 skin.parts_map 약속 그대로): 셸 = R 가닥 밝기 · G 결 위상(0.5 = 흐름 그대로) · B 머리카락 마스크 1 · A 거칠기(곱슬일수록 큼),
카드 = R 가닥 밝기 · G 알파(가닥 있음 1 / 없음 0, 뿌리 v=1 → 끝 v=0으로 듬성) · B 1 · A 거칠기.
"""
import numpy as np

from . import bake

ROUGH = {"straight": 0.34, "wave": 0.38, "curl": 0.50, "coil": 0.56, "braid": 0.54, "dread": 0.60}
AMP = {"straight": 0.0, "wave": 0.08, "curl": 0.16, "coil": 0.22, "braid": 0.12, "dread": 0.05}   # 해석 곡선 옆 진폭 (타일 폭 비)
FREQ = {"straight": 0.0, "wave": 2.0, "curl": 5.0, "coil": 9.0, "braid": 6.0, "dread": 3.0}       # 타일 길이당 굽이 수
WIDTH = {"straight": 0.5, "wave": 0.55, "curl": 0.6, "coil": 0.7, "braid": 1.4, "dread": 2.0}      # 가닥 굵기 (px) — 088 T23: 결 고주파(062 FR-43)


def profiles(strands, m=64):
    """표본 가닥 (k, n, 3) → 옆 편차 곡선 (k, m) (현 길이로 나눔)."""
    out = []
    for s in strands:
        a, b = s[0], s[-1]
        ch = b - a
        L = np.linalg.norm(ch)
        if L < 1e-4:
            continue
        ax = ch / L
        rel = s - a
        along = rel @ ax
        lat = rel - along[:, None] * ax
        # 옆 편차 중 가장 큰 축으로 투영
        _, _, vt = np.linalg.svd(lat - lat.mean(axis=0), full_matrices=False)
        dev = lat @ vt[0]
        t = np.linspace(0, along[-1], m)
        out.append(np.interp(t, along, dev) / L)
    return np.array(out) if out else np.zeros((0, m))


def _analytic(strand, k, m, rng):
    t = np.linspace(0, 1, m)
    ph = rng.uniform(0, 2 * np.pi, (k, 1))
    a = AMP[strand] * rng.uniform(0.6, 1.2, (k, 1))
    f = FREQ[strand] * rng.uniform(0.85, 1.15, (k, 1))
    return a * np.sin(2 * np.pi * f * t[None] + ph)


def _draw(img, us, vs, width, val, chan):
    """폴리라인 점들을 굵기 width로 찍는다 (최대값 합성, 결정론)."""
    h, w = img.shape[:2]
    r = max(1, int(np.ceil(width)))
    xs = (us * w).astype(int)
    ys = ((1 - vs) * h).astype(int)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            wgt = max(0.0, 1.0 - np.hypot(dx, dy) / (width + 0.5))
            if wgt <= 0:
                continue
            x = np.clip(xs + dx, 0, w - 1)
            y = np.clip(ys + dy, 0, h - 1)
            for c, v in zip(chan, val):
                np.maximum.at(img[..., c], (y, x), v * wgt)


CLUMP_PX = {"straight": 6, "wave": 7, "curl": 7, "coil": 6, "braid": 10, "dread": 13}    # 다발 폭 (px) — 062 hairtex 6.5 mm/0.28 m 비율
FINE_PX = 1.0                                                                            # 가는 가닥 폭 (px)


def _hash(a, b, seed):
    """정수 격자 해시 → 0~1 (결정론)."""
    h = (a.astype(np.int64) * 73856093) ^ (b.astype(np.int64) * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def tile_shell(strand, size, prof, seed):
    """셸 타일 (장 방식, 062 hairtex와 같은 구조): 다발 띠 사이 어두운 틈 + 다발 안 가는 가닥(길이 방향으로 천천히 변하는 밝기).
    다발 중심은 그 결 계열 그루밍 가닥의 옆 편차 곡선(prof)을 따라 굽이친다 → 세로(v = 결 방향) 이방성.
    셰이더가 frac으로 반복하므로 **가로·세로 주기적**(이음매 없음): 다발 수·가닥 수·굽이 주기·밝기 구간이 타일 크기로 나누어떨어진다."""
    rng = np.random.default_rng(seed)
    h, w = size
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    n_cl = max(4, int(round(w / CLUMP_PX[strand])))
    W = w / n_cl
    pool = prof if len(prof) >= 8 else _analytic(strand, 64, 96, rng)
    sway = np.zeros((n_cl, h))
    for k in range(n_cl):
        pr = pool[rng.integers(len(pool))]
        reps = int(rng.integers(1, 3))                        # 세로 굽이 반복 수 (정수 → 주기적)
        t = ((ys[:, 0] / h) * reps + rng.random()) % 1.0
        prc = np.concatenate([pr, pr[:1]])                    # 곡선 끝을 처음과 이어 붙여 주기적으로
        prc = prc - np.linspace(0, 1, len(prc)) * (prc[-1] - prc[0])
        sway[k] = np.interp(t, np.linspace(0, 1, len(prc)), prc) * W * (0.6 + AMP[strand] * 6.0)
    k0 = np.floor(xs / W).astype(int)
    n_fine = max(1, int(round(W / FINE_PX)))
    Ny = max(4, int(round(h / (h * 0.05))))                   # 길이 방향 밝기 구간 수 (정수 → 주기적)
    L = h / Ny
    lum = np.zeros((h, w))
    gph = np.full((h, w), 0.5)
    best = np.full((h, w), -1.0)
    for dk in (-1, 0, 1):
        kk = k0 + dk
        k = np.mod(kk, n_cl)
        cx = (kk + 0.5) * W + sway[k, ys.astype(int)]
        dist = np.abs(xs - cx) / (0.5 * W)
        body = np.clip(1.0 - dist, 0, 1)
        sel = body > best
        best = np.where(sel, body, best)
        kf = np.mod(np.floor((xs - cx) / FINE_PX), n_fine * 4)
        gy = ys / L + np.floor(_hash(kf + k * 97, kf * 0, seed + 9) * Ny)
        iy = np.floor(gy)
        fy = gy - iy
        fy = fy * fy * (3 - 2 * fy)
        fine = _hash(kf + k * 97, np.mod(iy, Ny), seed) * (1 - fy) + _hash(kf + k * 97, np.mod(iy + 1, Ny), seed) * fy
        cid = _hash(k, k * 0, seed + 1)
        gap = np.clip((1.0 - dist) / 0.4, 0, 1)
        gap = gap * gap * (3 - 2 * gap)
        lum = np.where(sel, 0.02 + gap * (0.12 + 0.8 * fine + 0.08 * cid), lum)
        gph = np.where(sel, np.clip(0.5 + 3.0 * (fine - 0.5) + 0.3 * (cid - 0.5), 0.05, 0.95), gph)
    out = np.zeros((h, w, 4))
    out[..., 0] = lum
    out[..., 1] = gph
    out[..., 2] = 1.0
    out[..., 3] = ROUGH[strand]
    return out[::-1]   # 행 0 = 위 (v 1)


def tile(strand, size, card, prof, seed):
    """→ (H, W, 4) 0~1 타일. 셸은 장 방식(tile_shell), 카드는 가닥 폴리라인(뿌리 v=1 → 끝 v=0 듬성)."""
    if not card:
        return tile_shell(strand, size, prof, seed)
    rng = np.random.default_rng(seed)
    h, w = size
    img = np.zeros((h, w, 4))
    img[..., 2] = 1.0
    img[..., 3] = ROUGH[strand]
    img[..., 0] = 0.0 if card else 0.05
    if card:
        img[..., 1] = 0.0
    else:                                                   # 셸: 결 방향(v)으로 늘인 줄무늬 잡음 위상 — 가닥 사이 잔결 반짝임 (062 FR-43 고주파)
        nz = rng.random((h, w))
        k = 7
        cs = np.cumsum(np.pad(nz, ((k, k), (0, 0)), mode="wrap"), axis=0)
        streak = (cs[2 * k:] - cs[:-2 * k])[:h] / (2 * k)
        img[..., 1] = np.clip(0.5 + 1.6 * (streak - 0.5) * 2.0, 0.1, 0.9)
    n = 950 if not card else 300
    m = 96
    pool = prof if len(prof) >= 8 else _analytic(strand, 64, m, rng)
    for i in range(n):
        p = pool[rng.integers(len(pool))]
        p = np.interp(np.linspace(0, 1, m), np.linspace(0, 1, len(p)), p)
        u0 = rng.random()
        if card:
            reach = 0.25 + 0.75 * rng.random() ** 0.6               # 이 가닥이 끝 쪽으로 얼마나 내려오는가
            v = np.linspace(1.0, 1.0 - reach, m)
            u = u0 + p * 0.5 * (h / w)
        else:
            v0 = rng.random()
            v = (v0 + np.linspace(0, 0.6, m)) % 1.0
            u = (u0 + p * 0.6 * (h / w)) % 1.0
        u = np.clip(u, 0, 0.999)
        bright = 0.2 + 0.8 * rng.random()
        phase = 0.5 + np.clip(np.gradient(p) * 300, -0.45, 0.45)   # 결 위상: 가닥 굽이 기울기 (062 FR-43 고주파)
        dense = np.linspace(0, 1, m * 4)
        U = np.interp(dense, np.linspace(0, 1, m), u)
        V = np.interp(dense, np.linspace(0, 1, m), v)
        PH = np.interp(dense, np.linspace(0, 1, m), phase)
        if card:
            _draw(img, U, V, WIDTH[strand], (bright, 1.0), (0, 1))
        else:
            _draw(img, U, V, WIDTH[strand], (bright,), (0,))
            hh, ww = img.shape[:2]
            xs = np.clip((U * ww).astype(int), 0, ww - 1)
            ys = np.clip(((1 - V) * hh).astype(int), 0, hh - 1)
            img[ys, xs, 1] = PH
    return img


def fill(out, u, v, samples, seed=0):
    """parts 배열 out (S,S,4)에 셸·카드 타일을 쓴다. samples = {결 계열: 표본 가닥 (k,n,3)}."""
    S = out.shape[0]
    for i, strand in enumerate(bake.STRANDS):
        prof = profiles(samples[strand]) if strand in samples else np.zeros((0, 64))
        for card, rect in ((False, bake.shell_rect(strand)), (True, bake.card_rect(strand))):
            x0, y0, x1, y1 = rect
            c0, c1 = int(round(x0 * S)), int(round(x1 * S))
            r0, r1 = int(round((1 - y1) * S)), int(round((1 - y0) * S))
            t = tile(strand, (r1 - r0, c1 - c0), card, prof, seed + 17 * i + (100 if card else 0))
            out[r0:r1, c0:c1] = t
            # 여백(PAD)을 채운다: 셸 = 주기 타일 감싸기(셰이더 반복·밉이 여백을 읽어도 이음매·밝은 선 없음), 카드 = 가장자리 복제
            p = int(round(bake.PAD * S))
            ry = np.arange(r0 - p, r1 + p)
            rx = np.arange(c0 - p, c1 + p)
            if card:
                iy, ix = np.clip(ry - r0, 0, r1 - r0 - 1), np.clip(rx - c0, 0, c1 - c0 - 1)
            else:
                iy, ix = np.mod(ry - r0, r1 - r0), np.mod(rx - c0, c1 - c0)
            ok_y = (ry >= 0) & (ry < S)
            ok_x = (rx >= 0) & (rx < S)
            out[np.ix_(ry[ok_y], rx[ok_x])] = t[np.ix_(iy[ok_y], ix[ok_x])]
