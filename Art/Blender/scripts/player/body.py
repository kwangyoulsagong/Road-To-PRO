"""선수 몸 LOD 메시 (specs/056 FR-01·10~13·50·60, 043 FR-10·11 대체).

순서 (LOD마다):
1. 기준 선수(mpfb_src.Reference, MPFB2 CC0 베이스 메시 + 체형 타깃, 043 레스트 포즈로 재정렬)의 몸 표면(`body` 그룹)을
   Blender Decimate(COLLAPSE, X 대칭)로 감량한다. 머리 정점에 가중치를 줘 머리 삼각형 몫을 지킨다(FR-60 머리 ≥ 2.0k).
2. 유니폼 경계(소매·칼라·셔츠 밑단·반바지 밑단·양말 윗단·축구화 목)를 평면으로 잘라 곧은 고리를 만든다(bisect).
3. 모든 정점을 기준 표면에 **무게중심 좌표로 묶는다**(최근접 삼각형). 가중치(MPFB game_engine → 043 22뼈)·체형/얼굴 델타는 묶음으로 옮긴다.
4. 면을 부위(피부·셔츠·소매·반바지·양말·축구화)로 나누고, 유니폼 면은 피부와 정점을 나눠 바깥으로 띄운 셸로 만든다.
   덮인 피부는 없으므로 피부가 유니폼을 뚫을 수 없다(FR-50). 경계에는 턱(두께 띠)을 붙인다.
5. UV: 피부 = MakeHuman UV(감량 때 보간), 유니폼 = 043 uv.py 영역(번호·이름 영역 불변, FR-51).

순수 numpy + bpy(감량·BVH). 같은 입력 → 같은 결과(정점 순서 = Blender 감량 결과 순서, 결정론).
"""
import math

import numpy as np

from . import mpfb_src as MS
from . import skeleton as S
from . import uv as U

BODY, KIT = 0, 1

# LOD 예산(몸 표면 삼각형, 머리 몫). 눈·머리카락·턱은 따로 더해진다 → checks.TRI_BUDGET 안 (FR-60).
LODS = {
    0: dict(tris=7000, head=2400),
    1: dict(tris=1650, head=430),
    2: dict(tris=480, head=110),
}

# 유니폼 부위별 (바깥 띄움 m, 매끄럽게 반복 수). 셔츠는 헐렁하게, 양말은 붙게, 축구화는 발가락을 지운다.
KIT_PARTS = {
    "top": (0.010, 5),
    "shorts": (0.008, 3),
    "sock": (0.0035, 2),
    "boot": (0.006, 6),
}
RIM = {"top": 0.006, "shorts": 0.005, "sock": 0.004, "boot": 0.003}  # 경계 턱 안쪽 깊이(띄움과 함께 두께가 된다)
# UV 부위 → 셸 묶음 (같은 묶음은 정점을 나누지 않는다: 셔츠 몸판·소매 사이 틈 없음)
GROUP = {"skin": "skin", "shirt": "top", "sleeve": "top", "shorts": "shorts", "sock": "sock", "boot": "boot"}

SHAPES = tuple(MS.BODY_MACROS)  # Thin, Muscular, Heavy

_REF = None


def reference():
    """기준 선수 (한 번 만든다). skeleton 모듈을 기준 관절로 맞춘다."""
    global _REF
    if _REF is None:
        _REF = MS.Reference()
        _smooth_weights(_REF)
        S.configure({n: (tuple(float(x) for x in h), tuple(float(x) for x in t)) for n, (h, t) in _REF.joints.items()})
        _REF.body_deltas = {k: _REF.macro_delta(v) for k, v in MS.BODY_MACROS.items()}
        _REF.extra_deltas = {}
        from . import face as F
        F.register(_REF)
    return _REF


WEIGHT_SMOOTH = 12  # 기준 가중치 평활 반복 (관절 주름 접힘 감소 — 실험: 0회 대비 뒤집힘 약 −45 %(8회))


def _smooth_weights(R):
    """MPFB game_engine 가중치를 몸 표면에서 라플라시안 평활 (재정렬 뒤 043 뼈 가중치에만, 정점 위치는 그대로)."""
    faces, _ = _body_faces(R)
    nb = _neighbors(R.n, faces)
    mv = np.zeros(R.n, dtype=bool)
    mv[R.groups["body"]] = True
    W = _smooth(R.weights, nb, mv, WEIGHT_SMOOTH, mu=None)
    R.weights = W / W.sum(axis=1, keepdims=True)


