"""눈썹·속눈썹 메시 (specs/088 FR-53, T28). Mindfront CC0 프록시(가닥 하나 = 정점 12개 작은 관)를 선수 기준 머리에 맞춘다.

- 클로즈업(`*_Closeup`): 원본 가닥 형상 그대로(감량 없음).
- LOD0(`*_LOD0`): 가닥 일부(LOD0_KEEP)를 골라 가닥마다 뿌리→끝 납작한 띠(삼각형 2개)로. 띠 폭은 원본 가닥 굵기 × LOD0_WIDEN으로
  덮이는 면적(정면 실루엣)을 원본과 맞춘다(checks.brow_lash가 정면 덮임 비율로 확인).
- LOD1·2: 메시 없음 — 피부 마스크(brow_mask.png) 방식 그대로. LOD0·클로즈업 피부 정점은 UV1.y = 1로 표시해 셰이더가 마스크를 끈다.

프록시 정점은 기준 정점에 선형이라 체형·얼굴 축·바탕 머리·표정·HideHead 델타를 같은 가중 합으로 옮긴다(mhassets.Proxy.delta).
띠 정점은 가닥 뿌리·끝 무리 평균이라 델타도 같은 평균. 클로즈업 조각은 HideHead를 접지 않는다(headlod와 같은 규칙).
색: 눈썹 = 머리카락 셸 결 타일(직모, 머리색·Kajiya-Kay — UV v = 가닥 방향), 속눈썹 = parts 눈 칸 동공(거의 검정, 입안 캡과 같은 점).
"""
import numpy as np

from . import body as B
from . import mhassets as MH
from . import mpfb_src as MS

BROWS = {"Brow_02": MH.EYEBROWS[0], "Brow_04": MH.EYEBROWS[1], "Brow_09": MH.EYEBROWS[2]}   # PlayerLook.BrowMeshes 순서 = Appearance.Eyebrow
LASH = "Lash"
LOD0_KEEP = 4          # LOD0: 가닥 4개 중 1개 [제안] — 2개 중 1개는 면적을 맞추면 띠 폭 0.05~0.1 mm(LOD0 거리에서 화소보다 가늘어 깜빡임)
# LOD0 띠 폭 = 원본 굵기 × 배율. 배율은 에셋마다 정면 덮임 면적이 원본과 같아지도록 맞춘다(_fit_widen, 시작값·허용 오차).
# T28: 고정 배율은 에셋마다 0.6~0.8배 차이(가닥 굵기 측정 = 관 단면 최대 폭이 실제 투영 폭보다 크다)라 맞춤으로 바꿨다
LOD0_WIDEN0 = 1.0
LOD0_AREA_TOL = 0.03
TIP_TAPER = 0.4        # 띠 끝 폭 / 뿌리 폭
BROW_UV_U0 = 10.0 + 0 * 100.0 + 40.0   # groom.bake 셸 UV: 10 + 결 타일(0 = straight) × 100 + 40
BROW_UV_K = 0.6        # 원본 UV(0~1) → 셸 연속 좌표 배율 (타일 반복 0.6회)
LASH_UV = B.MOUTH_UV_CAP

_CACHE = {}


def _strands(px):
    """프록시 obj 연결 성분 = 가닥 → [정점 인덱스 배열] (정렬, 결정론)."""
    if px.rel in _CACHE:
        return _CACHE[px.rel]
    n = len(px.refs)
    par = np.arange(n)

    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]
            a = par[a]
        return a
    for f in px.faces:
        r0 = find(f[0])
        for v in f[1:]:
            r = find(v)
            if r != r0:
                par[r] = r0
    roots = np.array([find(i) for i in range(n)])
    out = [np.flatnonzero(roots == r) for r in np.unique(roots)]
    out.sort(key=lambda a: int(a[0]))
    _CACHE[px.rel] = out
    return out


def _weights(n):
    W = np.zeros((n, len(B.S.NAMES)))
    W[:, MS.OUR_NAMES.index("Head")] = 1.0
    return W


