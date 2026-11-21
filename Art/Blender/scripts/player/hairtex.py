"""머리카락 결 텍스처 5계열 (specs/062 FR-43). parts 아틀라스 머리카락 칸(hair.REGION) 안 2열 × 3행 타일(hair.tile_rect):
직모 · 곱슬(S자 컬 다발) · 코일(작은 고리) · 브레이드(땋은 마디 줄 + 가르마) · 드레드(꼬인 로프). 6번째 칸은 예비(직모).

타일 좌표: tu 0 → 1 = 머리 둘레 정면 → 뒤 (≈ 0.28 m), tv 0 → 1 = 아래 → 정수리 (≈ 0.33 m, hair.hair_uv). 가닥 방향 = v.
채널 (skin.parts_map과 같은 약속): R 가닥 밝기, G 결 위상(0.5 = 가닥 방향 그대로, 셰이더가 이방성 방향을 흔든다), B 머리카락 마스크 1, A 거칠기.
절차 생성만 (외부 에셋 없음), 해시 노이즈라 같은 시드 → 같은 바이트.
"""
import numpy as np

from . import hair as H
from .textures import _noise

U_M, V_M = 0.28, 0.33   # 타일 한 칸의 실제 크기 (m) — 결 주기를 미터로 정한다
FINE = 0.0012           # 가닥 묶음 폭 (m) ≈ 텍셀 1.6개 — 텍셀보다 가는 가닥은 밉에서 사라지고 지표(σ 1 LoG)에도 안 잡힌다


def _smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _vn(x, y, seed, cx, cy):
    """보간 값 노이즈: 셀 크기 cx × cy (m), 쌍선형 + smoothstep (해시 정수 연산 — 결정론)."""
    gx, gy = x / cx, y / cy
    ix, iy = np.floor(gx), np.floor(gy)
    fx, fy = gx - ix, gy - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)

    def h(a, b):
        return _noise(a / 65536.0, b / 65536.0, seed, 65536)

    return (h(ix, iy) * (1 - fx) * (1 - fy) + h(ix + 1, iy) * fx * (1 - fy)
            + h(ix, iy + 1) * (1 - fx) * fy + h(ix + 1, iy + 1) * fx * fy)


def _strands(x, y, seed, w=FINE, length=0.03):
    """가는 가닥: 가로로는 가닥마다 따로(경계가 또렷), 길이 방향으로는 천천히 보간되는 밝기 (가닥 하나하나가 줄로 보인다)."""
    k = np.floor(x / w)
    gy = y / length + _noise(k / 65536.0, k * 0, seed + 9, 65536) * 7.0   # 가닥마다 길이 방향 위상
    iy = np.floor(gy)
    fy = gy - iy
    fy = fy * fy * (3 - 2 * fy)
    a = _noise(k / 65536.0, iy / 65536.0, seed, 65536)
    b = _noise(k / 65536.0, (iy + 1) / 65536.0, seed, 65536)
    return a * (1 - fy) + b * fy


def _straight(x, y, seed):
    """가는 가닥(0.7 mm) + 다발(5~8 mm, 길이 방향으로 살짝 흔들림) 사이 어두운 틈 + 길이 방향 느린 변화."""
    sway = 0.002 * (_vn(x * 0, y, seed + 7, 1.0, 0.05) - 0.5) + 0.003 * (_vn(x, y, seed + 8, 0.03, 0.06) - 0.5)
    xs = x + sway
    w = 0.0065
    k = np.floor(xs / w)
    cx = (xs / w) % 1.0
    gap = _smooth(np.abs(cx - 0.5), 0.5, 0.30)
    cid = _noise(k / 65536.0, k * 0, seed + 1, 65536)
    fine = _strands(xs, y, seed, FINE, 0.04)
    lum = 0.12 + gap * (0.25 + 0.55 * fine + 0.12 * cid)
    return lum, 0.5 + 0.35 * (cid - 0.5) + 0.15 * (fine - 0.5)


def _curl(x, y, seed):
    """S자 컬 다발: 다발마다 주기(1.8~3 cm)·위상·진폭(3~5 mm)이 다르다, 다발 폭 8~10 mm, 다발 안 가는 가닥."""
    w = 0.009
    k0 = np.floor(x / w)
    best = None
    lum = np.zeros_like(x)
    slope = np.zeros_like(x)
    for dk in (-1, 0, 1):  # 이웃 다발이 흔들려 넘어오는 몫까지
        k = k0 + dk
        r1 = _noise(k / 65536.0, k * 0, seed, 65536)
        r2 = _noise(k / 65536.0, k * 0, seed + 1, 65536)
        r3 = _noise(k / 65536.0, k * 0, seed + 2, 65536)
        per = 0.018 + 0.012 * r1
        amp = 0.003 + 0.002 * r2
        ang = y / per * 6.283 + r3 * 6.283
        cxk = (k + 0.5) * w + amp * np.sin(ang)
        dist = np.abs(x - cxk) / (0.5 * w * (0.9 + 0.2 * r2))
        body = np.clip(1.0 - dist, 0, 1)
        sel = body > lum
        lum = np.where(sel, body, lum)
        slope = np.where(sel, np.cos(ang), slope)
    fine = _strands(x + 0.004 * slope, y, seed + 3, FINE, 0.012)
    out = 0.10 + _smooth(lum, 0.0, 0.5) * (0.30 + 0.55 * fine)
    return out, 0.5 + 0.4 * slope


