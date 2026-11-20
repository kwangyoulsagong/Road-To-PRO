"""062 시각 지표 (specs/062 plan 단계 A, T01). 기준 조명 음영을 UV 공간에서 계산한다 — 렌더러 없이 결정론.

정의 (모든 062 "A 기준 × 배수" 비교가 같은 함수를 쓴다):
- 기준 조명 음영 S = max(0, N'·L), L = 정규화(0.35, 0.6, 0.72) (선수 좌표: 오른쪽·위·정면). N' = 정점 법선 보간을
  삼각형 접선 틀로 노멀 맵(피부 `skin_normal.png`, 키트 `kit_normal.png` × `_FabricTile` 24)으로 기울인 값.
- 중주파 = 가우스 σ 1.5 − σ 6 텍셀 (UV 512² 래스터, 유효 영역 정규화 합성곱). 대비 = 영역 RMS, 분산 = 제곱.
- 근육 영역 = 피부 면 중 주 뼈가 위팔·아래팔·허벅지·종아리. 천 영역 = 키트 top·shorts 면.
- 키트 주색 = 기준 팀 색(빨강 sRGB 200,30,30) × (0.3 + 0.7·S) + 정반사(블린–퐁, 광택 16) 0.04 → CIE L* 표준편차,
  포화 비율 = HSV 채도가 입력 채도의 98 % 이상인 텍셀 비율.
- 헤어: 볼륨 비율 = (머리+머리카락 꼭대기 − 두개골 꼭대기) / 두개골 높이(꼭대기 − 턱), 옆 폭 = 귀 위 띠(두개골 위 40 %)에서
  (머리카락 최대 반폭 − 두개골 최대 반폭) / 두개골 폭. 실루엣 = 정면·옆·뒤 3/4(135°) 정사영 128² 마스크, 쌍 차이 = 세 시점 (1 − IoU) 최댓값.
- 헤어 결 고주파 = parts 머리카락 칸 밝기의 LoG(σ 1) RMS, 이방성 = 가로/세로 기울기 RMS 비(큰 쪽/작은 쪽).
"""
import math

import numpy as np

from . import body as B
from . import mpfb_src as MS

L_DIR = np.array([0.35, 0.6, 0.72]) / np.linalg.norm([0.35, 0.6, 0.72])
VIEW = np.array([0.0, 0.25, 1.0]) / np.linalg.norm([0.0, 0.25, 1.0])
RASTER = 512
FABRIC_TILE = 24.0
TEAM_SRGB = (200, 30, 30)
MUSCLE_BONES = {f"{s}{b}" for s in ("Left", "Right") for b in ("UpperArm", "LowerArm", "UpperLeg", "LowerLeg")}
CLOTH_PARTS = ("top", "shorts")
SIL = 128


# ---------------------------------------------------------------- 공통

def _gauss(img, valid, sigma):
    """유효 영역 정규화 가우스 (경계 밖 값이 새지 않는다)."""
    r = int(math.ceil(sigma * 3))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    w = valid.astype(float)

    def conv(a):
        a = np.apply_along_axis(lambda x: np.convolve(x, k, mode="same"), 0, a)
        return np.apply_along_axis(lambda x: np.convolve(x, k, mode="same"), 1, a)

    num, den = conv(img * w), conv(w)
    return np.where(den > 1e-6, num / np.maximum(den, 1e-6), 0.0)


def band(img, valid, lo=1.5, hi=6.0):
    return _gauss(img, valid, lo) - _gauss(img, valid, hi)


def _sample(tex, uv):
    """tex (H,W,C) 행 0 = v 0 (bpy 픽셀 순서), uv 반복(fract) 최근접."""
    h, w = tex.shape[:2]
    u = np.mod(uv[..., 0], 1.0)
    v = np.mod(uv[..., 1], 1.0)
    return tex[np.minimum((v * h).astype(int), h - 1), np.minimum((u * w).astype(int), w - 1)]


def load_image(path):
    import bpy
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4).astype(float)


# ---------------------------------------------------------------- UV 음영