def build(R, name, closeup):
    """→ body.Mesh (눈썹 `Brow_*` 또는 속눈썹 `Lash`)."""
    px = MH.Proxy(BROWS[name] if name in BROWS else MH.EYELASHES)
    P = px.fit(R.verts)
    alld = B._all_deltas(R)
    D = {k: px.delta(alld[k]) for k in sorted(alld)}
    if closeup:
        D["HideHead"] = np.zeros_like(P)
    m = B.Mesh()
    part = "brow" if name in BROWS else "lash"
    if closeup:
        T, TU = px.triangles()
        m.verts = P
        m.W = _weights(len(P))
        m.deltas = D
        m.vpart = [part] * len(P)
        for t, tu in zip(T, TU):
            if part == "brow":
                uv = tuple((BROW_UV_U0 + float(px.uv[i][0]) * BROW_UV_K, float(px.uv[i][1]) * BROW_UV_K) for i in tu)
            else:
                uv = (LASH_UV,) * 3
            m.faces.append((tuple(int(x) for x in t), B.BODY, uv, part, None))
    else:
        _ribbons(R, px, P, D, m, part, _fit_widen(R, name, px, P, D, part))
    m.hairdata = np.tile([1.0, 0.0], (len(m.verts), 1))
    return m


class _Tris:
    def __init__(self, verts, tris):
        self.verts = verts
        self.faces = [(tuple(t), None, None, None, None) for t in tris]


def _fit_widen(R, name, px, P, D, part):
    """LOD0 띠 폭 배율: 띠 정면 덮임 / 원본 정면 덮임 = 1 ± LOD0_AREA_TOL이 되도록 (면적은 폭에 거의 비례 — 겹침만큼 몇 번 고친다)."""
    key = ("widen", name)
    if key in _CACHE:
        return _CACHE[key]
    T, _ = px.triangles()
    target = front_area(_Tris(P, T))
    k = LOD0_WIDEN0
    for _ in range(8):
        m = B.Mesh()
        _ribbons(R, px, P, {}, m, part, k)
        ratio = front_area(m) / max(target, 1e-12)
        if abs(ratio - 1.0) <= LOD0_AREA_TOL:
            break
        k /= ratio
    _CACHE[key] = k
    return k


def _ribbons(R, px, P, D, m, part, widen):
    """가닥 → 띠 (뿌리 2정점·끝 2정점, 삼각형 2개). 뿌리 = 프록시 오프셋(피부에서 떨어진 거리)이 작은 끝."""
    ctr = R.verts[np.flatnonzero(R.weights[:, MS.OUR_NAMES.index("Head")] > 0.9)].mean(axis=0)
    offn = np.linalg.norm(px.off, axis=1)
    verts, Dl, faces = [], {k: [] for k in D}, []
    for si, s in enumerate(_strands(px)):
        if si % LOD0_KEEP:
            continue
        Q = P[s]
        c = Q.mean(axis=0)
        u, sv, vt = np.linalg.svd(Q - c, full_matrices=False)
        ax = vt[0]
        t = (Q - c) @ ax
        lo, hi = t <= np.percentile(t, 25), t >= np.percentile(t, 75)
        if offn[s][hi].mean() < offn[s][lo].mean():   # 뿌리를 lo 쪽으로
            lo, hi = hi, lo
            ax = -ax
        r, tip = Q[lo].mean(axis=0), Q[hi].mean(axis=0)
        nrm = r - ctr
        nrm /= np.linalg.norm(nrm) + 1e-12
        side = np.cross(tip - r, nrm)
        ln = np.linalg.norm(side)
        if ln < 1e-12:
            continue
        side /= ln
        thick = float(np.ptp((Q - c) @ vt[1])) if len(sv) > 1 else 0.0003
        w = max(thick, 2e-4) * widen * 0.5
        base = len(verts)
        verts += [r - side * w, r + side * w, tip + side * w * TIP_TAPER, tip - side * w * TIP_TAPER]
        for k in D:
            dr, dt = D[k][s][lo].mean(axis=0), D[k][s][hi].mean(axis=0)
            Dl[k] += [dr, dr, dt, dt]
        faces.append((base, base + 1, base + 2))
        faces.append((base, base + 2, base + 3))
    m.verts = np.array(verts)
    m.W = _weights(len(verts))
    m.deltas = {k: np.array(v) for k, v in Dl.items()}
    m.vpart = [part] * len(verts)
    for f in faces:
        if part == "brow":
            # UV v = 가닥 방향(뿌리 0 → 끝 1) — Kajiya-Kay 가닥 방향(바이탄젠트)
            vv = {0: 0.0, 1: 0.0, 2: 1.0, 3: 1.0}
            uv = tuple((BROW_UV_U0 + ((i - f[0]) % 4 in (1, 2)) * 0.05, vv[(i - f[0]) % 4] * BROW_UV_K) for i in f)
        else:
            uv = (LASH_UV,) * 3
        m.faces.append((f, B.BODY, uv, part, None))


