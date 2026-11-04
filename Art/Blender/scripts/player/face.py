"""얼굴 (specs/056 FR-20~24).

- 얼굴 형태는 MPFB2 CC0 얼굴 타깃(코·눈·입·턱·광대·이마·머리·귀·눈썹)을 축으로 쓴다. 축 하나 = 셰이프키 2개(`Face_<축>_pos`·`_neg`).
  선수마다 가중치는 Unity FaceGenerator(선수 id 시드)가 정한다 — 실존 인물 입력 경로 없음(FR-24).
- 랜드마크: 눈 중심·눈 사이 거리 d로 정한 정면/옆 좌표에서 기준 표면으로 광선을 쏴 맞은 점(무게중심) → 기준 위치 + 키별 델타.
  `player.face.json`으로 내보내 C# FaceLandmarks가 거리 벡터를 계산한다(AC-04).
- 눈: MPFB 눈 헬퍼(안구) 형상을 그대로 쓴다(타깃이 함께 움직인다). UV는 parts 아틀라스 눈 칸(평면 투영, 홍채 가운데).
- 수염: 4종(none·stubble·short·full). 형상은 `Beard_*` 셰이프키(피부 법선 방향 두께), 색은 피부 텍스처 수염 채널(textures).
- HideHead: 클로즈업 머리가 대신 그려질 때 몸 메시 머리를 머리 관절로 접는다(FR-61).
"""
import math

import numpy as np

from . import mpfb_src as MS
from . import skeleton as S

# (축 id, 음 방향 타깃들, 양 방향 타깃들, 범위). 좌우 타깃은 양쪽을 같이 (얼굴 비대칭은 쓰지 않는다).
AXES = [
    ("nose_width", ["nose-scale-horiz-decr"], ["nose-scale-horiz-incr"], 0.8),
    ("nose_length", ["nose-scale-vert-decr"], ["nose-scale-vert-incr"], 0.7),
    ("nose_depth", ["nose-scale-depth-decr"], ["nose-scale-depth-incr"], 0.7),
    ("nose_hump", ["nose-hump-decr"], ["nose-hump-incr"], 0.8),
    ("nose_tip_width", ["nose-point-width-decr"], ["nose-point-width-incr"], 0.8),
    ("nose_nostrils", ["nose-nostrils-width-decr"], ["nose-nostrils-width-incr"], 0.8),
    ("nose_base", ["nose-base-down"], ["nose-base-up"], 0.7),
    ("nose_curve", ["nose-curve-concave"], ["nose-curve-convex"], 0.7),
    ("mouth_width", ["mouth-scale-horiz-decr"], ["mouth-scale-horiz-incr"], 0.7),
    ("mouth_height", ["mouth-trans-down"], ["mouth-trans-up"], 0.5),
    ("lip_lower", ["mouth-lowerlip-volume-decr"], ["mouth-lowerlip-volume-incr"], 0.8),
    ("lip_upper", ["mouth-upperlip-volume-decr"], ["mouth-upperlip-volume-incr"], 0.8),
    ("chin_prominent", ["chin-prominent-decr"], ["chin-prominent-incr"], 0.8),
    ("chin_width", ["chin-width-decr"], ["chin-width-incr"], 0.8),
    ("chin_height", ["chin-height-decr"], ["chin-height-incr"], 0.7),
    ("jaw", ["chin-jaw-drop-decr"], ["chin-jaw-drop-incr"], 0.7),
    ("cheekbones", ["l-cheek-bones-decr", "r-cheek-bones-decr"], ["l-cheek-bones-incr", "r-cheek-bones-incr"], 0.8),
    ("cheek_volume", ["l-cheek-volume-decr", "r-cheek-volume-decr"], ["l-cheek-volume-incr", "r-cheek-volume-incr"], 0.7),
    ("eye_size", ["l-eye-scale-decr", "r-eye-scale-decr"], ["l-eye-scale-incr", "r-eye-scale-incr"], 0.5),
    ("eye_height", ["l-eye-trans-down", "r-eye-trans-down"], ["l-eye-trans-up", "r-eye-trans-up"], 0.5),
    ("eye_spacing", ["l-eye-trans-in", "r-eye-trans-in"], ["l-eye-trans-out", "r-eye-trans-out"], 0.5),
    ("eye_fold", ["l-eye-eyefold-down", "r-eye-eyefold-down"], ["l-eye-eyefold-up", "r-eye-eyefold-up"], 0.6),
    ("brow_height", ["eyebrows-trans-down"], ["eyebrows-trans-up"], 0.6),
    ("brow_angle", ["eyebrows-angle-down"], ["eyebrows-angle-up"], 0.6),
    ("brow_ridge", ["forehead-nubian-decr"], ["forehead-nubian-incr"], 0.8),
    ("forehead_height", ["forehead-scale-vert-decr"], ["forehead-scale-vert-incr"], 0.6),
    ("head_width", ["head-scale-horiz-decr"], ["head-scale-horiz-incr"], 0.4),
    ("head_depth", ["head-scale-depth-decr"], ["head-scale-depth-incr"], 0.4),
    ("ear_size", ["l-ear-scale-decr", "r-ear-scale-decr"], ["l-ear-scale-incr", "r-ear-scale-incr"], 0.6),
    ("ear_flap", ["l-ear-flap-decr", "r-ear-flap-decr"], ["l-ear-flap-incr", "r-ear-flap-incr"], 0.6),
]
BEARDS = [("none", 0.0), ("stubble", 0.0006), ("short", 0.0022), ("full", 0.0055)]  # (id, 두께 m)
EYE_GROUPS = ("helper-l-eye", "helper-r-eye")


