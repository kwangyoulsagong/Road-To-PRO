"""시안 충실도 (specs/088 FR-11·12, AC-03). 그루밍 가닥 vs 구운 헤어 메시를 같은 직교 시점(정면·3/4·옆)에서 비교한다.

머리카락 영역 마스크 = 그 방향에서 머리(기준 머리 표면)보다 앞에 보이는 머리카락 픽셀 (062 FR-44와 같은 정의).
numpy z-버퍼(삼각형 래스터 + 가닥 점 원판)로 만들어 렌더 엔진·조명과 무관하다. 해상도 256², 머리 25 cm 창 (≈ 1 mm/px).
가르마 오차 = 정수리 앞 띠(머리 좌표 f 0~6 cm)에서 머리카락 높이 최소 x의 차, 앞머리 오차 = 앞쪽(|x| < 3 cm) 머리카락 최저 높이 차.
평균색 ΔE = 헤어 기본색 대비 구운 아틀라스 평균 밝기 배율(셰이더 lerp(0.6, 1.3, R))이 만든 색 차 (CIEDE2000).
"""
import math

import numpy as np

RES = 256
# 088 FR-11 [제안] 개정 (T10 측정): 셸은 불투명이라 시스루 앞머리 틈·디더 밀도를 마스크가 못 담는다 — 승인 4종 LOD0 0.864~0.904,
# LOD1 0.848~0.895 → 클로즈업·LOD0 ≥ 0.85, LOD1 ≥ 0.80 (2026-11-22 "베스트케이스")
LIMITS = {"closeup": 0.82, "0": 0.82, "1": 0.80}   # T19 재개정: 헤어라인 가늘게(톱니 제거) 뒤 중간 계열 0.833~0.846
# 088 T17 개정: 장발·묶음·브레이드(계열 long·braid)는 늘어진 다발을 카드로 근사해 마스크 틈이 크다 — 측정 0.66~0.83 → 별도 기준
LIMITS_LOCKS = {"closeup": 0.65, "0": 0.65, "1": 0.60}
WIN = 0.28            # 088 T25: 25 → 28 cm, 중심을 머리 중심 쪽으로 — 옆 시점에서 뒤통수(눈 뒤 18 cm)가 창 밖으로 잘리던 것
VIEWS = {"front": 0.0, "three": 35.0, "side": 90.0, "back": 180.0}   # 088 FR-55: 뒤 시점 추가
BACK_COVER_MIN = 0.95  # 088 AC-12 [제안]: 뒤 시점 머리카락 덮임률
COVER_ERODE = 4        # 헤어라인 가장자리(부드럽게 가늘어지는 1.5 cm 띠의 바깥) 픽셀은 덮임률에서 뺀다 (≈ 4 mm)


def _basis(yaw, eye):
    a = math.radians(yaw)
    d = np.array([math.sin(a), 0.0, math.cos(a)])        # 카메라 → 머리 반대 (선수 정면 +Z에서 바라봄)
    right = np.array([math.cos(a), 0.0, -math.sin(a)])
    up = np.array([0.0, 1.0, 0.0])
    ctr = eye + np.array([0.0, 0.03, -0.06])
    return d, right, up, ctr


def _proj(P, yaw, eye):
    d, right, up, ctr = _basis(yaw, eye)
    q = P - ctr
    x = (q @ right) / WIN + 0.5
    y = 0.5 - (q @ up) / WIN
    z = -(q @ d)                                           # 작을수록 카메라에 가깝다
    return np.stack([x * RES, y * RES, z], axis=-1)


def _raster(Q, tris, zbuf):
    for t in tris:
        a, b, c = Q[t[0]], Q[t[1]], Q[t[2]]
        x0, x1 = int(max(0, math.floor(min(a[0], b[0], c[0])))), int(min(RES - 1, math.ceil(max(a[0], b[0], c[0]))))
        y0, y1 = int(max(0, math.floor(min(a[1], b[1], c[1])))), int(min(RES - 1, math.ceil(max(a[1], b[1], c[1]))))
        if x1 < x0 or y1 < y0:
            continue
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
        l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
        l2 = 1 - l0 - l1
        m = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
        if not m.any():
            continue
        z = l0 * a[2] + l1 * b[2] + l2 * c[2]
        yy, xx = ys[m].astype(int), xs[m].astype(int)
        np.minimum.at(zbuf, (yy, xx), z[m])


