"""specs/059 골망 지표 (FR-10~15·20~23, AC-07·08). imagechecks.run이 TYPES_059 종류를 여기로 보낸다.

캡처 영역(GoalNetScreen): net = 그물 상자 화면 사각형 [x, y, w, h], cellPx = 뒤 그물 칸 하나의 화면 크기(px),
출렁임 화면은 case·contacts·time·maxDisp(로직 최대 변위, m)·stateHash.
같은 시점의 그물 끔(off)·균일 판(plate, 평균 덮임만) 캡처와 비교해 배경을 지운다.
"""
import numpy as np

from . import imagechecks as ic

TYPES_059 = ("netVisible", "netHoles", "netSaturation", "netMoire", "netStatic", "netLodStep", "netRipple")


def _net(arr, reg, region="net"):
    if region not in reg:
        raise KeyError(f"영역 {region} 없음")
    return ic._lum(ic._crop(arr, reg[region]))


def _pair(a, b):
    h, w = min(a.shape[0], b.shape[0]), min(a.shape[1], b.shape[1])
    return a[:h, :w], b[:h, :w]


def _highpass(a, size):
    """칸 크기 상자 흐림을 뺀 잔차 — 배경의 큰 밝기 변화(관중석·잔디 줄무늬)를 지우고 망 무늬만 남긴다."""
    k = max(3, int(round(size)) | 1)
    pad = k // 2
    p = np.pad(a, pad, mode="edge")
    c = np.cumsum(np.cumsum(p, axis=0), axis=1)
    c = np.pad(c, ((1, 0), (1, 0)))
    box = (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)
    return a - box[:a.shape[0], :a.shape[1]]


def net_visible(on, off, plate, reg, min_mean=6.0, min_std_ratio=1.5, region="netBack", min_cell_px=4.0):
    """FR-10: 골문 뒤 그물 영역에서 그물 있음·없음 평균 밝기 차 ≥ min_mean/255, 칸 크기 고역 잔차 표준편차 — 망(on − off) / 판(plate − off) ≥ min_std_ratio배."""
    a, b = _pair(_net(on, reg, region), _net(off, reg, region))
    p, _ = _pair(_net(plate, reg, region), b)
    a, p = _pair(a, p)
    b = b[:a.shape[0], :a.shape[1]]
    mean = float(np.abs(a - b).mean())
    cell = max(3.0, float(reg.get("cellPx", 3)))
    s_on, s_plate = float(_highpass(a - b, cell).std()), float(_highpass(p - b, cell).std())
    ratio = s_on / max(s_plate, 1e-6)
    pattern = float(reg.get("cellPx", 0)) >= min_cell_px   # FR-10 개정(2026-11-26): 무늬 대비는 칸 4 px 이상 시점만
    ok = mean >= min_mean and (not pattern or ratio >= min_std_ratio)
    return {"ok": bool(ok), "mean": mean, "stdRatio": ratio,
            "detail": f"그물 있음−없음 {mean:.2f}/255 (≥ {min_mean}), 망 고역 표준편차 / 판 {ratio:.2f}배 ({'≥ ' + str(min_std_ratio) if pattern else '기록만 — 칸 4 px 미만'}), 칸 {reg.get('cellPx')} px"}


