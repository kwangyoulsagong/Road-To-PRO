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
    0: dict(tris=6900, head=2400),  # 057: 칼라 고리 정점 추가(약 +110) 상쇄로 7,000 → 6,900
    1: dict(tris=1650, head=430),
    2: dict(tris=480, head=110),
}
# 088 FR-52 (T27): LOD0는 위 감량 몸(칼라·체형·관통·정장 검사 기준 토폴로지 그대로)에서 목 이음 평면 위를 원본 MPFB 머리로 바꾼다.
# 평면은 앞이 낮다(턱 밑·목젖 앞을 지나 턱·입은 모두 원본 쪽, 뒤는 목덜미) — 칼라 평면(Neck − 3.5 cm)과 2 cm 넘게 떨어진다.
HEAD_SEAM_UP = 0.03       # 이음 평면 기준점: Neck 관절 위 (m) — 0.021은 극단 체형에서 칼라 앞 셔츠 2면 뒤집힘(이음 면이 칼라 띠와 가까움)
HEAD_SEAM_TILT = 0.36     # 법선 (0, 1, tilt) — 앞(+z)으로 갈수록 낮다

# 유니폼 부위별 (바깥 띄움 m, 매끄럽게 반복 수). 셔츠는 헐렁하게, 양말은 붙게, 축구화는 발가락을 지운다.
KIT_PARTS = {
    "top": (0.010, 5),
    "shorts": (0.008, 3),
    "sock": (0.0035, 2),
    "boot": (0.006, 6),
}
RIM_SPLIT = 0.0002  # 턱 면 바깥 정점을 셸 정점에서 떼는 거리 (m, 057)
RIM = {"top": 0.006, "shorts": 0.005, "sock": 0.004, "boot": 0.003}  # 경계 턱 안쪽 깊이(띄움과 함께 두께가 된다)
# UV 부위 → 셸 묶음 (같은 묶음은 정점을 나누지 않는다: 셔츠 몸판·소매 사이 틈 없음)
GROUP = {"skin": "skin", "shirt": "top", "sleeve": "top", "shorts": "shorts", "sock": "sock", "boot": "boot"}

SHAPES = tuple(MS.BODY_MACROS) + tuple(MS.BODY_TARGETS)  # Thin, Muscular, Heavy + 062 Broad, Limb

_REF = None

# 062 FR-27 정장 모드 (suit.py가 켠다): 같은 감량·바인딩·셸 경로에 절단 평면·부위만 바꾼다.
# 부위 이름은 유니폼 것을 재사용한다 — shirt = 재킷 몸판, sleeve = 재킷 소매(손목까지), shorts = 바지(발목까지), boot = 구두.
# 양말·밑깔개·키트 보조 뼈 없음(천 시뮬 없음, FR-21 원칙). 셔츠 깃·V존·소맷부리는 정장 마스크(textures.suit_mask) 색 영역.
OUTFIT = ["kit"]
SUIT_HEM_BELOW_HIPS = 0.10   # 재킷 밑단 = 고관절 높이 − 10 cm (엉덩이를 덮는 길이, 손이 닿는 허벅지는 덜 덮게)
SUIT_SLEEVE_T = 0.90         # 재킷 소매 끝 = 아래팔 길이의 90 % (손목 바로 위)
SUIT_TROUSER_Y = 0.105       # 바지 밑단 = 축구화 목과 같은 높이 (0.095는 발 뼈 면과 섞여 고리가 계단, T32)


def suit_mode():
    return OUTFIT[0] == "suit"


# 정장 셸 (띄움 m, 평활 반복): 바지는 양말처럼 붙인다 — 허벅지·정강이에 손이 스치는 클립에서 8 mm 셸은 손가락이 닿는다(T32)
SUIT_PARTS = {"top": (0.010, 5), "shorts": (0.0035, 2), "boot": (0.006, 6)}


def kit_parts():
    return SUIT_PARTS if suit_mode() else KIT_PARTS


def reference():
    """기준 선수 (한 번 만든다). skeleton 모듈을 기준 관절로 맞춘다."""
    global _REF
    if _REF is None:
        _REF = MS.Reference()
        _smooth_weights(_REF)
        _base_radial(_REF)  # 062 FR-03 바탕 팔 두께 보정
        from . import face as F
        _REF.verts = _REF.verts + F.proto_head(_REF)[0]   # 088 FR-51 (T26): 머리·목 = 시안 체형 머리 모양 (몸은 062)
        S.configure({n: (tuple(float(x) for x in h), tuple(float(x) for x in t)) for n, (h, t) in _REF.joints.items()})
        _REF.body_deltas = {k: _REF.macro_delta(v) for k, v in MS.BODY_MACROS.items()}
        _REF.body_deltas.update({k: _REF.target_delta(v) * MS.BODY_TARGET_GAIN.get(k, 1.0)
                                 for k, v in MS.BODY_TARGETS.items()})  # 062 FR-01
        # Broad(어깨·가슴)가 위팔까지 굵혀 극단 체형 위팔/키가 0.24를 넘는다 → 팔 가중치만큼 줄인다 (어깨 폭은 몸통·어깨 정점이 낸다)
        arm = np.zeros(_REF.n)
        for s_ in ("Left", "Right"):
            h, t = (np.array(x) for x in _REF.joints[s_ + "UpperArm"])
            ln = float(np.linalg.norm(t - h))
            tt = ((_REF.verts - h) @ ((t - h) / ln)) / ln
            fade = np.clip((tt - 0.30) / 0.15, 0.0, 1.0)  # 삼각근(위팔 위 30 %)은 남기고 이두 높이부터 뺀다
            arm += _REF.weights[:, MS.OUR_NAMES.index(s_ + "UpperArm")] * fade
            arm += _REF.weights[:, MS.OUR_NAMES.index(s_ + "LowerArm")] + _REF.weights[:, MS.OUR_NAMES.index(s_ + "Hand")]
            arm += sum(_REF.weights[:, j] for j in range(MS.CORE_BONES, len(MS.OUR_NAMES)) if MS.OUR_NAMES[j].startswith(s_))
        _REF.body_deltas["Broad"] = _REF.body_deltas["Broad"] * (1.0 - np.clip(arm, 0.0, 1.0))[:, None]
        _REF.extra_deltas = {}
        from . import face as F
        F.register(_REF)
    return _REF


def _base_radial(R):
    """062 FR-03: 팔 뼈 축에서 수직 방향으로 반경을 배율만큼 넓힌다. 이동량 = (배율 − 1) × 축까지 거리 × 그 뼈 가중치
    (관절 쪽은 가중치가 줄어 자연히 감쇠). 몸 정점만, 결정론(정점 순서 연산)."""
    body = np.zeros(R.n, dtype=bool)
    body[R.groups["body"]] = True
    for bone, k in sorted(MS.BASE_RADIAL.items()):
        for side in ("Left", "Right"):
            name = side + bone
            h, t = (np.array(x) for x in R.joints[name])
            ax = (t - h) / np.linalg.norm(t - h)
            w = R.weights[:, MS.OUR_NAMES.index(name)] * body
            P = R.verts - h
            if bone == "UpperArm":  # 어깨·칼라 쪽(위팔 위 20 %)은 건드리지 않는다 — 칼라 고리·어깨 윤곽 유지
                ln = float(np.linalg.norm(t - h))
                w = w * np.clip(((P @ ax) / ln - 0.2) / 0.2, 0.0, 1.0)
            radial = P - np.outer(P @ ax, ax)
            R.verts = R.verts + radial * ((k - 1.0) * w)[:, None]


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
        self.under = []   # 062 T17a 밑깔개 면 인덱스

    @property
    def triangles(self):
        return sum(len(f[0]) - 2 for f in self.faces)

    @property
    def weights(self):
        """rig.py 호환: 정점별 {뼈: 가중치} (상위 4본, 정규화는 rig가)."""
        out = []
        for row in self.W:
            idx = np.argsort(-row, kind="stable")[:4]
            out.append({S.NAMES[i]: float(row[i]) for i in idx if row[i] > 1e-6})  # 열 = S.NAMES 앞부분 (손가락까지 = OUR_NAMES, 몸 LOD는 + 보조 뼈)
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
        if lod == 1:
            fl, uvs = _drop_mouth_inside(R, co, fl, uvs)   # 088 T30a (LOD2는 LOD1에서 이어받는다)
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


def _head_seam():
    return (np.array((0.0, S.HEAD["Neck"][1] + HEAD_SEAM_UP, 0.0)), np.array(_unit((0.0, 1.0, HEAD_SEAM_TILT))))


