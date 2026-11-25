"""피부·눈·머리카락 텍스처 (specs/056 FR-30·32·21·40 → 088 FR-01·04: 피부 색은 MakeHuman CC0 diffuse 3세트, 절차 피부 색 생성 없음).
088: skin_<세트>.png (2048², RGB = MakeHuman diffuse(sRGB 값 그대로), A = 거칠기) — LOD0·클로즈업이 같이 쓴다(밉맵).
     skin_normal.png (2048², 공유) = light 세트 diffuse 밝기의 고역 통과 높이장 → 노멀 (MakeHuman 피부에 노멀 맵이 없다).
     brow_mask.png (1024², RGB = Mindfront 눈썹 02·04·09 덮임) — 셰이더가 선수 눈썹 종류 채널 × 머리색으로 칠한다.
     parts 눈 칸 = MakeHuman 눈 재질(brown) 밝기 + 홍채 마스크.
(아래 056·057 설명은 수염·두께·지표 함수에만 해당)

피부는 MakeHuman UV(기준 메시 UV) 위에 그린다: 기준 메시 삼각형을 UV 공간에 래스터화해 텍셀마다 3D 위치를 얻고,
그 위치(눈 중심·눈 사이 거리 d 기준 비율)로 얼굴 부위를 정한다. 그래서 LOD·클로즈업 머리가 같은 UV로 같은 텍스처를 쓴다.

skin.png (1024², RGBA, 선형):  R 밝기 디테일(0.8 = 1.0배) · G 붉은 기운(볼·코·귀·입술·무릎·팔꿈치) ·
                                B 수염 밀도(셰이더가 _BeardLevel로 문턱) · A 거칠기(이마·코 약간 번들, 입술, 나머지 무광)
skin_normal.png (1024², RGB): 탄젠트 공간 노멀(모공·잔주름 높이장 → UV 미분). 가장자리 번짐 처리.
skin_closeup.png / skin_closeup_normal.png (2048²): 같은 내용, 클로즈업 머리 전용(FR-32).
parts.png (1024², RGBA): u ∈ [1,2]로 들어오는 조각. 눈 칸(face.EYE_REGION): R 밝기(흰자·홍채 결·동공·반사점), G 홍채 마스크, A 거칠기.
                         머리카락·카드 칸: 088 공유 결 아틀라스(groom.atlas — 셸 타일 6 · 카드 타일 6, 그루밍 가닥 모양).
"""
import math

import numpy as np

from . import face as F
from .textures import _noise, _to8, write_png

SKIN = 1024
PORE_AMP = 0.45     # 057: 056 대비 모공 높이 진폭
PORE_SMOOTH = 0.35  # 057: 이마·눈가·입술 등 코·볼 밖 얼굴의 모공 가중치 [제안]
LAST_METRICS = {}   # 마지막 write_all의 클로즈업 피부 지표 (checks.skin_detail 입력)
PORE_BODY = 0.6     # 057: 얼굴 밖 몸 피부 모공 가중치 (원거리 깜빡임 완화, FR-22)
CLOSEUP = 2048
PARTS = 1024
THICK = 512         # plan 데이터·스키마: skin_thickness 512
BEARD_MAP = 1024    # 062 plan: beard_mask 1K
FEAT = {}           # 062 T19: FR-91 얼굴 디테일 → 기여 최대값 (높이장 단위 / 밝기 배율), summary bakedMaps 근거
LIP_GRAIN = 0.05    # 062 FR-91 입술 세로 결 진폭 (높이장 단위, 모공 0.45 × 0.5 대비 작게)
WRINKLE_EYE = 0.06  # 062 FR-91 눈가 잔주름(까마귀발) 진폭
WRINKLE_NL = 0.05   # 062 FR-91 팔자 주름 진폭


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