def net_holes(matte, reg, min_hole=0.40, line_thr=8.0, min_cell_px=4.0, max_fragment=0.05, region="netBack"):
    """FR-11: 그물만 검은 배경에 그린 캡처(matte)의 뒤 그물 한 겹 영역 — 구멍(밝기 < line_thr) ≥ min_hole,
    줄 픽셀 중 작은 조각(칸 둘레의 1/2 미만 연결 성분)에 속한 비율 ≤ max_fragment(끊긴 줄 없음). 칸이 min_cell_px 미만이면 구멍만 본다."""
    line = _net(matte, reg, region) >= line_thr
    hole = 1.0 - float(line.mean())
    cell = float(reg.get("cellPx", 0))
    frag = 0.0
    if cell >= min_cell_px and line.any():
        sizes = _component_sizes(line)
        small = max(4, int(cell * 3.0 * 0.5))   # 칸 둘레 ≈ 3.46·칸 — 그 절반보다 작은 조각 = 끊긴 줄
        frag = float(sum(n for n in sizes if n < small)) / float(line.sum())
    ok = hole >= min_hole and frag <= max_fragment
    return {"ok": bool(ok), "hole": hole, "fragment": frag,
            "detail": f"구멍 {hole * 100:.1f}% (≥ {min_hole * 100:.0f}%), 끊긴 조각 줄 {frag * 100:.2f}% (≤ {max_fragment * 100:.0f}%, 칸 {cell:.1f} px, {region})"}


def _component_sizes(m):
    """4-연결 성분 크기 목록 (반복 라벨링, 의존성 없음)."""
    h, w = m.shape
    seen = np.zeros((h, w), bool)
    sizes = []
    ys, xs = np.nonzero(m)
    for y0, x0 in zip(ys, xs):
        if seen[y0, x0]:
            continue
        stack = [(y0, x0)]
        seen[y0, x0] = True
        n = 0
        while stack:
            y, x = stack.pop()
            n += 1
            for yy, xx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= yy < h and 0 <= xx < w and m[yy, xx] and not seen[yy, xx]:
                    seen[yy, xx] = True
                    stack.append((yy, xx))
        sizes.append(n)
    return sizes


def net_saturation(on, reg, max_ratio=0.05, level=250.0):
    """FR-15: 그물 영역 포화(밝기 ≥ level) 픽셀 비율 ≤ max_ratio."""
    a = _net(on, reg)
    r = float((a >= level).mean())
    return {"ok": r <= max_ratio, "ratio": r, "detail": f"포화 {r * 100:.2f}% (≤ {max_ratio * 100:.0f}%)"}