def key_names():
    out = []
    for a, _, _, _ in AXES:
        out += [f"Face_{a}_pos", f"Face_{a}_neg"]
    return out


# ---------------------------------------------------------------- 기준 표면 도우미

def vertex_normals(R):
    body = set(R.groups["body"])
    n = np.zeros_like(R.verts)
    for f in R.faces:
        if not all(v in body for v in f):
            continue
        p = R.verts[list(f)]
        fn = np.zeros(3)
        for k in range(1, len(f) - 1):
            fn += np.cross(p[k] - p[0], p[k + 1] - p[0])
        for v in f:
            n[v] += fn
    ln = np.linalg.norm(n, axis=1, keepdims=True)
    ln[ln < 1e-12] = 1.0
    # MPFB 면 감김은 Blender 좌표 기준 — 선수 좌표 변환(반사, 행렬식 −1)에서 감김이 뒤집히므로 부호를 바꾼다
    n = -n / ln
    tip = int(np.argmax(np.where(np.abs(R.verts[:, 0]) < 0.005, R.verts[:, 2], -9)))
    assert n[tip, 2] > 0.5, "법선이 안쪽을 향한다 (코끝 법선 z ≤ 0.5)"
    return n


def eye_centers(R):
    return [R.verts[R.groups[g]].mean(axis=0) for g in EYE_GROUPS]  # [왼눈(−X), 오른눈(+X)]


def eye_frame(R):
    el, er = eye_centers(R)
    if el[0] > er[0]:
        el, er = er, el
    d = float(np.linalg.norm(er - el))
    c = (el + er) / 2
    return c, d


def beard_mask(R, p):
    """수염 자리 0~1 (정점 위치 p, 선수 좌표). 랜드마크 기준 비율로 정한다."""
    c, d = eye_frame(R)
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    rel_y = (y - c[1]) / d
    rel_x = np.abs(x - c[0]) / d
    front = z > (c[2] - 1.6 * d)
    # 위 경계: 광대 아래(옆으로 갈수록 낮게), 아래 경계: 턱 밑 목 위쪽
    top = -0.95 - 0.25 * np.clip(rel_x - 0.4, 0, 2)
    m = np.clip((top - rel_y) / 0.18, 0, 1) * np.clip((rel_y + 3.1) / 0.3, 0, 1)
    # 입술·입 둘레 비우기 (타원)
    mouth_y = -1.5
    lip = ((rel_x / 0.62) ** 2 + ((rel_y - mouth_y) / 0.20) ** 2) < 1.0
    m = np.where(lip, 0.0, m)
    # 귀 뒤·뒷목 제외
    m = np.where(front, m, 0.0)
    m = m * np.clip((1.35 - rel_x) / 0.2, 0, 1)
    return m


