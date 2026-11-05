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
PORE_AMP = 0.45     # 057: 056 대비 모공 높이 진폭
PORE_SMOOTH = 0.35  # 057: 이마·눈가·입술 등 코·볼 밖 얼굴의 모공 가중치 [제안]
LAST_METRICS = {}   # 마지막 write_all의 클로즈업 피부 지표 (checks.skin_detail 입력)
PORE_BODY = 0.6     # 057: 얼굴 밖 몸 피부 모공 가중치 (원거리 깜빡임 완화, FR-22)
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
    # 수염 밀도 (057 FR-24): 매끈한 밀도 마스크 × 낮은 대비 결(아래로 자라는 방향으로 늘인 노이즈 — 털 결).
    # 056의 모낭 점 노이즈(0.35 + 0.65 × 점)는 소금·후추처럼 보여 버렸다. 셰이더가 _BeardLevel로 부드럽게 문턱을 넘긴다.
    bm = F.beard_mask(R, pos) * valid
    grain = _noise(u * 1.0, v * 0.35, 13 + seed, size // 2) * 0.6 + _noise(u, v * 0.5, 14 + seed, size // 4) * 0.4
    beard = np.clip(bm * (0.88 + 0.12 * grain), 0, 1)
    # 거칠기
    rough = 0.68 - 0.18 * _blob(rx, ry, 0.0, 0.9, 0.7, 0.45) * face - 0.16 * _blob(rx, ry, 0.0, -0.8, 0.25, 0.4) * face
    rough = rough - 0.22 * lips + 0.02 * (_noise(u, v, 17 + seed, size // 4) - 0.5)
    # 높이장 (노멀): 모공 + 이마 잔주름.
    # 057 FR-20·21: 모공 진폭 PORE_AMP배(클로즈업 사포 느낌 제거), 부위 가중치 — 코·볼 1.0, 이마·눈가·입술 PORE_SMOOTH, 몸 PORE_BODY
    pw = np.clip(_blob(rx, ry, 0.0, -0.6, 0.25, 0.45) + _blob(ax, ry, 0.78, -0.62, 0.38, 0.32), 0, 1)
    pw = np.where(face, PORE_SMOOTH + (1.0 - PORE_SMOOTH) * pw, PORE_BODY)
    pores = (_noise(u, v, 21 + seed, size // 2) * 0.6 + _noise(u, v, 23 + seed, size) * 0.4 - 0.5) * PORE_AMP * pw + 0.5
    wr = 0.5 + 0.5 * np.sin(ry * 60.0) * _blob(rx, ry, 0.0, 1.0, 0.8, 0.25) * face * 0.3
    height = pores * 0.7 + wr * 0.3
    return lum, red, beard, rough, height


def _normal_from_height(h, strength):
    gy, gx = np.gradient(h)
    n = np.stack([-gx * strength, gy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def skin_maps(R, size, seed, metrics=None):
    pos, valid = _raster(R, size)
    lum, red, beard, rough, height = _fields(R, pos, valid, size, seed)
    rgba = np.stack([lum, red, beard, rough], axis=-1)
    rgba = _dilate(rgba, valid)
    nrm = _normal_from_height(height, 0.5 * size / 1024)
    nrm = _dilate(nrm, valid)
    if metrics is not None:
        metrics.update(detail_metrics(R, pos, valid, rgba, nrm))
    return _to8(rgba), _to8(nrm)


# ---------------------------------------------------------------- 057 피부 디테일 지표 (AC-05, FR-20·21·23)

def _box(img, r):
    """정사각 상자 블러 (반경 r 텍셀, 가장자리 반복). 누적합 — 결정론."""
    k = 2 * r + 1
    p = np.pad(img, ((r + 1, r), (r + 1, r)), mode="edge")
    c = p.cumsum(0).cumsum(1)
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


def face_regions(R, pos, valid):
    """얼굴 부위 마스크 (눈 중심·눈 사이 거리 d 기준 비율). → {이름: bool (S,S)}"""
    c, d = F.eye_frame(R)
    rx = (pos[..., 0] - c[0]) / d
    ax = np.abs(rx)
    ry = (pos[..., 1] - c[1]) / d
    rz = (pos[..., 2] - c[2]) / d
    front = valid & (rz > -0.6)
    return {
        "nose": front & (ax < 0.22) & (ry > -1.0) & (ry < -0.25),
        "cheek": front & (ax > 0.55) & (ax < 1.0) & (ry > -0.95) & (ry < -0.35),
        "forehead": front & (ax < 0.6) & (ry > 0.75) & (ry < 1.3),
        "eyeArea": front & (ax > 0.3) & (ax < 0.8) & (ry > -0.35) & (ry < -0.12),
        "lips": front & (ax < 0.35) & (ry > -1.62) & (ry < -1.38),
    }


def detail_metrics(R, pos, valid, rgba, nrm):
    """부위별 고주파 표준편차: 밝기(R − 상자 블러 r=2), 노멀 기울기(xy − 블러), 거칠기 평균. 6자리."""
    lum = rgba[..., 0]
    hl = lum - _box(lum, 2)
    nx, ny = nrm[..., 0] - 0.5, nrm[..., 1] - 0.5
    hn = np.sqrt((nx - _box(nx, 2)) ** 2 + (ny - _box(ny, 2)) ** 2)
    out = {}
    for name, m in face_regions(R, pos, valid).items():
        if not m.any():
            out[name] = None
            continue
        out[name] = {"lumHf": round(float(hl[m].std()), 6), "normalHf": round(float(np.sqrt((hn[m] ** 2).mean())), 6),
                     "rough": round(float(rgba[..., 3][m].mean()), 6), "texels": int(m.sum())}
    out["beard"] = beard_metrics(R, pos, valid, rgba[..., 2])
    allm = np.zeros_like(valid)
    for m in face_regions(R, pos, valid).values():
        allm |= m
    out["face"] = {"lumHf": round(float(hl[allm].std()), 6), "normalHf": round(float(np.sqrt((hn[allm] ** 2).mean())), 6)}
    return out


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
    # 057 가닥 카드 칸 (H.CARD_REGION): R 가닥 밝기, G 알파(뿌리 v=1 → 끝 v=0 으로 가닥이 듬성해진다), B 머리카락 마스크 1, A 거칠기
    cm = (u >= H.CARD_REGION[0]) & (u < H.CARD_REGION[2]) & (v >= H.CARD_REGION[1]) & (v < H.CARD_REGION[3])
    cu = (u - H.CARD_REGION[0]) / (H.CARD_REGION[2] - H.CARD_REGION[0])
    cv = (v - H.CARD_REGION[1]) / (H.CARD_REGION[3] - H.CARD_REGION[1])
    lane = _noise(cu * 0.9, cv * 0.004, 51 + seed, 512)          # 가닥마다 길이(가로로만 변하는 값)
    clump = _noise(cu * 0.9, cv * 0.004, 53 + seed, 64)
    reach = 0.10 + 0.90 * (0.6 * lane + 0.4 * clump)            # 이 가닥이 끝(0)까지 얼마나 내려오는가
    # 057: 알파는 거의 0/1 (가닥이 있거나 없거나) — 중간 알파가 넓으면 디더 점이 박힌 사각형으로 보인다(smoke 캡처 2026-11-05)
    alpha = np.clip((cv - (1.0 - reach)) / 0.02, 0, 1) * np.clip(cv / 0.02, 0, 1)
    gap = _noise(cu * 0.9, cv * 0.004, 57 + seed, 900)
    alpha = alpha * np.clip((gap - 0.40) / 0.03, 0, 1)
    out[..., 0] = np.where(cm, 0.5 + 0.3 * lane + 0.2 * _noise(cu, cv * 0.05, 59 + seed, 700), out[..., 0])
    out[..., 1] = np.where(cm, alpha, out[..., 1])
    out[..., 2] = np.where(cm, 1.0, out[..., 2])
    out[..., 3] = np.where(cm, 0.45, out[..., 3])
    return _to8(out)


def write_all(out_dir, R, seed=0):
    import os
    out = {}
    s, n = skin_maps(R, SKIN, seed)
    write_png(os.path.join(out_dir, "skin.png"), s)
    write_png(os.path.join(out_dir, "skin_normal.png"), n)
    out["skin.png"] = out["skin_normal.png"] = (SKIN, SKIN)
    LAST_METRICS.clear()
    s, n = skin_maps(R, CLOSEUP, seed, LAST_METRICS)
    write_png(os.path.join(out_dir, "skin_closeup.png"), s)
    write_png(os.path.join(out_dir, "skin_closeup_normal.png"), n)
    out["skin_closeup.png"] = out["skin_closeup_normal.png"] = (CLOSEUP, CLOSEUP)
    write_png(os.path.join(out_dir, "parts.png"), parts_map(R, seed))
    out["parts.png"] = (PARTS, PARTS)
    return out


def beard_metrics(R, pos, valid, b):
    """057 FR-24 수염 채널(B) 지표: 영역 안 고주파 RMS(점 노이즈), 아래 목 누출(울대 아래 평균), 입꼬리 밀도(입 둘레 타원 구멍 여부),
    경계 부드러움(0.1 < b < 0.9 비율 — 칼로 자른 경계면 작다)."""
    c, d = F.eye_frame(R)
    rx = (pos[..., 0] - c[0]) / d
    ry = (pos[..., 1] - c[1]) / d
    rz = (pos[..., 2] - c[2]) / d
    front = valid & (rz > -1.6)
    core = front & (b > 0.5)
    hf = b - _box(b, 2)
    low_neck = front & (ry < -2.95) & (ry > -3.6) & (np.abs(rx) < 0.8)
    corner = front & (np.abs(np.abs(rx) - 0.5) < 0.06) & (np.abs(ry + 1.5) < 0.05)
    some = front & (b > 0.1)
    return {"coreHf": round(float(np.sqrt((hf[core] ** 2).mean())) if core.any() else 0.0, 6),
            "lowNeck": round(float(b[low_neck].mean()) if low_neck.any() else 0.0, 6),
            "mouthCorner": round(float(b[corner].mean()) if corner.any() else 0.0, 6),
            "softEdge": round(float(((b > 0.1) & (b < 0.9))[some].mean()) if some.any() else 0.0, 6)}