def uv_shading(m, mat, normal_tex, tile=1.0, size=RASTER, face_pred=None, detail=None):
    """detail: [(노멀 텍스처, 반복, 세기)] — 화이트아웃 합성(xy 더하기, z 곱하기)으로 겹친다 (셰이더 062 _MuscleDef와 같은 식)."""
    """→ (S (size,size), valid, region id 배열 — 면의 주 뼈 인덱스, 면 부위 이름 목록 인덱스)."""
    co = m.verts
    faces = [f[0] for f in m.faces]
    vn = B._normals(co, faces)
    dom = np.argmax(m.W, axis=1)
    S = np.zeros((size, size))
    valid = np.zeros((size, size), dtype=bool)
    bone = np.full((size, size), -1, dtype=np.int64)
    part = np.full((size, size), "", dtype=object)
    for f, fm, uvs, fpart, _ in m.faces:
        if fm != mat or (face_pred is not None and not face_pred(f, fpart)):
            continue
        for k in range(1, len(f) - 1):
            idx = (0, k, k + 1)
            vi = [f[i] for i in idx]
            uv = np.array([uvs[i] for i in idx], dtype=float)
            uv = uv - np.floor(uv.min(axis=0))  # 카드(u+2) 같은 칸 밀기를 0~1로
            if uv.max() > 1.0001:
                continue
            P = co[vi]
            e1, e2 = P[1] - P[0], P[2] - P[0]
            d1, d2 = uv[1] - uv[0], uv[2] - uv[0]
            det = d1[0] * d2[1] - d2[0] * d1[1]
            if abs(det) < 1e-12:
                continue
            T = (e1 * d2[1] - e2 * d1[1]) / det
            Bt = (e2 * d1[0] - e1 * d2[0]) / det
            px = uv * size
            x0, x1 = max(int(px[:, 0].min()), 0), min(int(math.ceil(px[:, 0].max())), size)
            y0, y1 = max(int(px[:, 1].min()), 0), min(int(math.ceil(px[:, 1].max())), size)
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            a, b, c = px
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-12:
                continue
            l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
            l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
            l2 = 1 - l0 - l1
            ins = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
            if not ins.any():
                continue
            N = l0[ins, None] * vn[vi[0]] + l1[ins, None] * vn[vi[1]] + l2[ins, None] * vn[vi[2]]
            N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
            t = T - N * (N @ T)[:, None]
            t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
            hand = np.where(N @ np.cross(T, Bt) < 0, -1.0, 1.0)[:, None]  # UV 감김(거울 UV) 방향
            bt = np.cross(N, t) * hand
            puv = np.stack([xs[ins], ys[ins]], axis=-1) / size
            nm = _sample(normal_tex, puv * tile)[:, :3] * 2 - 1
            nz = np.sqrt(np.clip(1 - nm[:, 0] ** 2 - nm[:, 1] ** 2, 0, 1))
            for dtex, dtile, dg in detail or ():
                dn = _sample(dtex, puv * dtile)[:, :3] * 2 - 1
                nm = nm.copy()
                nm[:, :2] += dg * dn[:, :2]
                nz = nz * np.sqrt(np.clip(1 - dn[:, 0] ** 2 - dn[:, 1] ** 2, 0, 1))
                ln = np.sqrt(nm[:, 0] ** 2 + nm[:, 1] ** 2 + nz ** 2)
                nm, nz = nm / ln[:, None], nz / ln
            Np = t * nm[:, :1] + bt * nm[:, 1:2] + N * nz[:, None]
            Np /= np.maximum(np.linalg.norm(Np, axis=1, keepdims=True), 1e-9)
            rows, cols = ys[ins].astype(int), xs[ins].astype(int)
            S[rows, cols] = np.maximum(Np @ L_DIR, 0.0)
            valid[rows, cols] = True
            bone[rows, cols] = dom[vi[0]]
            part[rows, cols] = fpart
    return S, valid, bone, part


def _rms(a, mask):
    return float(np.sqrt(np.mean(a[mask] ** 2))) if mask.any() else 0.0


# 셰이더 _MuscleDef = 0.5 + 0.5 × Muscular(0~1) — 프로 선수라 최소도 0.5 (062 plan 접근 1 보충, T06).
MUSCLE_DEF_REF = 0.75  # 판정 기준 = 중간 근육량(Muscular 0.5). 최소(0.5)·최대(1.0)도 기록