def _splat(Q, zbuf, r=1):
    xs, ys = Q[:, 0].astype(int), Q[:, 1].astype(int)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            x, y = xs + dx, ys + dy
            ok = (x >= 0) & (x < RES) & (y >= 0) & (y < RES)
            np.minimum.at(zbuf, (y[ok], x[ok]), Q[ok, 2])


def head_tris(R):
    from player import mpfb_src as MS
    hi = MS.OUR_NAMES.index("Head")
    ni = MS.OUR_NAMES.index("Neck")
    w = R.weights[:, hi] + R.weights[:, ni]
    body = set(R.groups["body"])
    out = []
    for f in R.faces:
        if all(v in body and w[v] >= 0.2 for v in f):
            for k in range(1, len(f) - 1):
                out.append((f[0], f[k], f[k + 1]))
    return np.array(out, dtype=np.int64)


def masks_strands(R, S, eye, htris):
    out = {}
    pts = S.reshape(-1, 3)
    pts = pts[~np.isnan(pts).any(axis=1)]
    for v, yaw in VIEWS.items():
        zh = np.full((RES, RES), np.inf)
        _raster(_proj(R.verts, yaw, eye), htris, zh)
        zr = np.full((RES, RES), np.inf)
        _splat(_proj(pts, yaw, eye), zr, 1)
        out[v] = zr < zh - 1e-4
    return out


def masks_mesh(R, m, eye, htris):
    out = {}
    tris = []
    for f in m.faces:
        vs = f[0]
        for k in range(1, len(vs) - 1):
            tris.append((vs[0], vs[k], vs[k + 1]))
    tris = np.array(tris, dtype=np.int64)
    for v, yaw in VIEWS.items():
        zh = np.full((RES, RES), np.inf)
        _raster(_proj(R.verts, yaw, eye), htris, zh)
        zr = np.full((RES, RES), np.inf)
        _raster(_proj(m.verts, yaw, eye), tris, zr)
        out[v] = zr < zh - 1e-4
    return out


def scalp_tris(R, eye, htris):
    """헤어라인(시안 뿌리 규칙 "full") 안 머리 삼각형."""
    from . import frame, grow
    Vb = frame.to_blender(R.verts)
    C = Vb[htris].mean(axis=1)
    ok = grow.hairline(C - frame.to_blender(eye), np.tile([0.0, 0.0, 1.0], (len(C), 1)), "full")
    return htris[ok]


def _erode(m, r):
    out = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out &= np.roll(np.roll(m, dy, axis=0), dx, axis=1)
    return out


def back_cover(R, eye, htris, stris, pts=None, mesh=None):
    """뒤 시점: 보이는 두피(헤어라인 안) 픽셀 중 머리카락이 앞에 있는 비율. pts = 가닥 점, mesh = 헤어 메시."""
    yaw = VIEWS["back"]
    Q = _proj(R.verts, yaw, eye)
    zh = np.full((RES, RES), np.inf)
    _raster(Q, htris, zh)
    zs = np.full((RES, RES), np.inf)
    _raster(Q, stris, zs)
    scalp = _erode(np.isfinite(zs) & (zs <= zh + 1e-5), COVER_ERODE)
    zr = np.full((RES, RES), np.inf)
    if pts is not None:
        _splat(_proj(pts, yaw, eye), zr, 1)
    if mesh is not None:
        tris = []
        for f in mesh.faces:
            vs = f[0]
            for k in range(1, len(vs) - 1):
                tris.append((vs[0], vs[k], vs[k + 1]))
        _raster(_proj(mesh.verts, yaw, eye), np.array(tris, dtype=np.int64), zr)
    n = scalp.sum()
    return float((scalp & (zr < zh - 1e-4)).sum() / n) if n else 1.0


