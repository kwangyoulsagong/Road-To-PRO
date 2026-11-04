"""피부·눈·머리카락 텍스처 (specs/056 FR-30·32·21·40). 외부 이미지 없이 절차 생성 — 같은 입력·시드 → 같은 바이트.

피부는 MakeHuman UV(기준 메시 UV) 위에 그린다: 기준 메시 삼각형을 UV 공간에 래스터화해 텍셀마다 3D 위치를 얻고,
그 위치(눈 중심·눈 사이 거리 d 기준 비율)로 얼굴 부위를 정한다. 그래서 LOD·클로즈업 머리가 같은 UV로 같은 텍스처를 쓴다.

skin.png (1024², RGBA, 선형):  R 밝기 디테일(0.8 = 1.0배) · G 붉은 기운(볼·코·귀·입술·무릎·팔꿈치) ·
                                B 수염 밀도(셰이더가 _BeardLevel로 문턱) · A 거칠기(이마·코 약간 번들, 입술, 나머지 무광)
skin_normal.png (1024², RGB): 탄젠트 공간 노멀(모공·잔주름 높이장 → UV 미분). 가장자리 번짐 처리.
skin_closeup.png / skin_closeup_normal.png (2048²): 같은 내용, 클로즈업 머리 전용(FR-32).
parts.png (1024², RGBA): u ∈ [1,2]로 들어오는 조각. 눈 칸(face.EYE_REGION): R 밝기(흰자·홍채 결·동공·반사점), G 홍채 마스크, A 거칠기.
                         머리카락 칸(hair.REGION): R 가닥 밝기, B 머리카락 마스크 1, A 거칠기, G 가닥 결 위상.
"""
import math

import numpy as np

from . import face as F
from . import hair as H
from .textures import _noise, _to8, write_png

SKIN = 1024
CLOSEUP = 2048
PARTS = 1024


# ---------------------------------------------------------------- 래스터화

def _raster(R, size):
    """→ (위치 (S,S,3), 유효 마스크 (S,S)). 행 0 = v 1."""
    body = np.zeros(R.n, dtype=bool)
    body[R.groups["body"]] = True
    pos = np.zeros((size, size, 3))
    valid = np.zeros((size, size), dtype=bool)
    for f, fu in zip(R.faces, R.face_uvs):
        if not body[list(f)].all():
            continue
        for k in range(1, len(f) - 1):
            idx = (0, k, k + 1)
            uv = np.array([fu[i] for i in idx]) * size
            P = R.verts[[f[i] for i in idx]]
            x0, x1 = int(math.floor(uv[:, 0].min())), int(math.ceil(uv[:, 0].max()))
            y0, y1 = int(math.floor(uv[:, 1].min())), int(math.ceil(uv[:, 1].max()))
            x0, y0 = max(x0, 0), max(y0, 0)
            x1, y1 = min(x1, size), min(y1, size)
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            a, b, c = uv
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-12:
                continue
            l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
            l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
            l2 = 1 - l0 - l1
            m = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
            if not m.any():
                continue
            p = l0[..., None] * P[0] + l1[..., None] * P[1] + l2[..., None] * P[2]
            rows = size - 1 - ys[m].astype(int)
            cols = xs[m].astype(int)
            pos[rows, cols] = p[m]
            valid[rows, cols] = True
    return pos, valid