def muscle_contrast(m, skin_normal, body_detail=None):
    mb = np.array([MS.OUR_NAMES.index(n) for n in sorted(MUSCLE_BONES)])
    out = {}
    for key, g in (("bandRms", MUSCLE_DEF_REF), ("bandRmsMax", 1.0), ("bandRmsMin", 0.5)):
        det = [(body_detail, 1.0, g)] if body_detail is not None else None
        S, valid, bone, _ = uv_shading(m, B.BODY, skin_normal, detail=det)
        mask = valid & np.isin(bone, mb)
        out[key] = _rms(band(S, valid), mask)
        out["texels"] = int(mask.sum())
        if body_detail is None:
            break
    return out


def _srgb_to_lin(c):
    c = np.asarray(c, dtype=float)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _lstar(lin):
    y = lin @ np.array([0.2126, 0.7152, 0.0722])
    return np.where(y > 216 / 24389, 116 * np.cbrt(y) - 16, y * 24389 / 27)


def kit_metrics(m, kit_normal, tile=FABRIC_TILE):
    """062 T14부터 kit_detail_n(키트 UV 그대로, tile 1)로 잰다 — 기준선(main)은 kit_normal × 24 반복."""
    S, valid, _, part = uv_shading(m, B.KIT, kit_normal, tile)
    mask = valid & np.isin(part, CLOTH_PARTS)
    bd = band(S, valid)
    alb = _srgb_to_lin(np.array(TEAM_SRGB) / 255.0)
    H = (L_DIR + VIEW) / np.linalg.norm(L_DIR + VIEW)
    spec = 0.04 * (np.clip(S, 0, 1) ** 16)  # S ≈ N·L 근사 하이라이트 (기준선·이후 같은 식)
    col = alb[None, None, :] * (0.3 + 0.7 * S[..., None]) + spec[..., None] * float(H @ L_DIR)
    Ls = _lstar(col)
    mx, mn = col.max(axis=-1), col.min(axis=-1)
    sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
    s_in = (alb.max() - alb.min()) / alb.max()
    return {"clothBandVar": float(np.mean(bd[mask] ** 2)) if mask.any() else 0.0,
            "kitLStd": float(Ls[mask].std()) if mask.any() else 0.0,
            "kitSaturated": float(np.mean(sat[mask] >= 0.98 * s_in)) if mask.any() else 0.0,
            "texels": int(mask.sum())}


# ---------------------------------------------------------------- 헤어

def _head_points(m):
    hi = MS.OUR_NAMES.index("Head")
    dom = np.argmax(m.W, axis=1)
    return m.verts[dom == hi]


def _tri_mask(P2, tris, lo, span):
    img = np.zeros((SIL, SIL), dtype=bool)
    q = (P2 - lo) / span * (SIL - 1)
    for t in tris:
        a, b, c = q[list(t)]
        x0, x1 = int(max(min(a[0], b[0], c[0]), 0)), int(min(max(a[0], b[0], c[0]) + 1, SIL))
        y0, y1 = int(max(min(a[1], b[1], c[1]), 0)), int(min(max(a[1], b[1], c[1]) + 1, SIL))
        if x1 <= x0 or y1 <= y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
        l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
        ins = (l0 >= 0) & (l1 >= 0) & (1 - l0 - l1 >= 0)
        img[ys[ins].astype(int), xs[ins].astype(int)] = True
    return img


VIEWS = {"front": 0.0, "side": 90.0, "back34": 135.0}


def _tris_of(m, pred=None):
    out = []
    for f, *_ in m.faces:
        if pred is None or pred(f):
            out += [(f[0], f[k], f[k + 1]) for k in range(1, len(f) - 1)]
    return out


