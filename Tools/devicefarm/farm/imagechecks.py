"""캡처 이미지 검사 (specs/044 AC-10~13, specs/043 AC-13). 시나리오 `imageChecks`가 캡처 이름과 검사 종류를 지정한다.

영역 좌표는 드라이버가 캡처 때 남긴 `regions`(PNG 픽셀, 왼쪽 위 원점):
- 사각형 [x, y, w, h], 선분 {"a": [x, y], "b": [x, y]}, 점 [x, y], 점 목록 [[x, y], ...].
"""
import re

import numpy as np

TYPES = ("notBlank", "bandFlip", "lineContinuity", "blobX", "deltaE", "glyphInside", "contactDistance", "sameImage",
         "differentImage")
PAIRED = ("bandFlip", "sameImage", "differentImage")  # 캡처 두 장 (capture + other)


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
                  for k, v in c.items() if k not in ("type", "capture", "other", "ac")}
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
            else:
                raise KeyError(f"알 수 없는 검사 {t}")
            row.update(ok=bool(r["ok"]), detail=r["detail"])
        except (KeyError, ValueError, IndexError, TypeError) as e:
            row["detail"] = str(e).strip("'")
        out.append(row)
    return out