def _dilate(img, valid, iters=8):
    """UV 섬 밖으로 번지기 (밉맵 이음매 방지)."""
    img = img.copy()
    v = valid.copy()
    for _ in range(iters):
        acc = np.zeros_like(img)
        cnt = np.zeros(v.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sv = np.roll(v, (dy, dx), axis=(0, 1))
            si = np.roll(img, (dy, dx), axis=(0, 1))
            acc += si * sv[..., None]
            cnt += sv
        grow = (~v) & (cnt > 0)
        img[grow] = acc[grow] / cnt[grow][:, None]
        v = v | grow
    return img


def _blob(rx, ry, cx, cy, sx, sy):
    return np.exp(-(((rx - cx) / sx) ** 2 + ((ry - cy) / sy) ** 2))


def _fields(R, pos, valid, size, seed):
    c, d = F.eye_frame(R)
    rx = (pos[..., 0] - c[0]) / d
    ax = np.abs(rx)
    ry = (pos[..., 1] - c[1]) / d
    rz = (pos[..., 2] - c[2]) / d
    face = (rz > -1.4) & (ry > -3.0) & (ry < 2.2)
    u, v = np.meshgrid((np.arange(size) + 0.5) / size, ((np.arange(size) + 0.5) / size)[::-1])
    # 밝기: 저주파 얼룩 + 고주파 결
    lum = 0.8 + 0.025 * (_noise(u, v, 3 + seed, 48) - 0.5) + 0.008 * (_noise(u, v, 5 + seed, size // 2) - 0.5)
    # 눈썹: 눈썹뼈 위 곡선 띠, 가닥 노이즈
    brow_y = 0.36 + 0.10 * np.exp(-((ax - 0.45) / 0.3) ** 2) - 0.12 * np.clip(ax - 0.75, 0, 1)
    brow = np.exp(-((ry - brow_y) / 0.075) ** 2) * np.clip((ax - 0.12) / 0.08, 0, 1) * np.clip((0.95 - ax) / 0.12, 0, 1)
    brow = brow * face * (0.55 + 0.45 * _noise(u * 0.5, v * 3.0, 9 + seed, size))
    lum = lum * (1 - 0.62 * np.clip(brow, 0, 1))
    # 눈가 속눈썹 선·눈꺼풀 주름 살짝 어둡게
    lid = np.exp(-((ry - 0.06) / 0.05) ** 2) * np.exp(-((ax - 0.52) / 0.3) ** 2) * face
    lum = lum * (1 - 0.25 * lid)
    # 붉은 기운
    red = 0.10
    red = red + 0.55 * _blob(ax, ry, 0.78, -0.62, 0.38, 0.32) * face          # 볼
    red = red + 0.45 * _blob(rx, ry, 0.0, -0.85, 0.22, 0.25) * face          # 코끝
    lips = _blob(rx, ry, 0.0, -1.5, 0.52, 0.15) * face * (rz > -0.6)
    red = red + 0.85 * lips
    red = red + 0.45 * np.clip((ax - 1.15) / 0.2, 0, 1) * (ry > -1.2) * (ry < 0.6)  # 귀
    knee = np.exp(-((pos[..., 1] - 0.53) / 0.05) ** 2) * (pos[..., 2] > 0.0)
    red = red + 0.25 * knee
    red = np.clip(red, 0, 1)
    lum = lum * (1 - 0.08 * lips)
    # 수염 밀도 (점 노이즈 = 모낭)
    bm = F.beard_mask(R, pos) * valid
    follicle = _noise(u, v, 13 + seed, size // 1)
    beard = np.clip(bm * (0.35 + 0.65 * follicle), 0, 1)
    # 거칠기
    rough = 0.68 - 0.18 * _blob(rx, ry, 0.0, 0.9, 0.7, 0.45) * face - 0.16 * _blob(rx, ry, 0.0, -0.8, 0.25, 0.4) * face
    rough = rough - 0.22 * lips + 0.02 * (_noise(u, v, 17 + seed, size // 4) - 0.5)
    # 높이장 (노멀): 모공 + 이마 잔주름
    pores = _noise(u, v, 21 + seed, size // 2) * 0.6 + _noise(u, v, 23 + seed, size) * 0.4
    wr = 0.5 + 0.5 * np.sin(ry * 60.0) * _blob(rx, ry, 0.0, 1.0, 0.8, 0.25) * face * 0.3
    height = pores * 0.7 + wr * 0.3
    return lum, red, beard, rough, height


def _normal_from_height(h, strength):
    gy, gx = np.gradient(h)
    n = np.stack([-gx * strength, gy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def skin_maps(R, size, seed):
    pos, valid = _raster(R, size)
    lum, red, beard, rough, height = _fields(R, pos, valid, size, seed)
    rgba = np.stack([lum, red, beard, rough], axis=-1)
    rgba = _dilate(rgba, valid)
    nrm = _normal_from_height(height, 0.5 * size / 1024)
    nrm = _dilate(nrm, valid)
    return _to8(rgba), _to8(nrm)


def parts_map(R, seed):
    s = PARTS
    u, v = np.meshgrid((np.arange(s) + 0.5) / s, ((np.arange(s) + 0.5) / s)[::-1])
    out = np.zeros((s, s, 4))
    out[..., 0] = 0.8
    out[..., 3] = 0.6
    # 눈 칸
    u0, v0, u1, v1 = F.EYE_REGION
    m = (u >= u0) & (u < u1) & (v >= v0) & (v < v1)
    eu, ev = (u - u0) / (u1 - u0) * 2 - 1, (v - v0) / (v1 - v0) * 2 - 1
    r = np.sqrt(eu ** 2 + ev ** 2) / 0.9            # eye_uv의 0.9 축척을 되돌린다 → r = 안구 반지름 비율
    ang = np.arctan2(ev, eu)
    iris_r, pupil_r = 0.46, 0.18
    iris = np.clip((iris_r - r) / 0.03, 0, 1)
    pupil = np.clip((pupil_r - r) / 0.02, 0, 1)
    fibers = 0.45 + 0.25 * (_noise(ang / (2 * math.pi) + 0.5, r, 31 + seed, 128) - 0.5) + 0.12 * (1 - r / iris_r)
    limbal = np.exp(-((r - iris_r) / 0.04) ** 2)
    sclera = 0.92 - 0.12 * np.clip((r - 0.7) / 0.3, 0, 1)
    lum = sclera * (1 - iris) + fibers * iris
    lum = lum * (1 - 0.5 * limbal) * (1 - 0.9 * pupil)
    catch = np.exp(-(((eu + 0.16) / 0.07) ** 2 + ((ev - 0.20) / 0.07) ** 2))
    lum = np.clip(lum + 0.9 * catch, 0, 1)
    out[..., 0] = np.where(m, lum, out[..., 0])
    out[..., 1] = np.where(m, iris * (1 - pupil), 0.0)
    out[..., 3] = np.where(m, 0.04, out[..., 3])
    # 머리카락 칸: 가닥 = v 방향 줄 (u 방향 고주파), 굵은 다발 + 가는 가닥
    u0, v0, u1, v1 = H.REGION
    m = (u >= u0) & (u < u1) & (v >= v0) & (v < v1)
    hu, hv = (u - u0) / (u1 - u0), (v - v0) / (v1 - v0)
    clump = _noise(hu, hv * 0.02, 41 + seed, 96)
    fine = _noise(hu, hv * 0.05, 43 + seed, 700)
    strand = 0.55 + 0.25 * clump + 0.2 * fine
    out[..., 0] = np.where(m, strand, out[..., 0])
    out[..., 1] = np.where(m, _noise(hu, hv * 0.03, 47 + seed, 300), out[..., 1])
    out[..., 2] = np.where(m, 1.0, 0.0)
    out[..., 3] = np.where(m, 0.45, out[..., 3])
    return _to8(out)


def write_all(out_dir, R, seed=0):
    import os
    out = {}
    s, n = skin_maps(R, SKIN, seed)
    write_png(os.path.join(out_dir, "skin.png"), s)
    write_png(os.path.join(out_dir, "skin_normal.png"), n)
    out["skin.png"] = out["skin_normal.png"] = (SKIN, SKIN)
    s, n = skin_maps(R, CLOSEUP, seed)
    write_png(os.path.join(out_dir, "skin_closeup.png"), s)
    write_png(os.path.join(out_dir, "skin_closeup_normal.png"), n)
    out["skin_closeup.png"] = out["skin_closeup_normal.png"] = (CLOSEUP, CLOSEUP)
    write_png(os.path.join(out_dir, "parts.png"), parts_map(R, seed))
    out["parts.png"] = (PARTS, PARTS)
    return out
