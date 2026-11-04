"""선수 텍스처 생성 (specs/043 FR-02·12·20·24). numpy로 그리고 직접 PNG로 쓴다(외부 파일 없음, 같은 입력 → 같은 바이트).

- kit_mask_<패턴>.png (1024², RGBA): R 주색 · G 보조색 · B 포인트색 가중치(합 1), A = 1 팀 색 / 0 축구화(고정색).
  패턴 6종(가상, 실존 클럽 디자인 재현 아님): plain, stripes(세로줄), hoops(가로줄), halves(반반), yoke(어깨), sash(사선 띠).
  공통: 반바지 = 보조색, 양말 = 주색 + 윗단 포인트, 칼라·소매 끝 = 포인트.
- kit_normal.png (512², RGB): 원단 직조 노말(탄젠트 공간, 타일링).
- body.png (1024², RGBA): R 피부·머리카락 명암 디테일, G 머리카락 마스크, B 얼굴 특징 어둡게(1 = 그대로), A 눈 흰자 마스크.
  얼굴 칸 8개(uv.BODY_REGIONS["faces"], 4×2): 가상 얼굴 프리셋 — 눈·눈썹·코·입 도형 조합. 실존 인물 아님.
영역 좌표는 uv.py와 같다. PNG 텍스처 좌표: 행 0 = v 1(위).
"""
import os
import struct
import zlib

import numpy as np

from . import uv as U

KIT_SIZE = 1024
NORMAL_SIZE = 512
BODY_SIZE = 1024
PATTERNS = ("plain", "stripes", "hoops", "halves", "yoke", "sash")


# ---------------------------------------------------------------- PNG

def write_png(path, arr):
    """arr: (H, W, C) uint8, C ∈ {1, 3, 4}. 압축 수준 고정, 시간 청크 없음."""
    h, w = arr.shape[:2]
    c = 1 if arr.ndim == 2 else arr.shape[2]
    color = {1: 0, 3: 2, 4: 6}[c]
    raw = b"".join(b"\x00" + arr[y].tobytes() for y in range(h))

    def chunk(t, data):
        return struct.pack(">I", len(data)) + t + data + struct.pack(">I", zlib.crc32(t + data) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, color, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)


def _grid(n):
    """픽셀 중심 (u, v) 격자, v 위쪽 (행 0 = v 최대)."""
    c = (np.arange(n) + 0.5) / n
    u, v = np.meshgrid(c, c[::-1])
    return u, v


def _in(u, v, r):
    u0, v0, u1, v1 = r
    return (u >= u0) & (u < u1) & (v >= v0) & (v < v1)


def _local(u, v, r):
    u0, v0, u1, v1 = r
    return (u - u0) / (u1 - u0), (v - v0) / (v1 - v0)


def _to8(x):
    return np.clip(np.round(x * 255.0), 0, 255).astype(np.uint8)


# ---------------------------------------------------------------- 키트

def kit_mask(pattern):
    u, v = _grid(KIT_SIZE)
    R = np.zeros_like(u)
    G = np.zeros_like(u)
    Bc = np.zeros_like(u)
    A = np.ones_like(u)
    KR = U.KIT_REGIONS
    shirt = _in(u, v, KR["shirtFront"]) | _in(u, v, KR["shirtBack"])
    front = _in(u, v, KR["shirtFront"])
    # 셔츠 판의 각도 비율 f(0~1, 0 = +X, 0.25 정면, 0.5 −X, 0.75 등)와 높이 비율 h
    f = np.where(front, u, u)  # 앞판 u∈[0,0.5] = f, 뒤판 u∈[0.5,1] = f
    h = np.where(shirt, (v - 0.5) / 0.5, 0.0)
    sec = np.zeros_like(u, dtype=bool)
    if pattern == "stripes":
        sec = shirt & (np.floor(f * 24.0) % 2 == 1)
    elif pattern == "hoops":
        sec = shirt & (np.floor(h * 7.0) % 2 == 1)
    elif pattern == "halves":
        sec = shirt & (f >= 0.25) & (f < 0.75)  # 선수 왼쪽 절반
    elif pattern == "yoke":
        sec = shirt & (h > 0.80)
    elif pattern == "sash":
        fl, hl = _local(u, v, KR["shirtFront"])
        bl, bh = _local(u, v, KR["shirtBack"])
        sec = (front & (np.abs(hl - (1.0 - fl) * 0.9) < 0.09)) | (_in(u, v, KR["shirtBack"]) & (np.abs(bh - bl * 0.9) < 0.09))
    R = np.where(shirt, 1.0, R)
    R = np.where(sec, 0.0, R)
    G = np.where(sec, 1.0, G)
    collar = shirt & (h > 0.96)
    for key in ("sleeveL", "sleeveR"):
        m = _in(u, v, KR[key])
        _, lv = _local(u, v, KR[key])
        cuff = m & (lv > 0.88)
        body = m & ~cuff
        if pattern == "yoke":
            G = np.where(body, 1.0, G)
        else:
            R = np.where(body, 1.0, R)
        Bc = np.where(cuff, 1.0, Bc)
    for key in ("shortsWaist", "shortsL", "shortsR"):
        G = np.where(_in(u, v, KR[key]), 1.0, G)
    for key in ("sockL", "sockR"):
        m = _in(u, v, KR[key])
        _, lv = _local(u, v, KR[key])
        top = m & (lv > 0.80) & (lv < 0.90)
        R = np.where(m & ~top, 1.0, R)
        Bc = np.where(top, 1.0, Bc)
    # 칼라는 포인트색
    R = np.where(collar, 0.0, R)
    G = np.where(collar, 0.0, G)
    Bc = np.where(collar, 1.0, Bc)
    boots = _in(u, v, KR["boots"])
    A = np.where(boots, 0.0, A)
    R = np.where(boots, 0.0, R)
    # 영역 밖(빈 공간)은 주색 — 밉맵 번짐 방지
    used = shirt | boots | collar
    for key in ("sleeveL", "sleeveR", "shortsWaist", "shortsL", "shortsR", "sockL", "sockR"):
        used |= _in(u, v, KR[key])
    R = np.where(~used, 1.0, R)
    return np.stack([_to8(R), _to8(G), _to8(Bc), _to8(A)], axis=-1)


