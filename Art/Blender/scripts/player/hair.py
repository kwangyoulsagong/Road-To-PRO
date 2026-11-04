"""머리카락 10종 (specs/056 FR-40·41). 043 스타일 순서 그대로: 0 버즈 · 1 짧은 · 2 긴(턱선) · 3 곱슬 · 4 번 · 5 페이드 · 6 아프로 · 7 모히칸 · 8 드레드 · 9 삭발.

방식: **볼륨 셸**. 두피(머리 뼈 정점, 스타일별 헤어라인 위)를 기준 표면에서 떼어 법선 방향으로 스타일 두께만큼 띄운다.
가장자리는 두께가 헤어라인 쪽으로 줄어들고 두피 안쪽으로 턱 띠를 내려 끝이 얇은 판처럼 보이지 않게 한다.
가닥 방향은 parts 아틀라스 머리카락 칸(정수리에서 내려오는 줄무늬, UV v = 정수리→헤어라인)과 셰이더 이방성 하이라이트가 맡는다.
알파 카드는 쓰지 않는다(2026-11-04 플랜 결정: 드로우콜·알파 클립 비용) — 품질은 AC-09 육안 승인으로 판단.

각 스타일은 별도 메시 조각(`Hair_<스타일>_LOD0..2`)으로 glb에 들어가고, Unity PlayerMeshBaker가 선수의 스타일 조각만 몸 메시에 합친다.
머리카락은 머리 뼈에 고정(가중치 Head 1) — 목·어깨는 헤어라인 아래라 겹치지 않는다(FR-41, checks.penetration).
델타: 두피 묶음점의 얼굴·머리 타깃·HideHead 델타를 그대로 받는다(머리 모양이 바뀌면 머리카락도 따라간다).
삭발(9)은 형상이 없다(피부 텍스처 그대로) → 메시 조각 없음.
"""
import math

import numpy as np

from . import mpfb_src as MS
from . import skeleton as S

STYLES = ("Hair_Buzz", "Hair_Short", "Hair_Long", "Hair_Curly", "Hair_Bun", "Hair_Fade", "Hair_Afro", "Hair_Mohawk",
          "Hair_Dreads", "Hair_Bald")
MESH_STYLES = STYLES[:-1]
HAIR_TRIS = {0: 1100, 1: 230, 2: 70}
HAIRLINE_SMOOTH = 12  # 헤어라인 고리 평활 반복
REGION = (0.25, 0.0, 1.0, 0.75)   # parts 아틀라스 머리카락 칸 (u0, v0, u1, v1)


HEAD_AXIS_Z = [0.0]  # 머리 수직축 z (머리 정점 앞뒤 가운데) — build에서 정한다


def _phi(p, c):
    """머리 수직축 둘레 각: 0 = 정면, π = 뒤 (좌우 대칭이라 절댓값). 축은 눈 중심이 아니라 머리 앞뒤 가운데."""
    return abs(math.atan2(p[0] - c[0], p[2] - HEAD_AXIS_Z[0]))


def hairline(style, phi, d):
    """헤어라인 높이 (눈 중심 기준, d 단위). phi 0 정면 → π 뒤."""
    pts = [(0.0, 1.18), (0.55, 0.85), (1.05, 0.15), (1.35, -0.35), (1.9, -0.55), (math.pi, -1.30)]
    if style in ("Hair_Long", "Hair_Dreads"):
        pts = [(0.0, 1.10), (0.55, 0.80), (1.05, 0.0), (1.35, -0.75), (1.9, -1.15), (math.pi, -1.75)]
    for (a0, y0), (a1, y1) in zip(pts, pts[1:]):
        if phi <= a1:
            t = (phi - a0) / (a1 - a0)
            return y0 + (y1 - y0) * t
    return pts[-1][1]