class Mesh:
    """verts (V,3) 선수 좌표, weights (V,22) 043 뼈 순서(mpfb_src.OUR_NAMES), vpart[i] 부위,
    faces[j] = (정점 인덱스 튜플, 머티리얼, 코너 UV 튜플, 부위, None), deltas[이름] = (V,3)."""

    def __init__(self):
        self.verts = None
        self.W = None
        self.vpart = []
        self.faces = []
        self.deltas = {}

    @property
    def triangles(self):
        return sum(len(f[0]) - 2 for f in self.faces)

    @property
    def weights(self):
        """rig.py 호환: 정점별 {뼈: 가중치} (상위 4본, 정규화는 rig가)."""
        out = []
        for row in self.W:
            idx = np.argsort(-row, kind="stable")[:4]
            out.append({MS.OUR_NAMES[i]: float(row[i]) for i in idx if row[i] > 1e-6})
        return out


# ---------------------------------------------------------------- 1. 감량

def _body_faces(R):
    body = np.zeros(R.n, dtype=bool)
    body[R.groups["body"]] = True
    faces, uvs = [], []
    for f, fu in zip(R.faces, R.face_uvs):
        if body[list(f)].all():
            faces.append(f)
            uvs.append(fu)
    return faces, uvs


def _head_weight(R):
    hi = MS.OUR_NAMES.index("Head")
    return R.weights[:, hi]