def hair_metrics(body_m, hair_parts):
    """hair_parts: {스타일: Mesh(LOD0) 또는 None(삭발)} → 스타일별 볼륨·옆 폭, 쌍별 실루엣 차이."""
    skull = _head_points(body_m)
    hi = MS.OUR_NAMES.index("Head")
    dom = np.argmax(body_m.W, axis=1)
    head_tris = _tris_of(body_m, lambda f: all(dom[v] == hi for v in f))
    top, chin = skull[:, 1].max(), skull[:, 1].min()
    cx = 0.5 * (skull[:, 0].max() + skull[:, 0].min())
    ear = chin + 0.6 * (top - chin)  # 옆 폭은 귀 위 띠(두개골 높이 위 40 %)에서만 — 귀가 반폭을 키우지 않게
    half = np.abs(skull[skull[:, 1] >= ear, 0] - cx).max()
    lo = np.array([-0.2, chin - 0.15])
    span = 0.4
    out, masks = {}, {}
    for style, hm in hair_parts.items():
        sk_idx = np.flatnonzero(dom == hi)
        remap = {v: i for i, v in enumerate(sk_idx)}
        tris = [(remap[a], remap[b], remap[c]) for a, b, c in head_tris]
        pts = skull
        if hm is not None:
            off = len(skull)
            pts = np.vstack([skull, hm.verts])
            tris += [(a + off, b + off, c + off) for a, b, c in _tris_of(hm)]
        hv = pts[len(skull):] if hm is not None else skull[:0]
        vol = 0.0 if hm is None else max(0.0, float(hv[:, 1].max() - top)) / float(top - chin)
        up = hv[hv[:, 1] >= ear] if hm is not None else hv
        side = 0.0 if len(up) == 0 else max(0.0, float(np.abs(up[:, 0] - cx).max() - half)) / float(2 * half)
        ms = {}
        for name, deg in VIEWS.items():
            r = math.radians(deg)
            x = (pts[:, 0] - cx) * math.cos(r) + pts[:, 2] * math.sin(r)
            ms[name] = _tri_mask(np.stack([x, pts[:, 1]], axis=1), tris, lo, span)
        masks[style] = ms
        out[style] = {"volume": round(vol, 4), "side": round(side, 4)}
    styles = list(hair_parts)
    best = (2.0, None)
    below = []
    for i in range(len(styles)):
        for j in range(i + 1, len(styles)):
            d = 0.0
            for v in VIEWS:
                a, b = masks[styles[i]][v], masks[styles[j]][v]
                u = np.logical_or(a, b).sum()
                d = max(d, 1.0 - (np.logical_and(a, b).sum() / u if u else 1.0))
            if d < 0.08:
                below.append([styles[i], styles[j], round(d, 4)])
            if d < best[0]:
                best = (d, (styles[i], styles[j]))
    return {"styles": out, "pairsBelow008": below, "minPairSilhouette": round(best[0], 4), "minPair": list(best[1]) if best[1] else None}


def hair_texture(parts_tex, region=(0.25, 0.0, 1.0, 0.75)):
    h, w = parts_tex.shape[:2]
    u0, v0, u1, v1 = region
    cell = parts_tex[int(v0 * h):int(v1 * h), int(u0 * w):int(u1 * w), :3]
    lum = cell @ np.array([0.2126, 0.7152, 0.0722])
    valid = np.ones_like(lum, dtype=bool)
    g = _gauss(lum, valid, 1.0)
    log = (np.roll(g, 1, 0) + np.roll(g, -1, 0) + np.roll(g, 1, 1) + np.roll(g, -1, 1) - 4 * g)[2:-2, 2:-2]
    gx = np.diff(lum, axis=1)[1:-1, 1:-1]
    gy = np.diff(lum, axis=0)[1:-1, 1:-1]
    rx, ry = float(np.sqrt(np.mean(gx ** 2))), float(np.sqrt(np.mean(gy ** 2)))
    return {"strandHf": float(np.sqrt(np.mean(log ** 2))),
            "anisotropy": max(rx, ry) / max(min(rx, ry), 1e-9)}


# ---------------------------------------------------------------- 묶음

def all_metrics(mm, pm, tex_dir, styles):
    import os
    lod0 = mm[min(mm)]
    skin_n = load_image(os.path.join(tex_dir, "skin_normal.png"))
    kd = os.path.join(tex_dir, "kit_detail_n.png")
    kit_n, kit_tile = (load_image(kd), 1.0) if os.path.exists(kd) else (load_image(os.path.join(tex_dir, "kit_normal.png")), FABRIC_TILE)
    parts = load_image(os.path.join(tex_dir, "parts.png"))
    bd_path = os.path.join(tex_dir, "body_detail_n.png")
    body_detail = load_image(bd_path) if os.path.exists(bd_path) else None
    hair = {s: pm.get(f"{s}_LOD0") for s in styles}
    return {"ok": True, "lod": min(mm),  # 기록용 (판정은 테스트가 배수 기준으로)
            "muscle": muscle_contrast(lod0, skin_n, body_detail),
            "kit": kit_metrics(lod0, kit_n, kit_tile),
            "hair": hair_metrics(lod0, hair),
            "hairTexture": hair_texture(parts)}