def thickness(style, p, c, d, top):
    """두께 (m). p 위치, c 눈 중심, top 정수리 y."""
    phi = _phi(p, c)
    up = (p[1] - c[1]) / d                    # 눈 중심에서 위로 (d 단위)
    crown = np.clip((p[1] - (top - 0.06)) / 0.06, 0, 1)  # 정수리 근처 1
    side = abs(p[0] - c[0]) / d
    if style == "Hair_Buzz":
        return 0.0028
    if style == "Hair_Short":
        return 0.006 + 0.008 * crown + 0.002 * (phi < 0.6)
    if style == "Hair_Fade":
        fade = np.clip((up - 0.2) / 1.0, 0, 1)
        return 0.0015 + 0.016 * fade ** 1.5
    if style == "Hair_Long":
        return 0.014 + 0.008 * crown + 0.010 * np.clip((0.3 - up) / 1.2, 0, 1)
    if style == "Hair_Curly":
        n = math.sin(p[0] * 210) * math.sin(p[1] * 190) * math.sin(p[2] * 230)
        return 0.020 + 0.006 * crown + 0.004 * n
    if style == "Hair_Bun":
        b = np.array([c[0], top - 0.055, c[2] - 0.115])
        bun = max(0.0, 0.045 - float(np.linalg.norm(np.array(p) - b)))
        return 0.007 + 1.4 * bun
    if style == "Hair_Afro":
        return 0.038 + 0.020 * crown - 0.006 * np.clip(-up, 0, 1)
    if style == "Hair_Mohawk":
        strip = np.clip((0.42 - side) / 0.15, 0, 1)
        return 0.0012 + 0.040 * strip * np.clip((up - 0.4) / 0.8, 0, 1) + 0.006 * strip
    if style == "Hair_Dreads":
        n = math.sin(p[0] * 160 + p[2] * 140)
        return 0.017 + 0.006 * crown + 0.004 * n
    raise ValueError(style)


def build(R, style, lod):
    """→ body.Mesh (머리카락 조각). 두피를 띄운 셸 + 가장자리 턱."""
    from . import body as B
    from . import face as F
    c, d = F.eye_frame(R)
    hi = MS.OUR_NAMES.index("Head")
    headw = R.weights[:, hi]
    ears = set(R.groups.get("ears", []))
    bodyset = set(R.groups["body"])
    top = float(R.verts[R.groups["body"], 1].max())
    N = R.normals
    hz = R.verts[[v for v in R.groups["body"] if headw[v] >= 0.5], 2]
    HEAD_AXIS_Z[0] = float((hz.min() + hz.max()) / 2)
    # 두피 정점: 머리 뼈 정점, 헤어라인 위, 귀 제외
    scalp = np.zeros(R.n, dtype=bool)
    for v in R.groups["body"]:
        p = R.verts[v]
        if v in ears or headw[v] < 0.5:
            continue
        if (p[1] - c[1]) / d > hairline(style, _phi(p, c), d) and N[v][1] > -0.35:
            scalp[v] = True
    faces = [f for f in R.faces if all(scalp[v] for v in f) and all(v in bodyset for v in f)]
    ids = sorted({v for f in faces for v in f})
    remap = {v: i for i, v in enumerate(ids)}
    fl = [tuple(remap[v] for v in f) for f in faces]
    base = R.verts[ids]
    # 가장자리까지 거리(고리 수)로 두께를 줄인다
    nb = B._neighbors(len(ids), fl)
    edge_cnt = {}
    for f in fl:
        for k in range(len(f)):
            e = (min(f[k], f[(k + 1) % len(f)]), max(f[k], f[(k + 1) % len(f)]))
            edge_cnt[e] = edge_cnt.get(e, 0) + 1
    boundary = sorted({v for e, n in edge_cnt.items() if n == 1 for v in e})
    ring = np.full(len(ids), 99)
    frontier = list(boundary)
    for v in frontier:
        ring[v] = 0
    k = 0
    while frontier:
        k += 1
        nxt = []
        for v in frontier:
            for u in nb[v]:
                if ring[u] > k:
                    ring[u] = k
                    nxt.append(u)
        frontier = nxt
    # 헤어라인 계단 펴기: 가장자리 고리 정점을 고리 이웃 평균으로 옮긴 뒤 머리 표면에 다시 붙인다(면 단위 선택의 계단 제거)
    bset = set(boundary)
    bnb = {v: [u for u in nb[v] if u in bset and (min(u, v), max(u, v)) in edge_cnt and edge_cnt[(min(u, v), max(u, v))] == 1]
           for v in boundary}
    base = base.copy()
    head_tree = B._subset_tree(R, [f for f in R.faces if all(headw[v] >= 0.3 and v in bodyset for v in f)])
    for _ in range(HAIRLINE_SMOOTH):
        nxt = base.copy()
        for v in boundary:
            if len(bnb[v]) == 2:
                nxt[v] = 0.5 * base[v] + 0.25 * (base[bnb[v][0]] + base[bnb[v][1]])
        for v in boundary:
            loc = head_tree[0].find_nearest(tuple(nxt[v]))[0]
            if loc is not None:
                nxt[v] = np.array(loc)
        base = nxt
    taper = np.clip(ring / 4.0, 0.25, 1.0)
    T = np.array([thickness(style, base[i], c, d, top) for i in range(len(ids))]) * taper
    shell = base + N[ids] * T[:, None]
    # 감량 (셸 위치에서)
    co, f2 = B._decimate_faces(shell, fl, min(HAIR_TRIS[lod], sum(len(f) - 2 for f in fl)))
    # 묶음: 셸 점 → 두피 최근접(셸을 두께만큼 되돌린 점 기준)
    tree = B._subset_tree(R, faces)
    anchor_tri = np.zeros((len(co), 3), dtype=np.int64)
    anchor_bc = np.zeros((len(co), 3))
    off = np.zeros((len(co), 3))
    for i, p in enumerate(co):
        (t, bc), _ = B._bind_point(R, tree, p)
        anchor_tri[i], anchor_bc[i] = t, bc
        a = (R.verts[t] * bc[:, None]).sum(axis=0)
        off[i] = p - a
    bind = B.Binding.__new__(B.Binding)
    bind.tri, bind.bc = anchor_tri, anchor_bc
    P = bind.apply(R.verts) + off
    W = np.zeros((len(co), len(MS.OUR_NAMES)))
    W[:, hi] = 1.0
    alld = B._all_deltas(R)
    D = {k2: bind.apply(v) for k2, v in sorted(alld.items())}
    m = B.Mesh()
    m.verts, m.W, m.deltas, m.vpart = P, W, D, ["hair"] * len(P)
    for f in f2:
        m.faces.append((tuple(f), B.BODY, tuple(hair_uv(P[i], c, d, top) for i in f), "hair", None))
    _rim(m, f2, off, c, d, top)
    return m