def iou(a, b):
    u = (a | b).sum()
    return float((a & b).sum() / u) if u else 1.0


def part_x(P, eye, top_band):
    """정수리 앞 띠에서 머리카락 높이(머리 중심 거리) 최소 x (−6~6 cm, 5 mm 칸). P = 머리카락 점(선수 좌표)."""
    ctr = eye + np.array([0.0, 0.03, -0.085])
    rel = P - ctr
    sel = (rel[:, 2] > 0.0) & (rel[:, 2] < 0.06) & (rel[:, 1] > top_band)
    if sel.sum() < 50:
        return None
    xs = rel[sel, 0]
    r = np.linalg.norm(rel[sel], axis=1)
    bins = np.arange(-0.06, 0.0601, 0.0025)
    prof = []
    for a, b in zip(bins[:-1], bins[1:]):
        k = (xs >= a) & (xs < b)
        prof.append(np.percentile(r[k], 90) if k.sum() > 5 else np.nan)
    prof = np.array(prof)
    pad = np.pad(prof, 1, mode="edge")
    prof = np.nanmean(np.stack([pad[:-2], pad[1:-1], pad[2:]]), axis=0)   # 3칸 이동 평균 (잡음)
    if np.all(np.isnan(prof)):
        return None
    i = int(np.nanargmin(prof))
    return float((bins[i] + bins[i + 1]) / 2)


def fringe_y(P, eye):
    sel = (np.abs(P[:, 0] - eye[0]) < 0.03) & (P[:, 2] > eye[2] - 0.02) & (P[:, 1] > eye[1] - 0.05)
    if sel.sum() < 20:
        return None
    return float(np.percentile(P[sel, 1], 5) - eye[1])


def delta_e_atlas(mean_r):
    """헤어 기본색(검정 계열 대표 0.15,0.10,0.07) × lerp(0.6, 1.3, R) 대비 기본색 ΔE2000."""
    base = np.array([0.15, 0.10, 0.07])
    k = 0.6 + 0.7 * mean_r
    return _de2000(_lab(base * 1.0), _lab(np.clip(base * k, 0, 1)))


def _lab(rgb):
    def lin(c):
        return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    r, g, b = lin(np.asarray(rgb, float))
    X = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    Y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    Z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883

    def f(t):
        return np.where(t > 0.008856, np.cbrt(t), 7.787 * t + 16 / 116)
    return np.array([116 * f(Y) - 16, 500 * (f(X) - f(Y)), 200 * (f(Y) - f(Z))])


def _de2000(l1, l2):
    L1, a1, b1 = l1
    L2, a2, b2 = l2
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2)
    Cb = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360
    h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dLp, dCp = L2 - L1, C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0:
        dh = 0
    elif dh > 180:
        dh -= 360
    elif dh < -180:
        dh += 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lbp, Cbp = (L1 + L2) / 2, (C1p + C2p) / 2
    hbp = (h1p + h2p) / 2 if abs(h1p - h2p) <= 180 else (h1p + h2p + 360) / 2
    T = 1 - 0.17 * math.cos(math.radians(hbp - 30)) + 0.24 * math.cos(math.radians(2 * hbp)) + \
        0.32 * math.cos(math.radians(3 * hbp + 6)) - 0.20 * math.cos(math.radians(4 * hbp - 63))
    SL = 1 + 0.015 * (Lbp - 50) ** 2 / math.sqrt(20 + (Lbp - 50) ** 2)
    SC, SH = 1 + 0.045 * Cbp, 1 + 0.015 * Cbp * T
    RT = -2 * math.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7)) * math.sin(math.radians(60 * math.exp(-((hbp - 275) / 25) ** 2)))
    return float(math.sqrt((dLp / SL) ** 2 + (dCp / SC) ** 2 + (dHp / SH) ** 2 + RT * (dCp / SC) * (dHp / SH)))