# ---------------------------------------------------------------- 델타 등록

def register(R):
    """R.extra_deltas에 얼굴·수염·HideHead 델타를 넣는다 (기준 정점 전체, 선수 좌표)."""
    out = {}
    for a, neg, pos, rng in AXES:
        out[f"Face_{a}_pos"] = R.target_delta(pos)
        out[f"Face_{a}_neg"] = R.target_delta(neg)
    N = vertex_normals(R)
    bm = beard_mask(R, R.verts)
    hi = MS.OUR_NAMES.index("Head")
    headw = R.weights[:, hi]
    for bid, th in BEARDS[1:]:
        out[f"Beard_{bid}"] = N * (bm * th)[:, None]
    hc = np.array(S.HEAD["Head"]) + np.array([0.0, 0.06, 0.0])
    k = np.clip((headw - 0.3) / 0.4, 0, 1)
    out["HideHead"] = (hc[None, :] - R.verts) * k[:, None]
    R.extra_deltas.update(out)
    R.beard_mask = bm
    R.normals = N
    return out


def axes_json():
    t = []
    for a, _, _, rng in AXES:
        t.append({"id": f"Face_{a}_pos", "axis": a, "sign": 1, "min": 0.0, "max": rng})
        t.append({"id": f"Face_{a}_neg", "axis": a, "sign": -1, "min": 0.0, "max": rng})
    return t


# ---------------------------------------------------------------- 랜드마크

def _front_spots(d):
    """(이름, x/d, y/d, 방향) 눈 중심 기준. 방향 'front' = +Z에서, 'side' = 바깥 X에서, 'back' = −Z에서."""
    s = [("noseBridge", 0, 0, "front"), ("noseTip", 0, -0.9, "front"), ("philtrum", 0, -1.22, "front"),
         ("upperLip", 0, -1.38, "front"), ("lowerLip", 0, -1.62, "front"), ("chin", 0, -2.2, "front"),
         ("forehead", 0, 0.95, "front"), ("glabella", 0, 0.32, "front"), ("occiput", 0, 0.4, "back")]
    for side, sx in (("L", -1), ("R", 1)):
        s += [(f"browRidge{side}", sx * 0.5, 0.33, "front"), (f"eyeInner{side}", sx * 0.27, 0.0, "front"),
              (f"eyeOuter{side}", sx * 0.78, 0.02, "front"), (f"cheekbone{side}", sx * 0.85, -0.42, "front"),
              (f"alar{side}", sx * 0.33, -0.98, "front"), (f"mouthCorner{side}", sx * 0.47, -1.5, "front"),
              (f"jaw{side}", sx * 0.78, -1.68, "front"), (f"temple{side}", sx * 1.02, 0.45, "front"),
              (f"earTop{side}", sx, 0.15, "side"), (f"earLobe{side}", sx, -0.85, "side")]
    return s


def landmarks(R, tree_tri):
    """→ [{name, base, deltas{키: Δ}}] 기준 위치(선수 좌표)."""
    from mathutils import Vector
    tree, tri = tree_tri
    c, d = eye_frame(R)
    ear_z = float(S.HEAD["Head"][2]) - 0.01
    keys = key_names()
    out = []
    for name, fx, fy, mode in _front_spots(d):
        x, y = c[0] + fx * d, c[1] + fy * d
        if mode == "front":
            o, dirv = Vector((x, y, c[2] + 1.0)), Vector((0, 0, -1))
        elif mode == "back":
            o, dirv = Vector((x, y, c[2] - 1.0)), Vector((0, 0, 1))
        else:
            sx = 1.0 if fx > 0 else -1.0
            o, dirv = Vector((sx * 1.0, y, ear_z)), Vector((-sx, 0, 0))
        loc, nrm, idx, dist = tree.ray_cast(o, dirv)
        if idx is None:
            raise RuntimeError(f"랜드마크 광선이 얼굴에 안 맞았다: {name}")
        t = tri[idx]
        from .body import _bary
        bc = _bary(np.array(loc), R.verts[t[0]], R.verts[t[1]], R.verts[t[2]])
        base = (R.verts[t] * bc[:, None]).sum(axis=0)
        deltas = {}
        for k in keys:
            dd = (R.extra_deltas[k][t] * bc[:, None]).sum(axis=0)
            if np.abs(dd).max() > 1e-7:
                deltas[k] = [round(float(v), 6) for v in dd]
        out.append({"name": name, "base": [round(float(v), 6) for v in base], "deltas": deltas})
    return out