def _rim(m, faces, off, c, d, top):
    """셸 가장자리 → 두피 안쪽(두께 + 1 mm 아래)으로 띠."""
    from . import body as B
    edge = {}
    for f in faces:
        for k in range(len(f)):
            a, b = f[k], f[(k + 1) % len(f)]
            edge.setdefault((min(a, b), max(a, b)), []).append((a, b))
    new = {}
    P = m.verts
    extra = []
    for key in sorted(edge):
        if len(edge[key]) != 1:
            continue
        a, b = edge[key][0]
        for v in (a, b):
            if v not in new:
                n = off[v] / max(np.linalg.norm(off[v]), 1e-9)
                new[v] = len(P) + len(extra)
                extra.append((v, P[v] - off[v] - n * 0.001))
        m.faces.append(((b, a, new[a], new[b]), B.BODY,
                        tuple(hair_uv(x, c, d, top) for x in (P[b], P[a], P[a], P[b])), "hair", None))
    if extra:
        src = [v for v, _ in extra]
        m.verts = np.vstack([P, np.array([p for _, p in extra])])
        m.W = np.vstack([m.W, m.W[src]])
        for k in m.deltas:
            m.deltas[k] = np.vstack([m.deltas[k], m.deltas[k][src]])
        m.vpart += ["hair"] * len(extra)


def hair_uv(p, c, d, top):
    """u = 머리 둘레 각(정면 0 → 뒤 1, 좌우 대칭), v = 정수리 1 → 헤어라인 0. parts 칸 + u 1 오프셋."""
    phi = _phi(p, c) / math.pi
    v = np.clip((p[1] - (c[1] - 1.8 * d)) / (top + 0.06 - (c[1] - 1.8 * d)), 0, 1)
    u0, v0, u1, v1 = REGION
    return (1.0 + u0 + (u1 - u0) * phi, v0 + (v1 - v0) * float(v))