# ---------------------------------------------------------------- 062 AC-02 윤곽 · 이음매 · 볼록 껍질 (손 지표 공용)

def convex_fill(pts, shape):
    """정수 픽셀 점들의 볼록 껍질을 채운 마스크."""
    P = sorted(set(map(tuple, np.asarray(pts).tolist())))
    if len(P) < 3:
        return np.zeros(shape, dtype=bool)
    def cr(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for q in P:
        while len(lo) >= 2 and cr(lo[-2], lo[-1], q) <= 0:
            lo.pop()
        lo.append(q)
    for q in reversed(P):
        while len(up) >= 2 and cr(up[-2], up[-1], q) <= 0:
            up.pop()
        up.append(q)
    hull = lo[:-1] + up[:-1]
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]]
    inside = np.ones(shape, dtype=bool)
    for i in range(len(hull)):
        (x0, y0), (x1, y1) = hull[i], hull[(i + 1) % len(hull)]
        inside &= (x1 - x0) * (yy - y0) - (y1 - y0) * (xx - x0) >= -0.5
    return inside


def label_count(mask, min_px):
    """4이웃 연결 성분 중 크기 ≥ min_px 개수."""
    seen = np.zeros(mask.shape, dtype=bool)
    H, W = mask.shape
    n = 0
    for y0, x0 in zip(*np.nonzero(mask)):
        if seen[y0, x0]:
            continue
        stack, size = [(y0, x0)], 0
        seen[y0, x0] = True
        while stack:
            y, x = stack.pop()
            size += 1
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < H and 0 <= nx < W and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    stack.append((ny, nx))
        n += size >= min_px
    return n


def raster_mask(Q, tris, lo, px, shape):
    img = np.zeros(shape, dtype=bool)
    q = (Q - lo) / px
    H, W = shape
    for t in tris:
        a, b, c = q[list(t)]
        x0, x1 = int(max(min(a[0], b[0], c[0]), 0)), int(min(max(a[0], b[0], c[0]) + 1, W))
        y0, y1 = int(max(min(a[1], b[1], c[1]), 0)), int(min(max(a[1], b[1], c[1]) + 1, H))
        if x1 <= x0 or y1 <= y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
        l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
        ins = (l0 >= 0) & (l1 >= 0) & (1 - l0 - l1 >= 0)
        img[ys[ins].astype(int), xs[ins].astype(int)] = True
    return img


def trace_boundary(m):
    ys, xs = np.nonzero(m)
    if len(xs) == 0:
        return []
    i = np.lexsort((xs, ys))[0]
    start = (int(xs[i]), int(ys[i]))
    H, W = m.shape
    nb = [(-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1)]
    pts, cur, back = [start], start, 0
    for _ in range(4 * int(m.sum()) + 8):
        found = False
        for k in range(8):
            dd = (back + 1 + k) % 8
            nx, ny = cur[0] + nb[dd][0], cur[1] + nb[dd][1]
            if 0 <= nx < W and 0 <= ny < H and m[ny, nx]:
                back = (dd + 4) % 8
                cur = (nx, ny)
                found = True
                break
        if not found or cur == start:
            break
        pts.append(cur)
    return pts


def contour_turns(mask, step):
    """imagechecks_062.outline_turn과 같은 식: 이동 평균 창 step + 앞뒤 step 픽셀 현 꺾임(도). → (점 (N,2), 꺾임 (N,))."""
    P = np.array(trace_boundary(mask), dtype=float)
    k = np.ones(step) / step
    Sm = np.stack([np.convolve(np.concatenate([P[-step:, d], P[:, d], P[:step, d]]), k, mode="same")[step:-step]
                   for d in (0, 1)], axis=1)
    a = Sm - np.roll(Sm, step, axis=0)
    b = np.roll(Sm, -step, axis=0) - Sm
    return P, np.degrees(np.arctan2(np.abs(a[:, 0] * b[:, 1] - a[:, 1] * b[:, 0]), (a * b).sum(axis=1)))