def _with_original_head(R, co, fl, fu):
    """088 FR-52 (T27): 감량 몸(co, fl, fu)에서 이음 평면에 걸치거나 위인 면을 빼고, 원본 몸 메시를 평면으로 잘라 위(머리·목 윗부분)를 붙인다.
    감량 쪽 들쭉날쭉한 위 경계와 원본 쪽 평면 고리를 띠로 잇는다(bridge — 같은 평면 두 고리를 이으면 폭 0 띠라 계단·구멍이 보였다).
    띠 면 방향은 이웃 원래 면에 맞추고, UV는 경계 변을 공유하는 원래 면의 코너 UV를 쓴다. 정점은 build()에서 기준 표면에 다시 묶인다."""
    import bmesh
    pt, nrm = _head_seam()
    faces, uvs = _body_faces(R)
    ids = sorted({v for f in faces for v in f})
    remap = {v: i for i, v in enumerate(ids)}
    co_o = np.array([R.verts[v] for v in ids])
    fl_o = [tuple(remap[v] for v in f) for f in faces]
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    tag = bm.faces.layers.int.new("part")

    def add(c, fs, us, k):
        vs = [bm.verts.new(tuple(p)) for p in c]
        new = []
        for f, u in zip(fs, us):
            bf = bm.faces.new([vs[i] for i in f])
            bf[tag] = k
            for loop, uv in zip(bf.loops, u):
                loop[uvl].uv = uv
            new.append(bf)
        return vs, new

    def above(v):  # BMVert 또는 좌표
        return float(np.dot(np.array(getattr(v, "co", v)) - pt, nrm))

    # 감량 몸: 평면에 걸치거나 위인 면을 통째로 뺀다 → 위 경계는 평면 아래 들쭉날쭉한 고리(폭 0 띠 없이 이을 틈)
    vs0, f0 = add(co, fl, fu, 0)
    bmesh.ops.delete(bm, geom=[f for f in f0 if max(above(v) for v in f.verts) > 0], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in vs0 if v.is_valid and not v.link_faces], context="VERTS")
    # 원본 머리: 평면으로 잘라 위만
    vs1, f1 = add(co_o, fl_o, uvs, 1)
    edges = list({e for f in f1 for e in f.edges})
    bmesh.ops.bisect_plane(bm, geom=vs1 + edges + f1, dist=1e-6, plane_co=pt, plane_no=nrm)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[tag] == 1 and above(f.calc_center_median()) < 0], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    ring = {0: [], 1: []}
    for e in bm.edges:
        if not e.is_boundary:
            continue
        k = e.link_faces[0][tag]
        if k == 0 or all(abs(above(v)) < 1e-5 for v in e.verts):
            ring[k].append(e)
    bm.verts.index_update()
    loops = {k: chain_loops(sorted((min(e.verts[0].index, e.verts[1].index), max(e.verts[0].index, e.verts[1].index)) for e in es))
             for k, es in ring.items()}
    assert all(len(v) == 1 and v[0][1] for v in loops.values()), f"목 이음 고리가 하나씩이 아니다: {[len(v) for v in loops.values()]}"
    old = set(bm.faces)
    res = bmesh.ops.bridge_loops(bm, edges=ring[0] + ring[1])
    seam = [f for f in res["faces"] if f not in old]
    flip = []
    for f in seam:  # 면 방향: 원래 면과 공유하는 변을 반대 방향으로 지나야 한다
        for i, loop in enumerate(f.loops):
            e = loop.edge
            g = next((g for g in e.link_faces if g in old), None)
            if g is not None:
                gl = next(l for l in g.loops if l.edge == e)
                if gl.vert == loop.vert:
                    flip.append(f)
                break
    bmesh.ops.reverse_faces(bm, faces=flip)
    for f in seam:  # UV: 경계 변을 공유하는 원래 면의 코너 UV, 경계 변이 없는 꼭짓점은 같은 면 다른 꼭짓점 UV에 가장 가까운 후보
        got = {}
        for e in f.edges:
            for g in e.link_faces:
                if g in old:
                    for loop in g.loops:
                        if loop.vert in e.verts:
                            got[loop.vert] = loop[uvl].uv.copy()
        for loop in f.loops:
            v = loop.vert
            if v in got:
                loop[uvl].uv = got[v]
                continue
            ref = np.mean([np.array(u) for u in got.values()], axis=0) if got else None
            cands = [np.array(l2[uvl].uv) for g in v.link_faces if g in old for l2 in g.loops if l2.vert == v]
            loop[uvl].uv = min(cands, key=lambda u: float(np.linalg.norm(u - ref))) if ref is not None else cands[0]
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.verts.index_update()
    c2 = np.array([tuple(v.co) for v in bm.verts], dtype=np.float64)
    f2, u2 = [], []
    for f in bm.faces:
        f2.append(tuple(v.index for v in f.verts))
        u2.append(tuple((float(l[uvl].uv[0]), float(l[uvl].uv[1])) for l in f.loops))
    bm.free()
    return c2, f2, u2


_DEC = {}
_LOD = [0]


def _decimated_cached(R, lod):
    """LOD1·2는 감량 몸(LOD0 감량 결과)에서 이어 줄이고, LOD0만 원본 머리로 바꾼다 (088 FR-52)."""
    if lod not in _DEC:
        _DEC[lod] = _decimated(R, lod)
    return _DEC[lod]


def _lod_mesh(R, lod):
    if lod > 0:
        return _decimated_cached(R, lod)
    if "head0" not in _DEC:
        _DEC["head0"] = _with_original_head(R, *_decimated_cached(R, 0))
    return _DEC["head0"]


# ---------------------------------------------------------------- 2. 유니폼 경계 평면

