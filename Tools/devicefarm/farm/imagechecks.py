"""캡처 이미지 검사 (specs/044 AC-10~13, specs/043 AC-13). 시나리오 `imageChecks`가 캡처 이름과 검사 종류를 지정한다.

영역 좌표는 드라이버가 캡처 때 남긴 `regions`(PNG 픽셀, 왼쪽 위 원점):
- 사각형 [x, y, w, h], 선분 {"a": [x, y], "b": [x, y]}, 점 [x, y], 점 목록 [[x, y], ...].
"""
import re

import numpy as np

TYPES = ("notBlank", "bandFlip", "lineContinuity", "blobX", "deltaE", "glyphInside", "contactDistance", "sameImage",
         "differentImage",
         # specs/061 그래픽 품질 지표
         "colorStats", "exposure", "exposureMatch", "sky", "regionRatio", "grayRatio", "clipping", "rimContrast",
         "contactDarkening", "modeGap", "frameDeltaE", "aaEdge", "panFlicker", "groundLineStd", "regionStd",
         "sharpness", "grassShade",
         # specs/062 선수 비주얼 지표 (imagechecks_062.py)
         "outlineTurn", "seamStep", "handConcavity", "kitStats", "silhouetteDiff", "highlightAnisotropy",
         "tierMatch", "textSpots")
TYPES_062 = TYPES[-8:]
PAIRED = ("bandFlip", "sameImage", "differentImage", "modeGap", "aaEdge", "sharpness", "tierMatch")  # 캡처 두 장 (capture + other)
MULTI = ("exposureMatch", "frameDeltaE", "panFlicker", "silhouetteDiff")  # 캡처 여러 장 (capture + others)


def _lum(a):
    a = a.astype(np.float64)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def _rect_mean(lum, r):
    x, y, w, h = (int(round(v)) for v in r)
    H, W = lum.shape
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        raise ValueError(f"영역이 화면 밖 {r}")
    return float(lum[y0:y1, x0:x1].mean())