OUTLINE_PX = 0.0015   # 클로즈업 픽셀 ≈ 1.5 mm (얼굴이 1080p 화면 높이 25 %일 때)
OUTLINE_STEP = 6
OUTLINE_BOX = 0.08    # 관절 중심 ± 8 cm


def outline_metrics(m):
    """062 FR-05: LOD0 레스트(A-포즈) 정면 실루엣의 어깨·소매 끝·팔꿈치·무릎 윤곽 꺾임 최댓값(좌우 중 큰 값),
    소매 경계 밝기 단차(피부 띠 1 cm vs 소매 띠 1 cm, 정점 법선 기준 조명) / 두 띠 표준편차 평균."""
    from . import skeleton as S
    V = m.verts
    tris = [(f[0], f[k], f[k + 1]) for f, mat, uv, part, _ in m.faces if part not in ("hair", "eye")
            for k in range(1, len(f) - 1)]
    Q = V[:, :2].copy()
    lo = Q.min(axis=0) - 0.02
    shape = (int((Q[:, 1].max() + 0.02 - lo[1]) / OUTLINE_PX) + 1, int((Q[:, 0].max() + 0.02 - lo[0]) / OUTLINE_PX) + 1)
    mask = raster_mask(Q, tris, lo, OUTLINE_PX, shape)
    P, turn = contour_turns(mask, OUTLINE_STEP)
    import os
    if os.environ.get("PLAYER_DEBUG_DIR"):
        np.save(os.path.join(os.environ["PLAYER_DEBUG_DIR"], "outline_mask.npy"), mask)
        np.save(os.path.join(os.environ["PLAYER_DEBUG_DIR"], "outline_lo.npy"), lo)
    planes = {pl[0]: pl for pl in B.cut_planes()}
    centres = {}
    for s in ("Left", "Right"):
        centres.setdefault("shoulder", []).append(S.HEAD[s + "UpperArm"])
        centres.setdefault("sleeve", []).append(planes["sleeve" + s[0]][1])
        centres.setdefault("elbow", []).append(S.HEAD[s + "LowerArm"])
        centres.setdefault("knee", []).append(S.HEAD[s + "LowerLeg"])
    out = {"turnDeg": {}}
    n = len(P)
    for k, cs in centres.items():
        worst = 0.0
        for c in cs:
            cx, cy = (np.array(c[:2]) - lo) / OUTLINE_PX
            r = OUTLINE_BOX / OUTLINE_PX
            ins = (np.abs(P[:, 0] - cx) <= r) & (np.abs(P[:, 1] - cy) <= r)
            ok = ins & np.roll(ins, OUTLINE_STEP) & np.roll(ins, -OUTLINE_STEP)
            if ok.any():
                worst = max(worst, float(turn[ok].max()))
        out["turnDeg"][k] = round(worst, 2)
    # 소매 이음매
    vn = B._normals(V, [f[0] for f in m.faces])
    lit = np.maximum(vn @ L_DIR, 0.0)
    skin_v = sorted({v for f, mat, uv, part, _ in m.faces if part == "skin" for v in f})
    kit_v = sorted({v for f, mat, uv, part, _ in m.faces if part == "sleeve" for v in f})
    ratios = []
    for s in ("L", "R"):
        pl = planes["sleeve" + s]
        dsk = np.array([B._side(V[v], pl) for v in skin_v])
        dkt = np.array([B._side(V[v], pl) for v in kit_v])
        side_ok_s = np.array([(V[v, 0] < 0) == (s == "L") for v in skin_v])
        side_ok_k = np.array([(V[v, 0] < 0) == (s == "L") for v in kit_v])
        A = lit[np.array(skin_v)[(dsk > -0.01) & (dsk <= 0) & side_ok_s]]
        Bk = lit[np.array(kit_v)[(dkt >= 0) & (dkt < 0.01) & side_ok_k]]
        if len(A) and len(Bk):
            var = max(0.5 * (A.std() + Bk.std()), 1e-6)
            ratios.append(abs(A.mean() - Bk.mean()) / var)
    out["sleeveSeamRatio"] = round(float(max(ratios)) if ratios else 0.0, 3)
    out["ok"] = all(v <= 25.0 for v in out["turnDeg"].values()) and out["sleeveSeamRatio"] <= 1.5
    return out
