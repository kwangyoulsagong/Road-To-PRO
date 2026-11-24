"""그루밍 → 게임 헤어 굽기 (specs/088 FR-10·12·13·16, T10).

1. 요약(summarize, 스타일마다 1회 — 캐시): 그루밍 가닥(선수 좌표)에서
   · 두피 = 뿌리가 심긴 기준 정점(구멍 메움), 정점별 두께 = 그 정점에 가장 가까운 가닥 점들의 피부 위 높이 90 백분위(평활),
   · 정점별 흐름 = 가닥 접선(피부 접평면 투영) 평균,
   · 돌출 가닥(두께 봉투 밖으로 3 mm 넘게 나가거나 끝이 두피 밖) → 다발(k-평균) 중심선 5점 + 폭,
   · 앞머리 끝 높이·가르마 위치(지표용)를 뽑는다.
2. 굽기(build, LOD마다): 두피 + 두께 → 셸(감량·피부 밀어내기·머리 뼈 묶음, 헤어 062와 같은 묶음 방식), 셸 UV = 면마다 흐름을 v로 둔
   결 아틀라스 타일 좌표, 다발 → 가닥 카드(UV u + 2, 공유 카드 타일). 정점 HairData = (밀도, 컬러 포인트).
"""
import hashlib
import inspect
import json
import os

import numpy as np

from . import frame, styles

BAKE_VERSION = 8   # build만 바뀌면 올리지 않는다 (요약 캐시 유지)
SHELL_TRIS = {0: 1300, 1: 380, 2: 120}         # 셸 감량 목표 (LOD0은 두피 막)
CARD_TRIS = {0: 10700, 1: 120, 2: 0}          # 카드 몫 상한 (헤어 LOD0 합계 ≤ HAIR_MAX[0])
HAIR_MAX = {0: 12000, 1: 450, 2: 150}         # 088 FR-54 (T29) [제안]: LOD0·클로즈업 헤어 ≤ 12k (이전 1.9k), LOD1·2 그대로
STRAND_CARD_LODS = (0,)                       # 088 FR-54 (T29): 그루밍 가닥을 다발 카드로 (셸은 속심) — LOD1·2는 봉투 셸 + 돌출 카드
CARD_STRAND_SKIP = ("curl", "coil")          # 곱슬·코일: 다발 중심선이 곧게 뻗어 고슴도치 가시처럼 보인다(T29 렌더) → LOD0도 봉투 셸 + 결 타일
CARD_STYLE_FORCE = ("Hair_Twists",)           # 코일이지만 꼰 밧줄 다발(드레드처럼 곧게 늘어짐) → 카드 (셸이면 충실도 0.816 < 0.82)
CORE_IN = 0.004                               # LOD0 속심 셸 = 봉투 − 4 mm (≥ 피부 + CAP_T): 번·묶음 덩어리 부피와 카드 사이 빈틈을 맡는다
CAP_T = 0.0015                                # 속심 최소 두께 (피부 위)
CARD_CHORD_MIN = 0.6                          # 다발 카드 후보: 현/호 ≥ 0.6 (번 고리 가닥이 카드가 되면 사방으로 가시 — 속심이 덩어리를 맡는다)
CARD_SEG = {0: 4, 1: 2}                       # 카드 길이 분할 (삼각형 = 2 × 분할)
MIN_T = 0.0006
CUT_D = 1.62                                  # 062: 장발 하한 (눈 높이 − 1.62 × 눈 사이 거리)
CHORD_MIN = 0.75                              # 카드 후보: 현/호 비율 하한 (곧은 가닥만)
EXT_MAX = 1.25                                # 카드 길이 연장 상한
CARD_W_MAX = 0.012                            # 카드 폭 상한 (m)
# 088 T30g: 밧줄 계열(드레드)은 밧줄마다 관 하나 — k-평균 평면 카드(≈ 1,300장)는 밧줄 하나를 짧은 조각 20여 장으로 쪼개
# 네모 조각이 흩어지고 두피가 비쳤다(사용자 "탈모는 머고").
# 2차(사용자 "여전히 탈모가 있음"): k-평균(65)이 그루밍 실제 밧줄(≈ 58개, 뿌리 2~3.5 cm 구역에서 모여 굵기 ≈ 6 mm)을 다시 쪼개 토막·T자가 생기고,
# 관이 가늘어(반지름 ≤ 6.5 mm) 사이로 두피가 보였다 → 경로(뒤 절반)가 ROPE_GROUP 안인 가닥을 한 밧줄로 묶고, 굵기는 뿌리 구역 크기를 따른다 [제안]
ROPE_STYLES = {"Hair_Dreads": 0.012}          # 스타일 → 밧줄 묶음 거리 (경로 뒤 절반 평균, m)
ROPE_SIDES = 6                                # 관 단면 변 수
ROPE_SEG = 12                                 # 관 길이 마디 (8은 마디 현이 두피 곡률 안으로 파고들었다)
ROPE_CLEAR = 0.002                            # 관 안쪽 면 ~ 피부 최소 간격 (두피 셸 CAP_T 1.5 mm 위) [제안]
ROPE_R = (0.0045, 0.0080)                     # 관 반지름 범위 (m) [제안]
ROPE_ROOT_K = 0.35                            # 관 반지름 = 뿌리 구역 반경(70 백분위) × 이 값 (구역을 덮는 굵기) [제안]
ROPE_MIN_KEEP = 0.04                          # 자른 뒤 이보다 짧은(또는 원래 길이 절반 미만) 밧줄은 그리지 않는다 (m) [제안]
CUT_D_BACK = 1.2                              # 머리 뒤 카드 하한 (고개 숙임·GK 동작에서 등·목 관통 방지)
OVER = 0.002                                  # 봉투 밖 돌출 판정 (m)
CACHE = os.path.join(os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "..")), ".farm", "cache", "groom")

# 공유 결 아틀라스 (parts.png): 셸 타일 = 머리카락 칸 (0.25..1, 0..0.75) 3열 × 2행, 카드 타일 = 카드 칸 (0.25..1, 0.75..1) 6열.
STRANDS = ("straight", "wave", "curl", "coil", "braid", "dread")
SHELL_REGION = (0.25, 0.0, 1.0, 0.75)
CARD_REGION = (0.25, 0.75, 1.0, 1.0)
TILE_M = 0.03                                 # 셸 타일 한 칸 = 3 cm
PAD = 6 / 1024


