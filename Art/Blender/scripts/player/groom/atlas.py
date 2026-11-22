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
WIDTH = {"straight": 1.2, "wave": 1.3, "curl": 1.4, "coil": 1.5, "braid": 2.6, "dread": 3.4}      # 가닥 굵기 (px)


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


def tile(strand, size, card, prof, seed):
    """→ (H, W, 4) 0~1 타일."""
    rng = np.random.default_rng(seed)
    h, w = size
    img = np.zeros((h, w, 4))
    img[..., 2] = 1.0
    img[..., 3] = ROUGH[strand]
    img[..., 0] = 0.0 if card else 0.35
    img[..., 1] = 0.0 if card else 0.5
    n = 220 if not card else 140
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
        bright = 0.45 + 0.5 * rng.random()
        phase = 0.5 + np.clip(np.gradient(p) * 40, -0.4, 0.4)
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
            out[r0:r1, c0:c1] = tile(strand, (r1 - r0, c1 - c0), card, prof, seed + 17 * i + (100 if card else 0))
