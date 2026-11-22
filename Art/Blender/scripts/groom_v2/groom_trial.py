"""남자 헤어 가닥 생성 시험 (groom2.py가 도우미로 불러 씀). MPFB 사람(MakeHuman CC0 피부·눈·눈썹) 두피에 가닥을 심고
스타일별 길이·빗질·중력·곱슬·볼륨을 준 뒤 머리 표면 밖으로 밀어내는 충돌을 풀어 헤어 커브로 렌더한다(정면·3/4).
인자: -- <출력 폴더> [스타일,...]"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:]
OUT = ARGS[0]
os.makedirs(OUT, exist_ok=True)
from bl_ext.blender_org.mpfb.services.humanservice import HumanService  # noqa: E402

# 스타일: (피부, 머리색, 가닥 수)
STYLES = {
    "buzz": ("young_african_male", (0.02, 0.015, 0.012, 1), 90000),
    "fade_crop": ("young_caucasian_male", (0.10, 0.06, 0.035, 1), 90000),
    "curly_short": ("young_african_male", (0.02, 0.015, 0.012, 1), 80000),
    "big_afro": ("young_african_male", (0.03, 0.02, 0.015, 1), 90000),
    "two_block": ("young_asian_male", (0.015, 0.012, 0.010, 1), 90000),
    "part_55": ("young_asian_male", (0.02, 0.015, 0.012, 1), 90000),
    "pomade": ("young_caucasian_male", (0.05, 0.03, 0.02, 1), 90000),
    "seethrough": ("young_asian_male", (0.02, 0.015, 0.012, 1), 80000),
    "gail": ("young_asian_male", (0.02, 0.015, 0.012, 1), 85000),
    "messy_slick": ("young_caucasian_male", (0.07, 0.045, 0.03, 1), 85000),
    "long_male": ("young_caucasian_male", (0.09, 0.055, 0.03, 1), 70000),
    "man_bun": ("young_caucasian_male", (0.06, 0.035, 0.02, 1), 70000),
}
RACE = {"young_caucasian_male": {"caucasian": 0.9, "african": 0.0, "asian": 0.1},
        "young_african_male": {"caucasian": 0.05, "african": 0.95, "asian": 0.0},
        "young_asian_male": {"caucasian": 0.1, "african": 0.0, "asian": 0.9}}


def human(skin):
    info = HumanService._create_default_human_info_dict()
    info["phenotype"].update({"gender": 1.0, "age": 0.5, "muscle": 0.85, "weight": 0.55, "proportions": 0.7, "height": 0.6})
    info["phenotype"]["race"] = RACE[skin]
    info.update({"eyes": "high-poly/high-poly.mhclo", "eyes_material_type": "MAKESKIN",
                 "eyebrows": "mindfront_eyebrows_04/mindfront_eyebrows_04.mhclo",
                 "eyelashes": "mindfront_eyelashes_02/mindfront_eyelashes_02.mhclo",
                 "skin_mhmat": f"skins/{skin}/{skin}.mhmat", "skin_material_type": "ENHANCED_SSS", "alternative_materials": {}})
    st = HumanService.get_default_deserialization_settings()
    st["subdiv_levels"] = 0
    return HumanService.deserialize_from_dict(info, st)


def body_geo(bm):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = bm.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    mw = bm.matrix_world
    V = np.array([tuple(mw @ v.co) for v in me.vertices])
    body = bm.vertex_groups["body"].index
    inb = np.array([any(g.group == body and g.weight > 0.5 for g in v.groups) for v in me.vertices])
    T = np.array([t.vertices[:] for t in me.loop_triangles])
    T = T[inb[T].all(axis=1)]
    ev.to_mesh_clear()
    return V, T


def eye_center(bm):
    E = np.array([tuple(o.matrix_world @ v.co) for o in bm.children if "high-poly" in o.name for v in o.data.vertices])
    return E.mean(axis=0)


def hairline(rel, n, style):
    """rel = 점 − 눈 중심 (MPFB: 앞 = −Y, 위 = +Z). 스타일별 이마선·옆선·뒤 목선."""
    x, y, z = rel[:, 0], rel[:, 1], rel[:, 2]
    fwd = -(y - 0.085)                                 # 각도 기준 = 머리 중심(눈에서 8.5 cm 뒤) — 눈 기준이면 이마가 '뒤'로 판정됨
    t = np.arctan2(np.abs(x), -fwd) / math.pi        # 0 뒤 · 0.5 옆 · 1 앞
    front, side, nape = 0.066, 0.0, -0.075
    if style == "full":
        side = -0.012
    if style in ("fade_crop", "two_block", "pomade", "gail"):
        side = 0.035                                   # 옆·뒤 아래는 짧게 친 부분 → 가닥 없음(두피 텍스처 몫)
        nape = 0.005
    # 앞(t ≥ 0.78)은 높이 일정한 평평한 이마선 — 이마가 앞으로 둥글어 각도 보간만 쓰면 가운데가 V로 내려간다
    tf = np.clip((t - 0.5) / 0.28, 0, 1)
    th = np.where(t < 0.5, nape + (side - nape) * (t / 0.5) ** 1.5, side + (front - side) * (tf * tf * (3 - 2 * tf)))
    # 남자 이마선: 거의 수평, 관자놀이 모서리만 둥글게 살짝 올라감 (V자·흔들기 없음)
    ax = np.abs(x)
    th = th + np.where(fwd > 0.10, 0.007 * np.clip((ax - 0.025) / 0.03, 0, 1) ** 2, 0.0)
    ear = (np.abs(x) > 0.05) & (z < 0.05) & (fwd > 0.02) & (fwd < 0.135)
    if style == "full":   # v2 그루밍: 관자놀이까지 심고 귀 바로 위만 뺀다
        ear = (np.abs(x) > 0.05) & (z < 0.012) & (fwd > 0.05) & (fwd < 0.125)
    if RETURN_MARGIN[0]:
        return z - th, ear
    return (z > th) & ~ear & (n[:, 2] > -0.7)


def roots(V, T, eye, n, style, rng):
    C = V[T].mean(axis=1)
    keep = (C[:, 2] > eye[2] - 0.12) & (np.linalg.norm(C[:, :2] - eye[:2], axis=1) < 0.16)
    T = T[keep]
    a, b, c = V[T[:, 0]], V[T[:, 1]], V[T[:, 2]]
    cr = np.cross(b - a, c - a)
    area = np.linalg.norm(cr, axis=1) * 0.5
    N = cr / (np.linalg.norm(cr, axis=1, keepdims=True) + 1e-12)
    idx = rng.choice(len(T), size=n * 4, p=area / area.sum())
    u, v = rng.random(n * 4), rng.random(n * 4)
    f = u + v > 1
    u[f], v[f] = 1 - u[f], 1 - v[f]
    P = a[idx] + (b[idx] - a[idx]) * u[:, None] + (c[idx] - a[idx]) * v[:, None]
    Nn = N[idx]
    ok = hairline(P - eye, Nn, style)
    return P[ok][:n], Nn[ok][:n]


RETURN_MARGIN = [False]


def margin_of(rel, style):
    RETURN_MARGIN[0] = True
    try:
        return hairline(rel, np.tile([0.0, 0.0, 1.0], (len(rel), 1)), style)
    finally:
        RETURN_MARGIN[0] = False


VOLUME = {"messy_slick": 0.0016, "seethrough": 0.0010, "gail": 0.0014, "fade_crop": 0.0010, "two_block": 0.0007, "part_55": 0.0006, "pomade": 0.0016, "man_bun": 0.0004, "long_male": 0.0004}
HUG = {"messy_slick": 0.7, "seethrough": 0.85, "gail": 0.9, "buzz": 0.0, "fade_crop": 0.8, "curly_short": 0.2, "big_afro": 0.0, "two_block": 0.9, "part_55": 0.9,
       "pomade": 0.95, "long_male": 0.9, "man_bun": 0.95}
LIFT_MIN = {"messy_slick": 0.3, "seethrough": 0.12, "gail": 0.15, "fade_crop": 0.18, "curly_short": 0.3, "two_block": 0.08, "part_55": 0.08, "pomade": 0.15, "long_male": 0.05, "man_bun": 0.05}


def unit(a):
    return a / (np.linalg.norm(a, axis=-1, keepdims=True) + 1e-9)


def grow(P, N, eye, style, tree, rng, steps=14):
    k = len(P)
    rel = P - eye
    sx = np.sign(rel[:, 0])
    top = np.clip((rel[:, 2] - 0.03) / 0.09, 0, 1)
    fwd = np.clip(-rel[:, 1] / 0.09, -1, 1)          # + 앞
    up = np.array([0.0, 0.0, 1.0])
    back = np.array([0.0, 1.0, 0.0])
    fr = np.array([0.0, -1.0, 0.0])
    L = np.full(k, 0.03)
    comb = np.tile(back - 0.3 * up, (k, 1))
    g, lift, curl, curlr = 0.03, 0.4, 0.0, 0.0
    if style == "buzz":
        L[:], lift, g = 0.0018, 0.3, 0.0
    elif style == "fade_crop":
        L = 0.010 + 0.035 * top ** 1.3
        comb = np.tile(fr * 0.8 + up * 0.2, (k, 1))  # 앞으로 내린 크롭
        g, lift = 0.02, 0.3
    elif style == "curly_short":
        L = 0.02 + 0.03 * top
        lift, curl, curlr, g = 0.75, 1.7, 0.0045, 0.0
    elif style == "big_afro":
        L = 0.075 + 0.02 * top
        lift, curl, curlr, g = 1.0, 2.2, 0.006, 0.0
    elif style == "two_block":
        L = 0.055 + 0.02 * top
        comb = np.tile(back * 0.3 + fr * 0.2 + up * 0.5, (k, 1))   # 윗머리는 볼륨 있게 위로
        bang = fwd > 0.55
        comb[bang] = fr * 0.8 - up * 0.2                           # 시스루 앞머리: 이마로 내림
        L[bang] = 0.065
        comb[fwd < -0.2] = back - up * 0.6
        g, lift = 0.06, 0.3
    elif style == "part_55":
        # 가운데 가르마 커튼: 앞머리가 양옆 이마로 흘러내리고, 옆·뒤는 귀를 덮는 중간 길이, 웨이브 볼륨
        L = 0.08 + 0.02 * top
        comb = np.stack([sx * 0.7, -0.35 * np.ones(k), -0.45 * np.ones(k)], 1)
        comb[fwd < 0.0] = np.stack([sx[fwd < 0.0] * 0.4, 0.6 * np.ones((fwd < 0.0).sum()), -0.7 * np.ones((fwd < 0.0).sum())], 1)
        g, lift, curl, curlr = 0.12, 0.35, 0.8, 0.005
    elif style == "pomade":
        L = 0.05 + 0.04 * top
        part = 0.03                                    # 왼쪽 가르마 (x = +0.03)
        comb = np.where((rel[:, 0] > part)[:, None], np.array([0.75, 0.55, -0.1]), np.array([-0.45, 0.7, 0.45]))
        front = fwd > 0.35
        comb[front] = np.array([-0.15, 0.35, 0.95])   # 앞머리: 이마선에서 바로 위로 세워 넘김(퀴프) — 이마 경계 띠 가림
        L[front] = 0.095
        g, lift = 0.0, 0.45
    elif style == "seethrough":
        # 윗머리 볼륨 + 가닥가닥 갈라진 앞머리(이마 비침) + 귀 위를 살짝 덮는 옆머리, 끝 흐트러짐
        L = 0.055 + 0.03 * top
        comb = np.tile(fr * 0.55 + up * 0.25 + back * 0.1, (k, 1))
        bang = fwd > 0.40
        fan = np.clip(rel[:, 0] / 0.05, -1, 1)
        comb[bang] = np.stack([fan[bang] * 0.35, -0.8 * np.ones(bang.sum()), -0.5 * np.ones(bang.sum())], 1)  # 이마 전체로 부채꼴
        L[bang] = 0.08 + 0.01 * rng.random(bang.sum())
        sidev = np.abs(rel[:, 0]) > 0.05
        comb[sidev] = np.stack([sx[sidev] * 0.6, np.zeros(sidev.sum()), -0.8 * np.ones(sidev.sum())], 1)
        comb[fwd < -0.2] = back * 0.6 - up * 0.6
        g, lift, curl, curlr = 0.10, 0.35, 0.5, 0.003
    elif style == "gail":
        # 깊은 옆가르마(x = +0.04): 큰 쪽 앞머리가 이마를 가로질러 반대쪽으로 넘어가며 끝이 안으로 말림(쉼표), 옆 짧게
        L = 0.06 + 0.03 * top
        part = 0.04
        big = rel[:, 0] <= part
        comb = np.where(big[:, None], np.array([-0.55, 0.35, 0.55]), np.array([0.8, 0.45, -0.25]))
        bang = big & (fwd > 0.4)
        comb[bang] = np.array([-0.45, -0.75, -0.45])           # 이마 쪽으로 내려와 반대편으로 넘어감
        L[bang] = 0.11
        g, lift = 0.05, 0.4
    elif style == "messy_slick":
        # 텍스처 슬릭백: 위·뒤로 넘기되 가닥이 흐트러지고 앞에서 몇 가닥이 이마로 흘러내림
        L = 0.07 + 0.03 * top
        comb = np.tile(back * 0.6 + up * 0.6, (k, 1))
        fall = (fwd > 0.5) & (rng.random(k) < 0.08)
        comb[fall] = fr * 0.6 - up * 0.6
        g, lift, curl, curlr = 0.03, 0.4, 0.5, 0.003
    elif style == "long_male":
        L = 0.15 + 0.06 * np.clip(-fwd, 0, 1)          # 턱~어깨, 앞은 짧게
        comb = np.stack([0.12 * np.ones(k), 0.85 * np.ones(k), -0.3 * np.ones(k)], 1)  # 가르마 없이 뒤로 넘김 → 귀 뒤로
        g, lift, curl, curlr = 0.22, 0.3, 0.9, 0.006        # 웨이브
    elif style == "man_bun":
        L[:] = 0.16
        g, lift = 0.0, 0.2
    # 옆 아래(귀 위 띠) 가닥: 짧고 두피에 붙인다 — 바깥으로 튀어나오는 덩어리 방지 (장발·5:5는 길이 유지)
    low_side = (np.abs(rel[:, 0]) > 0.045) & (rel[:, 2] < 0.065)
    if style not in ("long_male", "part_55", "big_afro", "curly_short"):
        L = np.where(low_side, L * 0.45, L)
    d = unit(N * lift + unit(comb) * (1 - lift))
    d[low_side] = unit(N[low_side] * 0.05 + unit(comb[low_side]) * 0.95)
    seg = L * (0.85 + 0.3 * rng.random(k)) / steps
    S = np.zeros((k, steps + 1, 3))
    S[:, 0] = P
    gather = eye + np.array([0.0, 0.09, 0.075])
    for i in range(1, steps + 1):
        if style == "man_bun":
            d = unit(d * 0.5 + unit(gather - S[:, i - 1]) * 0.5)
        d = unit(d + np.array([0, 0, -g]))
        # 두피를 따라 눕기: 가까운 표면 법선 성분을 줄인다 (뿌리 쪽은 살짝 서고, 뒤로 갈수록 눕는다)
        hug = HUG.get(style, 0.6)
        if hug > 0:
            nn = np.zeros((k, 3))
            for j in range(k):
                loc, nrm, _, _ = tree.find_nearest(Vector(S[j, i - 1]), 0.2)
                if loc is not None:
                    nn[j] = nrm
            dn = (d * nn).sum(axis=1, keepdims=True)
            keep_up = np.clip(LIFT_MIN.get(style, 0.08), 0, 1)
            d = unit(d - nn * np.clip(dn - keep_up, 0, None) * hug)
        p = S[:, i - 1] + d * seg[:, None]
        vol = VOLUME.get(style, 0.0) * top                     # 정수리 볼륨(헤어 높이)
        layer = 0.0012 + ((0.0035 if style in ("big_afro", "curly_short") else 0.0007) + vol) * i   # (k,)
        for j in range(k):
            loc, nrm, _, _ = tree.find_nearest(Vector(p[j]), 0.2)
            if loc is None:
                continue
            o = p[j] - np.array(loc)
            lj = float(layer[j]) if np.ndim(layer) else layer
            if low_side[j]:
                lj = min(lj, 0.0016 + 0.0002 * i)
            if o @ np.array(nrm) < lj:
                p[j] = np.array(loc) + np.array(nrm) * lj
        # 진행 방향을 실제 이동으로 갱신 (충돌로 휜 방향을 따라간다)
        d = unit(p - S[:, i - 1])
        S[:, i] = p
    if style in ("seethrough", "messy_slick"):
        for i in range(steps // 2, steps + 1):
            S[:, i] += rng.normal(0, 0.0012 * (i / steps), (k, 3))   # 끝 흐트러짐
    if style == "gail":
        bang = (rel[:, 0] <= 0.04) & (fwd > 0.4)
        for i in range(steps * 2 // 3, steps + 1):
            t = (i - steps * 2 / 3) / (steps / 3)
            S[bang, i] += np.array([0.012, -0.004, 0.01]) * t ** 2       # 끝이 안·위로 말림(쉼표)
    if curl > 0:
        ph = rng.random(k) * 6.283
        for i in range(1, steps + 1):
            t = (i / steps) ** 0.7
            S[:, i] += curlr * t * np.stack([np.cos(ph + i * curl), np.sin(ph + i * curl), np.cos(ph * 1.3 + i * curl * 0.7)], 1)
    if style == "man_bun":
        r = 0.026
        for i in range(steps * 2 // 3, steps + 1):
            a = rng.random(k) * 6.283
            S[:, i] = gather + np.stack([np.cos(a) * r, 0.014 * np.sin(a * 3), np.sin(a) * r * 0.8], 1)
    # 뭉침: 끝을 이웃 평균 쪽으로 (가닥 덩어리 → 머리카락 다발처럼)
    cl = {"buzz": 0.0, "fade_crop": 0.3, "curly_short": 0.45, "big_afro": 0.3, "two_block": 0.4, "part_55": 0.4,
          "pomade": 0.5, "long_male": 0.5, "man_bun": 0.2, "seethrough": 0.75, "gail": 0.55, "messy_slick": 0.65}[style]
    if cl > 0:
        m = max(60, k // (90 if style == "seethrough" else 40))   # 시스루: 큰 다발 적게 → 가닥가닥
        cen = S[rng.choice(k, m, replace=False)]
        tipd = np.linalg.norm(S[:, None, steps // 2] - cen[None, :, steps // 2], axis=2)
        owner = np.argmin(tipd, axis=1)
        for i in range(1, steps + 1):
            t = (i / steps) ** 1.5
            S[:, i] = S[:, i] * (1 - cl * t) + cen[owner, i] * (cl * t)
    return S


def scalp_shell(name, V, T, eye, style, color):
    """두피 머리색 셸 (0.4 mm 띄움): 헤어라인 여유 −1 cm 안 삼각형 → 2번 분할 → 정점 알파 = 헤어라인 거리 smoothstep(경계 흐림).
    페이드 계열은 옆·뒤 아래로 갈수록 옅어지는 짧은 털 그늘(최대 0.65)."""
    import bmesh
    C = V[T].mean(axis=1)
    keep = (C[:, 2] > eye[2] - 0.13) & (np.linalg.norm(C[:, :2] - eye[:2] - np.array([0, 0.085]), axis=1) < 0.15)
    T2 = T[keep]
    mg, ear = margin_of(V[T2].mean(axis=1) - eye, "buzz")
    T2 = T2[(mg > -0.02) & ~ear]
    used = np.unique(T2)
    remap = {int(v): i for i, v in enumerate(used)}
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in V[used]], [], [tuple(remap[int(v)] for v in t) for t in T2])
    bmx = bmesh.new()
    bmx.from_mesh(me)
    bmesh.ops.subdivide_edges(bmx, edges=bmx.edges[:], cuts=2, use_grid_fill=True)
    bmx.normal_update()
    for v in bmx.verts:
        v.co = v.co + v.normal * 0.0004
    bmx.to_mesh(me)
    bmx.free()
    P = np.array([tuple(v.co) for v in me.vertices])
    m_main, ear_v = margin_of(P - eye, style)
    a = np.clip((m_main + 0.004) / 0.012, 0, 1)
    a = a * a * (3 - 2 * a)
    if style == "buzz":
        a = a * 0.45
    if style in ("fade_crop", "two_block", "pomade", "buzz", "gail", "full"):
        m_low, _ = margin_of(P - eye, "buzz" if style != "full" else "full")
        zl = np.clip((P[:, 2] - eye[2] + 0.075) / 0.11, 0, 1)      # 목선 0 → 옆 위 1
        lo_a, hi_a = (0.15, 0.5)
        a = np.maximum(a, np.clip(m_low / 0.01, 0, 1) * (lo_a + hi_a * zl))
    a[ear_v] = 0
    # 귀 자체(머리 옆면보다 바깥으로 튀어나온 부분)는 칠하지 않는다 — 귀가 검게 칠해지던 문제
    relp = P - eye
    ear_out = (np.abs(relp[:, 0]) > 0.068) & (relp[:, 2] < 0.04) & (relp[:, 1] > 0.03) & (relp[:, 1] < 0.13)
    a[ear_out] = 0
    relq = P - eye
    sidem = (np.abs(relq[:, 0]) > 0.05) & (relq[:, 2] < 0.03) & (relq[:, 2] > -0.03)
    print("[shell]", style, len(P), "alpha mean", float(a.mean()), "side verts", int(sidem.sum()), "side alpha", float(a[sidem].mean()) if sidem.any() else -1)
    col = me.color_attributes.new("a", "FLOAT_COLOR", "POINT")
    col.data.foreach_set("color", np.column_stack([np.ones((len(P), 3)), a]).ravel().astype(np.float32))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    m = bpy.data.materials.new(name + "_m")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (color[0] * 0.55, color[1] * 0.55, color[2] * 0.55, 1)
    bsdf.inputs["Roughness"].default_value = 0.85
    for nm in ("Specular IOR Level", "Specular"):
        if nm in bsdf.inputs:
            bsdf.inputs[nm].default_value = 0.0
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "a"
    nt.links.new(attr.outputs["Alpha"], bsdf.inputs["Alpha"])
    me.materials.append(m)
    return ob


def to_curves(name, S, radius_root):
    k, n, _ = S.shape
    cv = bpy.data.hair_curves.new(name)
    cv.add_curves([n] * k)
    cv.position_data.foreach_set("vector", S.reshape(-1).astype(np.float32))
    rad = np.tile(radius_root * (1 - 0.88 * np.linspace(0, 1, n) ** 1.6), k).astype(np.float32)   # 끝으로 갈수록 가늘게(테이퍼)
    att = cv.attributes.get("radius") or cv.attributes.new("radius", "FLOAT", "POINT")
    att.data.foreach_set("value", rad)
    ob = bpy.data.objects.new(name, cv)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def hair_mat(color):
    m = bpy.data.materials.new("hair")
    m.use_nodes = True
    nt = m.node_tree
    for nd in list(nt.nodes):
        nt.nodes.remove(nd)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    h = nt.nodes.new("ShaderNodeBsdfHairPrincipled")
    h.parametrization = "MELANIN"
    lum = 0.3 * color[0] + 0.59 * color[1] + 0.11 * color[2]
    h.inputs["Melanin"].default_value = float(np.clip(1.0 - lum * 3.2, 0.25, 0.97))
    h.inputs["Melanin Redness"].default_value = float(np.clip((color[0] - color[2]) * 4.0, 0.05, 0.9))
    if "Roughness" in h.inputs:
        h.inputs["Roughness"].default_value = 0.42
    for nm, v in (("Random Color", 0.18), ("Random Roughness", 0.2), ("Coat", 0.0)):
        if nm in h.inputs:
            h.inputs[nm].default_value = v
    nt.links.new(h.outputs[0], out.inputs[0])
    return m


def setup(sc):
    sc.world = bpy.data.worlds.new("w")
    sc.world.use_nodes = True
    sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.65, 0.85, 1)
    sc.world.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    for rot, en in (((math.radians(55), 0, math.radians(30)), 4.0), ((math.radians(75), 0, math.radians(195)), 3.0)):
        l = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        l.data.energy = en
        l.rotation_euler = rot
        sc.collection.objects.link(l)
    sc.render.engine = "CYCLES"     # 시험: 헤어 BSDF 형태 판단용 (게임은 카드 + 실시간 셰이더)
    sc.cycles.samples = 48
    sc.cycles.device = "CPU"
    sc.render.resolution_x, sc.render.resolution_y = 600, 700
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 85
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def main():
    styles = ARGS[1].split(",") if len(ARGS) > 1 else list(STYLES)
    bpy.ops.wm.read_homefile(use_empty=True)
    sc = bpy.context.scene
    cam = setup(sc)
    for i, st in enumerate(styles):
        skin, col, n = STYLES[st]
        rng = np.random.default_rng(100 + i)
        bm = human(skin)
        bm.location.x = i * 1.5
        bpy.context.view_layer.update()
        V, T = body_geo(bm)
        eye = eye_center(bm)
        tree = BVHTree.FromPolygons([tuple(p) for p in V], [tuple(t) for t in T.tolist()])
        P, N = roots(V, T, eye, n, st, rng)
        S = grow(P, N, eye, st, tree, rng)
        hob = to_curves(f"hair_{st}", S, 0.00005 if st == "buzz" else 0.0001 if st != "big_afro" else 0.00012)
        mat = hair_mat(col)
        hob.data.materials.append(mat)
        shell = scalp_shell(f"scalp_{st}", V, T, eye, st, col)
        under = None
        if st in ("fade_crop", "two_block", "pomade", "gail"):
            P2, N2 = roots(V, T, eye, 30000, "buzz", rng)
            low = (P2[:, 2] - eye[2]) < 0.045
            S2 = grow(P2[low], N2[low], eye, "buzz", tree, rng, steps=3)
            S2[:, 1:] = S2[:, :1] + (S2[:, 1:] - S2[:, :1]) * 0.35   # 1~2 mm
            under = to_curves(f"under_{st}", S2, 0.00005)
            under.data.materials.append(mat)
        for o in bpy.data.objects:
            if o.type in ("MESH", "CURVES"):
                o.hide_render = not (o == bm or o.parent == bm or o == hob or o == under or o == shell)
        for view, yaw in (("front", 0.0), ("three", 40.0)):
            tgt = Vector((eye[0], eye[1], eye[2] + 0.03))
            a, dd = math.radians(yaw), 0.95
            cam.location = (tgt.x + dd * math.sin(a), tgt.y - dd * math.cos(a), tgt.z + 0.04)
            dv = tgt - cam.location
            cam.rotation_euler = (math.atan2(math.hypot(dv.x, dv.y), -dv.z), 0, math.atan2(dv.y, dv.x) - math.pi / 2)
            sc.render.filepath = os.path.join(OUT, f"{i:02d}_{st}_{view}.png")
            bpy.ops.render.render(write_still=True)
        print("[groom]", st, len(P), "strands")
    print("[groom] done")


main()