def shell_rect(strand):
    i = STRANDS.index(strand)
    u0, v0, u1, v1 = SHELL_REGION
    w, h = (u1 - u0) / 3, (v1 - v0) / 2
    c, r = i % 3, i // 3
    return (u0 + c * w + PAD, v0 + r * h + PAD, u0 + (c + 1) * w - PAD, v0 + (r + 1) * h - PAD)


def card_rect(strand):
    i = STRANDS.index(strand)
    u0, v0, u1, v1 = CARD_REGION
    w = (u1 - u0) / len(STRANDS)
    return (u0 + i * w + PAD, v0 + PAD, u0 + (i + 1) * w - PAD, v1 - PAD)


# ---------------------------------------------------------------- 캐시

def _fn_sig(fn, depth=0):
    """함수 소스 + 클로저 값(재귀) — 매개변수형 스타일(param.make·post 클로저)은 소스가 같아도 값이 다르다 (T23 캐시 버그)."""
    out = [inspect.getsource(fn)] if callable(fn) and hasattr(fn, "__code__") else [repr(fn)]
    for c in (getattr(fn, "__closure__", None) or ()):
        v = c.cell_contents
        out.append(_fn_sig(v, depth + 1) if callable(v) and hasattr(v, "__code__") and depth < 4 else repr(v))
    return "|".join(out)


def _key(name, R):
    st = styles.STYLES[name]
    h = hashlib.sha256()
    for fn in (st["fn"], st["post"]):
        h.update(_fn_sig(fn).encode())
    h.update(inspect.getsource(styles.groom).encode())
    from . import grow
    h.update(inspect.getsource(grow).encode())
    h.update(json.dumps({k: v for k, v in st.items() if k not in ("fn", "post")}, sort_keys=True, default=str).encode())
    h.update(np.round(R.verts[R.groups["body"]], 5).tobytes())
    h.update(str(BAKE_VERSION).encode())
    return h.hexdigest()[:16]


SAMPLES = {}          # 결 계열 → 표본 가닥 (아틀라스 입력, summary가 채운다)


def summary(name, R, use_cache=True):
    key = (name, id(R))
    if key in _MEMO:
        return _MEMO[key]
    s = _summary(name, R, use_cache)
    _MEMO[key] = s
    SAMPLES.setdefault(styles.STYLES[name]["strand"], s["strands"][:3000].astype(np.float64))
    return s


_MEMO = {}


def _summary(name, R, use_cache=True):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, f"{name}-{_key(name, R)}.npz")
    if use_cache and os.path.exists(path) and not os.environ.get("GROOM_NO_CACHE"):
        z = np.load(path, allow_pickle=False)
        return {k: z[k] for k in z.files}
    s = summarize(name, R)
    np.savez_compressed(path, **s)
    return s


# ---------------------------------------------------------------- 요약

ICO_SUB = 5            # 머리 중심 구면 격자 (정점 10,242 · 면 20,480 — 간격 ≈ 2°)
CONE = 0.045           # 구면 정점 이웃 반경 (단위 방향 거리, ≈ 2.6°)
MIN_PTS = 3            # 머리카락 칸 판정 최소 점 수
FACE_GAP = 0.012       # 얼굴 쪽 셸 허용 두께 (앞머리는 이마 위 1.2 cm 안에 눕는다)