def names():
    return list(BROWS) + [LASH]


def front_area(m, cell=1e-4):
    """정면(−z 방향에서 본 x·y) 투영 덮임 면적 (m², cell 격자) — LOD0 띠가 원본 실루엣을 유지하는지(checks.brow_lash)."""
    T = np.array([m.verts[list(f)][:, :2] for f, *_ in m.faces])
    lo = T.reshape(-1, 2).min(axis=0)
    n = np.ceil((T.reshape(-1, 2).max(axis=0) - lo) / cell).astype(int) + 2
    grid = np.zeros((n[1], n[0]), dtype=bool)
    for t in T:
        q = (t - lo) / cell
        x0, y0 = np.floor(q.min(axis=0)).astype(int)
        x1, y1 = np.ceil(q.max(axis=0)).astype(int)
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        p = np.stack([xs.ravel(), ys.ravel()], axis=1)
        a, b, c = q
        d1 = (p[:, 0] - b[0]) * (a[1] - b[1]) - (a[0] - b[0]) * (p[:, 1] - b[1])
        d2 = (p[:, 0] - c[0]) * (b[1] - c[1]) - (b[0] - c[0]) * (p[:, 1] - c[1])
        d3 = (p[:, 0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (p[:, 1] - a[1])
        inside = ~(((d1 < 0) | (d2 < 0) | (d3 < 0)) & ((d1 > 0) | (d2 > 0) | (d3 > 0)))
        pi = p[inside].astype(int)
        grid[pi[:, 1], pi[:, 0]] = True
    return float(grid.sum()) * cell * cell


INSIDE_MM = 1.0   # 피부 안 깊이 문턱 (mm)


def skin_depth(R, tree_tri, key_sets):
    """눈썹·속눈썹 원본 정점이 피부 안으로 INSIDE_MM 넘게 들어간 비율 — 키 조합(표정·눈썹 얼굴 축)마다, 쉬는 얼굴 대비.
    프록시는 기준 정점에 선형이라 키를 걸면 피부와 같이 움직인다 — 셰이프 키 전달 누락이면 여기서 박힌다(plan 위험)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    _, tri = tree_tri
    out = {}
    for nm in names():
        px = MH.Proxy(BROWS[nm] if nm in BROWS else MH.EYELASHES)
        worst, rest = 0.0, None
        for ks in key_sets:
            V = R.verts.copy()
            for k, w in ks.items():
                V = V + B._all_deltas(R)[k] * w
            t2 = BVHTree.FromPolygons([tuple(p) for p in V], [tuple(t) for t in tri], all_triangles=True)
            P = px.fit(V)
            ins = 0
            for p in P:
                loc, nrm, _, d = t2.find_nearest(Vector(tuple(p)))
                if loc is not None and d > INSIDE_MM * 1e-3 and (Vector(tuple(p)) - loc).dot(nrm) > 0:   # 선수 좌표는 반사 → 삼각형 감김 법선이 안쪽(body._normals)
                    ins += 1
            frac = ins / len(P)
            if rest is None:
                rest = frac
            worst = max(worst, frac)
        out[nm] = {"rest": round(rest, 4), "worst": round(worst, 4)}
    return out