def _normal_from_height(h, strength):
    gy, gx = np.gradient(h)
    n = np.stack([-gx * strength, gy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def beard_fields(R, pos, valid, size, seed):
    """062 FR-66: 수염 영역 4개(face.beard_masks) × 057 털 결 (skin._fields 수염과 같은 결)."""
    u, v = np.meshgrid((np.arange(size) + 0.5) / size, ((np.arange(size) + 0.5) / size)[::-1])
    grain = _noise(u * 1.0, v * 0.35, 13 + seed, size // 2) * 0.6 + _noise(u, v * 0.5, 14 + seed, size // 4) * 0.4
    out = {}
    for k, m in F.beard_masks(R, pos).items():
        out[k] = np.clip(m * valid * (0.88 + 0.12 * grain), 0, 1)
    return out


def cheek_density(R, pos, valid, b):
    """볼 영역(입꼬리 바깥, 광대 아래) 평균 밀도 — 콧수염·염소수염은 ≤ 0.01 (062 FR-66)."""
    c, d = F.eye_frame(R)
    rx = np.abs(pos[..., 0] - c[0]) / d
    ry = (pos[..., 1] - c[1]) / d
    rz = (pos[..., 2] - c[2]) / d
    m = valid & (rz > -1.6) & (rx > 0.75) & (rx < 1.10) & (ry > -1.40) & (ry < -0.95)
    return round(float(b[m].mean()) if m.any() else 0.0, 6)


def beard_map(R, size, seed):
    """beard_mask0.png (RGBA = full · mustache · goatee · heavy, 피부 UV) — 셰이더가 수염 종류별 채널을 고른다(T29)."""
    pos, valid = _raster(R, size)
    bm = beard_fields(R, pos, valid, size, seed)
    rgba = np.stack([bm[k] for k in F.BEARD_CHANNELS], axis=-1)
    return _to8(_dilate(rgba, valid))


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
    mh_parts_eye(out, u, v)   # 088: 눈 칸 = MakeHuman 눈 재질
    mh_parts_teeth(out, u, v)  # 088 T30k: 치아 칸 = MakeHuman teeth_base 텍스처
    # 088: 머리카락 칸·카드 칸 = 그루밍 가닥 모양 공유 결 아틀라스 (groom.atlas, 표본은 헤어 굽기 때 모은다)
    from .groom import atlas as GA, bake as GB
    GA.fill(out, u, v, GB.SAMPLES, seed)
    return _to8(out)


def thickness_map(R, size=THICK):
    """062 FR-32·33 skin_thickness.png (피부 UV, RGBA 선형): R 얇은 부위 두께(1 = 역광 비침 최대: 귀·코끝·손가락 끝),
    G 땀 마스크(이마·관자·목·팔), B 0, A 1. 셰이더: wrap 역광 × R, `_Sweat`이면 G 부위 거칠기 − SWEAT_DROP."""
    from . import mpfb_src as MS
    pos, valid = _raster(R, size)
    c, d = F.eye_frame(R)
    rx = (pos[..., 0] - c[0]) / d
    ax = np.abs(rx)
    ry = (pos[..., 1] - c[1]) / d
    rz = (pos[..., 2] - c[2]) / d
    face = (rz > -1.4) & (ry > -3.0) & (ry < 2.2)
    ear = np.clip((ax - 1.15) / 0.2, 0, 1) * (ry > -1.2) * (ry < 0.6)
    nose = _blob(rx, ry, 0.0, -0.85, 0.18, 0.2) * face * (rz > 0.3)

    def seg_dist(p, a, b):
        a, b = np.asarray(a, float), np.asarray(b, float)
        ab = b - a
        t = np.clip(((p - a) @ ab) / (ab @ ab), 0, 1)
        return np.linalg.norm(p - (a + t[..., None] * ab), axis=-1)

    tips = np.zeros(valid.shape)
    for side in ("Left", "Right"):
        for Fn, _ in MS.FINGERS:
            h, t_ = R.joints[side + Fn + "Distal"]
            dd = np.linalg.norm(pos - np.asarray(t_, float), axis=-1)
            tips = np.maximum(tips, np.clip(1 - dd / 0.012, 0, 1))
    thick = np.clip(np.maximum(np.maximum(ear, nose), tips), 0, 1)
    fore = _blob(rx, ry, 0.0, 0.95, 0.75, 0.35) * face
    temple = _blob(ax, ry, 1.0, 0.45, 0.2, 0.3) * face
    nh, nt = R.joints["Neck"]
    neck = np.clip(1 - seg_dist(pos, nh, nt) / 0.075, 0, 1) * (~face)
    arm = np.zeros(valid.shape)
    for side in ("Left", "Right"):
        for b in ("UpperArm", "LowerArm"):
            h, t_ = R.joints[side + b]
            arm = np.maximum(arm, np.clip(1 - (seg_dist(pos, h, t_) - 0.04) / 0.06, 0, 1))   # 위팔 반지름 ≈ 5 cm까지 1
    sweat = np.clip(np.maximum.reduce([fore, temple, neck, arm]), 0, 1)
    img = np.stack([thick, sweat, np.zeros_like(thick), np.ones_like(thick)], axis=-1) * valid[..., None]
    img = _dilate(img, valid)
    FEAT["skin.thickness"] = float(thick[valid].max())
    FEAT["skin.sweat"] = float(sweat[valid].max())
    return _to8(img)


# ---------------------------------------------------------------- 088 MakeHuman 피부

MH_SKIN = 2048          # 088: 세트당 2K 한 장(LOD0·클로즈업 공유, 밉맵) — 1K + 2K 따로면 텍스처 메모리 +27 %
MH_NORMAL_HF = 0.02     # 088: 얼굴 노멀 고주파 목표(057 기준 0.035 이하·0.002 초과 안) — diffuse 고역 통과 세기를 이 값에 맞춘다


def _rough(R, pos, valid, size, seed):
    """부위별 거칠기 (056 값 유지: 이마·코 약간 번들, 입술 매끈, 나머지 무광)."""
    c, d = F.eye_frame(R)
    rx = (pos[..., 0] - c[0]) / d
    ry = (pos[..., 1] - c[1]) / d
    rz = (pos[..., 2] - c[2]) / d
    face = (rz > -1.4) & (ry > -3.0) & (ry < 2.2)
    u, v = np.meshgrid((np.arange(size) + 0.5) / size, ((np.arange(size) + 0.5) / size)[::-1])
    lips = _blob(rx, ry, 0.0, -1.5, 0.52, 0.15) * face * (rz > -0.6)
    rough = 0.68 - 0.18 * _blob(rx, ry, 0.0, 0.9, 0.7, 0.45) * face - 0.16 * _blob(rx, ry, 0.0, -0.8, 0.25, 0.4) * face
    return rough - 0.22 * lips + 0.02 * (_noise(u, v, 17 + seed, size // 4) - 0.5)


def _mh_diffuse(key, size):
    from . import mhassets as MH
    m = MH.read_mhmat(MH.SKINS[key])
    return MH.resize(MH.load_png(m["diffusePath"])[..., :3], size)


def _lum(rgb):
    return rgb @ np.array([0.2126, 0.7152, 0.0722])


def mh_normal(R, pos, valid, size, metrics=None):
    """light 세트 diffuse 밝기 → 고역 통과(상자 r=6) 높이장 → 노멀. 세기는 얼굴 노멀 고주파가 MH_NORMAL_HF가 되게 한 번 맞춘다."""
    lum = _lum(_mh_diffuse("light", size))
    h = lum - _box(lum, max(2, size // 340))
    regions = face_regions(R, pos, valid)
    allm = np.zeros_like(valid)
    for m in regions.values():
        allm |= m

    def face_hf(n):
        nx, ny = n[..., 0] - 0.5, n[..., 1] - 0.5
        hn = np.sqrt((nx - _box(nx, 2)) ** 2 + (ny - _box(ny, 2)) ** 2)
        return float(np.sqrt((hn[allm] ** 2).mean())) if allm.any() else 0.0

    s1 = 4.0 * size / 1024
    hf1 = face_hf(_normal_from_height(h, s1))
    strength = s1 * (MH_NORMAL_HF / hf1) if hf1 > 1e-6 else s1
    # 062 FR-91 얼굴 디테일 기여 (bakedMaps): MakeHuman diffuse에 그려진 결을 부위별 고역 통과 진폭(밝기 단위)으로 기록
    for key, reg in (("face.pores", ("nose", "cheek")), ("face.fineWrinkles", ("forehead",)), ("face.lipGrain", ("lips",)),
                     ("face.eyelidCreases", ("eyeArea",))):
        m = np.zeros_like(valid)
        for r in reg:
            m |= regions[r]
        FEAT[key] = max(FEAT.get(key, 0.0), float(np.abs(h[m]).max()) if m.any() else 0.0)
    return _dilate(_normal_from_height(h, strength), valid, 4)


def mh_skin_maps(R, key, size, seed, nrm=None, metrics=None):
    """→ RGBA (S,S,4) 0~1: RGB = MakeHuman diffuse, A = 거칠기."""
    pos, valid = _raster(R, size)
    rgb = _mh_diffuse(key, size)
    rgba = np.concatenate([rgb, _dilate(_rough(R, pos, valid, size, seed)[..., None], valid)], axis=-1)
    if metrics is not None and nrm is not None:
        bm = F.beard_mask(R, pos) * valid
        probe = np.stack([_lum(rgb), np.zeros_like(bm), bm, rgba[..., 3]], axis=-1)
        metrics.update(detail_metrics(R, pos, valid, probe, nrm))
        bmaps = beard_fields(R, pos, valid, size, seed)
        stack = _dilate(np.stack([bmaps[k] for k in F.BEARD_CHANNELS], axis=-1), valid)
        bmaps = {k: stack[..., i] for i, k in enumerate(F.BEARD_CHANNELS)}
        metrics["beards"] = {}
        for bid, region in F.BEARD_REGION.items():
            m = beard_metrics(R, pos, valid, bmaps[region])
            m["cheek"] = cheek_density(R, pos, valid, bmaps[region])
            metrics["beards"][bid] = m
    return rgba


def mh_parts_eye(out, u, v):
    """parts 눈 칸 ← MakeHuman 눈 재질(brown_eye.png): R 밝기, G 홍채 마스크(채도·중심 거리), A 거칠기 0.04.
    눈 메시 UV는 face.eye_uv가 이 칸으로 옮긴다(원본 UV 그대로 축척)."""
    from . import mhassets as MH
    m = MH.read_mhmat(MH.EYE_MATERIAL)
    u0, v0, u1, v1 = F.EYE_REGION
    cell = (u >= u0) & (u < u1) & (v >= v0) & (v < v1)
    rows, cols = np.where(cell)
    hgt, wid = rows.max() - rows.min() + 1, cols.max() - cols.min() + 1
    img = MH.resize(MH.load_png(m["diffusePath"])[..., :3], max(hgt, wid))[:hgt, :wid]
    lum = _lum(img)
    mx, mn = img.max(axis=-1), img.min(axis=-1)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    iris = np.clip((sat - 0.18) / 0.12, 0, 1) * np.clip((0.75 - lum) / 0.2, 0, 1)
    r0, c0 = rows.min(), cols.min()
    out[r0:r0 + hgt, c0:c0 + wid, 0] = lum
    out[r0:r0 + hgt, c0:c0 + wid, 1] = iris
    out[r0:r0 + hgt, c0:c0 + wid, 2] = 0.0
    out[r0:r0 + hgt, c0:c0 + wid, 3] = 0.04


TEETH_ROUGH = (0.3, 0.5)   # 088 T30k: 거칠기 치아·잇몸 [제안] (치아 법랑질 약간 번들, 잇몸 젖은 점막)


def mh_parts_teeth(out, u, v):
    """parts 치아 칸(face.TEETH_REGION) ← MakeHuman teeth_base 텍스처(2048², 섬 밖 알파 0): R 밝기, G 잇몸(붉은 기운), A 거칠기.
    축소는 알파 곱(섬 밖 검정이 섞이지 않게) 후 나누고, 섬 밖으로 번져 밉 이음매를 막는다. 치아 메시 UV는 body.teeth_uv가 이 칸으로 옮긴다."""
    from . import mhassets as MH
    u0, v0, u1, v1 = F.TEETH_REGION
    cell = (u >= u0) & (u < u1) & (v >= v0) & (v < v1)
    rows, cols = np.where(cell)
    hgt, wid = rows.max() - rows.min() + 1, cols.max() - cols.min() + 1
    src = MH.load_png(MH.path("teeth/teeth_base/teeth.png"))
    a = src[..., 3:4]
    pre = MH.resize(np.concatenate([src[..., :3] * a, a], axis=-1), max(hgt, wid))[:hgt, :wid]
    al = pre[..., 3]
    rgb = pre[..., :3] / np.maximum(al[..., None], 1e-4)
    lum = _lum(rgb)
    gum = np.clip((rgb[..., 0] - 0.5 * (rgb[..., 1] + rgb[..., 2])) / np.maximum(rgb[..., 0], 1e-4) / 0.35, 0, 1)
    img = np.stack([lum, gum, np.zeros_like(lum), TEETH_ROUGH[0] + (TEETH_ROUGH[1] - TEETH_ROUGH[0]) * gum], axis=-1)
    img = _dilate(img, al > 0.5, iters=16)
    r0, c0 = rows.min(), cols.min()
    out[r0:r0 + hgt, c0:c0 + wid] = img


def write_all(out_dir, R, seed=0):
    import os
    from . import mhassets as MH
    out = {}
    FEAT.clear()
    LAST_METRICS.clear()
    pos, valid = _raster(R, MH_SKIN)
    nrm = mh_normal(R, pos, valid, MH_SKIN)
    write_png(os.path.join(out_dir, "skin_normal.png"), _to8(nrm))
    out["skin_normal.png"] = (MH_SKIN, MH_SKIN)
    for key in MH.SKINS:
        rgba = mh_skin_maps(R, key, MH_SKIN, seed, nrm, LAST_METRICS if key == "light" else None)
        write_png(os.path.join(out_dir, f"skin_{key}.png"), _to8(rgba))
        out[f"skin_{key}.png"] = (MH_SKIN, MH_SKIN)
    write_png(os.path.join(out_dir, "brow_mask.png"), _to8(_dilate(F.brow_masks(R, BROW_MAP), F.brow_valid(R, BROW_MAP), 2)))
    out["brow_mask.png"] = (BROW_MAP, BROW_MAP)
    write_png(os.path.join(out_dir, "parts.png"), parts_map(R, seed))
    out["parts.png"] = (PARTS, PARTS)
    write_png(os.path.join(out_dir, "skin_thickness.png"), thickness_map(R))   # 062 FR-32·33
    out["skin_thickness.png"] = (THICK, THICK)
    write_png(os.path.join(out_dir, "beard_mask0.png"), beard_map(R, BEARD_MAP, seed))   # 062 FR-66 수염 영역 4채널
    out["beard_mask0.png"] = (BEARD_MAP, BEARD_MAP)
    return out


BROW_MAP = 1024


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