def net_moire(on, plate, reg, max_ratio=0.3, long_period=3.0, region="netBack"):
    """FR-13: (on − plate) 잔차의 2차원 스펙트럼에서 칸 주기보다 long_period배 넘게 긴 주기 대역 / 칸 주파수 대역 ≤ max_ratio.
    plate는 같은 배경·같은 평균 덮임이라 잔차에는 망 무늬만 남는다. 칸이 2 px 미만이면 셰이더가 평균 덮임이라 무늬 없음(통과)."""
    a, p = _pair(_net(on, reg, region), _net(plate, reg, region))
    cell = float(reg.get("cellPx", 0))
    d = a - p
    if cell < 2.0 or d.size < 64:
        return {"ok": True, "ratio": 0.0, "detail": f"칸 {cell:.2f} px — 평균 덮임 구간, 무늬 없음"}
    d = d - d.mean()
    win = np.outer(np.hanning(d.shape[0]), np.hanning(d.shape[1]))
    f = np.abs(np.fft.fftshift(np.fft.fft2(d * win))) ** 2
    h, w = f.shape
    yy, xx = np.mgrid[0:h, 0:w]
    # 주기(px) = 크기 / 반경(주파수 칸) — 축마다 크기가 달라 정규화 반경을 쓴다
    fy, fx = (yy - h // 2) / h, (xx - w // 2) / w
    rad = np.sqrt(fx * fx + fy * fy)            # cycles/px
    fc = 1.0 / cell                             # 칸 주파수 (육각 격자 대략)
    low = (rad > 1.5 / max(h, w)) & (rad < fc / long_period)
    band = (rad > fc * 0.6) & (rad < fc * 1.6)
    lo, ce = float(f[low].sum()), float(f[band].sum())
    r = lo / max(ce, 1e-9)
    return {"ok": r <= max_ratio, "ratio": r, "detail": f"긴 주기 / 칸 주파수 에너지 {r:.3f} (≤ {max_ratio}, 칸 {cell:.1f} px, {region})"}


def net_static(a_arr, b_arr, reg, max_mean=1.0):
    """FR-12 ①: 그물·카메라가 멈춘 연속 두 프레임 그물 영역 밝기 변화 평균 ≤ max_mean/255."""
    a, b = _pair(_net(a_arr, reg), _net(b_arr, reg))
    m = float(np.abs(a - b).mean())
    return {"ok": m <= max_mean, "mean": m, "detail": f"정지 프레임 변화 {m:.3f}/255 (≤ {max_mean})"}


def net_lod_step(a_arr, reg_a, b_arr, reg_b, max_mean=3.0):
    """FR-14: 거리 단계(평균 덮임 페이드) 앞뒤 FOV에서 그물 영역 평균 밝기 차 ≤ max_mean/255."""
    ma, mb = float(_net(a_arr, reg_a).mean()), float(_net(b_arr, reg_b).mean())
    d = abs(ma - mb)
    return {"ok": d <= max_mean, "delta": d,
            "detail": f"전환 앞뒤 평균 {ma:.2f} / {mb:.2f} 차 {d:.2f}/255 (≤ {max_mean}, 칸 {reg_a.get('cellPx')}→{reg_b.get('cellPx')} px)"}


def net_ripple(frames, min_peak=0.05, max_settled=0.01, min_image_delta=1.0):
    """FR-20~23 화면: 출렁임 캡처들(첫 접촉 + 0.1·0.3·0.6·1.5·3.0 s) — 접촉이 있고, 0.1~0.6 s 중 최대 변위 ≥ min_peak m,
    3.0 s는 < max_settled m(정지 모양), 부푼 순간과 3.0 s 그물 영역 화면 차 ≥ min_image_delta/255(보이는 출렁임)."""
    (a0, r0) = frames[0]
    if int(r0.get("contacts", 0)) < 1:
        return {"ok": False, "detail": f"{r0.get('case')}: 그물 접촉 없음"}
    disps = [float(r.get("maxDisp", 0)) for _, r in frames]
    peak = max(disps[:3])
    settled = disps[-1]
    k = int(np.argmax(disps[:3]))
    a, b = _pair(_net(frames[k][0], frames[k][1]), _net(frames[-1][0], frames[-1][1]))
    img = float(np.abs(a - b).mean())
    ok = peak >= min_peak and settled < max_settled and img >= min_image_delta
    return {"ok": bool(ok), "peak": peak, "settled": settled, "imageDelta": img,
            "detail": f"{r0.get('case')}: 접촉 {r0.get('contacts')}, 최대 변위 {['%.3f' % d for d in disps]} m (부풂 ≥ {min_peak}, 3 s < {max_settled}), "
                      f"부풂−정지 화면 차 {img:.2f}/255 (≥ {min_image_delta})"}


def run_059(t, c, params, captures):
    def get(name):
        if name not in captures:
            raise KeyError(f"캡처 없음: {name}")
        return captures[name]
    arr, reg = get(c["capture"])
    if t == "netVisible":
        return net_visible(arr, get(c["other"])[0], get(params.pop("plate"))[0], reg, **params)
    if t == "netHoles":
        return net_holes(arr, reg, **params)
    if t == "netSaturation":
        return net_saturation(arr, reg, **params)
    if t == "netMoire":
        return net_moire(arr, get(c["other"])[0], reg, **params)
    if t == "netStatic":
        return net_static(arr, get(c["other"])[0], reg, **params)
    if t == "netLodStep":
        b, rb = get(c["other"])
        return net_lod_step(arr, reg, b, rb, **params)
    if t == "netRipple":
        frames = [(arr, reg)] + [get(n) for n in c.get("others") or []]
        return net_ripple(frames, **params)
    raise KeyError(f"알 수 없는 검사 {t}")