def face_json(R, tree_tri):
    return {"schemaVersion": 1, "targets": axes_json(), "beards": [{"id": b, "thickness": t} for b, t in BEARDS],
            "landmarks": landmarks(R, tree_tri), "eyeDistance": round(eye_frame(R)[1], 6)}


# ---------------------------------------------------------------- 눈 (LOD별 안구)

EYE_REGION = (0.0, 0.75, 0.25, 1.0)   # parts 아틀라스 눈 칸 (u0, v0, u1, v1)
EYE_TRIS = {0: None, 1: 48, 2: 16}     # LOD0 = MPFB 눈 헬퍼 그대로, 그 아래는 감량


def eye_geometry(R):
    """MPFB 눈 헬퍼 면 → [(정점 인덱스(기준) 튜플)]."""
    out = []
    for g in EYE_GROUPS:
        s = set(R.groups[g])
        out += [f for f in R.faces if all(v in s for v in f)]
    return out


def eye_uv(R, p):
    """안구 점 → parts UV (정면 평면 투영, 홍채가 칸 가운데). u는 +1 (셰이더가 parts 텍스처로 보낸다)."""
    el, er = eye_centers(R)
    c = el if p[0] < 0 else er
    r = 0.0125
    fu = 0.5 + (p[0] - c[0]) / (2 * r) * 0.9
    fv = 0.5 + (p[1] - c[1]) / (2 * r) * 0.9
    fu, fv = min(max(fu, 0.0), 1.0), min(max(fv, 0.0), 1.0)
    u0, v0, u1, v1 = EYE_REGION
    return (1.0 + u0 + (u1 - u0) * fu, v0 + (v1 - v0) * fv)


def eyes_covered(R, tree_tri, key_sets):
    """안구 정점이 피부 밖으로 나오는 수 (정면 광선으로 피부 앞에 있으면 노출). key_sets = [{키: 가중치}] 극값 조합."""
    from mathutils.bvhtree import BVHTree
    from mathutils import Vector
    tree, tri = tree_tri
    ids = sorted({v for g in EYE_GROUPS for v in R.groups[g]})
    worst = 0
    cases = 0
    eye_z = [c[2] for c in eye_centers(R)]
    for ks in key_sets:
        V = R.verts.copy()
        for k, w in ks.items():
            V = V + R.extra_deltas[k] * w
        t2 = BVHTree.FromPolygons([tuple(p) for p in V], [tuple(t) for t in tri], all_triangles=True)
        bad = 0
        for i in ids:
            p = V[i]
            # 정면에서 처음 맞는 피부가 안구 점 바로 뒤(0.5~6 mm)에 있으면 안구가 눈꺼풀·피부를 뚫고 나온 것.
            # 눈 틈(구멍)에서는 광선이 머리 안쪽 깊은 곳에 맞으므로 노출로 세지 않는다.
            if p[2] < eye_z[0 if p[0] < 0 else 1]:
                continue  # 안구 뒤쪽 반구는 보이지 않는다
            loc, nrm, idx, dist = t2.ray_cast(Vector((p[0], p[1], p[2] + 0.5)), Vector((0, 0, -1)))
            if loc is not None and 0.0005 < p[2] - loc[2] < 0.006:
                bad += 1
        worst = max(worst, bad)
        cases += 1
    return {"ok": worst == 0, "exposedOutsideLids": worst, "cases": cases}