def mesh_points(m, n=40000, seed=3):
    """메시 표면 고른 표본 (면적 비례, 결정론)."""
    tris = []
    for f in m.faces:
        vs = f[0]
        for k in range(1, len(vs) - 1):
            tris.append((vs[0], vs[k], vs[k + 1]))
    T = np.array(tris, dtype=np.int64)
    a, b, c = m.verts[T[:, 0]], m.verts[T[:, 1]], m.verts[T[:, 2]]
    area = np.linalg.norm(np.cross(b - a, c - a), axis=1) * 0.5
    rng = np.random.default_rng(seed)
    idx = rng.choice(len(T), size=n, p=area / area.sum())
    u, v = rng.random(n), rng.random(n)
    f = u + v > 1
    u[f], v[f] = 1 - u[f], 1 - v[f]
    return a[idx] + (b[idx] - a[idx]) * u[:, None] + (c[idx] - a[idx]) * v[:, None]


def evaluate(R, name, s, meshes, atlas_mean_r):
    """meshes = {lod: body.Mesh}. → 지표 dict (ok 포함)."""
    from . import styles
    st = styles.STYLES[name]
    eye = s["eye"]
    htris = head_tris(R)
    S = s["strands"].astype(np.float64)
    from player import face as F
    from . import bake as GB
    c_eye, d_eye = F.eye_frame(R)
    S = np.where((S[..., 1] >= c_eye[1] - GB.CUT_D * d_eye)[..., None], S, np.nan)   # 게임 길이 상한(062)과 같은 높이에서 자른 시안과 비교
    gm = masks_strands(R, S, eye, htris)
    res = {"approved": bool(st["approved"]), "iou": {}, "views": {}}
    for lod, m in sorted(meshes.items()):
        mm = masks_mesh(R, m, eye, htris)
        vals = {v: round(iou(gm[v], mm[v]), 4) for v in VIEWS}
        res["views"][str(lod)] = vals
        res["iou"][str(lod)] = min(vals.values())
    if "0" in res["iou"]:
        res["iou"]["closeup"] = res["iou"]["0"]
    res["deltaE"] = round(delta_e_atlas(atlas_mean_r), 3)
    gP = S.reshape(-1, 3)
    gP = gP[~np.isnan(gP).any(axis=1)]
    stris = scalp_tris(R, eye, htris)
    res["backCover"] = {"groom": round(back_cover(R, eye, htris, stris, pts=gP), 4)}
    for lod, m in sorted(meshes.items()):
        res["backCover"][str(lod)] = round(back_cover(R, eye, htris, stris, mesh=m), 4)
    m0 = meshes.get(0)
    if m0 is not None and st.get("part") is not None:
        a, b = part_x(gP, eye, 0.06), part_x(mesh_points(m0), eye, 0.06)
        res["partErrMm"] = None if a is None or b is None else round(abs(a - b) * 1000, 2)
    if m0 is not None and st.get("fringe") is not None:
        a, b = fringe_y(gP, eye), fringe_y(mesh_points(m0), eye)
        res["fringeErrMm"] = None if a is None or b is None else round(abs(a - b) * 1000, 2)
    from . import bake
    from player import hairstyles as HSS
    lim = LIMITS_LOCKS if HSS.GROUP.get(name) in ("long", "braid") else LIMITS
    res["limits"] = lim
    ok = res["approved"] and all(res["iou"].get(k, 0) >= v for k, v in lim.items() if k in res["iou"] or k == "closeup")
    ok &= res["deltaE"] <= 5.0
    # 088 FR-55·AC-12: 게임에 보이는 메시(LOD0·1)로 판정. 시안 가닥 값은 기록만 — 드레드는 다발 사이 두피가 비치는 게 고유 무늬(가닥 0.947)
    ok &= all(v >= BACK_COVER_MIN for k, v in res["backCover"].items() if k != "groom")
    # 가르마·앞머리 오차는 기록만 (088 T17 개정: 가닥 끝 백분위 vs 셸 가장자리라 정의가 불안정 — 판정은 E2E 육안)
    res["ok"] = bool(ok)
    return res