def _decimated(R, lod):
    """→ (정점 (V,3), 면 목록, 면별 코너 UV).
    Blender COLLAPSE의 정점 그룹 계수는 사실상 켜기/끄기(0 = 무시, > 0 = 그룹 밖 정점 보호)라서 두 단계로 나눈다:
    ① 머리(y > Head 관절 − 1 cm)만 줄여 머리 몫을 맞추고 ② 나머지만 줄여 전체를 맞춘다. 경계 고리는 두 단계 모두에서 한쪽만 움직인다."""
    import bpy
    cfg = LODS[lod]
    if lod > 0:
        # 단계 감량: 이전 LOD 결과에서 줄인다 (한 번에 3 % 까지 줄이면 손발이 먼저 사라진다)
        co, fl, uvs = _decimated_cached(R, lod - 1)
    else:
        faces, uvs = _body_faces(R)
        ids = sorted({v for f in faces for v in f})
        remap = {v: i for i, v in enumerate(ids)}
        co = np.array([R.verts[v] for v in ids])
        fl = [tuple(remap[v] for v in f) for f in faces]
    head_y = S.HEAD["Head"][1] - 0.01

    def tris(fs):
        return sum(len(f) - 2 for f in fs)

    def head_tris(c, fs):
        return sum(len(f) - 2 for f in fs if c[list(f), 1].mean() > head_y + 0.01)

    def stage(c, fs, fu, mask_head, target_total):
        me = bpy.data.meshes.new(f"_dec{lod}")
        me.from_pydata([tuple(p) for p in c], [], [list(f) for f in fs])
        uvl = me.uv_layers.new(name="UVMap")
        for p, u in zip(me.polygons, fu):
            for li, uv in zip(p.loop_indices, u):
                uvl.data[li].uv = uv
        me.update()
        obj = bpy.data.objects.new(f"_dec{lod}", me)
        bpy.context.scene.collection.objects.link(obj)
        vg = obj.vertex_groups.new(name="m")
        sel = [i for i in range(len(c)) if (c[i, 1] > head_y) == mask_head]
        vg.add(sel, 1.0, "REPLACE")
        mod = obj.modifiers.new("dec", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.use_symmetry = True
        mod.symmetry_axis = "X"
        mod.ratio = max(0.0, min(1.0, target_total / tris(fs)))
        mod.vertex_group = "m"
        mod.invert_vertex_group = False
        mod.vertex_group_factor = 1.0
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(dg)
        m2 = ev.to_mesh()
        c2 = np.array([tuple(v.co) for v in m2.vertices], dtype=np.float64)
        f2 = [tuple(p.vertices) for p in m2.polygons]
        ul = m2.uv_layers[0].data
        u2 = [tuple((float(ul[li].uv[0]), float(ul[li].uv[1])) for li in p.loop_indices) for p in m2.polygons]
        ev.to_mesh_clear()
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(me)
        return c2, f2, u2

    fu = list(uvs)
    h0 = head_tris(co, fl)
    co, fl, fu = stage(co, fl, fu, True, tris(fl) - h0 + cfg["head"])
    co, fl, fu = stage(co, fl, fu, False, cfg["tris"])
    return co, fl, fu


_DEC = {}
_LOD = [0]


def _decimated_cached(R, lod):
    if lod not in _DEC:
        _DEC[lod] = _decimated(R, lod)
    return _DEC[lod]


# ---------------------------------------------------------------- 2. 유니폼 경계 평면

def cut_planes():
    """[(이름, 점, 법선, 대상 뼈 집합)] 선수 좌표. 법선 쪽이 '위/바깥(몸통 쪽)'."""
    H, T = S.HEAD, S.TAIL
    knee_y = H["LeftLowerLeg"][1]
    hips_y = H["LeftUpperLeg"][1]
    out = [
        ("collar", (0.0, H["Neck"][1] - 0.035, 0.0), _unit((0.0, 1.0, -0.42)), {"Neck", "UpperChest", "Head", "LeftShoulder", "RightShoulder"}),
        ("hem", (0.0, hips_y - 0.005, 0.0), (0.0, 1.0, 0.0), {"Hips", "Spine", "Chest", "LeftUpperLeg", "RightUpperLeg"}),
    ]
    for n in ("Left", "Right"):
        a, b = np.array(H[n + "UpperArm"]), np.array(T[n + "UpperArm"])
        d = (b - a) / np.linalg.norm(b - a)
        out.append((f"sleeve{n[0]}", tuple(a + d * SLEEVE_T * np.linalg.norm(b - a)), tuple(-d),
                    {n + "UpperArm", n + "Shoulder", n + "LowerArm"}))
        out.append((f"shorts{n[0]}", (0.0, knee_y + SHORTS_ABOVE_KNEE, 0.0), (0.0, 1.0, 0.0), {n + "UpperLeg", n + "LowerLeg"}))
        out.append((f"sock{n[0]}", (0.0, knee_y - SOCK_BELOW_KNEE, 0.0), (0.0, 1.0, 0.0), {n + "LowerLeg", n + "UpperLeg"}))
        out.append((f"boot{n[0]}", (0.0, BOOT_Y, 0.0), (0.0, 1.0, 0.0), {n + "LowerLeg", n + "Foot", n + "Toes"}))
    return out


SLEEVE_T = 0.55            # 소매 끝 = 위팔 길이의 55 %
SHORTS_ABOVE_KNEE = 0.15   # 반바지 밑단 = 무릎 관절 위 15 cm
SOCK_BELOW_KNEE = 0.06     # 양말 윗단 = 무릎 관절 아래 6 cm
BOOT_Y = 0.105             # 축구화 목 높이


def _unit(v):
    v = np.array(v, dtype=np.float64)
    return tuple(v / np.linalg.norm(v))


def _dominant(W):
    return np.argmax(W, axis=1)


def _bisect(co, faces, fuv, R):
    """평면마다 해당 뼈 면만 잘라 고리를 만든다. → (co, faces, fuv)."""
    import bmesh
    import bpy
    bm = bmesh.new()
    vs = [bm.verts.new(tuple(p)) for p in co]
    bm.verts.ensure_lookup_table()
    uvl = bm.loops.layers.uv.new("UVMap")
    for f, fu in zip(faces, fuv):
        bf = bm.faces.new([vs[i] for i in f])
        for loop, u in zip(bf.loops, fu):
            loop[uvl].uv = u
    bm.faces.ensure_lookup_table()
    tree = _ref_tree(R)
    for name, pt, nrm, bones in cut_planes():
        bi = {MS.OUR_NAMES.index(b) for b in bones}
        geom = []
        for f in bm.faces:
            c = f.calc_center_median()
            w = _bind_point(R, tree, np.array(c))[1]
            if int(np.argmax(w)) in bi:
                geom.append(f)
        if not geom:
            continue
        edges = list({e for f in geom for e in f.edges})
        verts = list({v for f in geom for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=verts + edges + geom, dist=1e-5, plane_co=pt, plane_no=nrm)
    # 잘린 면은 n각형 → 삼각/사각으로 정리 (Unity·예산 계산 일관)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.verts.index_update()
    co2 = np.array([tuple(v.co) for v in bm.verts], dtype=np.float64)
    f2, u2 = [], []
    for f in bm.faces:
        f2.append(tuple(v.index for v in f.verts))
        u2.append(tuple((float(l[uvl].uv[0]), float(l[uvl].uv[1])) for l in f.loops))
    bm.free()
    return co2, f2, u2


# ---------------------------------------------------------------- 3. 묶음

_TREE = {}


def _ref_tree(R):
    if "body" in _TREE:
        return _TREE["body"]
    from mathutils.bvhtree import BVHTree
    faces, _ = _body_faces(R)
    tris = []
    for f in faces:
        for k in range(1, len(f) - 1):
            tris.append((f[0], f[k], f[k + 1]))
    tri = np.array(tris, dtype=np.int64)
    tree = BVHTree.FromPolygons([tuple(p) for p in R.verts], [tuple(t) for t in tri], all_triangles=True)
    _TREE["body"] = (tree, tri)
    return _TREE["body"]


def _bary(p, a, b, c):
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
    d20, d21 = v2 @ v0, v2 @ v1
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-18:
        return np.array([1.0, 0.0, 0.0])
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    u = 1.0 - v - w
    bc = np.clip(np.array([u, v, w]), 0.0, 1.0)
    return bc / bc.sum()


def _bind_point(R, tree_tri, p):
    tree, tri = tree_tri
    loc, nrm, idx, dist = tree.find_nearest(tuple(p))
    t = tri[idx]
    bc = _bary(np.array(loc), R.verts[t[0]], R.verts[t[1]], R.verts[t[2]])
    w = bc[0] * R.weights[t[0]] + bc[1] * R.weights[t[1]] + bc[2] * R.weights[t[2]]
    return (t, bc), w


class Binding:
    """정점마다 (기준 삼각형 3정점, 무게중심 좌표). 기준 배열 A (N,k) → 묶인 배열 (V,k)."""

    def __init__(self, R, co):
        tt = _ref_tree(R)
        self.tri = np.zeros((len(co), 3), dtype=np.int64)
        self.bc = np.zeros((len(co), 3))
        for i, p in enumerate(co):
            (t, bc), _ = _bind_point(R, tt, p)
            self.tri[i] = t
            self.bc[i] = bc

    def apply(self, A):
        return (A[self.tri] * self.bc[:, :, None]).sum(axis=1)


# ---------------------------------------------------------------- 4. 부위 · 유니폼 셸

def _side(p, plane):
    _, pt, nrm, _ = plane
    return float(np.dot(np.array(p) - np.array(pt), np.array(nrm)))


def classify(p, w):
    """정점·면 중심 위치 p와 가중치 w(22) → 부위 이름."""
    planes = {pl[0]: pl for pl in cut_planes()}
    b = MS.OUR_NAMES[int(np.argmax(w))]
    if b in ("Head",):
        return "skin"
    if b in ("Neck", "UpperChest", "Chest", "Spine", "Hips") or b.endswith("Shoulder"):
        if _side(p, planes["collar"]) > 0:
            return "skin"
        return "shirt" if _side(p, planes["hem"]) > 0 else "shorts"
    side = b[0]
    if b.endswith("UpperArm"):
        return "sleeve" if _side(p, planes["sleeve" + side]) > 0 else "skin"
    if b.endswith("LowerArm") or b.endswith("Hand"):
        return "skin"
    if b.endswith("UpperLeg"):
        if _side(p, planes["hem"]) > 0:
            return "shirt"
        return "shorts" if _side(p, planes["shorts" + side]) > 0 else "skin"
    if b.endswith("LowerLeg"):
        if _side(p, planes["sock" + side]) > 0:
            return "shorts" if _side(p, planes["shorts" + side]) > 0 else "skin"
        return "sock" if _side(p, planes["boot" + side]) > 0 else "boot"
    return "boot"  # Foot, Toes


def _boot_hull(P, parts, border):
    """축구화: 발(발가락 사이 골)을 발마다 볼록 껍질에 투영 → 신발 모양. 경계(양말과 닿는 고리)는 그대로. 정점 위치만 바꾼다(델타·가중치 유지)."""
    import bmesh
    from mathutils.bvhtree import BVHTree
    for sgn in (-1.0, 1.0):
        idx = [i for i in range(len(P)) if parts[i] == "boot" and P[i, 0] * sgn > 0]
        if len(idx) < 8:
            continue
        bm = bmesh.new()
        for i in idx:
            bm.verts.new(tuple(P[i]))
        res = bmesh.ops.convex_hull(bm, input=list(bm.verts))
        for g in res.get("geom_interior", []):
            if isinstance(g, bmesh.types.BMVert):
                bm.verts.remove(g)
        bm.verts.ensure_lookup_table()
        tree = BVHTree.FromBMesh(bm)
        for i in idx:
            if border[i]:
                continue
            loc = tree.find_nearest(tuple(P[i]))[0]
            if loc is not None:
                P[i] = np.array(loc)
        bm.free()


def _neighbors(nv, faces):
    nb = [set() for _ in range(nv)]
    for f in faces:
        for i in range(len(f)):
            a, b = f[i], f[(i + 1) % len(f)]
            nb[a].add(b)
            nb[b].add(a)
    return [sorted(s) for s in nb]


def _smooth(A, nb, movable, iters, lam=0.5, mu=-0.53):
    """Taubin 평활(λ 수축 → μ 팽창 번갈아, 부피가 줄지 않는다). 선형 → 델타에도 같은 연산. movable이 아닌 정점은 고정. A (V,k) 한 번에."""
    A = A.copy()
    ei = np.array([i for i in range(len(nb)) for _ in nb[i]], dtype=np.int64)
    ej = np.array([j for i in range(len(nb)) for j in nb[i]], dtype=np.int64)
    deg = np.array([len(x) for x in nb], dtype=np.float64)
    mv = np.asarray(movable) & (deg > 0)
    deg[deg == 0] = 1.0
    for it in range(iters):
        f = lam if it % 2 == 0 or mu is None else mu
        sums = np.zeros_like(A)
        np.add.at(sums, ei, A[ej])
        mean = sums / deg[:, None]
        A[mv] = A[mv] * (1 - f) + mean[mv] * f
    return A


def _normals(co, faces):
    n = np.zeros_like(co)
    for f in faces:
        p = co[list(f)]
        fn = np.zeros(3)
        for k in range(1, len(f) - 1):
            fn += np.cross(p[k] - p[0], p[k + 1] - p[0])
        for v in f:
            n[v] += fn
    ln = np.linalg.norm(n, axis=1, keepdims=True)
    ln[ln < 1e-12] = 1.0
    return -n / ln  # 선수 좌표는 반사 변환 → MPFB 감김 기준 법선을 뒤집는다 (face.vertex_normals와 같은 규칙)


def build(lod):
    R = reference()
    _LOD[0] = lod
    co, faces, fuv = _decimated_cached(R, lod)
    co, faces, fuv = _bisect(co, faces, fuv, R)
    bind = Binding(R, co)
    pos = bind.apply(R.verts)
    W = bind.apply(R.weights)
    W /= W.sum(axis=1, keepdims=True)
    deltas = {k: bind.apply(d) for k, d in sorted(R.body_deltas.items())}
    for k, d in sorted(R.extra_deltas.items()):
        deltas[k] = bind.apply(d)
    # 면 부위
    fpart = []
    for f in faces:
        c = pos[list(f)].mean(axis=0)
        fpart.append(classify(c, W[list(f)].mean(axis=0)))
    m = _assemble(pos, W, faces, fuv, fpart, deltas)
    _append_eyes(R, m, lod)
    return m


def _subset_tree(R, faces):
    from mathutils.bvhtree import BVHTree
    tris = []
    for f in faces:
        for k in range(1, len(f) - 1):
            tris.append((f[0], f[k], f[k + 1]))
    tri = np.array(tris, dtype=np.int64)
    return BVHTree.FromPolygons([tuple(p) for p in R.verts], [tuple(t) for t in tri], all_triangles=True), tri


def _decimate_faces(co, faces, target, uvs=None):
    """작은 조각 감량 (눈·머리카락·클로즈업 머리). uvs(면별 코너 UV)를 주면 보간된 UV도 돌려준다."""
    import bpy
    me = bpy.data.meshes.new("_part")
    me.from_pydata([tuple(p) for p in co], [], [list(f) for f in faces])
    if uvs is not None:
        uvl = me.uv_layers.new(name="UVMap")
        for p, u in zip(me.polygons, uvs):
            for li, uv in zip(p.loop_indices, u):
                uvl.data[li].uv = uv
    me.update()
    obj = bpy.data.objects.new("_part", me)
    bpy.context.scene.collection.objects.link(obj)
    mod = obj.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.use_symmetry = True
    mod.symmetry_axis = "X"
    mod.ratio = min(1.0, target / sum(len(f) - 2 for f in faces))
    bpy.context.view_layer.update()
    ev = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    m2 = ev.to_mesh()
    c2 = np.array([tuple(v.co) for v in m2.vertices], dtype=np.float64)
    f2 = [tuple(p.vertices) for p in m2.polygons]
    u2 = None
    if uvs is not None:
        ul = m2.uv_layers[0].data
        u2 = [tuple((float(ul[li].uv[0]), float(ul[li].uv[1])) for li in p.loop_indices) for p in m2.polygons]
    ev.to_mesh_clear()
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.meshes.remove(me)
    return (c2, f2) if uvs is None else (c2, f2, u2)


def append_part(m, P, W, D, faces, mat, uvs, part):
    """메시 조각을 m 뒤에 붙인다 (델타 키는 m과 같아야 한다)."""
    off = len(m.verts)
    m.verts = np.vstack([m.verts, P])
    m.W = np.vstack([m.W, W])
    for k in m.deltas:
        m.deltas[k] = np.vstack([m.deltas[k], D[k]])
    m.vpart += [part] * len(P)
    for f, u in zip(faces, uvs):
        m.faces.append((tuple(off + i for i in f), mat, tuple(u), part, None))


def _append_eyes(R, m, lod):
    from . import face as F
    ef = F.eye_geometry(R)
    for side in F.EYE_GROUPS:
        sset = set(R.groups[side])
        faces = [f for f in ef if f[0] in sset]
        ids = sorted({v for f in faces for v in f})
        remap = {v: i for i, v in enumerate(ids)}
        co = R.verts[ids]
        fl = [tuple(remap[v] for v in f) for f in faces]
        if F.EYE_TRIS[lod]:
            co, fl = _decimate_faces(co, fl, F.EYE_TRIS[lod])
            tree = _subset_tree(R, faces)
            b = Binding.__new__(Binding)
            b.tri = np.zeros((len(co), 3), dtype=np.int64)
            b.bc = np.zeros((len(co), 3))
            for i, p in enumerate(co):
                (t, bc), _ = _bind_point(R, tree, p)
                b.tri[i], b.bc[i] = t, bc
            P = b.apply(R.verts)
            W = b.apply(R.weights)
            D = {k: b.apply(_all_deltas(R)[k]) for k in m.deltas}
        else:
            P = R.verts[ids].copy()
            W = R.weights[ids].copy()
            D = {k: _all_deltas(R)[k][ids] for k in m.deltas}
        hi = MS.OUR_NAMES.index("Head")
        W = np.zeros_like(W)
        W[:, hi] = 1.0  # 안구는 머리에 고정
        uvs = [tuple(F.eye_uv(R, P[i]) for i in f) for f in fl]
        append_part(m, P, W, D, fl, BODY, uvs, "eye")


def _all_deltas(R):
    d = dict(R.body_deltas)
    d.update(R.extra_deltas)
    return d


def _assemble(pos, W, faces, fuv, fpart, deltas):
    """피부/유니폼 정점 나누기 + 셸 띄움 + 턱 + UV."""
    nv = len(pos)
    # 정점마다 쓰는 부위 목록 → (원래 정점, 부위)별 새 정점
    key_of = {}
    new_src, new_part = [], []

    def vid(v, part):
        k = (v, part)
        if k not in key_of:
            key_of[k] = len(new_src)
            new_src.append(v)
            new_part.append(part)
        return key_of[k]

    nf = []
    for f, part in zip(faces, fpart):
        nf.append(tuple(vid(v, GROUP[part]) for v in f))
    face_part = list(fpart)
    N0 = _normals(pos, faces)
    src = np.array(new_src)
    P = pos[src].copy()
    Wn = W[src].copy()
    D = {k: d[src].copy() for k, d in deltas.items()}
    parts = list(new_part)
    nb = _neighbors(len(P), nf)
    # 경계(다른 부위와 원래 정점을 공유) 정점은 평활에서 고정 → 피부와 맞닿는 고리가 움직이지 않는다
    shared = {}
    for (v, part), i in key_of.items():
        shared.setdefault(v, []).append(i)
    border = np.zeros(len(P), dtype=bool)
    for v, lst in shared.items():
        if len(lst) > 1:
            border[lst] = True
    keys = sorted(D)
    for part, (off, iters) in KIT_PARTS.items():
        mv = np.array([parts[i] == part and not border[i] for i in range(len(P))])
        if not mv.any():
            continue
        sel = np.array([parts[i] == part for i in range(len(P))])
        A = np.hstack([P] + [D[k] for k in keys])
        A2 = _smooth(A, nb, mv, iters)
        P[sel] = A2[sel, :3]
        for j, k in enumerate(keys):
            D[k][sel] = A2[sel, 3 + 3 * j:6 + 3 * j]
    N = N0[src]
    _boot_hull(P, parts, border)
    base = P.copy()
    for i in range(len(P)):
        part = parts[i]
        if part in KIT_PARTS and _LOD[0] < 2:
            P[i] = P[i] + N[i] * KIT_PARTS[part][0]  # LOD2는 띄우지 않는다(턱이 없으니 틈이 생기지 않게)
    # 축구화 바닥은 y ≥ 0 (접지)
    for i in range(len(P)):
        if parts[i] == "boot" and P[i, 1] < 0.0:
            P[i, 1] = 0.0
        if parts[i] == "boot" and P[i, 1] < 0.005:
            for k in D:  # 체형·얼굴 키가 바닥 아래로 밀지 않게 (극단 체형 접지, AC-02)
                D[k][i, 1] = max(D[k][i, 1], -P[i, 1])
    # 턱: 유니폼 경계 변 → 바깥 셸 고리에서 안쪽(피부 높이 − 깊이)으로 띠
    m = Mesh()
    rim_faces = []
    edge_faces = {}
    for fi, f in enumerate(nf):
        for k in range(len(f)):
            a, b = f[k], f[(k + 1) % len(f)]
            edge_faces.setdefault((min(new_src[a], new_src[b]), max(new_src[a], new_src[b])), []).append((fi, a, b))
    extra_P, extra_W, extra_D, extra_part = [], [], {k: [] for k in D}, []
    rim_of = {}

    def rim_vert(i, depth):
        if i in rim_of:
            return rim_of[i]
        j = len(P) + len(extra_P)
        extra_P.append(base[i] - N[i] * depth)
        extra_W.append(Wn[i])
        for k in D:
            extra_D[k].append(D[k][i])
        extra_part.append(parts[i])
        rim_of[i] = j
        return j

    for key in sorted(edge_faces):
        if _LOD[0] >= 2:
            break  # LOD2(원거리)는 턱을 생략한다 — 경계는 정점 공유 없이 닿아 있다(같은 묶음 위치)
        lst = edge_faces[key]
        if len(lst) != 2:
            continue
        (f0, a0, b0), (f1, a1, b1) = lst
        p0, p1 = parts[a0], parts[a1]
        if p0 == p1:
            continue
        # 띄움이 큰 쪽(바깥)에서 턱을 내린다
        o0 = KIT_PARTS.get(p0, (0.0, 0))[0]
        o1 = KIT_PARTS.get(p1, (0.0, 0))[0]
        if abs(o0 - o1) < 1e-9 and "skin" not in (p0, p1):
            continue
        if o0 >= o1:
            oa, ob, part = a0, b0, p0
        else:
            oa, ob, part = a1, b1, p1
        depth = RIM.get(part, 0.003)
        ra, rb = rim_vert(oa, depth), rim_vert(ob, depth)
        # 면 방향: 바깥 면의 변 a→b 와 반대로 감아 바깥을 향하게. UV 부위는 바깥 면의 부위
        rim_faces.append(((ob, oa, ra, rb), face_part[f0] if part == parts[a0] else face_part[f1]))
    if extra_P:
        P = np.vstack([P, np.array(extra_P)])
        Wn = np.vstack([Wn, np.array(extra_W)])
        for k in D:
            D[k] = np.vstack([D[k], np.array(extra_D[k])])
        parts += extra_part
    # UV · 머티리얼
    for fi, f in enumerate(nf):
        part = face_part[fi]
        if part == "skin":
            m.faces.append((f, BODY, tuple(fuv[fi]), "skin", None))
        else:
            m.faces.append((f, KIT, kit_uvs(P, f, part), part, None))
    for f, part in rim_faces:
        uvs = kit_uvs(P, f, part)
        m.faces.append((f, KIT, uvs, part + "_rim", None))
    m.verts = P
    m.W = Wn
    m.vpart = parts
    m.deltas = D
    return m


# ---------------------------------------------------------------- 5. 유니폼 UV (043 영역)

def _angle(dx, dz):
    """0 = +X, 0.25 = 정면(+Z), 0.5 = −X, 0.75 = 등 (043 uv.torso_shirt 규칙)."""
    a = math.atan2(dz, dx) / (2 * math.pi)
    return a % 1.0


def _unwrap(fs):
    c = fs[0]
    out = []
    for f in fs:
        while f - c > 0.5:
            f -= 1.0
        while f - c < -0.5:
            f += 1.0
        out.append(f)
    return out


def _limb_frame(bone):
    h, t = np.array(S.HEAD[bone]), np.array(S.TAIL[bone])
    d = (t - h) / np.linalg.norm(t - h)
    ref = np.array([0.0, 0.0, 1.0]) if abs(d[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    x = np.cross(ref, d)
    x /= np.linalg.norm(x)
    z = np.cross(d, x)
    return h, d, x, z, float(np.linalg.norm(t - h))


def kit_uvs(P, f, part):
    pts = P[list(f)]
    c = pts.mean(axis=0)
    base = part.replace("_rim", "")
    side = -1 if c[0] < 0 else 1
    sn = "Left" if side < 0 else "Right"
    if base == "shirt":
        cx = 0.0
        cz = S.HEAD["Chest"][2]
        fs = _unwrap([_angle(p[0] - cx, p[2] - cz) for p in pts])
        fc = sum(fs) / len(fs)
        out = []
        for p, fv in zip(pts, fs):
            if fc % 1.0 <= 0.5:
                fl = min(max(fv - math.floor(fc), 0.0), 0.5)
                out.append(U.torso_shirt(fl, p[1]))
            else:
                fl = min(max(fv - math.floor(fc), 0.5), 1.0)
                out.append(U.torso_shirt(fl, p[1]))
        return tuple(out)
    if base == "sleeve":
        h, d, x, z, L = _limb_frame(sn + "UpperArm")
        fs = _unwrap([_angle(float((p - h) @ x), float((p - h) @ z)) for p in pts])
        return tuple(U.sleeve(side, min(max(fv - math.floor(fs[0]), 0.0), 1.0), float((p - h) @ d) / (L * SLEEVE_T))
                     for p, fv in zip(pts, fs))
    if base == "shorts":
        y_hem = S.HEAD[sn + "LowerLeg"][1] + SHORTS_ABOVE_KNEE
        crotch = S.HEAD[sn + "UpperLeg"][1] - 0.08
        if c[1] > crotch:
            fs = _unwrap([_angle(p[0], p[2]) for p in pts])
            return tuple(U.torso_shorts(min(max(fv - math.floor(fs[0]), 0.0), 1.0), p[1]) for p, fv in zip(pts, fs))
        h, d, x, z, L = _limb_frame(sn + "UpperLeg")
        fs = _unwrap([_angle(float((p - h) @ x), float((p - h) @ z)) for p in pts])
        return tuple(U.shorts_leg(side, min(max(fv - math.floor(fs[0]), 0.0), 1.0), (p[1] - y_hem) / (crotch - y_hem))
                     for p, fv in zip(pts, fs))
    if base == "sock":
        top = S.HEAD[sn + "LowerLeg"][1] - SOCK_BELOW_KNEE
        h, d, x, z, L = _limb_frame(sn + "LowerLeg")
        fs = _unwrap([_angle(float((p - h) @ x), float((p - h) @ z)) for p in pts])
        return tuple(U.sock(side, min(max(fv - math.floor(fs[0]), 0.0), 1.0), (p[1] - BOOT_Y) / (top - BOOT_Y))
                     for p, fv in zip(pts, fs))
    # boot
    ax = np.array(S.HEAD[sn + "Foot"])
    fs = _unwrap([_angle(p[0] - ax[0], p[2] - ax[2] - 0.05) for p in pts])
    return tuple(U.boot(min(max(fv - math.floor(fs[0]), 0.0), 1.0), p[1] / BOOT_Y) for p, fv in zip(pts, fs))


def shape_keys(lod, m=None):
    """{키 이름: 정점 배열} — rig.py 호환 (기본 + 델타)."""
    m = m or build(lod)
    return {k: m.verts + d for k, d in m.deltas.items()}


# 자기 관통 검사 대상 (AC-03, checks.extremes): 서로 붙어 있지 않은 부위 쌍은 checks가 정점 그룹으로 정한다.
CHECK_PAIRS = []