def icosphere(sub=ICO_SUB):
    t = (1 + 5 ** 0.5) / 2
    V = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t),
         (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    F = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
         (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    V = [np.array(v, float) / np.linalg.norm(v) for v in V]
    for _ in range(sub):
        cache, F2 = {}, []

        def mid(a, b):
            key = (min(a, b), max(a, b))
            if key not in cache:
                m = V[a] + V[b]
                V.append(m / np.linalg.norm(m))
                cache[key] = len(V) - 1
            return cache[key]
        for a, b, c in F:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            F2 += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        F = F2
    return np.array(V), np.array(F, dtype=np.int64)


def head_center(eye):
    return eye + np.array([0.0, 0.03, -0.085])          # 시안 Coords와 같은 머리 중심 (선수 좌표: 뒤 = −Z)


def summarize(name, R):
    """그루밍 → 구면 봉투 요약 (선수 좌표). 봉투 반지름 = 방향 원뿔 안 가닥 점 반지름 93 백분위(피부 + 0.6 mm 이상)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    from mathutils.kdtree import KDTree
    H = frame.head(R)
    S_b, C = styles.groom(name, H.V, H.T, H.eye)
    S = frame.from_blender(S_b)                                  # (k, n, 3)
    k, n, _ = S.shape
    eye = frame.from_blender(H.eye)
    ctr = head_center(eye)
    tree = BVHTree.FromPolygons([tuple(p) for p in R.verts], [tuple(int(i) for i in t) for t in H.T], all_triangles=True)
    # 가닥 표본(≤ 45,000)을 호 길이 기준 촘촘한 점(가닥당 UP개)으로 다시 뽑는다 — 긴 가닥(묶음·장발)도 두피 근처가 비지 않게
    if not styles.STYLES[name].get("dense"):                      # 기본: 가닥 점 그대로 (승인 4종 기준 방식)
        sub = None
    else:
        sub = S[:: max(1, k // 45000)]
    UP = 40
    if sub is None:
        pts = S[:, 1:].reshape(-1, 3)
        step = max(1, len(pts) // 700000)
        pts = pts[::step]
        tan = (S[:, 2:] - S[:, :-2]).reshape(-1, 3)[::step] if n > 2 else np.zeros_like(pts)
        tan = np.vstack([tan, np.zeros((len(pts) - len(tan), 3))])[:len(pts)]
        sub = S[:0]
    if len(sub):                                                  # 묶음 스타일: 호 길이 기준 촘촘한 점 (긴 경로의 두피 근처가 비지 않게)
        seg = np.linalg.norm(np.diff(sub, axis=1), axis=2)
        cum = np.concatenate([np.zeros((len(sub), 1)), np.cumsum(seg, axis=1)], axis=1)
        q = np.linspace(0, 1, UP)[None, :] * cum[:, -1:]
        dense = np.empty((len(sub), UP, 3))
        for c in range(3):
            for j in range(len(sub)):
                dense[j, :, c] = np.interp(q[j], cum[j], sub[j, :, c])
        pts = dense[:, 1:].reshape(-1, 3)
        tan = np.gradient(dense, axis=1)[:, 1:].reshape(-1, 3)
    rel = pts - ctr
    rad = np.linalg.norm(rel, axis=1)
    dirs = rel / rad[:, None]
    D, F = icosphere()
    kd = KDTree(len(dirs))
    for i, d in enumerate(dirs):
        kd.insert(Vector(tuple(d)), i)
    kd.balance()
    env = np.zeros(len(D))
    cnt = np.zeros(len(D), dtype=np.int64)
    skin = np.zeros(len(D))
    flow = np.zeros((len(D), 3))
    for i, d in enumerate(D):
        hit = tree.ray_cast(Vector(tuple(ctr)), Vector(tuple(d)), 0.4)
        skin[i] = np.linalg.norm(np.array(hit[0]) - ctr) if hit[0] is not None else 0.0
        near = kd.find_range(Vector(tuple(d)), CONE)
        cnt[i] = len(near)
        if len(near) >= MIN_PTS:
            idx = np.array([j for _, j, _ in near])
            env[i] = float(np.percentile(rad[idx], 92))
            flow[i] = tan[idx].sum(axis=0)
    env = np.where(cnt >= MIN_PTS, np.maximum(env, skin + MIN_T), 0.0)
    # 얼굴 쪽 방향(정면·이마 아래)에서 피부와 떨어진 머리카락(옆으로 늘어진 장발 커튼)은 셸이 얼굴을 덮게 만든다 → 셸에서 빼고 카드로
    face_dir = (D[:, 2] > 0.35) & (D[:, 1] < -0.2)          # 눈 높이 아래 얼굴 방향만 (앞머리 이마 영역은 그대로)
    env = np.where(face_dir & (env - skin > FACE_GAP), 0.0, env)
    # 돌출 가닥: 봉투 밖으로 OVER 넘게 나간 점이 있는 가닥 (늘어진 장발·앞머리 끝 → 카드)
    # 돌출 판정은 원래 가닥 점(솎음)으로
    pts0 = S[:, 1:].reshape(-1, 3)
    st0 = max(1, len(pts0) // 600000)
    pts0 = pts0[::st0]
    sid = np.repeat(np.arange(k), n - 1)[::st0]
    rel0 = pts0 - ctr
    rad0 = np.linalg.norm(rel0, axis=1)
    near_v = np.array(_nearest_dirs(D, rel0 / rad0[:, None]))
    beyond = rad0 - env[near_v]
    smax = np.full(k, -1.0)
    np.maximum.at(smax, sid, beyond)
    # 앞머리 가닥(끝이 얼굴 앞·눈썹 + 3 cm 아래)은 봉투 안이어도 카드로 — 셸 가장자리가 눈썹 위에서 끊겨 앞머리 끝이 사라지지 않게
    bang = (S[:, -1, 2] > eye[2] - 0.02) & (np.abs(S[:, -1, 0] - eye[0]) < 0.06) & (S[:, -1, 1] < eye[1] + 0.03)
    pick = np.where((smax > OVER) | bang)[0]
    front = (np.abs(S[:, -1, 0] - eye[0]) < 0.03) & (S[:, -1, 2] > eye[2] - 0.02)
    fringe = float(np.percentile(S[front, -1, 1], 5) - eye[1]) if front.sum() > 20 else np.nan
    return {"dirs": D, "faces": F, "env": env, "count": cnt, "skin": skin, "flow": flow, "center": ctr,
            "over": S[pick][:, ::2].astype(np.float32), "fringe": np.array(fringe), "eye": eye,
            "strands": S[:: max(1, k // 12000)].astype(np.float32)}


def _nearest_dirs(D, dirs):
    from mathutils import Vector
    from mathutils.kdtree import KDTree
    kd = KDTree(len(D))
    for i, d in enumerate(D):
        kd.insert(Vector(tuple(d)), i)
    kd.balance()
    return [kd.find(Vector(tuple(d)))[1] for d in dirs]


# ---------------------------------------------------------------- 굽기

def _cluster(P, m, iters=8):
    """결정론 k-평균 (P (k, d)). 초기 중심 = 정렬 인덱스 등간격."""
    if len(P) <= m:
        return np.arange(len(P)), P.copy()
    cen = P[np.linspace(0, len(P) - 1, m).astype(int)].copy()
    lab = np.zeros(len(P), dtype=np.int64)
    for _ in range(iters):
        best = np.full(len(P), np.inf)
        for c in range(0, m, 64):
            d = ((P[:, None, :] - cen[None, c:c + 64, :]) ** 2).sum(axis=2)
            j = d.argmin(axis=1)
            dm = d[np.arange(len(P)), j]
            better = dm < best
            lab[better] = c + j[better]
            best[better] = dm[better]
        for c in range(m):
            sel = lab == c
            if sel.any():
                cen[c] = P[sel].mean(axis=0)
    return lab, cen


HAIRLINE_ITERS = 10    # 헤어라인(셸 가장자리) 평활 반복 — 구면 격자 계단(톱니) 제거
TAPER_RINGS = 2        # 가장자리에서 피부 쪽으로 가늘어지는 고리 수


def _smooth_hairline(R, ctr, Dl, envl, skinl, fl):
    """셸 가장자리 고리를 방향 공간에서 평활(톱니 제거)하고, 가장자리 → 안쪽 TAPER_RINGS 고리를 피부 쪽으로 가늘게 한다.
    피부 반지름은 기준 몸 표면 광선으로 다시 잰다(방향이 움직이므로). → 정점 (n,3)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    body = set(R.groups["body"])
    tris = []
    for f in R.faces:
        if all(v in body for v in f):
            for k in range(1, len(f) - 1):
                tris.append((f[0], f[k], f[k + 1]))
    tree = BVHTree.FromPolygons([tuple(p) for p in R.verts], tris, all_triangles=True)
    edge = {}
    for f in fl:
        for k in range(3):
            e = (min(f[k], f[(k + 1) % 3]), max(f[k], f[(k + 1) % 3]))
            edge[e] = edge.get(e, 0) + 1
    bnb = {}
    for (a, b), c in edge.items():
        if c == 1:
            bnb.setdefault(a, []).append(b)
            bnb.setdefault(b, []).append(a)
    Dn = Dl.copy()
    for _ in range(HAIRLINE_ITERS):
        nxt = Dn.copy()
        for v, nbs in bnb.items():
            if len(nbs) == 2:
                d = 0.5 * Dn[v] + 0.25 * (Dn[nbs[0]] + Dn[nbs[1]])
                nxt[v] = d / (np.linalg.norm(d) + 1e-12)
        Dn = nxt
    sk = skinl.copy()
    for v in bnb:
        hit = tree.ray_cast(Vector(tuple(ctr)), Vector(tuple(Dn[v])), 0.4)
        if hit[0] is not None:
            sk[v] = float(np.linalg.norm(np.array(hit[0]) - ctr))
    # 고리 번호 (가장자리 0 → 안쪽)
    nb = [[] for _ in range(len(Dl))]
    for f in fl:
        for k in range(3):
            nb[f[k]] += [f[(k + 1) % 3], f[(k + 2) % 3]]
    ring = np.full(len(Dl), 99)
    front = list(bnb)
    for v in front:
        ring[v] = 0
    r = 0
    while front and r < TAPER_RINGS:
        r += 1
        nxt = []
        for v in front:
            for u in nb[v]:
                if ring[u] > r:
                    ring[u] = r
                    nxt.append(u)
        front = nxt
    rings = int(np.clip(np.median(envl - skinl) / 0.004, TAPER_RINGS, 7))   # 두꺼운 머리(아프로)는 더 넓게 가늘어진다 (챙 방지)
    if rings > r:
        while front and r < rings:
            r += 1
            nxt = []
            for v in front:
                for u in nb[v]:
                    if ring[u] > r:
                        ring[u] = r
                        nxt.append(u)
            front = nxt
    w = np.clip(ring / (rings + 1.0), 0, 1)                       # 0 가장자리 → 1 안쪽
    w = w * w * (3 - 2 * w)
    rad = (sk + MIN_T * 1.5) * (1 - w) + envl * w
    rad = np.maximum(rad, sk + MIN_T)
    return ctr + Dn * rad[:, None]


def _edge_factor(co, faces, width=0.008):
    """셸 경계(헤어라인)에서 안쪽 거리 → 0.6(가장자리) ~ 1(width 안쪽) 밀도 배율 (T23: 1.5 cm·0.45는 디더 점무늬가 넓게 보임)."""
    edge = {}
    for f in faces:
        for k in range(3):
            e = (min(f[k], f[(k + 1) % 3]), max(f[k], f[(k + 1) % 3]))
            edge[e] = edge.get(e, 0) + 1
    bv = sorted({v for e, c in edge.items() if c == 1 for v in e})
    if not bv:
        return np.ones(len(co))
    B = co[bv]
    d = np.array([np.min(np.linalg.norm(B - p, axis=1)) for p in co])
    t = np.clip(d / width, 0, 1)
    return 0.6 + 0.4 * t * t * (3 - 2 * t)


def _inward(P, faces, ctr):
    """면마다 기하 법선(cross)이 머리 중심 쪽을 향하도록 감김을 맞춘다 (선수 좌표 = 반사 틀, body·hair 메시와 같은 규칙)."""
    out = []
    for f in faces:
        a, b, c = P[f[0]], P[f[1]], P[f[2]]
        n = np.cross(b - a, c - a)
        out.append(tuple(f) if n @ ((a + b + c) / 3 - ctr) < 0 else (f[0], f[2], f[1]))
    return out


def _rope_tubes(m, over, group_d, ctr, c_eye, d_eye, cut_y, crect, R, tree, alld):
    """088 T30g: 밧줄(드레드) — 경로가 group_d 안인 가닥끼리 한 밧줄로 묶어 중심선(대표 가닥)을 따라 ROPE_SIDES각 관을 붙인다.
    굵기는 다발 굵기와 뿌리 구역(× ROPE_ROOT_K) 중 큰 값. 자름 규칙은 카드와 같다(얼굴 앞에 들어서기 전까지, 머리 뒤는 CUT_D_BACK 위). 끝은 0.6배."""
    from player import hair_skin as HS
    # 밧줄 = 그루밍 다발(clump, 뿌리 근처에서 바로 한 줄): 경로 뒤 절반이 group_d 안인 가닥끼리 묶는다(정렬 순서대로 탐욕 — 결정론).
    # k-평균은 실제 밧줄 수와 어긋나 밧줄을 쪼개거나 섞었다(T자 토막)
    tail = over[:, over.shape[1] // 2:]
    lab = np.full(len(over), -1, dtype=np.int64)
    n_rope = 0
    for i in range(len(over)):
        if lab[i] >= 0:
            continue
        sel = (lab < 0) & (np.linalg.norm(tail - tail[i][None], axis=2).mean(axis=1) < group_d)
        lab[sel] = n_rope
        n_rope += 1
    for c in range(n_rope):
        grp = over[lab == c]
        if len(grp) < 3:
            continue
        root_r = np.percentile(np.linalg.norm(grp[:, 0] - grp[:, 0].mean(axis=0), axis=1), 70)
        # 대표 가닥(medoid — 무리 평균선에 가장 가까운 실제 가닥): 무리 안에 뒤·옆으로 갈라지는 가닥이 섞이면 점별 중간값 선이 쪼그라들어
        # 앞머리 쪽 밧줄이 위로 솟은 토막이 됐다
        glen = np.linalg.norm(np.diff(grp, axis=1), axis=2).sum(axis=1)
        cand = grp[glen >= np.median(glen)]                              # 짧은 가닥은 대표 후보에서 뺀다 (T자 토막)
        mean = cand.mean(axis=0)
        line = cand[int(np.argmin(np.linalg.norm(cand - mean[None], axis=2).sum(axis=1)))].astype(np.float64)
        grp = grp[np.linalg.norm(grp - line[None], axis=2).mean(axis=1) < 0.012]   # 대표 가닥 둘레 1.2 cm 안 가닥만 굵기 계산에
        if len(grp) < 3:
            continue
        back_cut = np.where(line[:, 2] < ctr[2] - 0.02, c_eye[1] - CUT_D_BACK * d_eye, cut_y)
        in_face = (line[:, 1] < c_eye[1] + 0.012) & (line[:, 2] > c_eye[2] - 0.035) & (np.abs(line[:, 0] - c_eye[0]) < 0.07)
        end = len(line)
        if in_face.any():
            end = min(end, int(np.argmax(in_face)))
        below = line[:, 1] < back_cut
        if below.any():
            end = min(end, int(np.argmax(below)))
        if end < 2:
            continue
        full_len = np.linalg.norm(np.diff(line, axis=0), axis=1).sum()
        line, grp = line[:end], grp[:, :end]
        # 길이 상한(062: 머리 뒤 눈 − 1.2 d)·얼굴 앞에서 뿌리 근처가 잘린 밧줄은 토막으로 튀어 보인다 → 그리지 않는다(바탕 셸이 맡는다) [제안]
        if np.linalg.norm(np.diff(line, axis=0), axis=1).sum() < max(ROPE_MIN_KEEP, 0.5 * full_len):
            continue
        idx = np.linspace(0, len(line) - 1, ROPE_SEG + 1).round().astype(int)
        line, g = line[idx], grp[:, idx]
        tan = np.gradient(line, axis=0)
        tan /= np.linalg.norm(tan, axis=1, keepdims=True) + 1e-12
        rel = g - line[None]
        rel -= tan[None] * (rel * tan[None]).sum(axis=2, keepdims=True)
        # 굵기는 밧줄 하나에 하나(마디마다 따로 재면 뿌리 쪽 모이는 가닥 때문에 마디 사이 굵기가 튀어 각진 원뿔 조각이 됐다), 끝만 가늘게
        r0 = np.clip(max(float(np.median(np.percentile(np.linalg.norm(rel, axis=2), 70, axis=0)[len(line) // 4:])), ROPE_ROOT_K * root_r), *ROPE_R)
        rad = np.full(len(line), r0)
        rad[-2:] *= (0.85, 0.6)
        # 관을 두피 위에 얹는다: 중심이 피부(법선 방향)에서 반지름 + ROPE_CLEAR 넘게 떨어지게 민다. 머리 중심 방향으로만 띄우면
        # 두피를 따라 휘는 밧줄이 마디 사이 현·옆머리 곡률로 머리 속에 묻혀 군데군데만 보였다(사용자 "여전히 탈모가 있음" — 정수리 앞 토막)
        for i in range(len(line)):
            loc, nrm, _, _ = tree[0].find_nearest(tuple(map(float, line[i])), 0.3)
            if loc is None:
                line[i] = line[i] + (line[i] - ctr) / np.linalg.norm(line[i] - ctr) * rad[i]
                continue
            loc, nrm = np.array(loc), np.array(nrm)
            if nrm @ (loc - ctr) < 0:
                nrm = -nrm
            h = (line[i] - loc) @ nrm
            want = rad[i] + ROPE_CLEAR if i else 0.3 * rad[i]             # 뿌리 고리는 두피 속에 반쯤 묻는다 (열린 관 입구가 안 보이게)
            line[i] = line[i] + nrm * max(0.0, want - h)
        tan = np.gradient(line, axis=0)
        tan /= np.linalg.norm(tan, axis=1, keepdims=True) + 1e-12
        # 단면 틀 = 회전 최소 틀(앞 마디 틀을 다음 접선에 수직으로 옮김). 머리 중심 방향을 접선에 수직으로 깎은 틀은 뿌리에서 밧줄이
        # 두피 밖으로 곧게 솟는 마디(접선 ≈ 바깥 방향)에서 0에 가까워 고리가 비틀려 납작한 띠 조각이 됐다
        outw = np.zeros_like(line)
        seed = line[len(line) // 2] - ctr
        for i in range(len(line)):
            v = seed if i == 0 else outw[i - 1]
            v = v - tan[i] * (v @ tan[i])
            if np.linalg.norm(v) < 1e-6:
                v = np.cross(tan[i], [1.0, 0.0, 0.0] if abs(tan[i][0]) < 0.9 else [0.0, 1.0, 0.0])
            outw[i] = v / np.linalg.norm(v)
        side = np.cross(tan, outw)
        ang = 2 * np.pi * np.arange(ROPE_SIDES) / ROPE_SIDES
        ring = [line[i] + rad[i] * (np.cos(ang)[:, None] * outw[i] + np.sin(ang)[:, None] * side[i]) for i in range(len(line))]
        P = np.vstack(ring)                                        # (마디+1) × 변
        cu0 = crect[0] + (crect[2] - crect[0]) * ((c * 37) % 5) / 5.0
        du = (crect[2] - crect[0]) / 5.0 / ROPE_SIDES
        faces = []
        for i in range(len(line) - 1):
            va = crect[3] - (crect[3] - crect[1]) * i / (len(line) - 1)
            vb = crect[3] - (crect[3] - crect[1]) * (i + 1) / (len(line) - 1)
            for k in range(ROPE_SIDES):
                k2 = (k + 1) % ROPE_SIDES
                a, b, c2, d = i * ROPE_SIDES + k, (i + 1) * ROPE_SIDES + k, (i + 1) * ROPE_SIDES + k2, i * ROPE_SIDES + k2
                ua, ub = 2 + cu0 + du * k, 2 + cu0 + du * (k + 1)
                for tri, uv in (((a, b, c2), ((ua, va), (ua, vb), (ub, vb))), ((a, c2, d), ((ua, va), (ub, vb), (ub, va)))):
                    p3 = P[list(tri)]
                    axis_pt = (line[i] + line[i + 1]) / 2           # 관 축 기준 안쪽 법선 감김 (카드·셸과 같은 규칙)
                    if np.cross(p3[1] - p3[0], p3[2] - p3[0]) @ (p3.mean(axis=0) - axis_pt) > 0:
                        tri, uv = (tri[0], tri[2], tri[1]), (uv[0], uv[2], uv[1])
                    faces.append((tri, uv))
        # 끝 마개 (열린 관 끝으로 안쪽 면이 보여 종이를 만 조각처럼 보였다)
        tip = len(P)
        P = np.vstack([P, line[-1] + tan[-1] * rad[-1]])
        i = len(line) - 1
        for k in range(ROPE_SIDES):
            k2 = (k + 1) % ROPE_SIDES
            tri = (i * ROPE_SIDES + k, tip, i * ROPE_SIDES + k2)
            uv = ((2 + cu0 + du * k, crect[1]), (2 + cu0 + du * (k + 0.5), crect[1]), (2 + cu0 + du * (k + 1), crect[1]))
            p3 = P[list(tri)]
            if np.cross(p3[1] - p3[0], p3[2] - p3[0]) @ (p3.mean(axis=0) - line[-1]) > 0:
                tri, uv = (tri[0], tri[2], tri[1]), (uv[0], uv[2], uv[1])
            faces.append((tri, uv))
        HS.append(m, P, faces, "hair", R, tree, hd=np.column_stack([np.ones(len(P)), np.zeros(len(P))]), alld=alld, vpart="card")


def strand_cards(name, lod):
    """088 FR-54 (T29): 이 스타일·LOD가 가닥 다발 카드(+ 두피 막)인지."""
    return lod in STRAND_CARD_LODS and (styles.STYLES[name]["strand"] not in CARD_STRAND_SKIP or name in CARD_STYLE_FORCE)


def build(R, name, lod, s):
    """→ body.Mesh (헤어 조각). s = summary()."""
    from mathutils import Vector
    from mathutils.kdtree import KDTree
    from player import body as B
    from player import hair_skin as HS
    from player import mpfb_src as MS
    st = styles.STYLES[name]
    D, Fc, env, cnt = s["dirs"], s["faces"], s["env"].copy(), s["count"]
    ctr = s["center"]
    hair = env > 0
    # 이웃 표 (구면 격자)
    nb = [[] for _ in range(len(D))]
    for a, b2, c in Fc:
        nb[a] += [b2, c]
        nb[b2] += [a, c]
        nb[c] += [a, b2]
    nb = [sorted(set(x)) for x in nb]
    from player import face as F
    c_eye, d_eye = F.eye_frame(R)
    hp = ctr + D * s["skin"][:, None]                             # 방향 광선이 피부에 닿는 점
    face_dir = (hp[:, 1] < c_eye[1] + 0.012) & (hp[:, 2] > c_eye[2] - 0.035) & (np.abs(hp[:, 0] - c_eye[0]) < 0.07)
    # 얼굴(눈썹 아래 앞면)에 닿는 방향: 피부에서 FACE_GAP 넘게 떨어진 머리카락은 셸에서 빼고(늘어진 커튼은 카드), 구멍 메우기도 안 한다
    drop = face_dir & ((env - s["skin"] > FACE_GAP) | (hp[:, 1] < c_eye[1] - 0.005))   # 눈 높이 아래 얼굴 앞면은 셸 없음
    env[drop] = 0.0
    hair &= ~drop
    # 088 T25 (FR-55): 헤어라인 안 두피에 닿는 방향은 항상 최소 셸 — 다발(드레드 등) 사이로 가닥 점이 적은 방향이 비어 뒤통수에 구멍이 났다.
    # 뿌리는 모든 스타일이 같은 헤어라인("full")에 심긴다. 페이드는 두께 밀도라 얇은 최소 셸은 비친다(062 FR-42 유지)
    from . import grow as GG
    mg, ear = GG.margin_of(frame.to_blender(hp - s["eye"]), "full")
    # 뒤통수 방향(D_z < −0.2)만 — 앞·옆(귀 둘레는 시안 뿌리 제외 영역이 헤어라인 "full"보다 넓다)에 깔면 정면 윤곽에 셸이 덧붙어 승인 스타일 IoU가 떨어졌다
    # 088 T30g: 밧줄 스타일(드레드)은 밧줄 사이로 두피가 보이는 게 기본이라 앞·정수리도 최소 셸(바탕 머리색) — 뒤통수만이면 정수리 앞이 맨살
    scalp = (s["skin"] > 0) & ~face_dir & (mg > 0) & ~ear & ((D[:, 2] < -0.2) | (name in ROPE_STYLES))
    env = np.where(scalp & ~hair, s["skin"] + MIN_T, env)
    hair |= scalp
    for _ in range(4):                                            # 구멍 메우기 (이웃 대부분이 머리카락)
        add = [i for i in range(len(D)) if not hair[i] and not face_dir[i] and sum(hair[j] for j in nb[i]) >= 0.6 * len(nb[i])]
        for i in add:
            hair[i] = True
            env[i] = np.mean([env[j] for j in nb[i] if env[j] > 0])
    for _ in range(2):                                            # 반지름 평활
        env = np.where(hair, 0.5 * env + 0.5 * np.array([np.mean([env[j] for j in nb[i] if hair[j]] or [env[i]])
                                                        for i in range(len(D))]), 0.0)
    env = np.where(hair, np.maximum(env, s["skin"] + MIN_T), 0.0)
    # 062 규칙: 머리카락이 눈 − 1.62 d 아래로 내려가면 목 아래·윗가슴·어깨를 뚫는다(머리 뼈에 묶여 고개를 돌리면 더) → 셸·카드를 그 높이에서 자른다
    cut_y = c_eye[1] - CUT_D * d_eye
    shell_cut = np.where(D[:, 2] < -0.3, c_eye[1] - CUT_D_BACK * d_eye, cut_y)   # 머리 뒤는 더 높게 (GK 동작 등·목 관통)
    # 088 T25: 자름 높이 아래로 나가는 방향은 빼지 않고 반지름을 그 높이까지 줄인다 — 부푼 장발에서 방향을 통째로 빼 목덜미 두피가 드러났다
    down = (D[:, 1] < -1e-6) & (D[:, 2] < 0.0)                 # 뒤쪽 반구만 (앞·옆은 1차 승인 실루엣 그대로 — 줄인 셸이 얼굴 옆에 남아 정면 IoU가 떨어졌다)
    env_cap = np.where(down, (ctr[1] - shell_cut) / np.where(down, -D[:, 1], 1.0), np.inf)
    env = np.where(hair & (env > env_cap) & (env_cap >= s["skin"] + MIN_T), env_cap, env)
    hair &= (ctr[1] + D[:, 1] * env) >= shell_cut - 1e-6
    env_full = env.copy()                                         # 밀도(페이드 두께)는 원래 봉투로
    if strand_cards(name, lod):                                   # 088 T29: 실루엣은 가닥 카드가 맡고 셸은 두피 막
        # 카드 후보 가닥(현/호 ≥ CARD_CHORD_MIN)이 지나는 방향만 속심으로 줄인다 — 번처럼 가닥이 고리로 감겨 카드가 없는 덩어리는 봉투 그대로
        cand = s["strands"].astype(np.float64)
        arc = np.linalg.norm(np.diff(cand, axis=1), axis=2).sum(axis=1)
        cand = cand[np.linalg.norm(cand[:, -1] - cand[:, 0], axis=1) > CARD_CHORD_MIN * np.maximum(arc, 1e-6)]
        covered = np.zeros(len(D), dtype=bool)
        if len(cand):
            cp = cand[:, 1::2].reshape(-1, 3)
            cp = cp[:: max(1, len(cp) // 200000)] - ctr
            covered[np.unique(_nearest_dirs(D, cp / (np.linalg.norm(cp, axis=1, keepdims=True) + 1e-12)))] = True
            for _ in range(2):                                    # 이웃 두 고리까지 (카드 폭)
                covered = covered | np.array([any(covered[j] for j in nb[i]) for i in range(len(D))])
        env = np.where(hair, np.where(covered, np.maximum(env - CORE_IN, s["skin"] + CAP_T), env), 0.0)
        if name in ROPE_STYLES:                                   # 088 T30g: 밧줄은 두피 위에 얹힌다 — 셸은 두피 바로 위 얇은 층만(봉투 높이면 관이 파묻혀 토막처럼 보였다)
            env = np.where(hair, s["skin"] + CAP_T, 0.0)
    faces = [tuple(f) for f in Fc if hair[f].all()]
    ids = sorted({v for f in faces for v in f})
    remap = {v: i for i, v in enumerate(ids)}
    fl = [tuple(remap[v] for v in f) for f in faces]
    P0 = _smooth_hairline(R, ctr, D[ids], env[ids], s["skin"][ids], fl)
    fl = _inward(P0, fl, ctr)                                    # 선수 좌표 감김 규칙: 기하 법선이 안쪽(반사 변환 — 기준 몸 면과 같게)
    co, f2 = B._decimate_faces(P0, fl, min(SHELL_TRIS[lod], len(fl)))
    f2 = _inward(np.asarray(co, dtype=np.float64), [tuple(f) for f in f2], ctr)
    co = co.astype(np.float64)
    skin_pts = ctr + D[ids] * s["skin"][ids][:, None]
    co = HS.clear(co, f2, skin_pts, D[ids])
    bodyset = set(R.groups["body"])
    hi = MS.OUR_NAMES.index("Head")
    ni = MS.OUR_NAMES.index("Neck")
    w = R.weights[:, hi] + R.weights[:, ni]
    tree = B._subset_tree(R, [f for f in R.faces if all(v in bodyset and w[v] >= 0.2 for v in f)])
    m = B.Mesh()
    m.style = name
    m.verts = np.zeros((0, 3))
    m.W = np.zeros((0, len(MS.OUR_NAMES)))
    alld = B._all_deltas(R)
    m.deltas = {k2: np.zeros((0, 3)) for k2 in sorted(alld) if lod == 0 or not k2.startswith(B.EXPR_PREFIX)}
    m.vpart = []
    m.hairdata = np.zeros((0, 2))
    kd = KDTree(len(ids))
    for i, v in enumerate(ids):
        kd.insert(Vector(tuple(D[v])), i)
    kd.balance()
    fv = s["flow"][ids]
    # 셸 UV (088 T23, glb 크기): 정점 공유 연속 좌표 — 머리 중심 방위각·고도 × 반지름 / 타일 크기. u = 10 + 타일 × 100 + 연속 u
    # (셰이더가 frac으로 결 타일을 반복 샘플). 방위각 이음매(뒤통수)를 건너는 면만 정점이 갈라진다. v = 고도(아래로 흐르는 결과 대체로 나란함)
    tile = STRANDS.index(st["strand"])
    rel = co - ctr
    th = np.arctan2(rel[:, 0], rel[:, 2])
    ph = np.arcsin(np.clip(rel[:, 1] / (np.linalg.norm(rel, axis=1) + 1e-12), -1, 1))
    k = 0.09 / TILE_M
    fuv = []
    for f in f2:
        t3 = th[list(f)].copy()
        if t3.max() - t3.min() > np.pi:
            t3[t3 < 0] += 2 * np.pi
        uvs = tuple((10.0 + tile * 100.0 + 40.0 + float(t3[i]) * k, float(ph[f[i]]) * k) for i in range(3))
        fuv.append((tuple(f), uvs))
    # 밀도: 봉투 두께(봉투 − 피부)가 얇은 곳(페이드·헤어라인)은 피부가 비친다
    thk = np.array([env_full[ids][kd.find(Vector(tuple((p - ctr) / (np.linalg.norm(p - ctr) + 1e-12))))[1]]
                   - s["skin"][ids][kd.find(Vector(tuple((p - ctr) / (np.linalg.norm(p - ctr) + 1e-12))))[1]] for p in co])
    # 밀도(디더): 페이드 계열만 두께를 따른다(옆·뒤 아래가 비친다, 062 FR-42). 나머지는 꽉 차고, 헤어라인 가장자리 0.8 cm만 부드럽게 비친다
    from player import hairstyles as HSS
    dens = np.clip((thk - 0.0012) / 0.004, 0.05, 1.0) if name in HSS.FADE else np.ones(len(co))   # 페이드 맨 아래 ≤ 0.2 (062 FR-42)
    edge = _edge_factor(co, f2)
    dens = dens * edge
    HS.append(m, co, fuv, "hair", R, tree, hd=np.column_stack([dens, HS.color_point(st.get("cp"), co, R)]), alld=alld)
    # 카드
    budget = min(CARD_TRIS[lod], HAIR_MAX[lod] - m.triangles)
    seg = CARD_SEG.get(lod, 0) if st["strand"] not in ("curl", "coil") else 0   # 곱슬·코일은 카드 없음 (가시처럼 튐) — 셸 + 결 타일
    over = s["over"].astype(np.float64)
    if strand_cards(name, lod):                                   # 088 T29: 돌출 가닥만이 아니라 모든 가닥 → 다발 카드
        seg = CARD_SEG[lod]
        over = s["strands"].astype(np.float64)
    if len(over):                                                  # 고리·말림 가닥(번·나선)은 카드로 못 펴서 뺀다 — 봉투(속심) 셸이 덩어리를 맡는다
        arc = np.linalg.norm(np.diff(over, axis=1), axis=2).sum(axis=1)
        chord = np.linalg.norm(over[:, -1] - over[:, 0], axis=1)
        over = over[chord > (CARD_CHORD_MIN if strand_cards(name, lod) else CHORD_MIN) * np.maximum(arc, 1e-6)]
    if name in ROPE_STYLES and strand_cards(name, lod):
        # 카드용 직선도 필터(chord/arc) 전 가닥 전부 — 머리 위를 넘어 뒤로 휘는 긴 밧줄이 필터에 빠져 정수리 밧줄이 토막이 됐다
        _rope_tubes(m, s["strands"].astype(np.float64), ROPE_STYLES[name], ctr, c_eye, d_eye, cut_y, card_rect(st["strand"]), R, tree, alld)
        seg = 0                                                    # 평면 카드는 만들지 않는다
    if seg and budget >= 2 * seg and len(over):
        ncard = int(budget // (2 * seg))
        feat = np.concatenate([over[:, over.shape[1] // 2], over[:, -1]], axis=1)
        if strand_cards(name, lod):                                # 뿌리도 — 다발이 두피 한 자리에서 나오게
            feat = np.concatenate([over[:, 0], feat], axis=1)
        lab, _ = _cluster(feat, ncard)
        crect = card_rect(st["strand"])
        cards = []
        for c in range(ncard):
            grp = over[lab == c]
            if len(grp) < 3:
                continue
            line = grp.mean(axis=0)                                  # (np, 3) 중심선
            # 평균 중심선은 다발 안 가닥보다 짧다(끝 엇갈림) → 끝 구간을 다발 가닥 길이 80 백분위까지 늘인다 (앞머리 끝 높이 보존)
            gl = np.linalg.norm(np.diff(grp, axis=1), axis=2).sum(axis=1)
            ll = np.linalg.norm(np.diff(line, axis=0), axis=1).sum()
            front_tip = strand_cards(name, lod) and line[-1, 2] > c_eye[2] - 0.035
            if ll > 1e-6 and not front_tip:
                # 088 T29: 다발 카드는 다발 길이 중간값까지만 늘이고, 얼굴 쪽에서 끝나는 다발(앞머리)은 늘이지 않는다
                # (80 백분위·앞머리까지 늘이면 앞머리 끝이 톱니로 내려오고, 안 늘이면 아프로 잔털 윤곽이 빠진다)
                ext = min(EXT_MAX, np.percentile(gl, 50 if strand_cards(name, lod) else 80) / ll)
                if ext > 1.0:
                    tipd = line[-1] - line[-2]
                    line = np.vstack([line[:-1], line[-1] + tipd * (ext - 1.0) * (len(line) - 1)])
            back_cut = np.where(line[:, 2] < ctr[2] - 0.02, c_eye[1] - CUT_D_BACK * d_eye, cut_y)   # 머리 뒤 카드는 더 높게 자른다
            ld = line - ctr
            ld = ld / (np.linalg.norm(ld, axis=1, keepdims=True) + 1e-12)
            in_face = (line[:, 1] < c_eye[1] + 0.012) & (line[:, 2] > c_eye[2] - 0.035) & (np.abs(line[:, 0] - c_eye[0]) < 0.07)
            if in_face.any():
                if not strand_cards(name, lod) or int(np.argmax(in_face)) < 2:
                    continue                                         # 얼굴 앞을 지나는 카드는 뺀다 (볼 위 점)
                cutf = int(np.argmax(in_face))                       # 088 T29: 다발 카드는 얼굴 앞에 들어서기 전까지만 (얼굴 옆 드레드 다발 유지)
                line, grp, back_cut = line[:cutf], grp[:, :cutf], back_cut[:cutf]
            keep_pts = line[:, 1] >= back_cut
            if keep_pts.sum() < 2:
                continue
            line = line[:int(np.argmin(keep_pts)) if not keep_pts.all() else len(line)]   # 자름 높이 아래는 버린다
            grp = grp[:, :len(line)]
            if len(line) < 2:
                continue
            idx = np.linspace(0, len(line) - 1, seg + 1).round().astype(int)
            line = line[idx]
            tan = np.gradient(line, axis=0)
            tan /= np.linalg.norm(tan, axis=1, keepdims=True) + 1e-12
            outw = line - ctr
            outw /= np.linalg.norm(outw, axis=1, keepdims=True) + 1e-12
            side = np.cross(tan, outw)
            side /= np.linalg.norm(side, axis=1, keepdims=True) + 1e-12
            if strand_cards(name, lod):                              # 088 T29: 중심선을 다발 바깥쪽(머리 중심 반대)으로 — 평균선은 바깥 실루엣보다 안쪽
                radial = ((grp[:, idx] - line[None]) * outw[None]).sum(axis=2)
                line = line + outw * np.clip(np.percentile(radial, 90, axis=0), 0.0, 0.006)[:, None]   # 바깥 90 백분위·≤ 6 mm (속심 − 4 mm 위 윤곽)
            spread = np.abs(((grp[:, idx] - line[None]) * side[None]).sum(axis=2))
            wdt = np.clip(np.percentile(spread, 80, axis=0) * 2.0, 0.003, CARD_W_MAX)
            wdt[-1] *= 0.6
            L = np.vstack([line - side * wdt[:, None] / 2, line + side * wdt[:, None] / 2])   # (2(seg+1), 3)
            cu0 = crect[0] + (crect[2] - crect[0]) * ((c * 37) % 5) / 5.0
            cu1 = cu0 + (crect[2] - crect[0]) / 5.0
            quads = []
            for k in range(seg):
                a, b2 = k, k + 1
                va = (crect[3] - (crect[3] - crect[1]) * a / seg)
                vb = (crect[3] - (crect[3] - crect[1]) * b2 / seg)
                quads.append(((a, b2, seg + 1 + b2), ((2 + cu0, va), (2 + cu0, vb), (2 + cu1, vb))))
                quads.append(((a, seg + 1 + b2, seg + 1 + a), ((2 + cu0, va), (2 + cu1, vb), (2 + cu1, va))))
            fixed = []
            for tri, uv in quads:                                     # 카드도 안쪽 법선 감김 (바깥에서 앞면이 보이게)
                a3, b3, c3 = L[tri[0]], L[tri[1]], L[tri[2]]
                if np.cross(b3 - a3, c3 - a3) @ ((a3 + b3 + c3) / 3 - ctr) > 0:
                    tri, uv = (tri[0], tri[2], tri[1]), (uv[0], uv[2], uv[1])
                fixed.append((tri, uv))
            cards.append((L, fixed))
        for L, quads in cards:
            HS.append(m, L, quads, "hair", R, tree, hd=np.column_stack([np.ones(len(L)), np.zeros(len(L))]), alld=alld,
                      vpart="card")
    return m