def kit_normal():
    n = NORMAL_SIZE
    c = (np.arange(n) + 0.5) / n
    x, y = np.meshgrid(c, c)
    k = 2 * np.pi * 32
    # 날실·씨실이 번갈아 오르내리는 직조 높이장
    hgt = 0.5 * (np.sin(k * x) * np.sign(np.sin(k * y / 2 + 1e-9)) + np.sin(k * y) * np.sign(np.sin(k * x / 2 + 1e-9)))
    gx = np.gradient(hgt, axis=1) * 6.0
    gy = np.gradient(hgt, axis=0) * 6.0
    nz = 1.0 / np.sqrt(1 + gx * gx + gy * gy)
    nx, ny = -gx * nz, gy * nz
    return np.stack([_to8(nx * 0.5 + 0.5), _to8(ny * 0.5 + 0.5), _to8(nz * 0.5 + 0.5)], axis=-1)


# ---------------------------------------------------------------- 몸

# 얼굴 프리셋 8종 (가상 얼굴, 실존 인물 아님): 눈 간격·눈 크기·눈썹 두께·눈썹 기울기·코 폭·입 폭·입술 두께·수염 자국
FACE_PRESETS = (
    (0.032, 1.00, 1.00, 0.00, 1.00, 1.00, 1.00, 0.0),
    (0.030, 0.92, 1.35, 0.10, 1.10, 0.92, 1.20, 0.5),
    (0.034, 1.08, 0.85, -0.06, 0.92, 1.08, 0.90, 0.0),
    (0.031, 0.88, 1.25, 0.05, 1.20, 1.00, 1.30, 0.8),
    (0.033, 1.04, 0.95, -0.08, 0.95, 0.88, 1.00, 0.2),
    (0.030, 0.95, 1.45, 0.12, 1.05, 1.12, 1.10, 0.0),
    (0.035, 1.12, 1.05, 0.00, 0.88, 0.96, 0.85, 0.6),
    (0.032, 0.96, 1.15, -0.03, 1.15, 1.04, 1.25, 0.1),
)


def _soft(d, edge=0.25):
    """d ≤ 1 안쪽 1, 바깥으로 부드럽게 0."""
    return np.clip((1.0 + edge - d) / edge, 0.0, 1.0)


def _ell(X, Y, cx, cy, rx, ry):
    return ((X - cx) / rx) ** 2 + ((Y - cy) / ry) ** 2