def not_blank(arr, regions, min_std=8.0, min_colors=3):
    """AC-10: 빈 화면·검은 화면 아님 — 밝기 표준편차와 색 수."""
    lum = _lum(arr)
    std = float(lum.std())
    colors = len(np.unique(arr.reshape(-1, 3)[:: max(1, arr.shape[0] * arr.shape[1] // 50000)], axis=0))
    ok = std >= min_std and colors >= min_colors and float(lum.mean()) > 5.0
    return {"ok": ok, "detail": f"밝기 표준편차 {std:.1f} (≥ {min_std}), 색 {colors} (≥ {min_colors})", "std": std}


def band_flip(arr_a, reg_a, arr_b, reg_b, min_delta=2.0):
    """AC-11: 같은 띠 두 개의 밝기 순서가 반대 시점에서 뒤집힌다."""
    la, lb = _lum(arr_a), _lum(arr_b)
    a1, a2 = _rect_mean(la, reg_a["bandA"]), _rect_mean(la, reg_a["bandB"])
    b1, b2 = _rect_mean(lb, reg_b["bandA"]), _rect_mean(lb, reg_b["bandB"])
    da, db = a1 - a2, b1 - b2
    ok = abs(da) >= min_delta and abs(db) >= min_delta and (da > 0) != (db > 0)
    return {"ok": ok, "detail": f"시점 A: 띠A {a1:.1f} / 띠B {a2:.1f} (차 {da:+.1f}), 시점 B: {b1:.1f} / {b2:.1f} (차 {db:+.1f}), 최소 차 {min_delta}",
            "deltaA": da, "deltaB": db}


def line_continuity(arr, regions, samples=200, max_width_std=1.0, half_scan=14, min_contrast=25.0):
    """AC-12: 라인 중심선을 따라 `samples`칸 샘플 — 각 칸에서 수직으로 훑어 라인 픽셀이 없으면 끊김, 폭 표준편차."""
    lum = _lum(arr)
    H, W = lum.shape
    seg = regions["line"]
    a = np.array(seg["a"], dtype=np.float64)
    b = np.array(seg["b"], dtype=np.float64)
    d = b - a
    length = float(np.hypot(*d))
    if length < samples:
        return {"ok": False, "detail": f"라인 화면 길이 {length:.0f}px < 샘플 {samples}", "gaps": samples}
    t = d / length
    n = np.array([-t[1], t[0]])
    start = (length - samples) / 2
    gaps, widths = 0, []
    for k in range(samples):
        c = a + t * (start + k + 0.5)
        vals = []
        for s in range(-half_scan, half_scan + 1):
            x, y = int(np.floor(c[0] + n[0] * s + 0.5)), int(np.floor(c[1] + n[1] * s + 0.5))
            vals.append(lum[y, x] if 0 <= x < W and 0 <= y < H else np.nan)
        v = np.array(vals)
        if np.isnan(v).any() or v.max() - v.min() < min_contrast:
            gaps += 1
            continue
        thr = (v.max() + v.min()) / 2
        widths.append(int((v > thr).sum()))
    std = float(np.std(widths)) if widths else float("inf")
    ok = gaps == 0 and std <= max_width_std
    return {"ok": ok, "detail": f"샘플 {samples}칸 중 끊김 {gaps}, 폭 평균 {np.mean(widths) if widths else 0:.2f}px 표준편차 {std:.2f}px (≤ {max_width_std})",
            "gaps": gaps, "widthStd": std}


def blob_x(arr, regions, min_darkening=0.12, box=6, ring=18):
    """AC-13: 예상 위치 4곳이 주변 지면보다 어둡고, 발 기준 대각 두 쌍이 서로 반대 방향(X자)."""
    lum = _lum(arr)
    foot = np.array(regions["foot"], dtype=np.float64)
    blobs = [np.array(c, dtype=np.float64) for c in regions["blobs"]]
    if len(blobs) != 4:
        return {"ok": False, "detail": f"블롭 위치 {len(blobs)}개 (4개 필요)"}
    darks = []
    for c in blobs:
        inner = _rect_mean(lum, [c[0] - box, c[1] - box, 2 * box, 2 * box])
        # 바깥 기준: 드라이버가 준 블롭 옆 지면 점(regions.refs), 없으면 발 방향과 수직으로 ring px
        k = len(darks)
        if regions.get("refs"):
            pts = [np.array(q, dtype=np.float64) for q in regions["refs"][k]]
        else:
            v = c - foot
            nv = np.array([-v[1], v[0]]) / max(np.hypot(*v), 1e-6)
            pts = [c + nv * ring, c - nv * ring]
        refs = [_rect_mean(lum, [p[0] - box, p[1] - box, 2 * box, 2 * box]) for p in pts]
        ref = max(refs)
        darks.append(0.0 if ref <= 0 else (ref - inner) / ref)
    dirs = [(c - foot) / max(np.hypot(*(c - foot)), 1e-6) for c in blobs]
    # 가장 반대인 짝을 찾아 두 쌍이 모두 반대인지
    pairs = sorted(((float(np.dot(dirs[i], dirs[j])), i, j) for i in range(4) for j in range(i + 1, 4)))
    used, opp = set(), []
    for dot, i, j in pairs:
        if i in used or j in used:
            continue
        used |= {i, j}
        opp.append(dot)
    x_ok = len(opp) == 2 and all(d < -0.7 for d in opp)
    dark_ok = all(d >= min_darkening for d in darks)
    return {"ok": x_ok and dark_ok,
            "detail": f"어두워진 비율 {', '.join(f'{d:.2f}' for d in darks)} (≥ {min_darkening}), 대각 쌍 내적 {', '.join(f'{d:.2f}' for d in opp)} (< −0.7)"}


# ---------------------------------------------------------------- specs/043 AC-13

def srgb_to_lab(rgb):
    """sRGB 0~255 (…, 3) → CIELAB D65. Presentation/Players/Logic/CieDe2000.cs와 같은 상수."""
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    x = (0.4124564 * lin[..., 0] + 0.3575761 * lin[..., 1] + 0.1804375 * lin[..., 2]) / 0.95047
    y = 0.2126729 * lin[..., 0] + 0.7151522 * lin[..., 1] + 0.0721750 * lin[..., 2]
    z = (0.0193339 * lin[..., 0] + 0.1191920 * lin[..., 1] + 0.9503041 * lin[..., 2]) / 1.08883

    def f(t):
        return np.where(t > 216.0 / 24389.0, np.cbrt(t), (24389.0 / 27.0 * t + 16.0) / 116.0)
    fx, fy, fz = f(x), f(y), f(z)
    return np.stack([116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz)], axis=-1)


def delta_e2000(lab1, lab2):
    """CIEDE2000 (Sharma·Wu·Dalal 2005), kL = kC = kH = 1. 스칼라 Lab 두 개."""
    L1, a1, b1 = (float(v) for v in lab1)
    L2, a2, b2 = (float(v) for v in lab2)
    import math
    c1, c2 = math.hypot(a1, b1), math.hypot(a2, b2)
    cb7 = ((c1 + c2) / 2) ** 7
    g = 0.5 * (1 - math.sqrt(cb7 / (cb7 + 25.0 ** 7)))
    a1p, a2p = (1 + g) * a1, (1 + g) * a2
    c1p, c2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1 = 0.0 if a1p == 0 and b1 == 0 else math.degrees(math.atan2(b1, a1p)) % 360
    h2 = 0.0 if a2p == 0 and b2 == 0 else math.degrees(math.atan2(b2, a2p)) % 360
    dL, dC = L2 - L1, c2p - c1p
    if c1p * c2p == 0:
        dh = 0.0
    elif abs(h2 - h1) <= 180:
        dh = h2 - h1
    elif h2 - h1 > 180:
        dh = h2 - h1 - 360
    else:
        dh = h2 - h1 + 360
    dH = 2 * math.sqrt(c1p * c2p) * math.sin(math.radians(dh / 2))
    lb, cpb = (L1 + L2) / 2, (c1p + c2p) / 2
    if c1p * c2p == 0:
        hb = h1 + h2
    elif abs(h1 - h2) <= 180:
        hb = (h1 + h2) / 2
    elif h1 + h2 < 360:
        hb = (h1 + h2 + 360) / 2
    else:
        hb = (h1 + h2 - 360) / 2
    t = (1 - 0.17 * math.cos(math.radians(hb - 30)) + 0.24 * math.cos(math.radians(2 * hb))
         + 0.32 * math.cos(math.radians(3 * hb + 6)) - 0.20 * math.cos(math.radians(4 * hb - 63)))
    dth = 30 * math.exp(-(((hb - 275) / 25) ** 2))
    rc = 2 * math.sqrt(cpb ** 7 / (cpb ** 7 + 25.0 ** 7))
    sl = 1 + 0.015 * (lb - 50) ** 2 / math.sqrt(20 + (lb - 50) ** 2)
    sc, sh = 1 + 0.045 * cpb, 1 + 0.015 * cpb * t
    rt = -math.sin(math.radians(2 * dth)) * rc
    return math.sqrt((dL / sl) ** 2 + (dC / sc) ** 2 + (dH / sh) ** 2 + rt * (dC / sc) * (dH / sh))


def _group_color(arr, rects):
    """사각형들 안 픽셀의 채널별 중앙값(글자·그림자에 강함)."""
    H, W = arr.shape[:2]
    px = []
    for r in rects:
        x, y, w, h = (int(round(v)) for v in r)
        x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
        if x1 > x0 and y1 > y0:
            px.append(arr[y0:y1, x0:x1, :3].reshape(-1, 3))
    if not px:
        raise ValueError(f"색 영역이 화면 밖 {rects}")
    return np.median(np.concatenate(px, axis=0), axis=0)


DELTA_PAIRS = (("home", "away"), ("homeGk", "home"), ("homeGk", "away"), ("awayGk", "home"), ("awayGk", "away"),
               ("referee", "home"), ("referee", "away"))


def delta_e(arr, regions, min_delta_e=20.0):
    """043 AC-13: 그룹(홈 필드·원정 필드·GK·심판) 셔츠 앞판 색 중앙값끼리 ΔE2000 ≥ 임계.
    캡처에 있는 그룹 쌍만 본다(줄마다 일부 그룹). 2개 이상 그룹 필요. regions.combos가 있으면 외형 조합 중복 0도 확인."""
    groups = regions["colorGroups"]
    labs = {k: srgb_to_lab(_group_color(arr, v)) for k, v in groups.items() if v}
    rows, ok, n = [], True, 0
    for a, b in DELTA_PAIRS:
        if a not in labs or b not in labs:
            continue
        d = delta_e2000(labs[a], labs[b])
        ok &= d >= min_delta_e
        n += 1
        rows.append(f"{a}–{b} {d:.1f}")
    if n == 0:
        ok = False
        rows.append(f"비교할 그룹 쌍 없음 ({', '.join(sorted(labs))})")
    detail = f"ΔE2000 {', '.join(rows)} (≥ {min_delta_e})"
    combos = regions.get("combos")
    if combos is not None:
        dup = len(combos) - len(set(combos))
        ok &= dup == 0
        detail += f"; 외형 조합 {len(combos)}개 중복 {dup}"
    return {"ok": ok, "detail": detail}


def glyph_inside(arr, regions, min_glyph_px=12, tolerance_px=2, margin=0.3):
    """043 AC-13: 뒤 번호·이름 영역(화면 투영 사각형) 밖 주변 띠에 글자 픽셀 0, 번호 글자 높이 ≥ min_glyph_px.
    regions.textAreas: [{"area": [x,y,w,h], "text": [r,g,b], "base": [r,g,b], "kind": "number"|"name"}] (색은 조명 전 sRGB).
    조명 배율 k = 주변 띠 중앙값 / base 로 추정 → 글자 픽셀 = 예상 글자색(text·k)에 더 가깝고 거리 < 60인 픽셀."""
    rows, ok = [], True
    H, W = arr.shape[:2]
    a = arr[..., :3].astype(np.float64)
    # 같은 엔티티의 다른 글자 영역(번호 바로 위 이름 등)은 '밖'으로 치지 않는다
    areas_by_entity = {}
    for t in regions["textAreas"]:
        areas_by_entity.setdefault(t.get("entity"), []).append([float(v) for v in t["area"]])
    for k, t in enumerate(regions["textAreas"]):
        x, y, w, h = (float(v) for v in t["area"])
        text = np.array(t["text"], dtype=np.float64)
        base = np.array(t["base"], dtype=np.float64)
        mx, my = w * margin, h * margin
        X0, Y0 = int(max(0, np.floor(x - mx))), int(max(0, np.floor(y - my)))
        X1, Y1 = int(min(W, np.ceil(x + w + mx))), int(min(H, np.ceil(y + h + my)))
        if X1 <= X0 or Y1 <= Y0:
            ok = False
            rows.append(f"#{k}: 영역이 화면 밖")
            continue
        sub = a[Y0:Y1, X0:X1]
        yy, xx = np.mgrid[Y0:Y1, X0:X1]
        inside = (xx >= x - tolerance_px) & (xx < x + w + tolerance_px) & (yy >= y - tolerance_px) & (yy < y + h + tolerance_px)
        other = np.zeros_like(inside)
        for ox, oy, ow, oh in areas_by_entity.get(t.get("entity"), []):
            other |= (xx >= ox - tolerance_px) & (xx < ox + ow + tolerance_px) & (yy >= oy - tolerance_px) & (yy < oy + oh + tolerance_px)
        band = sub[~(inside | other)]
        kl = 1.0
        if band.size:
            px = band.reshape(-1, 3)
            # 조명 배율은 바탕(셔츠)과 같은 색조 픽셀로만 — 띠가 반바지·피부·하늘에 걸치면 중앙값이 틀어진다 (specs/056 T19b)
            bn = base / max(np.linalg.norm(base), 1.0)
            cos = (px @ bn) / np.maximum(np.linalg.norm(px, axis=1), 1.0)
            same = px[cos > 0.97]
            med = np.median(same if len(same) >= 20 else px, axis=0)
            kl = float(np.clip(med.sum() / max(base.sum(), 1.0), 0.2, 3.0))
        tl, bl = np.clip(text * kl, 0, 255), np.clip(base * kl, 0, 255)
        dt = np.linalg.norm(sub - tl, axis=-1)
        db = np.linalg.norm(sub - bl, axis=-1)
        glyph = (dt < db) & (dt < 60.0)
        outside = int((glyph & ~(inside | other)).sum())
        rows_in = np.where((glyph & inside).any(axis=1))[0]
        height = int(rows_in.max() - rows_in.min() + 1) if rows_in.size else 0
        need_h = min_glyph_px if t.get("kind", "number") == "number" else 1
        good = outside == 0 and height >= need_h
        ok &= good
        rows.append(f"#{k} {t.get('kind', 'number')}: 높이 {height}px (≥ {need_h}), 영역 밖 {outside}px")
    if not regions["textAreas"]:
        ok = False
        rows.append("글자 영역 없음")
    return {"ok": ok, "detail": "; ".join(rows)}


def contact_distance(arr, regions, max_ratio=0.5):
    """043 AC-14: 접촉 프레임에서 접촉 신체 부위(클립 접촉 규칙을 실제 본으로 계산한 점)의 화면 투영과 공 중심 투영 거리 ≤ 공 화면 지름 × max_ratio."""
    c = regions["contact"]
    p, b, dia = np.array(c["point"], dtype=np.float64), np.array(c["ball"], dtype=np.float64), float(c["ballDiameter"])
    dist = float(np.hypot(*(p - b)))
    ok = dia > 0 and dist <= max_ratio * dia
    return {"ok": ok, "detail": f"{c.get('clip', '')}: 접촉점–공 {dist:.1f}px ≤ {max_ratio} × 공 지름 {dia:.1f}px ({max_ratio * dia:.1f})"}


def _crop(arr, rect):
    if rect is None:
        return arr
    x, y, w, h = (int(round(v)) for v in rect)
    H, W = arr.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        raise ValueError(f"영역이 화면 밖 {rect}")
    return arr[y0:y1, x0:x1]


def _pixel_diff(arr_a, arr_b, rect=None):
    a, b = _crop(arr_a, rect), _crop(arr_b, rect)
    if a.shape != b.shape:
        raise ValueError(f"크기 다름 {a.shape} vs {b.shape}")
    return np.abs(a[..., :3].astype(np.int16) - b[..., :3].astype(np.int16)).max(axis=-1)


def same_image(arr_a, reg_a, arr_b, reg_b, max_mean_diff=0.5, max_diff_ratio=0.001, rect=None):
    """043 AC-15: 같은 시뮬 시각을 다른 배속으로 캡처한 두 장이 같은 포즈(픽셀 평균 차·다른 픽셀 비율).
    rect [x, y, w, h]가 있으면 그 영역만 (048 AC-06: UI 영역은 렌더 스케일과 무관)."""
    if rect is None and arr_a.shape != arr_b.shape:
        return {"ok": False, "detail": f"크기 다름 {arr_a.shape} vs {arr_b.shape}"}
    d = _pixel_diff(arr_a, arr_b, rect)
    mean = float(d.mean())
    ratio = float((d > 8).mean())
    ok = mean <= max_mean_diff and ratio <= max_diff_ratio
    where = f" 영역 {rect}" if rect is not None else ""
    return {"ok": ok, "detail": f"평균 차 {mean:.3f} (≤ {max_mean_diff}), 다른 픽셀(>8) {ratio * 100:.3f}% (≤ {max_diff_ratio * 100:.3f}%){where}"}


def different_image(arr_a, reg_a, arr_b, reg_b, min_diff_ratio=0.02, threshold=8, rect=None):
    """048 AC-05: 같은 장면을 다른 프리셋으로 캡처한 두 장이 실제로 다르게 그려졌다 (다른 픽셀 비율 ≥ 기준)."""
    d = _pixel_diff(arr_a, arr_b, rect)
    ratio = float((d > threshold).mean())
    return {"ok": ratio >= min_diff_ratio, "ratio": ratio,
            "detail": f"다른 픽셀(>{threshold}) {ratio * 100:.2f}% (≥ {min_diff_ratio * 100:.2f}%)"}


def run(checks, captures):
    """checks: 시나리오 imageChecks. captures: {이름: (RGB 배열, regions)}. → 결과 목록."""
    out = []
    for c in checks:
        t = c["type"]
        params = {re.sub(r"([A-Z])", lambda m: "_" + m.group(1).lower(), k): v
                  for k, v in c.items() if k not in ("type", "capture", "other", "others", "ac")}
        row = {"type": t, "capture": c["capture"], "ac": c.get("ac"), "ok": False, "detail": ""}
        try:
            if c["capture"] not in captures:
                raise KeyError(f"캡처 없음: {c['capture']}")
            arr, reg = captures[c["capture"]]
            if t == "notBlank":
                r = not_blank(arr, reg, **params)
            elif t == "bandFlip":
                if c["other"] not in captures:
                    raise KeyError(f"캡처 없음: {c['other']}")
                arr2, reg2 = captures[c["other"]]
                r = band_flip(arr, reg, arr2, reg2, **params)
            elif t == "lineContinuity":
                r = line_continuity(arr, reg, **params)
            elif t == "blobX":
                r = blob_x(arr, reg, **params)
            elif t == "deltaE":
                r = delta_e(arr, reg, **params)
            elif t == "glyphInside":
                r = glyph_inside(arr, reg, **params)
            elif t == "contactDistance":
                r = contact_distance(arr, reg, **params)
            elif t == "sameImage":
                if c["other"] not in captures:
                    raise KeyError(f"캡처 없음: {c['other']}")
                arr2, reg2 = captures[c["other"]]
                r = same_image(arr, reg, arr2, reg2, **params)
            elif t == "differentImage":
                if c["other"] not in captures:
                    raise KeyError(f"캡처 없음: {c['other']}")
                arr2, reg2 = captures[c["other"]]
                r = different_image(arr, reg, arr2, reg2, **params)
            elif t in TYPES_062:
                from . import imagechecks_062
                r = imagechecks_062.run_062(t, c, params, captures)
            elif t in TYPES:
                r = run_061(t, c, params, captures)
            else:
                raise KeyError(f"알 수 없는 검사 {t}")
            row.update(ok=bool(r["ok"]), detail=r["detail"])
        except (KeyError, ValueError, IndexError, TypeError) as e:
            row["detail"] = str(e).strip("'")
        out.append(row)
    return out


# ---------------------------------------------------------------- specs/061 그래픽 품질 지표
# 마스크: 드라이버가 같은 프레임을 분류 색으로 다시 그린 캡처(시나리오 `mask` 인자에 그 캡처 이름).
# 분류 색 (±MASK_TOL): 하늘·잔디·선수·경기장 밖·라인·구조물. 마스크가 없으면 화면 전체(HUD는 꺼서 캡처).

MASK_COLORS = {"sky": (255, 0, 255), "grass": (0, 255, 0), "player": (255, 0, 0), "outside": (0, 0, 255),
               "line": (255, 255, 255), "structure": (128, 128, 128), "shade": (0, 255, 255), "sun": (255, 255, 0),
               # specs/062: 키트 주색·피부·머리카락 (선수를 부위별로 나눈 마스크)
               "kit": (255, 128, 0), "skin": (128, 0, 255), "hair": (0, 128, 255)}
MASK_TOL = 30


def masks_from(arr):
    """분류 색 캡처 → {분류: bool 배열}."""
    a = arr[..., :3].astype(np.int16)
    return {k: (np.abs(a - np.array(c, dtype=np.int16)).max(axis=-1) <= MASK_TOL) for k, c in MASK_COLORS.items()}


def _sel(arr, masks, include=None, exclude=None):
    """include 분류의 합집합(없으면 전체)에서 exclude 분류를 뺀 bool 배열."""
    H, W = arr.shape[:2]
    if masks is None or not include:
        m = np.ones((H, W), dtype=bool)
    else:
        m = np.zeros((H, W), dtype=bool)
        for k in include:
            m |= masks[k]
    if masks is not None:
        for k in exclude or ():
            m &= ~masks[k]
    return m


def _hsv(arr):
    """RGB 0~255 → (색상°, 채도 0~1, 명도 0~1)."""
    a = arr[..., :3].astype(np.float64) / 255.0
    mx, mn = a.max(axis=-1), a.min(axis=-1)
    d = mx - mn
    sat = np.where(mx > 0, d / np.maximum(mx, 1e-9), 0.0)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    dd = np.maximum(d, 1e-9)
    h = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60.0
    h = np.where(d > 0, h, 0.0)
    return h, sat, mx


def _lstar(arr):
    return srgb_to_lab(arr[..., :3])[..., 0]


def color_stats(arr, regions, masks, max_mean_sat=0.40, high_sat=0.85, max_high_ratio=0.05,
                grass_sat=(0.30, 0.55), grass_hue=(75.0, 110.0), baseline_mean_sat=None):
    """FR-05: 화면 평균 채도·고채도 비율(하늘 제외), 잔디 영역 평균 채도·색상, 기준선보다 낮아졌나."""
    h, s, _ = _hsv(arr)
    m = _sel(arr, masks, exclude=["sky"])
    mean = float(s[m].mean())
    high = float((s[m] >= high_sat).mean())
    ok = mean <= max_mean_sat and high <= max_high_ratio
    detail = f"평균 채도 {mean:.3f} (≤ {max_mean_sat}), 채도 ≥ {high_sat} {high * 100:.2f}% (≤ {max_high_ratio * 100:.1f}%)"
    if masks is not None and masks["grass"].any():
        g = masks["grass"]
        gs, gh = float(s[g].mean()), float(np.median(h[g]))
        ok &= grass_sat[0] <= gs <= grass_sat[1] and grass_hue[0] <= gh <= grass_hue[1]
        detail += f", 잔디 채도 {gs:.3f} ({grass_sat[0]}~{grass_sat[1]}) 색상 {gh:.0f}° ({grass_hue[0]:.0f}~{grass_hue[1]:.0f})"
    if baseline_mean_sat is not None:
        ok &= mean < baseline_mean_sat
        detail += f", 기준선 {baseline_mean_sat:.3f}보다 낮음"
    return {"ok": ok, "detail": detail, "meanSat": mean}


def exposure(arr, regions, masks, mean_range=(45.0, 62.0), width_range=(50.0, 85.0), max_grass_for_width=None):
    """FR-06: 화면 평균 L*와 L* 5~95 퍼센타일 폭. max_grass_for_width: 잔디(마스크)가 화면의 이 비율 이상이면 폭은 해당 없음 (061 Q11)."""
    L = _lstar(arr)
    mean = float(L.mean())
    p5, p95 = (float(v) for v in np.percentile(L, [5, 95]))
    w = p95 - p5
    grass = float(masks["grass"].mean()) if masks is not None else None
    width_na = max_grass_for_width is not None and grass is not None and grass >= max_grass_for_width
    ok = mean_range[0] <= mean <= mean_range[1] and (width_na or width_range[0] <= w <= width_range[1])
    wtxt = f"5~95% 폭 {w:.1f} — 잔디 {grass * 100:.0f}% ≥ {max_grass_for_width * 100:.0f}%라 해당 없음" if width_na else f"5~95% 폭 {w:.1f} ({width_range[0]}~{width_range[1]})"
    return {"ok": ok, "meanL": mean, "detail": f"평균 L* {mean:.1f} ({mean_range[0]}~{mean_range[1]}), {wtxt}"}


def exposure_match(arrs, max_diff=8.0):
    """FR-06: 같은 시간대 장면끼리 평균 L* 차."""
    means = [float(_lstar(a).mean()) for a in arrs]
    d = max(means) - min(means)
    return {"ok": d <= max_diff, "detail": f"평균 L* {', '.join(f'{m:.1f}' for m in means)} — 최대 차 {d:.1f} (≤ {max_diff})"}


def sky(arr, regions, masks, min_area=0.05, max_modal_ratio=0.60, min_std=4.0, min_top_bottom=8.0, day=True):
    """FR-11: 하늘 단색 비율·밝기 표준편차·(낮) 위아래 밝기 차. 하늘이 화면 5% 미만이면 해당 없음."""
    if masks is None:
        raise KeyError("sky 검사는 mask 캡처가 필요")
    m = masks["sky"]
    area = float(m.mean())
    if area < min_area:
        return {"ok": True, "detail": f"하늘 {area * 100:.1f}% < {min_area * 100:.0f}% — 해당 없음", "na": True}
    px = arr[..., :3][m].astype(np.int16)
    vals, counts = np.unique(px // 1, axis=0, return_counts=True)
    modal = vals[counts.argmax()]
    modal_ratio = float((np.abs(px - modal).max(axis=-1) <= 2).mean())
    lum = _lum(arr)
    std = float(lum[m].std())
    ok = modal_ratio <= max_modal_ratio and std >= min_std
    detail = f"하늘 {area * 100:.1f}%, 최빈색 ±2 {modal_ratio * 100:.1f}% (≤ {max_modal_ratio * 100:.0f}%), 밝기 표준편차 {std:.1f} (≥ {min_std})"
    if day:
        rows = np.where(m.any(axis=1))[0]
        n = max(1, int(len(rows) * 0.1))
        top = lum[rows[:n]][m[rows[:n]]].mean()
        bot = lum[rows[-n:]][m[rows[-n:]]].mean()
        tb = float(abs(bot - top))
        ok &= tb >= min_top_bottom
        detail += f", 위·아래 10% 밝기 차 {tb:.1f} (≥ {min_top_bottom})"
    return {"ok": ok, "detail": detail}


def _region_mask(arr, regions, masks, spec):
    """spec: 마스크 분류 이름 목록(문자열·목록) 또는 regions 키(사각형 목록)."""
    H, W = arr.shape[:2]
    if isinstance(spec, str) and spec in regions:
        m = np.zeros((H, W), dtype=bool)
        rects = regions[spec]
        rects = [rects] if rects and not isinstance(rects[0], (list, tuple)) else rects
        for r in rects:
            x, y, w, h = (int(round(v)) for v in r)
            m[max(0, y):min(H, y + h), max(0, x):min(W, x + w)] = True
        return m
    names = [spec] if isinstance(spec, str) else list(spec)
    if masks is None:
        raise KeyError(f"영역 {spec}: mask 캡처 또는 regions 필요")
    return _sel(arr, masks, include=names)


def region_ratio(arr, regions, masks, a="shade", b="sun", min_ratio=0.35, dark_below=10.0, max_dark_ratio=0.01):
    """FR-12·13: 영역 A 평균 밝기 / 영역 B 평균 밝기 ≥ 비율, A 안 아주 어두운 픽셀 비율."""
    lum = _lum(arr)
    ma, mb = _region_mask(arr, regions, masks, a), _region_mask(arr, regions, masks, b)
    if not ma.any() or not mb.any():
        raise ValueError(f"영역 비었음 A {int(ma.sum())}px, B {int(mb.sum())}px")
    ra, rb = float(lum[ma].mean()), float(lum[mb].mean())
    ratio = ra / max(rb, 1e-6)
    dark = float((lum[ma] < dark_below).mean())
    ok = ratio >= min_ratio and dark <= max_dark_ratio
    return {"ok": ok, "detail": f"A {ra:.1f} / B {rb:.1f} = {ratio:.2f} (≥ {min_ratio}), A 안 밝기 < {dark_below} {dark * 100:.2f}% (≤ {max_dark_ratio * 100:.1f}%)"}


def gray_ratio(arr, regions, masks, region="outside", max_ratio=0.10, max_sat=0.05):
    """FR-15: 경기장 밖 영역 중 단색 회색(채도 < 0.05이고 최빈색 ±2) 비율."""
    m = _region_mask(arr, regions, masks, region)
    if not m.any():
        return {"ok": True, "detail": "경기장 밖 영역 없음 — 해당 없음", "na": True}
    _, s, _ = _hsv(arr)
    px = arr[..., :3][m].astype(np.int16)
    vals, counts = np.unique(px, axis=0, return_counts=True)
    modal = vals[counts.argmax()]
    gray = (s[m] < max_sat) & (np.abs(px - modal).max(axis=-1) <= 2)
    r = float(gray.mean())
    return {"ok": r <= max_ratio, "detail": f"단색 회색 {r * 100:.1f}% (≤ {max_ratio * 100:.0f}%)"}


def clipping(arr, regions, masks, max_white=0.01, max_black=0.01, exclude_sky_black=True, white_kit_max=250.0):
    """FR-31: 255 클리핑·0~2 비율, 흰 키트 영역(regions.whiteKit) 평균."""
    lum = _lum(arr)
    m = _sel(arr, masks)
    white = float((arr[..., :3][m].max(axis=-1) >= 255).mean())
    mb = _sel(arr, masks, exclude=["sky"] if exclude_sky_black else None)
    black = float((lum[mb] <= 2).mean())
    ok = white <= max_white and black <= max_black
    detail = f"255 클리핑 {white * 100:.2f}% (≤ {max_white * 100:.0f}%), 0~2 {black * 100:.2f}% (≤ {max_black * 100:.0f}%)"
    if regions.get("whiteKit"):
        k = float(lum[_region_mask(arr, regions, masks, "whiteKit")].mean())
        ok &= k <= white_kit_max
        detail += f", 흰 키트 평균 {k:.1f} (≤ {white_kit_max})"
    return {"ok": ok, "detail": detail}


def _erode(m, n):
    out = m.copy()
    for _ in range(n):
        o = out.copy()
        o[1:, :] &= out[:-1, :]
        o[:-1, :] &= out[1:, :]
        o[:, 1:] &= out[:, :-1]
        o[:, :-1] &= out[:, 1:]
        out = o
    return out


def _dilate(m, n):
    return ~_erode(~m, n)


def rim_contrast(arr, regions, masks, band=2, baseline=None, min_gain=0.20, max_band_body=1.4):
    """FR-42: 선수 윤곽 안쪽 band px 띠와 바로 바깥 잔디의 밝기 대비(기준선 대비 +20%), 띠 / 몸 안쪽 ≤ 1.4배."""
    if masks is None:
        raise KeyError("rimContrast는 mask 캡처가 필요")
    p = masks["player"]
    if not p.any():
        raise ValueError("선수 마스크 비었음")
    lum = _lum(arr)
    inner = _erode(p, band)
    rim = p & ~inner
    ring = _dilate(p, band) & ~p & masks["grass"]
    if not rim.any() or not ring.any() or not inner.any():
        raise ValueError("윤곽 띠·바깥 잔디·몸 안쪽 중 비었음")
    rm, gm, bm = float(lum[rim].mean()), float(lum[ring].mean()), float(lum[inner].mean())
    contrast = abs(rm - gm) / max(gm, 1e-6)
    bb = rm / max(bm, 1e-6)
    ok = bb <= max_band_body
    detail = f"띠 {rm:.1f} / 바깥 잔디 {gm:.1f} 대비 {contrast:.3f}, 띠 / 몸 {bb:.2f} (≤ {max_band_body})"
    if baseline is not None:
        ok &= contrast >= baseline * (1 + min_gain)
        detail += f", 기준선 {baseline:.3f} × {1 + min_gain:.2f} 이상"
    return {"ok": ok, "detail": detail, "contrast": contrast}


def contact_darkening(arr, regions, masks, min_darkening=0.15):
    """FR-44: regions.feet = [[x, y, r_px(, ry_px)], …] — 발 중심 바닥 반경 0.3 m(화면 타원) 안 평균이 0.6~1.0 m 고리보다 15% 이상 어둡다."""
    lum = _lum(arr)
    H, W = lum.shape
    yy, xx = np.mgrid[0:H, 0:W]
    excl = masks["player"] if masks is not None else np.zeros((H, W), dtype=bool)
    rows, ok = [], True
    for foot in regions["feet"]:
        x, y, r = foot[0], foot[1], foot[2]
        ry = foot[3] if len(foot) > 3 else r  # 061: 바닥 원의 화면 타원 세로 반지름 (없으면 원)
        d = np.hypot(xx - x, (yy - y) * (r / max(ry, 1e-6)))  # 타원 거리를 가로 반지름 단위로
        inner = (d <= r) & ~excl
        ring = (d >= 2 * r) & (d <= 3.33 * r) & ~excl
        if not inner.any() or not ring.any():
            raise ValueError(f"발 ({x:.0f},{y:.0f}) 주변 지면 픽셀 없음")
        iv, rv = float(lum[inner].mean()), float(lum[ring].mean())
        dk = (rv - iv) / max(rv, 1e-6)
        ok &= dk >= min_darkening
        rows.append(f"{dk:.2f}")
    return {"ok": ok and bool(rows), "detail": f"접촉 어둡기 {', '.join(rows)} (≥ {min_darkening})"}


def _down(arr, f):
    H, W = arr.shape[:2]
    h, w = H // f * f, W // f * f
    return arr[:h, :w, :3].astype(np.float64).reshape(h // f, f, w // f, f, 3).mean(axis=(1, 3))


def mode_gap(arr_a, reg_a, arr_b, reg_b, max_lum=3.0, max_std_ratio=0.10, max_sat=0.03, max_de=3.0, down=8):
    """FR-84: 일반·고품질 모드 같은 프레임 — 평균 밝기 차, 대비(표준편차) 차, 평균 채도 차, 평균 색 ΔE.
    피사계 심도 흐림 영향을 줄이려 둘 다 down배 줄여 비교한다."""
    a, b = _down(arr_a, down), _down(arr_b, down)
    la, lb = _lum(a), _lum(b)
    dl = abs(float(la.mean() - lb.mean()))
    ds = abs(float(la.std() - lb.std())) / max(float(la.std()), 1e-6)
    _, sa, _ = _hsv(a.clip(0, 255).astype(np.uint8))
    _, sb, _ = _hsv(b.clip(0, 255).astype(np.uint8))
    dsat = abs(float(sa.mean() - sb.mean()))
    de = delta_e2000(srgb_to_lab(a.reshape(-1, 3).mean(axis=0)), srgb_to_lab(b.reshape(-1, 3).mean(axis=0)))
    ok = dl <= max_lum and ds <= max_std_ratio and dsat <= max_sat and de <= max_de
    return {"ok": ok, "detail": f"밝기 차 {dl:.2f} (≤ {max_lum}), 대비 차 {ds * 100:.1f}% (≤ {max_std_ratio * 100:.0f}%), "
                                f"채도 차 {dsat:.3f} (≤ {max_sat}), 평균 색 ΔE {de:.2f} (≤ {max_de})"}


def frame_delta_e(arrs, max_de=1.5):
    """FR-84: 연속 프레임 평균 색 변화 ΔE의 최대값."""
    labs = [srgb_to_lab(a[..., :3].reshape(-1, 3).astype(np.float64).mean(axis=0)) for a in arrs]
    des = [delta_e2000(labs[i], labs[i + 1]) for i in range(len(labs) - 1)]
    m = max(des) if des else 0.0
    return {"ok": m <= max_de, "detail": f"프레임 {len(arrs)}장 연속 평균 색 ΔE 최대 {m:.2f} (≤ {max_de})"}


def _edges(ref, thr=40.0):
    lum = _lum(ref)
    gx = np.zeros_like(lum)
    gy = np.zeros_like(lum)
    gx[:, 1:-1] = lum[:, 2:] - lum[:, :-2]
    gy[1:-1, :] = lum[2:, :] - lum[:-2, :]
    return np.hypot(gx, gy) >= thr


def aa_edge(arr, reg, ref, reg_ref, max_abs=6.0, baseline=None, max_baseline_ratio=0.5, edge_threshold=40.0, absolute_advisory=False):
    """FR-60: 4배 슈퍼샘플을 줄인 기준(other)과 고대비 경계 영역 평균 절대 차이 — ≤ 6/255, 기준선의 50% 이하."""
    if ref.shape != arr.shape:
        f = ref.shape[0] // arr.shape[0]
        if f < 1 or ref.shape[0] != arr.shape[0] * f:
            raise ValueError(f"슈퍼샘플 크기가 배수가 아님 {ref.shape} vs {arr.shape}")
        ref = _down(ref, f)
    e = _dilate(_edges(ref, edge_threshold), 1)
    if not e.any():
        raise ValueError("경계 픽셀 없음")
    d = np.abs(arr[..., :3].astype(np.float64) - ref[..., :3].astype(np.float64)).max(axis=-1)
    m = float(d[e].mean())
    # absolute_advisory: 절대값은 기록만, 기준선 대비만 판정 (061 Q12)
    ok = True if (absolute_advisory and baseline is not None) else m <= max_abs
    detail = f"경계 평균 차 {m:.2f} ({'기록: ' if absolute_advisory and baseline is not None else ''}≤ {max_abs})"
    if baseline is not None:
        ok &= m <= baseline * max_baseline_ratio
        detail += f", 기준선 {baseline:.2f} × {max_baseline_ratio} 이하"
    return {"ok": ok, "detail": detail, "edgeDiff": m}


def pan_flicker(arrs, refs, max_ratio=1.5, edge_threshold=40.0):
    """FR-60·58 류: 느린 팬 연속 프레임의 경계 깜빡임(프레임 간 경계 영역 평균 차)이 기준 시퀀스의 max_ratio배 이하."""
    def flick(seq):
        vals = []
        for i in range(len(seq) - 1):
            a, b = seq[i], seq[i + 1]
            e = _dilate(_edges(a, edge_threshold), 1)
            if e.any():
                vals.append(float(np.abs(_lum(a) - _lum(b))[e].mean()))
        return float(np.mean(vals)) if vals else 0.0
    f, r = flick(arrs), flick(refs)
    ok = f <= r * max_ratio if r > 0 else f == 0
    return {"ok": ok, "detail": f"깜빡임 {f:.2f} / 기준 {r:.2f} = {f / max(r, 1e-6):.2f}배 (≤ {max_ratio})"}


def ground_line_std(arr, regions, masks, region="groundBand", min_std=2.0, smooth=31):
    """AC-08 ①: 피치 레벨 캡처에서 지면(잔디) 위 경계선의 높이 표준편차 — 이동 평균을 뺀 잔여(px)."""
    m = _region_mask(arr, regions, masks, region)
    ys, xs = np.where(m)
    if not len(xs):
        raise ValueError("지면 경계 영역 없음")
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    h, s, v = _hsv(arr[y0:y1, x0:x1])
    green = (h >= 60) & (h <= 140) & (s >= 0.15)
    tops = []
    for c in range(green.shape[1]):
        col = np.where(green[:, c])[0]
        tops.append(col.min() if len(col) else np.nan)
    t = np.array(tops, dtype=np.float64)
    t = t[~np.isnan(t)]
    if len(t) < smooth * 2:
        raise ValueError(f"경계 열 {len(t)}개 부족")
    k = np.ones(smooth) / smooth
    trend = np.convolve(t, k, mode="same")
    res = (t - trend)[smooth:-smooth]
    std = float(res.std())
    return {"ok": std >= min_std, "detail": f"지면 경계 높이 잔여 표준편차 {std:.2f}px (≥ {min_std})", "std": std}


def region_std(arr, regions, masks, region="grass", baseline=None, min_ratio=1.0, min_std=0.0):
    """영역 밝기 표준편차 (기준선 대비 배수)."""
    m = _region_mask(arr, regions, masks, region)
    if not m.any():
        raise ValueError(f"영역 {region} 비었음")
    std = float(_lum(arr)[m].std())
    ok = std >= min_std
    detail = f"{region} 밝기 표준편차 {std:.2f}"
    if baseline is not None:
        ok &= std >= baseline * min_ratio
        detail += f" (기준선 {baseline:.2f} × {min_ratio} 이상)"
    return {"ok": ok, "detail": detail, "std": std}


_SINGLE_061 = {"colorStats": color_stats, "exposure": exposure, "sky": sky, "regionRatio": region_ratio,
               "grayRatio": gray_ratio, "clipping": clipping, "rimContrast": rim_contrast,
               "contactDarkening": contact_darkening, "groundLineStd": ground_line_std, "regionStd": region_std}


def run_061(t, c, params, captures):
    """061 검사 하나. params에서 mask(캡처 이름)·others(캡처 이름 목록)를 꺼내 해석한다."""
    def get(name):
        if name not in captures:
            raise KeyError(f"캡처 없음: {name}")
        return captures[name]
    mask_name = params.pop("mask", None)
    masks = masks_from(get(mask_name)[0]) if mask_name else None
    for k in ("mean_range", "width_range", "grass_sat", "grass_hue"):
        if k in params:
            params[k] = tuple(params[k])
    arr, reg = get(c["capture"])
    if t in _SINGLE_061:
        return _SINGLE_061[t](arr, reg, masks, **params)
    if t == "modeGap":
        arr2, reg2 = get(c["other"])
        return mode_gap(arr, reg, arr2, reg2, **params)
    if t == "aaEdge":
        arr2, reg2 = get(c["other"])
        return aa_edge(arr, reg, arr2, reg2, **params)
    if t == "sharpness":
        arr2, reg2 = get(c["other"])
        return sharpness(arr, reg, arr2, reg2, **params)
    if t == "grassShade":
        return grass_shade(arr, reg, masks, **params)
    others = [get(n)[0] for n in c.get("others") or []]
    if t == "exposureMatch":
        return exposure_match([arr] + others, **params)
    if t == "frameDeltaE":
        return frame_delta_e([arr] + others, **params)
    if t == "panFlicker":
        refs = [get(n)[0] for n in params.pop("refs")]
        return pan_flicker([arr] + others, refs, **params)
    raise KeyError(f"알 수 없는 검사 {t}")


def sharpness(arr_a, reg_a, arr_b, reg_b, region="scorer", min_ratio=0.95):
    """FR-83: 대상 영역 선명도(라플라시안 분산) — 고품질 모드(capture) / 일반 모드(other) ≥ 0.95."""
    def lap_var(arr, reg):
        if region not in reg:
            raise KeyError(f"영역 {region} 없음")
        a = _lum(_crop(arr, reg[region]))
        l = a[1:-1, 1:-1] * 4 - a[:-2, 1:-1] - a[2:, 1:-1] - a[1:-1, :-2] - a[1:-1, 2:]
        return float(l.var())
    va, vb = lap_var(arr_a, reg_a), lap_var(arr_b, reg_b)
    r = va / max(vb, 1e-6)
    return {"ok": r >= min_ratio, "detail": f"대상 선명도 {va:.1f} / 일반 모드 {vb:.1f} = {r:.3f} (≥ {min_ratio})"}


def grass_shade(arr, regions, masks, min_ratio=0.35, dark_below=10.0, max_dark_ratio=0.01, min_separation=12.0):
    """FR-12: 잔디 영역 밝기를 두 무리(오츠)로 나눠 그림자 안 평균 / 햇빛 평균 ≥ 0.35, 그림자 안 아주 어두운 픽셀 ≤ 1%.
    두 무리 차가 작으면(그림자 없음) 해당 없음."""
    if masks is None:
        raise KeyError("grassShade는 mask 캡처가 필요")
    g = _lum(arr)[masks["grass"]]
    if g.size < 1000:
        return {"ok": True, "detail": "잔디 영역 작음 — 해당 없음", "na": True}
    hist, edges = np.histogram(g, bins=256, range=(0, 256))
    p = hist / hist.sum()
    omega = np.cumsum(p)
    mu = np.cumsum(p * np.arange(256))
    with np.errstate(divide="ignore", invalid="ignore"):
        sb = (mu[-1] * omega - mu) ** 2 / (omega * (1 - omega))
    if not np.isfinite(sb).any():
        return {"ok": True, "detail": "잔디 밝기가 한 값 — 그림자 구역 없음, 해당 없음", "na": True}
    t = int(np.nanargmax(sb))
    cut = edges[t + 1]  # 오츠 경계(구간 위쪽) — 실수 밝기를 구간 경계로 나눈다
    dark, bright = g[g < cut], g[g >= cut]
    if dark.size == 0 or bright.size == 0 or bright.mean() - dark.mean() < min_separation:
        return {"ok": True, "detail": "잔디에 그림자 구역 없음 — 해당 없음", "na": True}
    ratio = float(dark.mean() / bright.mean())
    tiny = float((dark < dark_below).mean())
    return {"ok": ratio >= min_ratio and tiny <= max_dark_ratio,
            "detail": f"그림자 안 {dark.mean():.1f} / 햇빛 {bright.mean():.1f} = {ratio:.2f} (≥ {min_ratio}), 아주 어두움 {tiny * 100:.2f}% (≤ {max_dark_ratio * 100:.0f}%)"}