def _coil(x, y, seed):
    """작은 고리 (지름 ≈ 5 mm) 격자 + 흔들림: 고리 테두리 밝고 가운데 어둡다."""
    cell = 0.005
    gx, gy = x / cell, y / cell
    ix, iy = np.floor(gx), np.floor(gy)
    jx = _noise(ix / 4096.0, iy / 4096.0, seed, 4096) - 0.5
    jy = _noise(ix / 4096.0, iy / 4096.0, seed + 1, 4096) - 0.5
    dx, dy = gx - ix - 0.5 - 0.3 * jx, gy - iy - 0.5 - 0.3 * jy
    r = np.sqrt(dx * dx + dy * dy)
    ring = np.exp(-((r - 0.32) / 0.11) ** 2)
    ang = np.arctan2(dy, dx)
    lum = 0.08 + 0.75 * ring * (0.55 + 0.45 * np.cos(ang * 2 + jx * 6)) + 0.08 * _strands(x, y, seed + 2, FINE, 0.002)
    return lum, 0.5 + 0.45 * np.sin(ang)


def _braid(x, y, seed):
    """브레이드 줄 (간격 1.6 cm, 사이 가르마 2 mm 어두움), 줄 안 땋은 마디: 좌우 엇갈린 비스듬한 타원 (주기 1.1 cm)."""
    w, per = 0.016, 0.011
    k = np.floor(x / w)
    cx = (x / w) % 1.0 - 0.5                         # -0.5 ~ 0.5
    part = _smooth(np.abs(cx), 0.5, 0.40)            # 가르마(줄 사이) 0
    py = (y / per + 0.5 * (cx > 0)) % 1.0 - 0.5
    sx = np.abs(cx) * 2 - 0.5                         # 반쪽 안 위치
    lobe = 1.0 - np.clip((sx / 0.55) ** 2 + ((py + 0.6 * sx * np.sign(cx)) / 0.42) ** 2, 0, 1)
    fine = _strands(x, y, seed + 5, FINE, 0.004)
    lum = 0.10 + part * (0.20 + 0.55 * lobe * (0.65 + 0.35 * fine))
    return lum, 0.5 + 0.4 * np.sign(cx) * (lobe > 0.1)


def _dread(x, y, seed):
    """드레드 로프 (지름 ≈ 1.2 cm): 원통 음영 + 꼬임 줄무늬(비스듬, 주기 9 mm) + 잔털."""
    w = 0.012
    k = np.floor(x / w)
    jit = _noise(k / 64.0, k * 0, seed, 64)
    cx = (x / w) % 1.0 - 0.5
    cyl = np.sqrt(np.clip(1.0 - (cx * 2.1) ** 2, 0, 1))
    twist = 0.5 + 0.5 * np.sin((y + cx * 0.012) / 0.009 * 6.283 + jit * 6.283)
    fuzz = _strands(x, y, seed + 6, FINE, 0.002)
    lum = 0.10 + cyl * (0.30 + 0.30 * twist + 0.20 * fuzz)
    return lum, 0.5 + 0.35 * (twist - 0.5)


FUNCS = {"straight": _straight, "curl": _curl, "coil": _coil, "braid": _braid, "dread": _dread}


def fill(out, u, v, seed=0):
    """out (H, W, 4) 실수 배열의 머리카락 칸을 결 타일로 채운다. u, v = parts 아틀라스 좌표(0~1, v 위)."""
    u0, v0, u1, v1 = H.REGION
    reg = (u >= u0) & (u < u1) & (v >= v0) & (v < v1)
    out[..., 0] = np.where(reg, 0.45, out[..., 0])
    out[..., 1] = np.where(reg, 0.5, out[..., 1])
    out[..., 2] = np.where(reg, 1.0, out[..., 2])
    out[..., 3] = np.where(reg, 0.45, out[..., 3])
    slots = list(H.STRANDS) + ["straight"] * (H.TILE_COLS * H.TILE_ROWS - len(H.STRANDS))
    w, h = (u1 - u0) / H.TILE_COLS, (v1 - v0) / H.TILE_ROWS
    for i, strand in enumerate(slots):
        c, r = i % H.TILE_COLS, i // H.TILE_COLS
        cu0, cv1 = u0 + c * w, v1 - r * h
        m = (u >= cu0) & (u < cu0 + w) & (v >= cv1 - h) & (v < cv1)
        if i < len(H.STRANDS):  # 실제 UV 영역(여백 안쪽) 기준으로 미터 좌표 — 여백은 같은 무늬가 이어진다
            ru0, rv0, ru1, rv1 = H.tile_rect(strand)
        else:
            ru0, rv0, ru1, rv1 = cu0, cv1 - h, cu0 + w, cv1
        x = (u - ru0) / (ru1 - ru0) * U_M
        y = (v - rv0) / (rv1 - rv0) * V_M
        lum, ph = FUNCS[strand](x, y, seed * 7 + 101 + i * 13)
        # 가닥마다 결 위상을 흔든다 (가닥 단위로 하이라이트가 끊긴다 — 클로즈업 결 고주파, FR-43)
        ph = 0.5 + 0.3 * (ph - 0.5) + 2.4 * (_strands(x, y, seed * 7 + 151 + i, FINE, 0.008) - 0.5)
        lum = lum * (0.45 + 1.1 * _strands(x, y, seed * 7 + 171 + i, FINE, 0.006))   # 가닥마다 밝기도 다르다
        out[..., 0] = np.where(m, np.clip(lum, 0, 1), out[..., 0])
        out[..., 1] = np.where(m, np.clip(ph, 0, 1), out[..., 1])
        out[..., 3] = np.where(m, 0.40 + 0.12 * (strand in ("coil", "dread", "braid")), out[..., 3])
    return out