def face_cell(fu, fv, p):
    """얼굴 칸 하나 → (어둡게 B, 흰자 A). 좌표는 미터(uv.face와 같은 투영): X = 좌우, Y = 높이."""
    gap, esz, bth, btilt, nose, mw, lip, stub = p
    X = fu * (2 * U.FACE_X) - U.FACE_X
    Y = U.FACE_Y0 + fv * (U.FACE_Y1 - U.FACE_Y0)
    dark = np.zeros_like(X)
    white = np.zeros_like(X)
    ey = 1.713
    for s in (-1, 1):
        cx = s * gap
        sc = _ell(X, Y, cx, ey, 0.0135 * esz, 0.0058 * esz)
        white = np.maximum(white, _soft(sc, 0.15))
        iris = _ell(X, Y, cx, ey - 0.0004, 0.0056 * esz, 0.0056 * esz)
        dark = np.maximum(dark, 0.80 * _soft(iris, 0.2))
        dark = np.maximum(dark, 0.95 * _soft(_ell(X, Y, cx, ey - 0.0004, 0.0024, 0.0024), 0.3))
        # 윗눈꺼풀 선: 흰자 윗부분 테두리
        lid = _ell(X, Y, cx, ey + 0.0016, 0.0145 * esz, 0.0052 * esz)
        dark = np.maximum(dark, 0.70 * np.clip((1.0 - np.abs(lid - 1.0) * 5.0), 0, 1) * (Y > ey))
        # 눈썹: 바깥쪽이 기울기만큼 올라가거나 내려감
        tilt = btilt * (X - cx) * s / 0.02 * 0.004
        brow = _ell(X, Y - tilt, cx + s * 0.002, ey + 0.021, 0.0175, 0.0032 * bth)
        dark = np.maximum(dark, 0.78 * _soft(brow, 0.35))
        # 콧구멍
        dark = np.maximum(dark, 0.55 * _soft(_ell(X, Y, s * 0.0075 * nose, 1.668, 0.0035, 0.0022), 0.4))
        # 콧방울 옆 음영
        dark = np.maximum(dark, 0.18 * _soft(_ell(X, Y, s * 0.013 * nose, 1.675, 0.006, 0.010), 1.0))
    white = white * (1.0 - dark)  # 눈동자 위는 흰자 아님
    # 코끝 아래 그림자, 입(입술 사이 선 + 아랫입술 음영 + 윗입술)
    dark = np.maximum(dark, 0.22 * _soft(_ell(X, Y, 0.0, 1.662, 0.010 * nose, 0.004), 0.8))
    dark = np.maximum(dark, 0.62 * _soft(_ell(X, Y, 0.0, 1.636, 0.021 * mw, 0.0016), 0.5))
    dark = np.maximum(dark, 0.30 * _soft(_ell(X, Y, 0.0, 1.640, 0.018 * mw, 0.0035 * lip), 0.6))
    dark = np.maximum(dark, 0.22 * _soft(_ell(X, Y, 0.0, 1.630, 0.016 * mw, 0.0045 * lip), 0.8))
    # 턱 아래 음영
    dark = np.maximum(dark, 0.15 * _soft(_ell(X, Y, 0.0, 1.592, 0.05, 0.012), 1.0))
    if stub > 0:
        # 수염 자국: 턱·입 주변, 입술은 비움
        beard = _soft(_ell(X, Y, 0.0, 1.618, 0.060, 0.040), 0.6) * (1.0 - _soft(_ell(X, Y, 0.0, 1.636, 0.026, 0.009), 0.3))
        speck = _noise(fu * 3.0, fv * 3.0, 11, 256)
        dark = np.maximum(dark, 0.22 * stub * beard * (0.6 + 0.4 * speck))
    return 1.0 - dark, white


def _noise(u, v, seed, scale):
    """해시 기반 값 노이즈(결정론, numpy 버전 무관 정수 연산)."""
    x = np.floor(u * scale).astype(np.int64)
    y = np.floor(v * scale).astype(np.int64)
    h = (x * 374761393 + y * 668265263 + seed * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def body(seed=0):
    u, v = _grid(BODY_SIZE)
    R = 0.92 + 0.06 * _noise(u, v, 1 + 7 * seed, 256) + 0.02 * _noise(u, v, 2 + 7 * seed, 64)
    G = np.zeros_like(u)
    Bc = np.ones_like(u)
    BR = U.BODY_REGIONS
    hair = _in(u, v, BR["hair"])
    hu, hv = _local(u, v, BR["hair"])
    strands = 0.75 + 0.25 * _noise(hu * 4, hv * 0.25, 3 + 7 * seed, 256)
    R = np.where(hair, strands, R)
    G = np.where(hair, 1.0, G)
    faces = _in(u, v, BR["faces"])
    fu, fv = _local(u, v, BR["faces"])
    cu = np.floor(fu * 4.0)
    cv = np.floor(fv * 2.0)
    lu, lv = fu * 4.0 - cu, fv * 2.0 - cv
    # 칸 번호: 위 줄 왼쪽부터 0~3, 아래 줄 4~7 (칸 0 = [0,0.75]–[0.25,1])
    idx = (1 - cv) * 4 + cu
    A = np.zeros_like(u)
    for i, p in enumerate(FACE_PRESETS):
        m = faces & (idx == i)
        d, w = face_cell(lu, lv, p)
        Bc = np.where(m, d, Bc)
        A = np.where(m, w, A)
    return np.stack([_to8(R), _to8(G), _to8(Bc), _to8(A)], axis=-1)


def write_all(out_dir, seed=0, R=None):
    """→ {파일 이름: (가로, 세로)}. 056: 몸 텍스처는 skin.py(피부·노멀·클로즈업·parts) — R(기준 선수) 필요."""
    out = {}
    for p in PATTERNS:
        name = f"kit_mask_{p}.png"
        write_png(os.path.join(out_dir, name), kit_mask(p))
        out[name] = (KIT_SIZE, KIT_SIZE)
    write_png(os.path.join(out_dir, "kit_normal.png"), kit_normal())
    out["kit_normal.png"] = (NORMAL_SIZE, NORMAL_SIZE)
    from . import skin
    out.update(skin.write_all(out_dir, R, seed))
    return out