def cut_planes():
    """[(이름, 점, 법선, 대상 뼈 집합)] 선수 좌표. 법선 쪽이 '위/바깥(몸통 쪽)'."""
    if suit_mode():
        return _suit_planes()
    H, T = S.HEAD, S.TAIL
    knee_y = H["LeftLowerLeg"][1]
    hips_y = H["LeftUpperLeg"][1]
    out = [
        # 057: 크루넥은 뒤가 높고 앞이 낮다 — 056의 기울기(0, 1, −0.42)는 반대라 뒤가 깊게 파였다(smoke 캡처 2026-11-05)
        ("collar", (0.0, H["Neck"][1] - 0.035, 0.0), _unit((0.0, 1.0, COLLAR_TILT)), {"Neck", "UpperChest", "Head", "LeftShoulder", "RightShoulder"}),
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


def _suit_planes():
    H, T = S.HEAD, S.TAIL
    hips_y = H["LeftUpperLeg"][1]
    out = [
        ("collar", (0.0, H["Neck"][1] - 0.035, 0.0), _unit((0.0, 1.0, COLLAR_TILT)), {"Neck", "UpperChest", "Head", "LeftShoulder", "RightShoulder"}),
        ("hem", (0.0, hips_y - SUIT_HEM_BELOW_HIPS, 0.0), (0.0, 1.0, 0.0), {"Hips", "Spine", "Chest", "LeftUpperLeg", "RightUpperLeg"}),
    ]
    for n in ("Left", "Right"):
        a, b = np.array(H[n + "LowerArm"]), np.array(T[n + "LowerArm"])
        d = (b - a) / np.linalg.norm(b - a)
        out.append((f"sleeve{n[0]}", tuple(a + d * SUIT_SLEEVE_T * np.linalg.norm(b - a)), tuple(-d),
                    {n + "LowerArm", n + "Hand", n + "UpperArm"}))
        out.append((f"shorts{n[0]}", (0.0, SUIT_TROUSER_Y, 0.0), (0.0, 1.0, 0.0), {n + "LowerLeg", n + "Foot", n + "Toes"}))
    return out


def _classify_suit(p, w, planes):
    b = MS.OUR_NAMES[int(np.argmax(w))]
    if b == "Head":
        return "skin"
    if b in ("Neck", "UpperChest", "Chest", "Spine", "Hips") or b.endswith("Shoulder"):
        if _side(p, planes["collar"]) > 0:
            return "skin"
        return "shirt" if _side(p, planes["hem"]) > 0 else "shorts"
    side = b[0]
    if b.endswith("UpperArm"):
        return "sleeve"
    if b.endswith("LowerArm"):
        return "sleeve" if _side(p, planes["sleeve" + side]) > 0 else "skin"
    if b.endswith("Hand") or MS.OUR_NAMES.index(b) >= MS.CORE_BONES:
        return "skin"
    if b.endswith("UpperLeg"):
        return "shirt" if _side(p, planes["hem"]) > 0 else "shorts"
    if b.endswith("LowerLeg"):
        return "shorts" if _side(p, planes["shorts" + side]) > 0 else "boot"
    return "boot"


SLEEVE_T = 0.55            # 소매 끝 = 위팔 길이의 55 %
COLLAR_TILT = 0.10         # 칼라 평면 기울기 (+ = 앞이 낮고 뒤가 높다, 057 — 0.25 이상은 앞이 흉골 위 패임까지 내려가 고리가 패인다)
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
            # 057: 칼라는 띠 안 면도 주 뼈와 상관없이 자른다 (위팔·Head 주 뼈 면이 고리를 끊지 않게)
            if int(np.argmax(w)) in bi or (name == "collar" and any(in_collar_band(np.array(v.co)) for v in f.verts)):
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
        self.offset = {}  # 정점 → 위치 오프셋 (057 경계 평활, positions()에만 더한다)
        for i, p in enumerate(co):
            (t, bc), _ = _bind_point(R, tt, p)
            self.tri[i] = t
            self.bc[i] = bc

    def apply(self, A):
        return (A[self.tri] * self.bc[:, :, None]).sum(axis=1)

    def positions(self, R):
        P = self.apply(R.verts)
        for v in sorted(self.offset):
            P[v] = P[v] + self.offset[v]
        return P


# ---------------------------------------------------------------- 3b. 경계 고리 다듬기 (specs/057 FR-01·02)

RELAX_ITERS = 5
CORNER_DEG = 15.0       # 경계 고리 마지막 모서리 다듬기 문턱 (도)
CORNER_ITERS = 40       # 칼라 고리만 (정점 적은 소매·축구화 고리는 간격이 틀어진다)
NEIGHBOR_SMOOTH = 4    # 경계 고리 옆 두 줄 표면 평활 반복
BORDER_NORMAL_SPAN = 2  # 경계 정점 띄움 방향 = 고리 앞뒤 2정점까지 법선 평균
COLLAR_DENSITY = {0: 2.0}  # LOD별 칼라 고리 정점 배율 (057 FR-01: 클로즈업 = LOD0만, 꺾임 ≤ 20°)
LOOP_SMOOTH = 12        # 경계 고리 평면 안 Taubin 평활 반복 (칼라 밖)
LOOP_SMOOTH_MAX = 0.004  # 평활로 표면에서 떨어지는 최대 거리 (m) — 피부·유니폼이 같은 경계 정점을 쓰므로 틈은 생기지 않는다
COLLAR_SMOOTH = 30       # 칼라 고리: 목-승모근 모서리를 더 둥글게 (클로즈업, FR-01 ≤ 20°)
COLLAR_SMOOTH_MAX = 0.008
LOOP_PLANE_MAX = 0.02   # 고리 평균 평면 거리가 이보다 크면 절단 평면 고리가 아니다(건드리지 않음)


def chain_loops(edges):
    """무방향 변 목록 → [(정점 고리, 닫힘)] (작은 정점 id부터, 이웃은 작은 id 우선 — 결정론)."""
    adj = {}
    for a, b in edges:
        adj.setdefault(a, []).append(b)
        adj.setdefault(b, []).append(a)
    seen, loops = set(), []
    for s in sorted(adj):
        if s in seen:
            continue
        loop, prev, cur = [s], None, s
        seen.add(s)
        while True:
            nxt = [n for n in sorted(adj[cur]) if n != prev and n not in seen]
            if not nxt:
                break
            prev, cur = cur, nxt[0]
            seen.add(cur)
            loop.append(cur)
        loops.append((loop, len(loop) > 2 and loop[0] in adj[loop[-1]]))
    return loops


def _resample_closed(P):
    """닫힌 꺾은선 P (n,3)를 같은 호 길이 간격 n점으로. 시작 위상은 원래 점 이동 합이 작게(평균 위상)."""
    n = len(P)
    seg = np.linalg.norm(np.roll(P, -1, axis=0) - P, axis=1)
    L = float(seg.sum())
    if L < 1e-9:
        return P.copy()
    s = np.concatenate([[0.0], np.cumsum(seg)[:-1]])
    step = L / n
    phase = float(np.mean((s - np.arange(n) * step + L / 2) % L - L / 2))
    out = np.zeros_like(P)
    cs = np.concatenate([s, [L]])
    Pc = np.vstack([P, P[:1]])
    for i in range(n):
        t = (phase + i * step) % L
        j = int(np.searchsorted(cs, t, side="right") - 1)
        j = min(max(j, 0), n - 1)
        a = (t - cs[j]) / max(cs[j + 1] - cs[j], 1e-12)
        out[i] = Pc[j] * (1 - a) + Pc[j + 1] * a
    return out


def _split_edges(R, bind, faces, fuv, fpart, splits):
    """splits: [(a, b)] 변마다 중점 정점을 넣고 붙은 면을 그 정점에서 부채꼴 삼각형으로 나눈다(일직선 세 점 퇴화 면 방지).
    → (faces, fuv, fpart, {변: 새 정점})"""
    tt = _ref_tree(R)
    P = bind.positions(R)
    newv = {}
    tri, bc = [bind.tri], [bind.bc]
    n0 = len(bind.tri)
    for a, b in splits:
        key = (min(a, b), max(a, b))
        if key in newv:
            continue
        (t3, w3), _ = _bind_point(R, tt, 0.5 * (P[a] + P[b]))
        newv[key] = n0 + len(newv)
        tri.append(np.array([t3])), bc.append(np.array([w3]))
    bind.tri = np.vstack(tri)
    bind.bc = np.vstack(bc)
    of, ou, op = [], [], []
    for f, fu, part in zip(faces, fuv, fpart):
        k = len(f)
        ring, rus, mids = [], [], []
        for i in range(k):
            a, b = f[i], f[(i + 1) % k]
            ring.append(a), rus.append(tuple(fu[i]))
            m = newv.get((min(a, b), max(a, b)))
            if m is not None:
                mids.append(len(ring))
                ring.append(m)
                rus.append(tuple(0.5 * (fu[i][j] + fu[(i + 1) % k][j]) for j in (0, 1)))
        if not mids:
            of.append(f), ou.append(fu), op.append(part)
            continue
        s = mids[0]  # 첫 중점에서 부채꼴 (한 면에 경계 변이 둘이어도 퇴화 면 없음)
        ring, rus = ring[s:] + ring[:s], rus[s:] + rus[:s]
        for j in range(1, len(ring) - 1):
            of.append((ring[0], ring[j], ring[j + 1]))
            ou.append((rus[0], rus[j], rus[j + 1]))
            op.append(part)
    return of, ou, op, newv


def _relax_boundaries(R, bind, faces, fuv, fpart, lod=0):
    """부위 묶음(GROUP)이 바뀌는 변 = 유니폼 경계. 고리마다 가장 가까운 절단 평면을 찾아
    ① 평면 위로 투영 ② 같은 호 길이로 재배치 ③ 기준 표면 최근접점에 다시 묶기를 RELAX_ITERS번. bind를 고친다.
    → ({원래 정점: (평면 안 바깥 방향, 고리 접선, 앞뒤 고리 정점들)}, faces, fuv, fpart) — 경계 정점 셸 띄움·턱 방향."""
    tt = _ref_tree(R)
    planes = cut_planes()

    def boundary_loops():
        ef = {}
        for fi, f in enumerate(faces):
            for k in range(len(f)):
                a, b = f[k], f[(k + 1) % len(f)]
                ef.setdefault((min(a, b), max(a, b)), []).append(fi)
        edges = [e for e, fs in sorted(ef.items()) if len(fs) == 2 and GROUP[fpart[fs[0]]] != GROUP[fpart[fs[1]]]]
        return chain_loops(edges)

    # 칼라 고리 정점 늘리기 (LOD0): 고르게 흩은 변에 중점 삽입 → 고리 정점 COLLAR_DENSITY배
    dens = COLLAR_DENSITY.get(lod, 1.0)
    if dens > 1.0:
        P0 = bind.positions(R)
        col = planes[0]
        for loop, closed in boundary_loops():
            d = (P0[loop] - np.array(col[1])) @ np.array(col[2])
            if not closed or float(np.abs(d).mean()) > LOOP_PLANE_MAX:
                continue
            n = len(loop)
            k = int(math.ceil(n * (dens - 1.0)))
            pick = [i for i in range(n) if (i + 1) * k // n > i * k // n]
            faces, fuv, fpart, _ = _split_edges(R, bind, faces, fuv, fpart, [(loop[i], loop[(i + 1) % n]) for i in pick])
            break
    out = {}
    loops_done = []
    for loop, closed in boundary_loops():
        if not closed or len(loop) < 4:
            continue
        P = bind.apply(R.verts)[loop]
        best = min(planes, key=lambda pl: float(np.abs((P - np.array(pl[1])) @ np.array(pl[2])).mean()))
        n, pt = np.array(best[2]), np.array(best[1])
        if float(np.abs((P - pt) @ n).mean()) > LOOP_PLANE_MAX:
            continue
        loops_done.append(loop)
        for _ in range(RELAX_ITERS):
            P = bind.apply(R.verts)[loop]
            P = P - np.outer((P - pt) @ n, n)
            P = _resample_closed(P)
            for i, v in enumerate(loop):
                (tri, bc), _ = _bind_point(R, tt, P[i])
                bind.tri[v] = tri
                bind.bc[v] = bc
        # 마지막: 평면 안에서 고리를 평활하고 기준 표면에서 떨어진 만큼은 오프셋으로 둔다(정점 위치에만, 가중치·델타는 묶음 그대로)
        S0 = bind.apply(R.verts)[loop]
        S0 = S0 - np.outer((S0 - pt) @ n, n)
        S0 = _resample_closed(S0)  # 표면에 다시 붙이며 벌어진 간격을 평면 안에서 한 번 더 고르게 (차이는 오프셋이 맡는다)
        Q = S0.copy()
        is_collar = best[0] == "collar"
        for _ in range(COLLAR_SMOOTH if is_collar else LOOP_SMOOTH):  # Taubin (수축 없음)
            for lam in (0.5, -0.53):
                Q = Q + lam * (0.5 * (np.roll(Q, 1, axis=0) + np.roll(Q, -1, axis=0)) - Q)
        # 남은 모서리만 둥글게: 평면 투영 꺾임이 CORNER_DEG를 넘는 정점을 이웃 중점 쪽으로 (이동 상한 안에서)
        for _ in range(CORNER_ITERS if is_collar else 0):
            e0 = Q - np.roll(Q, 1, axis=0)
            e1 = np.roll(Q, -1, axis=0) - Q
            c = (e0 * e1).sum(axis=1) / np.maximum(np.linalg.norm(e0, axis=1) * np.linalg.norm(e1, axis=1), 1e-12)
            sharp = np.degrees(np.arccos(np.clip(c, -1.0, 1.0))) > CORNER_DEG
            if not sharp.any():
                break
            mid = 0.5 * (np.roll(Q, 1, axis=0) + np.roll(Q, -1, axis=0))
            Q = np.where(sharp[:, None], 0.5 * Q + 0.5 * mid, Q)
        dq = Q - S0
        ln = np.linalg.norm(dq, axis=1)
        lim = COLLAR_SMOOTH_MAX if is_collar else LOOP_SMOOTH_MAX
        dq *= np.minimum(1.0, lim / np.maximum(ln, 1e-12))[:, None]
        surf = bind.apply(R.verts)[loop]
        F = S0 + dq
        tang = np.roll(F, -1, axis=0) - np.roll(F, 1, axis=0)
        outw = np.cross(tang, n)
        outw /= np.maximum(np.linalg.norm(outw, axis=1, keepdims=True), 1e-12)
        cen = F.mean(axis=0)
        sign = np.where(((F - cen) * outw).sum(axis=1) >= 0, 1.0, -1.0)  # 고리 중심에서 바깥 (목·팔다리·몸통 고리는 별 모양)
        outw *= sign[:, None]
        k = len(loop)
        for i, v in enumerate(loop):
            bind.offset[v] = F[i] - surf[i]
            out[v] = (outw[i], tang[i] / max(np.linalg.norm(tang[i]), 1e-12),
                      tuple(loop[(i + j) % k] for j in range(-BORDER_NORMAL_SPAN, BORDER_NORMAL_SPAN + 1)))
    # 고리 옆 두 줄(고리 아닌 정점)을 표면 위에서 평활 — 고리 정점이 미끄러지며 붙은 면이 뒤집히지 않게
    onloop = {v for lp in loops_done for v in lp}
    nb = {}
    for f in faces:
        k = len(f)
        for i in range(k):
            a, b = f[i], f[(i + 1) % k]
            nb.setdefault(a, set()).add(b)
            nb.setdefault(b, set()).add(a)
    ring1 = sorted({u for v in onloop for u in nb.get(v, ()) if u not in onloop})
    ring2 = sorted({u for v in ring1 for u in nb.get(v, ()) if u not in onloop and u not in set(ring1)})
    moving = ring1 + ring2
    for _ in range(NEIGHBOR_SMOOTH):
        P = bind.positions(R)
        newp = {v: 0.5 * P[v] + 0.5 * np.mean([P[u] for u in sorted(nb[v])], axis=0) for v in moving}
        for v in moving:
            (tri, bc), _ = _bind_point(R, tt, newp[v])
            bind.tri[v] = tri
            bind.bc[v] = bc
    return out, faces, fuv, fpart


# ---------------------------------------------------------------- 4. 부위 · 유니폼 셸

HEM_BAND = 0.04   # 062 FR-05: 밑단에서 이 거리(m) 안은 셸 띄움을 줄인다 (운동복 밑단은 몸에 붙는다)
HEM_MIN = 0.3     # 밑단 끝 띄움 = 원래의 30 %


def _hem_taper(p, part, w, planes):
    """소매 끝·반바지 밑단·양말 윗단까지의 거리로 띄움 배율(HEM_MIN~1). 칼라·셔츠 밑단·축구화는 1."""
    side = "L" if p[0] < 0 else "R"
    b = MS.OUR_NAMES[int(np.argmax(w))]
    if part == "top" and b.endswith(("UpperArm", "LowerArm")):
        d = abs(_side(p, planes["sleeve" + side]))
    elif part == "shorts" and b.endswith(("UpperLeg", "LowerLeg")):
        d = abs(_side(p, planes["shorts" + side]))
    elif part == "sock":
        d = abs(_side(p, planes["sock" + side]))
    else:
        return 1.0
    t = min(max(d / HEM_BAND, 0.0), 1.0)
    return HEM_MIN + (1.0 - HEM_MIN) * (t * t * (3 - 2 * t))


def _bump(x, c, w):
    t = 1.0 - min(abs(x - c) / max(w, 1e-9), 1.0)
    return t * t * (3 - 2 * t)


def _kit_extra(p, part, w, n, planes):
    """062 FR-20·23·24: 셸 추가 띄움(m). 셔츠: 허리·등 아래 늘어짐, 겨드랑이 여유. 양말: 정강이 보호대(앞 볼록 + 위 끝 턱).
    축구화: 밑창 테두리(바닥 2 cm 띠에서 바깥으로). 경계 띠(_hem_taper)와 곱하지 않는다 — 밑단 쪽은 위치상 0이 되게 정한다."""
    b = MS.OUR_NAMES[int(np.argmax(w))]
    side = "L" if p[0] < 0 else "R"
    if part == "top" and suit_mode() and b.endswith("UpperLeg"):
        return 0.0   # 정장: 허벅지 위 재킷 자락은 늘어짐 없이 붙인다 (달리기 손가락이 닿음, T32)
    if part == "top" and not b.endswith(("UpperArm", "LowerArm")):
        hem_y = planes["hem"][1][1]
        d_hem = p[1] - hem_y
        back = max(0.0, -float(n[2]))                       # 등 쪽 법선
        waist = _bump(d_hem, 0.10, 0.10) * (0.004 + 0.004 * back)   # 밑단 위 5~20 cm, 등이 더 늘어진다
        arm_y = S.HEAD["RightUpperArm"][1]
        pit = _bump(p[1], arm_y - 0.10, 0.05) * max(0.0, abs(float(n[0])) - 0.5) * 0.008  # 겨드랑이 옆면
        return waist + pit
    if part == "sock" and b.endswith("LowerLeg"):
        knee_y = S.HEAD[("Left" if side == "L" else "Right") + "LowerLeg"][1]
        ank_y = S.HEAD[("Left" if side == "L" else "Right") + "Foot"][1]
        t = (knee_y - 0.09 - p[1]) / max(knee_y - 0.09 - (ank_y + 0.07), 1e-6)   # 0 = 보호대 위 끝, 1 = 아래 끝
        if 0.0 <= t <= 1.0:
            front = max(0.0, float(n[2]))
            ridge = 0.0015 if t < 0.06 else 0.0                                  # 위 끝 턱
            return (0.004 * math.sin(math.pi * min(max(t, 0.0), 1.0)) ** 0.5 + ridge) * front ** 1.5
        return 0.0
    if part == "boot":
        if p[1] < 0.022:
            out = math.hypot(float(n[0]), float(n[2]))
            return 0.0025 * (1.0 - p[1] / 0.022) * out                          # 밑창 테두리
        return 0.0
    return 0.0


def _side(p, plane):
    _, pt, nrm, _ = plane
    return float(np.dot(np.array(p) - np.array(pt), np.array(nrm)))


COLLAR_BAND = 0.05     # 칼라 평면에서 이 거리 안 (m)
COLLAR_RADIUS = 0.13   # 목 축(x = 0, z = Neck)에서 수평 거리 안 (m)


def in_collar_band(p):
    """칼라 띠: 칼라 평면 ± COLLAR_BAND, 목 축에서 수평 COLLAR_RADIUS 안, 어깨 관절 높이 − 6 cm 위."""
    pl = cut_planes()[0]
    if abs(_side(p, pl)) >= COLLAR_BAND:
        return False
    nz = S.HEAD["Neck"][2]
    if math.hypot(float(p[0]), float(p[2]) - nz) >= COLLAR_RADIUS:
        return False
    return float(p[1]) > S.HEAD["LeftShoulder"][1] - 0.06


def classify(p, w):
    """정점·면 중심 위치 p와 가중치 w(22) → 부위 이름."""
    planes = {pl[0]: pl for pl in cut_planes()}
    if in_collar_band(p):
        # 057 FR-01: 칼라 띠 안에서는 주 뼈(Head·Neck·어깨·위팔)와 상관없이 평면만 본다 — 경계가 가중치 변을 따라 계단이 되지 않게
        return "skin" if _side(p, planes["collar"]) > 0 else "shirt"
    if suit_mode():
        return _classify_suit(p, w, planes)
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
    if b.endswith("LowerArm") or b.endswith("Hand") or MS.OUR_NAMES.index(b) >= MS.CORE_BONES:  # 062: 손가락 뼈 = 손
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


def _boot_hull(P, parts, border, N=None, faces=None):
    """축구화 셸 = 발마다 축구화 정점의 볼록 껍질 면 (062 T17b). 원래 발 위상을 껍질에 투영하면 발가락 사이에서 서로 마주 보던
    옆면이 반드시 뒤집혀(앞코 349면 중 ≈ 20면, 3,000 mm²) Unity 용접 법선에 발가락 줄이 보인다 → 껍질 면 자체를 축구화 표면으로 쓴다.
    껍질 정점은 원래 축구화 정점의 일부라 가중치·체형 키·UV 규칙이 그대로 이어진다. 발목 고리(경계)만으로 된 뚜껑 면은 버린다.
    faces(원래 면 목록, 같은 정점 번호)가 있으면 그 감김 방향(다수결)에 맞춘다. N이 있으면 껍질 정점 띄움 방향 = 바깥 껍질 법선 평균.
    → [(정점 번호 튜플, ...)] 새 축구화 면 (껍질 밖 정점은 어느 면에도 안 쓰인다)."""
    import bmesh
    out = []
    for sgn in (-1.0, 1.0):
        idx = [i for i in range(len(P)) if parts[i] == "boot" and P[i, 0] * sgn > 0]
        if len(idx) < 8:
            continue
        bm = bmesh.new()
        for i in idx:
            bm.verts.new(tuple(P[i]))
        bm.verts.ensure_lookup_table()
        res = bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
        ctr = P[idx].mean(axis=0)
        # 원래 축구화 면의 감김 규칙: 법선 · (면 중심 − 중심) 부호 다수결
        sign = 1.0
        if faces is not None:
            sel = set(idx)
            votes = 0.0
            for f in faces:
                if len(f) >= 3 and all(v in sel for v in f):
                    a, b, c = P[f[0]], P[f[1]], P[f[2]]
                    votes += np.sign(np.cross(b - a, c - a) @ ((a + b + c) / 3 - ctr))
            sign = 1.0 if votes >= 0 else -1.0
        hull = [fa for fa in res.get("geom", []) if isinstance(fa, bmesh.types.BMFace)]
        vnorm = {}
        for fa in hull:
            t = [idx[v.index] for v in fa.verts]
            if all(border[i] for i in t):
                continue  # 발목 뚜껑
            a, b, c = P[t[0]], P[t[1]], P[t[2]]
            n = np.cross(b - a, c - a)
            outw = n @ ((a + b + c) / 3 - ctr) >= 0
            if outw != (sign > 0):
                t = t[::-1]
            nn = n / (np.linalg.norm(n) + 1e-12) * (1 if outw else -1)
            for i in t:
                vnorm[i] = vnorm.get(i, 0) + nn
            out.append(tuple(t))
        if N is not None:
            for i, q in vnorm.items():
                if not border[i]:
                    N[i] = q / (np.linalg.norm(q) + 1e-12)
        bm.free()
    return out


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
    co, faces, fuv = _lod_mesh(R, lod)
    co, faces, fuv = _bisect(co, faces, fuv, R)
    bind = Binding(R, co)
    pos = bind.apply(R.verts)
    W = bind.apply(R.weights)
    W /= W.sum(axis=1, keepdims=True)
    # 면 부위
    fpart = []
    for f in faces:
        c = pos[list(f)].mean(axis=0)
        fpart.append(classify(c, W[list(f)].mean(axis=0)))
    # 057: 경계 고리를 절단 평면 위로 투영·고르게 재배치하고 기준 표면에 다시 묶는다 (FR-01·02)
    border_plane, faces, fuv, fpart = _relax_boundaries(R, bind, faces, fuv, fpart, lod)
    pos = bind.positions(R)
    W = bind.apply(R.weights)
    W /= W.sum(axis=1, keepdims=True)
    if lod >= 1:
        W = _mitten(W)  # 062 FR-12: LOD1 이하 = 손가락을 합친 장갑형 (손가락 뼈 가중치 → 손)
    deltas = {k: bind.apply(d) for k, d in sorted(R.body_deltas.items())}
    for k, d in sorted(R.extra_deltas.items()):
        if lod >= 1 and k.startswith(EXPR_PREFIX):
            continue  # 062 FR-71: 표정은 LOD0·클로즈업 머리에만 (LOD1 이하 무표정)
        deltas[k] = bind.apply(d)
    m = _assemble(pos, W, faces, fuv, fpart, deltas, border_plane)
    _append_eyes(R, m, lod)
    if lod == 0:
        _append_mouth(R, m, "lod0")  # 062 FR-72: 입 안 (치아·혀·입안 캡)
    else:
        _append_lip_seal(R, m)  # 088 T30a: LOD1·2 입술 틈 뒤 입술색 판 (입 안쪽 면은 감량 전에 뺐다)
    if not suit_mode():
        _kit_bone_weights(m, lod)  # 062 FR-21 (정장은 보조 뼈 없음)
    # 088 T29: 눈썹·속눈썹은 피부 마스크(진한 바탕) + 메시(LOD0 띠·클로즈업 원본 가닥) — 시안(Cycles) 눈썹은 진한 띠 위 가닥 결
    return m


# 062 T17a 밑깔개: 보조 뼈가 밑단을 움직이면 턱 띠가 피부(셔츠 밑은 반바지) 경계에서 떨어져 구멍이 난다 →
# 바깥 옷 밑단 띠 아래에 안쪽 면을 이만큼(m, 절단 평면에서 몸통 쪽으로) 더 깔아 둔다. 경계 정점을 공유해 이어진다.
# 폭 ≥ 밑단 접선 이동(상자 × KIT_SHARE) + 5 mm (checks.kit_cover). 셔츠 밑 반바지 허리는 셔츠 셸보다 안쪽(UNDER_SHORTS_OFF)
UNDER = {"hem": 0.035, "sleeve": 0.02, "short": 0.02}
UNDER_SHORTS_OFF = 0.0   # 피부 높이 — 4 mm면 셔츠 앞 밑단을 앞으로 밀 때 닿는다 (T17a 진단)
UNDER_SINK = {"sleeve": 0.6, "short": 0.35, "hem": 0.35}   # 피부 밑깔개: 경계에서 멀어질수록 이 기울기로 안쪽으로 (허벅지·위팔이 몸통 쪽으로 굵어져 소매·반바지를 올리면 닿는다, T17a 진단 — 위팔은 팔 포즈에서 더 굵어져 0.35로 부족)
UNDER_LODS = {0: ("hem", "sleeve", "short"), 1: ("hem",)}   # 보조 뼈가 있는 LOD만 (KIT_LODS와 짝)


def underlay_kind(fp, part, planes, lod):
    """면 정점 위치 fp(k,3)·부위 → 밑깔개 (밑단 종류, 안쪽 부위, 절단 평면 이름) 또는 None.
    면이 밑단 띠(절단 평면에서 몸통 쪽 0 ~ UNDER)에 한 정점이라도 걸치면 넣는다 — 큰 감량 면도 빠지지 않게(띠가 끊기면 구멍)."""
    use = () if suit_mode() else UNDER_LODS.get(lod, ())
    c = fp.mean(axis=0)
    side = "L" if c[0] < 0 else "R"

    def touch(name, w):
        d = [_side(p, planes[name]) for p in fp]
        return _side(c, planes[name]) > 0.0 and min(d) < w

    if part == "shirt" and "hem" in use and touch("hem", UNDER["hem"]):
        return "hem", "shorts", "hem"
    if part == "sleeve" and "sleeve" in use and touch("sleeve" + side, UNDER["sleeve"]):
        return "sleeve", "skin", "sleeve" + side
    if part == "shorts" and "short" in use and touch("shorts" + side, UNDER["short"]):
        return "short", "skin", "shorts" + side
    return None


KIT_BAND = {"hem": 0.10, "sleeve": 0.06, "short": 0.07}   # 밑단에서 이 거리(m) 안에서만 보조 뼈가 끈다 (관통 방지: 밑단 띠 한정)
KIT_SHARE = 0.85                                          # 밑단 끝 정점의 보조 뼈 몫 (나머지는 원래 몸 뼈)
KIT_LODS = {0: ("Kit_HemF", "Kit_HemB", "Kit_Sleeve.L", "Kit_Sleeve.R", "Kit_Short.L", "Kit_Short.R"),
            1: ("Kit_HemF", "Kit_HemB")}                  # LOD1은 상의 밑단만, LOD2는 없음 (plan 성능 절)


def _kit_bone_weights(m, lod):
    """062 FR-21: 보조 뼈 열 6개를 W 뒤에 붙인다(S.NAMES 순서). 상의 밑단 띠 → 앞·뒤(z로 섞음), 소매 끝 띠 → 소매,
    반바지 밑단 띠 → 반바지. 띠 안 몫 = KIT_SHARE × smoothstep(1 − 거리/띠), 원래 뼈 가중치는 그만큼 줄인다."""
    nb = len(MS.OUR_NAMES)
    assert list(S.NAMES[:nb]) == list(MS.OUR_NAMES) and len(S.NAMES) == nb + len(S.KIT_NAMES)
    W = m.W[:, :nb].copy()
    add = np.zeros((len(W), len(S.KIT_NAMES)))
    use = set(KIT_LODS.get(lod, ()))
    col = {n: j for j, n in enumerate(S.KIT_NAMES)}
    planes = {pl[0]: pl for pl in cut_planes()}
    hem_y = planes["hem"][1][1]

    def band(d, w):
        t = 1.0 - min(max(d / w, 0.0), 1.0)
        return t * t * (3 - 2 * t)

    hips_mask = np.array([1 if n == "Hips" else 0 for n in MS.OUR_NAMES])

    for i, part in enumerate(m.vpart):
        if part not in ("top", "shorts"):
            continue
        p = m.verts[i]
        b = MS.OUR_NAMES[int(np.argmax(W[i]))]
        side = "L" if p[0] < 0 else "R"
        if part == "top" and not b.endswith(("UpperArm", "LowerArm", "Hand")):
            s_ = band(max(p[1] - hem_y, 0.0), KIT_BAND["hem"])
            f = min(max(0.5 + p[2] / 0.12, 0.0), 1.0)            # 앞(z+) ↔ 뒤, 옆구리에서 섞인다
            targets = (("Kit_HemF", s_ * f), ("Kit_HemB", s_ * (1 - f)))
        elif part == "top":
            targets = (("Kit_Sleeve." + side, band(abs(_side(p, planes["sleeve" + side])), KIT_BAND["sleeve"])),)
        elif b.endswith(("UpperLeg", "LowerLeg")):
            targets = (("Kit_Short." + side, band(abs(_side(p, planes["shorts" + side])), KIT_BAND["short"])),)
        else:
            continue
        # 062 T17c: 상의 밑단은 보조 뼈(골반 자식) 몫을 골반 가중치에서만 뗀다 — 허벅지·척추 몫까지 떼면 질주·웅크림에서 셔츠 밑단이
        # 그 뼈들을 안 따라가 같은 원래 가중치의 반바지 경계·밑깔개가 셔츠를 뚫는다 (T17c 진단: 오프셋 0에서도 밑단 위 1~3 cm 고리 전체 교차)
        pool = W[i] * hips_mask if part == "top" and not b.endswith(("UpperArm", "LowerArm", "Hand")) else W[i]
        avail = float(pool.sum())
        tot = 0.0
        for n, s_ in targets:
            if n in use and s_ > 0:
                add[i, col[n]] = KIT_SHARE * s_ * avail
                tot += KIT_SHARE * s_
        W[i] = W[i] - pool * tot
    m.W = np.hstack([W, add])


def _mitten(W):
    W = W.copy()
    for side in ("Left", "Right"):
        hi = MS.OUR_NAMES.index(side + "Hand")
        for j in range(MS.CORE_BONES, len(MS.OUR_NAMES)):
            if MS.OUR_NAMES[j].startswith(side):
                W[:, hi] += W[:, j]
                W[:, j] = 0.0
    return W


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


EYE_TRIS_088 = {"closeup": 420, 0: 240, 1: 160, 2: 16}   # 088: MakeHuman 고해상도 눈(2,040 tris 양쪽) 감량 목표(양쪽 합). T29: 원본(감량 없음)은 Unity에서 동공이 홍채를 덮고 눈 안쪽 구석에 검은 덩어리 → 420 유지. T30a: LOD1 48 → 160 — 눈 하나 24 tris는 홍채를 가로지르는 큰 면 안 UV 선형 보간으로 흰자에 빨강·파랑 줄(088-tier-match) [제안]
EYE_BACK = 0.0015                                         # 088 T23: 웃음 표정 눈꺼풀 노출(6 → 4점) → 1.5 mm 뒤로
EYE_SHRINK = 0.94                                         # 088: MakeHuman 눈은 MH 눈꺼풀 기준 — 표정 키 노출(웃음 6점) → 6 % 줄임


def _proxy_part(R, px, target, keys, face_ok=None):
    """MakeHuman 프록시 → (정점, 면, 면별 UV, 델타 dict). 감량 정점은 원본 프록시 삼각형에 무게중심으로 묶어 델타를 옮긴다."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    P = px.fit(R.verts)
    T, TU = px.triangles()
    if face_ok is not None:                                      # 088: 층 고르기 (눈 = 안구만, 각막 층 제외)
        keep = np.array([face_ok(px.uv[tu].mean(axis=0)) for tu in TU])
        T, TU = T[keep], TU[keep]
    alld = _all_deltas(R)
    D = {k: px.delta(alld[k]) for k in keys}
    uvs = [tuple(tuple(px.uv[i]) for i in tu) for tu in TU]
    if target is None:                                           # 088 T28: 원본 (클로즈업 눈)
        return P, [tuple(int(x) for x in t) for t in T], uvs, D
    co, fl, fu = _decimate_faces(P, [tuple(t) for t in T], target, uvs)
    tree = BVHTree.FromPolygons([tuple(p) for p in P], [tuple(t) for t in T], all_triangles=True)
    Pd = np.zeros((len(co), 3))
    Dd = {k: np.zeros((len(co), 3)) for k in keys}
    for i, p in enumerate(co):
        loc, _, idx, _ = tree.find_nearest(Vector(tuple(p)))
        a, b, c = P[T[idx]]
        v0, v1, v2 = b - a, c - a, np.array(loc) - a
        d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        w1 = (d11 * d20 - d01 * d21) / den if abs(den) > 1e-18 else 0.0
        w2 = (d00 * d21 - d01 * d20) / den if abs(den) > 1e-18 else 0.0
        w = np.array([1 - w1 - w2, w1, w2])
        Pd[i] = w @ P[T[idx]]
        for k in keys:
            Dd[k][i] = w @ D[k][T[idx]]
    return Pd, fl, fu, Dd


def _append_eyes(R, m, lod, closeup=False):
    """088 FR-02: 눈 = MakeHuman 고해상도 눈(CC0) 감량. UV = 원본 눈 재질 UV를 parts 눈 칸으로 축척(u + 1 → parts)."""
    from . import face as F
    from . import mhassets as MH
    px = MH.Proxy(MH.EYES)
    target = EYE_TRIS_088["closeup" if closeup else lod]
    # MakeHuman 고해상도 눈 = 안구 + 바깥 각막 층(텍스처 구석 u > 0.9·v < 0.15의 투명 하이라이트 영역에 매핑) → 각막은 흰자 위를 덮어 뺀다
    P, fl, fu, D = _proxy_part(R, px, target, list(m.deltas), face_ok=lambda uv: not (uv[0] > 0.9 and uv[1] < 0.15))
    for ctr in F.eye_centers(R):                       # 눈마다 중심 쪽으로 EYE_SHRINK — 표정 키에서 눈꺼풀 밖 노출 방지
        sel = np.sign(P[:, 0]) == np.sign(ctr[0])
        P[sel] = ctr + (P[sel] - ctr) * EYE_SHRINK
        P[sel, 2] -= EYE_BACK
        for k in D:
            D[k][sel] *= EYE_SHRINK
    u0, v0, u1, v1 = F.EYE_REGION
    uvs = [tuple((1.0 + u0 + (u1 - u0) * float(u), v0 + (v1 - v0) * float(v)) for u, v in f) for f in fu]
    W = np.zeros((len(P), R.weights.shape[1]))
    W[:, MS.OUR_NAMES.index("Head")] = 1.0  # 안구는 머리에 고정
    append_part(m, P, W, D, [tuple(f) for f in fl], BODY, uvs, "eye")


EXPR_PREFIX = "Expr_"
# 062 FR-72 입 안: MakeHuman 기준 메시(CC0) 치아·혀 헬퍼 + 절차 입안 캡. 감량 목표(삼각형) — LOD0 ≈ 160, 클로즈업 ≈ 230 (≤ 6k)
MOUTH_TRIS = {"lod0": {"teeth": 32, "tongue": 32, "cap": (3, 8)}, "closeup": {"teeth": 48, "tongue": 64, "cap": (4, 10)}}
MOUTH_GROUPS = {"teeth_upper": "helper-upper-teeth", "teeth_lower": "helper-lower-teeth", "tongue": "helper-tongue"}
# UV: 치아 = parts 눈 칸 흰자(밝고 매끈), 혀 = 피부 UV 입술(붉은 기운), 입안 캡 = parts 눈 칸 동공(거의 검정)
# 088 T30j: 치아 v + 1 = 셰이더 치아 표시(입 벌림 차폐) — 셰이더가 1을 빼 같은 흰자 텍셀(1.21875, 0.875)을 읽는다
MOUTH_UV_TEETH = (1.21875, 1.875)
MOUTH_UV_CAP = (1.125, 0.875)


def _mouth_tongue_uv(R):
    """아랫입술 가운데 정점의 피부 UV (혀 색 = 입술 붉은 기운)."""
    lips = R.groups["lips"]
    P = R.verts[lips]
    v = lips[int(np.argmin(np.abs(P[:, 0]) + np.abs(P[:, 1] - P[:, 1].min()) * 2))]
    for f, u in zip(R.faces, R.face_uvs):
        if v in f:
            return tuple(u[list(f).index(v)])
    return (0.5, 0.5)


MOUTH_BOX_PAD = 0.005     # 입 상자: 입술 높이 위아래 5 mm [제안]
MOUTH_INNER_BACK = 0.004  # 입술 맨 앞보다 4 mm 이상 뒤 [제안]
MOUTH_INNER_NZ = 0.1      # 앞을 거의 안 보는 면 (법선 z < 0.1) [제안]
MOUTH_DEPTH = 0.04        # 입술 맨 뒤보다 4 cm 뒤까지 = 구강 (그 뒤는 뒤통수·목) [제안]


def _drop_mouth_inside(R, co, fl, uvs):
    """088 T30a (FR-58): LOD1·2는 입 안(치아·혀·입안 캡)이 없어, 감량하면 입술 안쪽·구강 면이 뒤·아래를 향한 채 입술 사이로
    앞에 드러나 빛을 못 받는 어두운 마름모가 됐다(088-tier-match 원거리 짝). 감량 전에 입 상자(입술 x 범위 · 입술 높이 ± MOUTH_BOX_PAD ·
    입술 맨 앞보다 MOUTH_INNER_BACK 이상 뒤, 입술 맨 뒤보다 MOUTH_DEPTH 이내) 안에서 뒤를 향하거나(법선 z < MOUTH_INNER_NZ) 구강 UV 섬(u ≥ 0.75, v < 0.3 또는 > 0.7)인 면을 빼고,
    입술 틈 뒤는 _append_lip_seal이 막는다. (UV 근접만으로 고르면 입술 UV가 이어져 바깥 입술·턱까지 빠졌다)"""
    lips = R.verts[R.groups["lips"]]
    hx = float(np.abs(lips[:, 0]).max())
    y0, y1 = float(lips[:, 1].min()) - MOUTH_BOX_PAD, float(lips[:, 1].max()) + MOUTH_BOX_PAD
    zb = float(lips[:, 2].max()) - MOUTH_INNER_BACK
    zd = float(lips[:, 2].min()) - MOUTH_DEPTH
    keep_f, keep_u, n = [], [], 0
    for f, u in zip(fl, uvs):
        p = co[list(f)]
        c = p.mean(axis=0)
        if abs(c[0]) < hx and y0 < c[1] < y1 and zd < c[2] < zb:
            nn = -np.cross(p[1] - p[0], p[2] - p[0])
            nn /= np.linalg.norm(nn) + 1e-12
            uc = np.array(u).mean(axis=0)
            if nn[2] < MOUTH_INNER_NZ or (uc[0] >= 0.75 and not 0.3 <= uc[1] <= 0.7):
                n += 1
                continue
        keep_f.append(f)
        keep_u.append(u)
    print(f"[player] LOD1 입 안쪽 면 {n}개 뺌")
    return keep_f, keep_u


LIP_SEAL_SEGS = 12
LIP_SEAL_BACK = 0.010   # 입술 겉면 맨 앞보다 1 cm 뒤 — 입술 사이 틈 안 [제안]


def _append_lip_seal(R, m):
    """088 T30a: LOD1·2 입술 틈 뒤 입술색 타원 판(부채꼴 LIP_SEAL_SEGS tris, 앞을 향함, 머리 뼈 고정) — 다문 입처럼 보이게."""
    hi = MS.OUR_NAMES.index("Head")
    lip_ids = R.groups["lips"]
    lips = R.verts[lip_ids]
    cy = float((lips[:, 1].min() + lips[:, 1].max()) / 2)
    z = float(lips[:, 2].max()) - LIP_SEAL_BACK
    rx = float(np.abs(lips[:, 0]).max()) * 0.85
    ry = float((lips[:, 1].max() - lips[:, 1].min()) / 2) * 0.6
    pts = [np.array([0.0, cy, z])]
    for k in range(LIP_SEAL_SEGS):
        ph = 2 * math.pi * k / LIP_SEAL_SEGS
        pts.append(np.array([rx * math.cos(ph), cy + ry * math.sin(ph), z]))
    fl = [(0, 1 + k, 1 + (k + 1) % LIP_SEAL_SEGS) for k in range(LIP_SEAL_SEGS)]   # 입안 캡과 같은 감기(앞에서 보인다)
    P = np.array(pts)
    keep = [f for f in R.faces if all(v in set(lip_ids) for v in f)]
    tree = _subset_tree(R, keep)
    b = Binding.__new__(Binding)
    b.tri = np.zeros((len(P), 3), dtype=np.int64)
    b.bc = np.zeros((len(P), 3))
    off = np.zeros_like(P)
    for i, p in enumerate(P):
        (t, bc), _ = _bind_point(R, tree, p)
        b.tri[i], b.bc[i] = t, bc
        off[i] = p - (R.verts[t] * bc[:, None]).sum(axis=0)
    W = np.zeros((len(P), m.W.shape[1]))
    W[:, hi] = 1.0
    alld = _all_deltas(R)
    D = {k: (b.apply(alld[k]) if k in alld else np.zeros_like(P)) for k in m.deltas}
    uv = _mouth_tongue_uv(R)
    append_part(m, b.apply(R.verts) + off, W, D, fl, BODY, [tuple(uv for _ in f) for f in fl], "mouth_seal")


def _append_mouth(R, m, which):
    """062 FR-72: 치아(위·아래)·혀(헬퍼, 감량)·입안 캡(치아 뒤 반타원체, 앞이 열림). 머리 뼈 고정, 기준 델타(표정·얼굴·바탕 머리)를
    정점 묶음으로 받는다 — 입을 벌리면 아래 치아·혀가 MPFB 표정 단위를 따라 내려간다(위 치아는 그대로)."""
    tri = MOUTH_TRIS[which]
    hi = MS.OUR_NAMES.index("Head")
    alld = _all_deltas(R)
    all_ids = []
    proxy_teeth = which in TEETH_PROXY_TRIS   # 088 T30k: 치아 = MakeHuman teeth_base (클로즈업 원본, LOD0 감량)
    if proxy_teeth:
        _append_proxy_teeth(R, m, alld, hi, TEETH_PROXY_TRIS[which])
    for part, grp in MOUTH_GROUPS.items():
        gs = set(R.groups[grp])
        faces = [f for f in R.faces if all(v in gs for v in f)]
        ids = sorted({v for f in faces for v in f})
        all_ids += ids
        if proxy_teeth and part != "tongue":
            continue
        remap = {v: i for i, v in enumerate(ids)}
        target = tri["tongue" if part == "tongue" else "teeth"]
        co, fl = _decimate_faces(R.verts[ids], [tuple(remap[v] for v in f) for f in faces], target)
        tree = _subset_tree(R, faces)
        b = Binding.__new__(Binding)
        b.tri = np.zeros((len(co), 3), dtype=np.int64)
        b.bc = np.zeros((len(co), 3))
        for i, p in enumerate(co):
            (t, bc), _ = _bind_point(R, tree, p)
            b.tri[i], b.bc[i] = t, bc
        P = b.apply(R.verts)
        W = np.zeros((len(P), m.W.shape[1]))
        W[:, hi] = 1.0
        D = {k: (b.apply(alld[k]) if k in alld else np.zeros_like(P)) for k in m.deltas}
        uv = MOUTH_UV_TEETH if part != "tongue" else _mouth_tongue_uv(R)
        append_part(m, P, W, D, fl, BODY, [tuple(uv for _ in f) for f in fl], "mouth_" + part)
    # 입안 캡: 치아·혀를 감싸는 반타원체(뒤·위·아래·옆), 앞(입술 쪽)이 열린 그릇. 정점은 가까운 치아·혀·입술 면에 묶는다.
    lips = R.verts[R.groups["lips"]]
    inner = R.verts[all_ids]
    cx = 0.0
    cy = float((inner[:, 1].min() + inner[:, 1].max()) / 2)
    z_front = float(lips[:, 2].min()) - 0.008   # 입술 안쪽 뒤 — 입을 크게 벌려도 테두리가 입꼬리·볼 밖으로 나오지 않게 (T27 렌더)
    z_back = float(inner[:, 2].min()) - 0.004
    rx = float(np.abs(inner[:, 0]).max()) - 0.002
    ry = float((inner[:, 1].max() - inner[:, 1].min()) / 2) + 0.002
    rz = z_front - z_back
    rings, segs = tri["cap"]
    pts = [np.array([cx, cy, z_back])]
    for r in range(1, rings + 1):
        th = 0.5 * math.pi * r / rings          # 0 = 뒤 꼭짓점 → π/2 = 앞 테두리(열림)
        for k in range(segs):
            ph = 2 * math.pi * k / segs
            pts.append(np.array([cx + rx * math.sin(th) * math.cos(ph), cy + ry * math.sin(th) * math.sin(ph),
                                 z_back + rz * (1 - math.cos(th))]))
    fl = []
    for k in range(segs):  # 안쪽에서 보이게 감는다 (입 안을 앞에서 들여다본다)
        fl.append((0, 1 + k, 1 + (k + 1) % segs))
    for r in range(rings - 1):
        for k in range(segs):
            a, b2 = 1 + r * segs + k, 1 + r * segs + (k + 1) % segs
            c2, d2 = 1 + (r + 1) * segs + (k + 1) % segs, 1 + (r + 1) * segs + k
            fl.append((a, d2, c2, b2))
    P = np.array(pts)
    keep = [f for f in R.faces if all(v in set(all_ids) for v in f)] + \
           [f for f in R.faces if all(v in set(R.groups["lips"]) for v in f)]
    tree = _subset_tree(R, keep)
    b = Binding.__new__(Binding)
    b.tri = np.zeros((len(P), 3), dtype=np.int64)
    b.bc = np.zeros((len(P), 3))
    off = np.zeros_like(P)
    for i, p in enumerate(P):
        (t, bc), _ = _bind_point(R, tree, p)
        b.tri[i], b.bc[i] = t, bc
        off[i] = p - (R.verts[t] * bc[:, None]).sum(axis=0)
    W = np.zeros((len(P), m.W.shape[1]))
    W[:, hi] = 1.0
    D = {k: (b.apply(alld[k]) if k in alld else np.zeros_like(P)) for k in m.deltas}
    append_part(m, b.apply(R.verts) + off, W, D, fl, BODY, [tuple(MOUTH_UV_CAP for _ in f) for f in fl], "mouth_cap")


TEETH_PROXY = "teeth/teeth_base/teeth_base.mhclo"
TEETH_SPLIT_VERTS = 200   # 088 T30k: 이보다 큰 조각(잇몸)은 면마다 위·아래를 나눈다
TEETH_PROXY_TRIS = {"closeup": None, "lod0": 800}   # 088 T30k: None = 원본(≈ 7k), LOD0 800 [제안] (LOD0 예산 30k 중 여유 ≈ 2.8k)


def teeth_uv(uv):
    """MakeHuman 치아 UV (0~1) → parts 치아 칸(face.TEETH_REGION) + v + 1 치아 표시 (셰이더 입 벌림 차폐, T30j)."""
    from . import face as F
    u0, v0, u1, v1 = F.TEETH_REGION
    pad = 0.5 / 1024                       # 칸 가장자리 텍셀 반 개 안쪽 (옆 칸 번짐 방지)
    uu = np.clip(uv[..., 0], 0, 1) * (u1 - u0 - 2 * pad) + u0 + pad
    vv = np.clip(uv[..., 1], 0, 1) * (v1 - v0 - 2 * pad) + v0 + pad
    return np.stack([1.0 + uu, 1.0 + vv], axis=-1)


def proxy_teeth_split(R):
    """088 T30k: 치아 프록시 맞춤 + 면마다 위·아래 구분 → (px, P, T, TU, face_up, helper{part: (BVH, 헬퍼 정점)}).
    _append_proxy_teeth와 face.teeth_covered(T30m 검사)가 같은 규칙을 쓴다."""
    from . import mhassets as MH
    px = MH.Proxy(TEETH_PROXY)
    P = px.fit(R.verts)
    T, TU = px.triangles()
    n = len(P)
    par = np.arange(n)

    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]
            a = par[a]
        return a
    for f in T:
        r0 = find(int(f[0]))
        for v in f[1:]:
            r = find(int(v))
            if r != r0:
                par[r] = r0
    roots = np.array([find(i) for i in range(n)])
    helper = {}
    for part in ("teeth_upper", "teeth_lower"):
        gs = set(R.groups[MOUTH_GROUPS[part]])
        faces = [f for f in R.faces if all(v in gs for v in f)]
        helper[part] = (_subset_tree(R, faces), R.verts[sorted(gs)])
    def nearer_upper(c):
        """같은 (x, z) 근처(가까운 8정점) 위·아래 헬퍼 높이 중 가까운 쪽. 3D 최근접 정점으로 가르면 윗니가 아랫니 앞을 덮는
        구조라 아랫니 4개가 위로 분류돼 입을 벌려도 제자리에 떠 있었다."""
        def height(H):
            k = np.argsort(np.hypot(H[:, 0] - c[0], H[:, 2] - c[2]))[:8]
            return H[k, 1].mean()
        return abs(c[1] - height(helper["teeth_upper"][1])) <= abs(c[1] - height(helper["teeth_lower"][1]))
    # 치아 한 개 = 작은 조각 → 조각 통째로. 잇몸은 위·아래가 붙은 한 조각(정점 ≈ 1,850)이라 면마다 나눈다
    # (통째로 묶으면 입을 벌릴 때 윗잇몸이 턱을 따라 내려와 윗니를 가린다)
    counts = np.bincount(roots, minlength=n)
    comp_up = {int(r): nearer_upper(P[roots == r].mean(axis=0)) for r in np.unique(roots) if counts[r] <= TEETH_SPLIT_VERTS}
    face_up = np.array([comp_up[int(roots[f[0]])] if int(roots[f[0]]) in comp_up else nearer_upper(P[f].mean(axis=0)) for f in T])
    return px, P, T, TU, face_up, helper


def _append_proxy_teeth(R, m, alld, hi, target=None):
    """088 T30k: 클로즈업 치아·잇몸 = MakeHuman teeth_base 프록시(CC0) 원본 형상(감량 없음, 치아 하나하나·잇몸).
    프록시는 기준 정점 3개 + 오프셋의 강체 배치라 델타를 그대로 옮기면 입을 벌려도 아래 치아가 안 내려간다 →
    연결 성분(치아 한 개·잇몸 조각)마다 위·아래 치아 헬퍼 중 가까운 쪽 표면에 묶어 그 델타(표정·얼굴·바탕 머리)를 받는다.
    target(삼각형)을 주면 위·아래 각각 절반으로 감량(LOD0 — 환호 장면에서 클로즈업 2명 밖 선수도 크게 잡힌다, FR-58 같은 모델·텍스처)."""
    px, P, T, TU, face_up, helper = proxy_teeth_split(R)
    for part in ("teeth_upper", "teeth_lower"):
        sel = list(np.flatnonzero(face_up == (part == "teeth_upper")))
        if not sel:
            continue
        vs = np.unique(T[sel].ravel())
        remap = {int(v): i for i, v in enumerate(vs)}
        co = P[vs]
        fl = [tuple(remap[int(v)] for v in T[i]) for i in sel]
        fuv = [tuple(map(tuple, px.uv[TU[i]])) for i in sel]
        if target is not None:
            co, f2, u2 = _decimate_faces(co, fl, target // 2, fuv)
            fl, fuv = [], []
            for f, u in zip(f2, u2):          # 감량 결과의 다각형 → 부채꼴 삼각형
                for k in range(1, len(f) - 1):
                    fl.append((f[0], f[k], f[k + 1]))
                    fuv.append((u[0], u[k], u[k + 1]))
        b = Binding.__new__(Binding)
        b.tri = np.zeros((len(co), 3), dtype=np.int64)
        b.bc = np.zeros((len(co), 3))
        tree = helper[part][0]
        for i, p_ in enumerate(co):
            (t_, bc), _ = _bind_point(R, tree, p_)
            b.tri[i], b.bc[i] = t_, bc
        W = np.zeros((len(co), m.W.shape[1]))
        W[:, hi] = 1.0
        D = {k: (b.apply(alld[k]) if k in alld else np.zeros((len(co), 3))) for k in m.deltas}
        uvs = [tuple(map(tuple, teeth_uv(np.array(u)))) for u in fuv]
        append_part(m, co, W, D, fl, BODY, uvs, "mouth_" + part)


def _all_deltas(R):
    d = dict(R.body_deltas)
    d.update(R.extra_deltas)
    return d


def _assemble(pos, W, faces, fuv, fpart, deltas, border_plane=None):
    """피부/유니폼 정점 나누기 + 셸 띄움 + 턱 + UV. border_plane {원래 정점: 바깥 방향}: 경계 정점의 띄움·턱 방향
    (평면 안·고리에 수직) — 고리가 평면을 벗어나거나 순서가 꼬이지 않게 한다 (057 FR-01)."""
    border_plane = border_plane or {}
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
    for part, (off, iters) in kit_parts().items():
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
    for i, v in enumerate(new_src):
        o = border_plane.get(v)
        if o is not None:
            # 경계 정점: 띄움 방향 = 고리 이웃 (2 × BORDER_NORMAL_SPAN + 1)정점 법선 평균에서 고리 접선 성분을 뺀 것 (안쪽 정점과 같은 쪽으로 떠서 면이 접히지 않고,
            # 접선 성분이 없어 고리 순서가 꼬이지 않는다). 평균이 고리 바깥과 반대면(얇은 면 법선 뒤집힘) 평면 안 바깥 방향을 쓴다.
            outw, tg, around = o
            q = N0[list(around)].sum(axis=0)
            q = q - (q @ tg) * tg
            ln = np.linalg.norm(q)
            N[i] = q / ln if ln > 1e-6 else outw  # 5정점 평균이면 얇은 면 법선 뒤집힘은 묻힌다 — 바깥 방향 판정(중심 기준)은 볼록하지 않은 고리에서 틀려 쓰지 않는다
    fuv0 = fuv   # 원래 면 번호 기준 (밑깔개가 쓴다)
    boot_faces = _boot_hull(P, parts, border, N, nf)
    if boot_faces:
        keep = [k for k in range(len(nf)) if parts[nf[k][0]] != "boot"]
        drop = len(nf) - len(keep)
        nf = [nf[k] for k in keep] + boot_faces
        face_part = [face_part[k] for k in keep] + ["boot"] * len(boot_faces)
        fuv = [fuv[k] for k in keep] + [None] * len(boot_faces)   # 축구화는 kit_uvs
    base = P.copy()
    planes = {pl[0]: pl for pl in cut_planes()}
    for i in range(len(P)):
        part = parts[i]
        if part in kit_parts() and _LOD[0] < 2:
            # LOD2는 띄우지 않는다(턱이 없으니 틈이 생기지 않게). 062 FR-05: 소매·반바지·양말 밑단 띠에서는 몸에 붙게 줄인다
            P[i] = P[i] + N[i] * (kit_parts()[part][0] * _hem_taper(base[i], part, Wn[i], planes)
                                  + _kit_extra(base[i], part, Wn[i], N[i], planes))
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

    def rim_vert(i, depth, outer=False):
        key = (i, outer)
        if key in rim_of:
            return rim_of[key]
        j = len(P) + len(extra_P)
        extra_P.append(P[i] - N[i] * RIM_SPLIT if outer else base[i] - N[i] * depth)
        extra_W.append(Wn[i])
        for k in D:
            extra_D[k].append(D[k][i])
        extra_part.append(parts[i])
        rim_of[key] = j
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
        o0 = kit_parts().get(p0, (0.0, 0))[0]
        o1 = kit_parts().get(p1, (0.0, 0))[0]
        if abs(o0 - o1) < 1e-9 and "skin" not in (p0, p1):
            continue
        if o0 >= o1:
            oa, ob, part = a0, b0, p0
        else:
            oa, ob, part = a1, b1, p1
        depth = RIM.get(part, 0.003)
        ra, rb = rim_vert(oa, depth), rim_vert(ob, depth)
        # 057: 턱 면은 셸 바깥 정점의 복제본(RIM_SPLIT만큼 안쪽)을 쓴다 — 셸과 같은 위치면 Unity 굽기(위치 용접 법선)가
        #      수직 턱 면 법선을 셸 경계에 섞어 칼라 띠에 톱니 음영이 생긴다. 0.2 mm 떨어뜨려 용접 격자(0.01 mm)에서 갈라 둔다
        da, db = rim_vert(oa, 0.0, outer=True), rim_vert(ob, 0.0, outer=True)
        # 면 방향: 바깥 면의 변 a→b 와 반대로 감아 바깥을 향하게. UV 부위는 바깥 면의 부위
        rim_faces.append(((db, da, ra, rb), face_part[f0] if part == parts[a0] else face_part[f1]))
    # 062 T17a 밑깔개: 바깥 옷 밑단 띠 면을 안쪽 부위로 한 겹 더 (경계 정점은 안쪽 부위 정점을 그대로 써서 이어진다)
    under_faces = []
    under_of = {}
    for fi, (f, part) in enumerate(zip(faces, fpart)):
        uk = underlay_kind(pos[list(f)], part, planes, _LOD[0])
        if uk is None:
            continue
        inner = uk[1]
        g = GROUP[inner]
        idx = []
        for v in f:
            if (v, g) in key_of:
                idx.append(key_of[(v, g)])
                continue
            if (v, g) not in under_of:
                under_of[(v, g)] = len(P) + len(extra_P)
                off = (UNDER_SHORTS_OFF if inner == "shorts" else 0.0) - UNDER_SINK[uk[0]] * max(_side(pos[v], planes[uk[2]]), 0.0)
                extra_P.append(pos[v] + N0[v] * off)
                extra_W.append(W[v])   # 셔츠 밑단 띠와 같은 원래 가중치 (보조 뼈 몫은 셔츠에만 — 허벅지 몫은 둘 다 그대로, T17c)
                for k in D:
                    extra_D[k].append(deltas[k][v])
                extra_part.append(g)
            idx.append(under_of[(v, g)])
        under_faces.append((tuple(idx), fi, inner))
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
    m.under = []   # 밑깔개 면 인덱스 (검사용)
    for f, fi, inner in under_faces:
        m.under.append(len(m.faces))
        if inner == "skin":
            m.faces.append((f, BODY, tuple(fuv0[fi]), "skin", None))
        else:
            m.faces.append((f, KIT, kit_uvs(P, f, inner), inner, None))
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


COLLAR_UV_AFFINE = 0.05  # 칼라 평면 아래 이 거리(m)까지는 v 높이 = Y1 + d (위치의 1차식 → 삼각형 안 UV 보간이 정확, 띠 경계가 꺾이지 않는다)
COLLAR_UV_BLEND = 0.05   # 그 아래 이 거리에 걸쳐 원래 높이 매핑으로 이어진다 (띠가 생기지 않는 구간)


def shirt_v_height(p):
    """셔츠 v 계산용 높이 (057 FR-04). 칼라 포인트색 띠(v ≥ 0.96 ≈ 경계에서 2.3 cm)가 기울어진 칼라 평면을 따라가도록,
    평면 아래 COLLAR_UV_AFFINE 안에서는 높이 = SHIRT_Y1 + d (d = 평면 거리, 아래 음수)로 둔다. 이 값은 위치의 1차식이라
    큰 삼각형 안에서도 선형 보간이 정확해 띠 경계가 삼각형 변을 따라 톱니가 되지 않는다. 그 아래는 원래 높이로 섞되
    SHIRT_Y1 − COLLAR_UV_AFFINE 위로는 못 올라가게 해 등·어깨로 띠가 번지지 않는다. 번호·이름은 UV 영역 고정이라 겹치지 않는다."""
    d = _side(p, cut_planes()[0])
    y = float(p[1])
    if d >= -COLLAR_UV_AFFINE:
        return U.SHIRT_Y1 + min(d, 0.0)
    w = min(max(1.0 + (d + COLLAR_UV_AFFINE) / COLLAR_UV_BLEND, 0.0), 1.0)
    return min(y + (U.SHIRT_Y1 + d - y) * w, U.SHIRT_Y1 - COLLAR_UV_AFFINE)


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
            y = shirt_v_height(p)
            if fc % 1.0 <= 0.5:
                fl = min(max(fv - math.floor(fc), 0.0), 0.5)
                out.append(U.torso_shirt(fl, y))
            else:
                fl = min(max(fv - math.floor(fc), 0.5), 1.0)
                out.append(U.torso_shirt(fl, y))
        return tuple(out)
    if base == "sleeve" and suit_mode():
        # 재킷 소매: 위팔 + 아래팔(소매 끝까지) 길이로 v를 잰다 — 소맷부리(v ≥ 0.96)가 손목 쪽 끝이 되게
        h, d, x, z, L = _limb_frame(sn + "UpperArm")
        h2, d2, x2, z2, L2 = _limb_frame(sn + "LowerArm")
        tot = L + L2 * SUIT_SLEEVE_T
        fore = float((c - h2) @ d2) > 0.0
        fh, fx, fz = (h2, x2, z2) if fore else (h, x, z)
        fs = _unwrap([_angle(float((p - fh) @ fx), float((p - fh) @ fz)) for p in pts])
        vs = [(L + max(float((p - h2) @ d2), 0.0)) / tot if fore else float((p - h) @ d) / tot for p in pts]
        return tuple(U.sleeve(side, min(max(fv - math.floor(fs[0]), 0.0), 1.0), v) for v, fv in zip(vs, fs))
    if base == "sleeve":
        h, d, x, z, L = _limb_frame(sn + "UpperArm")
        fs = _unwrap([_angle(float((p - h) @ x), float((p - h) @ z)) for p in pts])
        return tuple(U.sleeve(side, min(max(fv - math.floor(fs[0]), 0.0), 1.0), float((p - h) @ d) / (L * SLEEVE_T))
                     for p, fv in zip(pts, fs))
    if base == "shorts":
        y_hem = SUIT_TROUSER_Y if suit_mode() else S.HEAD[sn + "LowerLeg"][1] + SHORTS_ABOVE_KNEE
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
