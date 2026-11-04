"""MPFB2 CC0 에셋 → 기준 선수 표면 (specs/056 FR-01~03·10~12).

MPFB2 확장(GPL 코드)은 **생성 도구로만** 쓴다. 산출물(glb·json·png)에는 CC0 에셋(베이스 메시·타깃·리그·가중치)에서 나온 값만 들어간다.
쓰는 MPFB 파일은 `used_files()`가 모으고, 검사(checks.licenses)가 `Art/ThirdParty/MPFB2/assets.json`(파일·sha256)과 대조한다.

좌표: 이 모듈의 출력은 **선수 좌표**(space.py, X 오른쪽·Y 위·Z 정면, 미터, 원점 = 두 발 사이 바닥).
MPFB 사람은 Blender에서 Z 위·−Y 정면으로 만들어진다 → space.from_blender 그대로.

기준 선수(REFERENCE_MACRO): 젊은 남성, 이상 비율, 근육 0.6(FR-11 하한), 체중 0.5, 키 매크로 0.75(머리 높이 ≈ 키/7.7, FR-12 — 0.5면 1/7.3). 키 1.80 m로 균등 스케일(키 변화는 Unity 루트 스케일, FR-10).
레스트 포즈 재정렬(repose): MPFB 기본 포즈(다리 벌림·팔꿈치 굽힘)를 043 레스트(다리 수직, 팔 관상면 45° 일직선)로
MPFB game_engine 리그·가중치(CC0)의 선형 블렌드 스키닝으로 옮긴다 — 043 클립(레스트 기준 델타)이 그대로 맞는다.
델타(타깃·체형)는 같은 정점별 회전으로 옮긴다(정점 위치 + 델타 → 회전, 이동은 공유).
"""
import hashlib
import json
import math
import os

import numpy as np

from .space import from_blender

MPFB_MODULE = os.environ.get("PLAYER_MPFB_MODULE", "bl_ext.blender_org.mpfb")  # 테스트: 없는 이름 → 종료 코드 4
MPFB_VERSION = "2.0.18"
INSTALL_HINT = (
    "MPFB2가 없다. 설치: /Applications/Blender.app/Contents/MacOS/Blender --online-mode --command extension sync && "
    "/Applications/Blender.app/Contents/MacOS/Blender --online-mode --command extension install mpfb --enable "
    f"(버전 {MPFB_VERSION} 고정, specs/056 FR-03)"
)

REF_HEIGHT = 1.80
ARM_ANGLE = 45.0

REFERENCE_MACRO = {
    "gender": 1.0, "age": 0.5, "muscle": 0.6, "weight": 0.5, "proportions": 1.0, "height": 0.75,
    "cupsize": 0.5, "firmness": 0.5, "race": {"asian": 1 / 3, "caucasian": 1 / 3, "african": 1 / 3},
}
# 체형 셰이프 (043 이름 유지). 값은 기준에서 바꾸는 MPFB 매크로 (FR-11: muscle 0.6~0.9, weight는 BMI에서 — 043 Thin/Heavy가 BMI 유도)
BODY_MACROS = {
    "Thin": {"weight": 0.2},
    "Muscular": {"muscle": 0.9},
    "Heavy": {"weight": 0.85},
}

# MPFB game_engine 뼈 → 043 뼈 (Humanoid 이름). 손가락은 Hand로 합친다.
BONE_MAP = {
    "pelvis": "Hips", "spine_01": "Spine", "spine_02": "Chest", "spine_03": "UpperChest", "neck_01": "Neck", "head": "Head",
}
for _s, _n in (("l", "Left"), ("r", "Right")):
    BONE_MAP.update({
        f"clavicle_{_s}": f"{_n}Shoulder", f"upperarm_{_s}": f"{_n}UpperArm", f"lowerarm_{_s}": f"{_n}LowerArm",
        f"hand_{_s}": f"{_n}Hand", f"thigh_{_s}": f"{_n}UpperLeg", f"calf_{_s}": f"{_n}LowerLeg",
        f"foot_{_s}": f"{_n}Foot", f"ball_{_s}": f"{_n}Toes",
    })
    for _f in ("thumb", "index", "middle", "ring", "pinky"):
        for _k in ("01", "02", "03"):
            BONE_MAP[f"{_f}_{_k}_{_s}"] = f"{_n}Hand"

# 043 뼈 계층 (부모가 먼저). tail은 지정 자식의 head, 잎 뼈는 MPFB tail.
OUR_BONES = [
    ("Hips", None, "Spine"), ("Spine", "Hips", "Chest"), ("Chest", "Spine", "UpperChest"), ("UpperChest", "Chest", "Neck"),
    ("Neck", "UpperChest", "Head"), ("Head", "Neck", None),
]
for _n in ("Left", "Right"):
    OUR_BONES += [(f"{_n}Shoulder", "UpperChest", f"{_n}UpperArm"), (f"{_n}UpperArm", f"{_n}Shoulder", f"{_n}LowerArm"),
                  (f"{_n}LowerArm", f"{_n}UpperArm", f"{_n}Hand"), (f"{_n}Hand", f"{_n}LowerArm", None)]
for _n in ("Left", "Right"):
    OUR_BONES += [(f"{_n}UpperLeg", "Hips", f"{_n}LowerLeg"), (f"{_n}LowerLeg", f"{_n}UpperLeg", f"{_n}Foot"),
                  (f"{_n}Foot", f"{_n}LowerLeg", f"{_n}Toes"), (f"{_n}Toes", f"{_n}Foot", None)]
OUR_NAMES = [b[0] for b in OUR_BONES]
OUR_PARENT = {b[0]: b[1] for b in OUR_BONES}
_INV_MAP = {}
for _k, _v in BONE_MAP.items():
    _INV_MAP.setdefault(_v, _k)  # 043 뼈 → 대표 MPFB 뼈 (손은 hand_*)


class MpfbMissing(Exception):
    pass


_USED = set()


def _note(path):
    _USED.add(os.path.realpath(path))


def used_files():
    """이번 생성에서 읽은 MPFB 데이터 파일 (데이터 폴더 기준 상대 경로, 정렬)."""
    root = os.path.realpath(data_dir())
    return sorted(os.path.relpath(p, root) for p in _USED)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


_MOD = None


def require():
    """MPFB 확장을 켜고 버전을 확인한다. 없으면 MpfbMissing(설치 안내)."""
    global _MOD
    if _MOD is not None:
        return _MOD
    import addon_utils
    import importlib
    try:
        importlib.import_module(MPFB_MODULE)
    except ImportError:
        raise MpfbMissing(INSTALL_HINT)
    addon_utils.enable(MPFB_MODULE, default_set=True)
    mod = importlib.import_module(MPFB_MODULE)
    man = os.path.join(os.path.dirname(mod.__file__), "blender_manifest.toml")
    ver = None
    with open(man, encoding="utf-8") as f:
        for line in f:
            if line.startswith("version"):
                ver = line.split("=", 1)[1].strip().strip('"')
                break
    if ver != MPFB_VERSION:
        raise MpfbMissing(f"MPFB 버전 {ver} ≠ 고정 {MPFB_VERSION}. {INSTALL_HINT}")
    _MOD = mod
    return mod


def data_dir():
    mod = require()
    return os.path.join(os.path.dirname(mod.__file__), "data")


def version():
    return MPFB_VERSION


# ---------------------------------------------------------------- MPFB 호출

def _services():
    require()
    from bl_ext.blender_org.mpfb.services.humanservice import HumanService
    from bl_ext.blender_org.mpfb.services.targetservice import TargetService
    from bl_ext.blender_org.mpfb.services.objectservice import ObjectService
    return HumanService, TargetService, ObjectService


def _macro(over=None):
    m = json.loads(json.dumps(REFERENCE_MACRO))
    for k, v in (over or {}).items():
        m[k] = v
    return m


def _evaluated(obj):
    import bpy
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    tmp = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", tmp)
    co[:] = tmp
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    # Blender (x,y,z) → 선수 (−x, z, −y)
    return np.stack([-co[:, 0], co[:, 2], -co[:, 1]], axis=1)


def _remove(obj):
    import bpy
    me = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def _human(over=None):
    HS, TS, OS = _services()
    obj = HS.create_human(mask_helpers=False, detailed_helpers=True, extra_vertex_groups=True, feet_on_ground=False,
                          scale=0.1, macro_detail_dict=_macro(over))
    return obj


def _macro_files(obj):
    """create_human이 읽은 매크로 타깃 파일을 기록한다 (라이선스 대조용). 셰이프키 이름 = 디코딩된 파일 이름."""
    HS, TS, OS = _services()
    if not obj.data.shape_keys:
        return
    want = set()
    for kb in obj.data.shape_keys.key_blocks:
        if kb.name == "Basis":
            continue
        nm = TS.decode_shapekey_name(kb.name)
        want.add(nm.split("/")[-1].replace("macrodetail-", "", 1))
    root = os.path.join(data_dir(), "targets", "macrodetails")
    for dp, _, files in sorted(os.walk(root)):
        for f in sorted(files):
            if f.endswith(".target.gz") and f[:-len(".target.gz")] in want:
                _note(os.path.join(dp, f))


# ---------------------------------------------------------------- 레스트 재정렬 (LBS)

def _qrot(a, b):
    """단위 벡터 a → b 최소 회전 행렬."""
    a = a / np.linalg.norm(a)
    b = b / np.linalg.norm(b)
    v = np.cross(a, b)
    c = float(np.dot(a, b))
    s = float(np.linalg.norm(v))
    if s < 1e-12:
        return np.eye(3)
    k = v / s
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    ang = math.atan2(s, c)
    return np.eye(3) + math.sin(ang) * K + (1 - math.cos(ang)) * (K @ K)


def _target_dirs():
    s45, c45 = math.sin(math.radians(ARM_ANGLE)), math.cos(math.radians(ARM_ANGLE))
    out = {}
    for side, sx in (("l", -1.0), ("r", 1.0)):
        down = np.array([0.0, -1.0, 0.0])
        arm = np.array([sx * s45, -c45, 0.0])
        out[f"thigh_{side}"] = down
        out[f"calf_{side}"] = down
        out[f"upperarm_{side}"] = arm
        out[f"lowerarm_{side}"] = arm
        out[f"hand_{side}"] = arm
    return out


class Reference:
    """기준 선수: 정점(전체, 헬퍼 포함)·면·UV·그룹·관절·가중치 + 델타 계산기."""

    def __init__(self):
        HS, TS, OS = _services()
        d = data_dir()
        self.rig_def = json.load(open(os.path.join(d, "rigs", "standard", "rig.game_engine.json")))
        _note(os.path.join(d, "rigs", "standard", "rig.game_engine.json"))
        wdef = json.load(open(os.path.join(d, "rigs", "standard", "weights.game_engine.json")))
        _note(os.path.join(d, "rigs", "standard", "weights.game_engine.json"))
        _note(os.path.join(d, "3dobjs", "base.obj"))
        _note(os.path.join(d, "mesh_metadata", "basemesh_vertex_groups.json"))
        obj = _human()
        _macro_files(obj)
        me = obj.data
        self.n = len(me.vertices)
        self.faces = [tuple(p.vertices) for p in me.polygons]
        uvl = me.uv_layers[0].data
        self.face_uvs = [tuple(tuple(uvl[li].uv) for li in p.loop_indices) for p in me.polygons]
        names = {g.index: g.name for g in obj.vertex_groups}
        groups = {}
        for v in me.vertices:
            for g in v.groups:
                groups.setdefault(names[g.group], []).append(v.index)
        self.groups = {k: sorted(v) for k, v in sorted(groups.items())}
        raw = _evaluated(obj)
        self._raw_base = raw
        _remove(obj)
        # MPFB 뼈 가중치 (정점별 dict)
        self.mpfb_bones = sorted(self.rig_def)
        W = np.zeros((self.n, len(self.mpfb_bones)))
        bi = {b: i for i, b in enumerate(self.mpfb_bones)}
        for b, lst in wdef["weights"].items():
            if b not in bi:
                continue
            for vi, w in lst:
                W[vi, bi[b]] += w
        tot = W.sum(axis=1)
        # 가중치 없는 정점(헬퍼 일부)은 머리 뼈에 둔다 → 아래에서 가장 가까운 가중치 정점을 따른다
        self._fill_unweighted(W, tot, raw)
        W /= W.sum(axis=1, keepdims=True)
        self.W = W
        # 원래 포즈 관절 (MPFB 뼈 head/tail)
        self.mpfb_head = {b: self._joint(self.rig_def[b]["head"], raw) for b in self.mpfb_bones}
        self.mpfb_tail = {b: self._joint(self.rig_def[b]["tail"], raw) for b in self.mpfb_bones}
        self._pose_mats()
        posed = self._apply(raw)
        # 스케일·바닥
        body = np.array(self.groups["body"])
        y0, y1 = posed[body, 1].min(), posed[body, 1].max()
        self.scale = REF_HEIGHT / (y1 - y0)
        self.offset = np.array([0.0, -y0, 0.0])
        self.verts = (posed + self.offset) * self.scale
        self.joints = self._our_joints()
        self.weights = self._our_weights()

    # -- 관절
    def _joint(self, spec, co):
        st = spec["strategy"]
        if st == "CUBE":
            ids = self.groups[spec["cube_name"]]
        elif st in ("MEAN", "VERTEX"):
            ids = spec.get("vertex_indices") or [spec["vertex_index"]]
        else:
            raise ValueError(f"모르는 관절 방식 {st}")
        return co[np.array(ids)].mean(axis=0)

    def _fill_unweighted(self, W, tot, co):
        bad = np.where(tot <= 1e-9)[0]
        if len(bad) == 0:
            return
        good = np.where(tot > 1e-9)[0]
        for i in bad:
            j = good[np.argmin(((co[good] - co[i]) ** 2).sum(axis=1))]
            W[i] = W[j]

    def _pose_mats(self):
        """MPFB 뼈마다 (월드 회전 R, 포즈 head). 부모가 먼저 오도록 순회."""
        dirs = _target_dirs()
        order, seen = [], set()

        def visit(b):
            if b in seen:
                return
            p = self.rig_def[b].get("parent") or ""
            if p:
                visit(p)
            seen.add(b)
            order.append(b)

        for b in self.mpfb_bones:
            visit(b)
        R, H = {}, {}
        for b in order:
            p = self.rig_def[b].get("parent") or ""
            h0 = self.mpfb_head[b]
            if p:
                Rp = R[p]
                H[b] = H[p] + Rp @ (h0 - self.mpfb_head[p])
            else:
                Rp = np.eye(3)
                H[b] = h0.copy()
            if b in dirs:
                d0 = Rp @ (self.mpfb_tail[b] - h0)
                R[b] = _qrot(d0, dirs[b]) @ Rp
            else:
                R[b] = Rp
        self.R, self.H = R, H
        self._Rm = np.stack([R[b] for b in self.mpfb_bones])  # (B,3,3)
        self._h0 = np.stack([self.mpfb_head[b] for b in self.mpfb_bones])
        self._h1 = np.stack([H[b] for b in self.mpfb_bones])

    def _apply(self, co):
        """LBS: v' = Σ w_b (H_b + R_b (v − h0_b))."""
        out = np.zeros_like(co)
        for bi in range(len(self.mpfb_bones)):
            w = self.W[:, bi]
            nz = w > 0
            if not nz.any():
                continue
            local = co[nz] - self._h0[bi]
            out[nz] += w[nz, None] * (local @ self._Rm[bi].T + self._h1[bi])
        return out

    def _apply_delta(self, delta):
        out = np.zeros_like(delta)
        for bi in range(len(self.mpfb_bones)):
            w = self.W[:, bi]
            nz = w > 0
            if not nz.any():
                continue
            out[nz] += w[nz, None] * (delta[nz] @ self._Rm[bi].T)
        return out

    def _our_joints(self):
        """043 뼈 이름 → (head, tail) 선수 좌표 (레스트 = 재정렬 후)."""
        def P(v):
            return (v + self.offset) * self.scale
        heads = {n: P(self.H[_INV_MAP[n]]) for n in OUR_NAMES}
        out = {}
        for n, parent, child in OUR_BONES:
            mb = _INV_MAP[n]
            if child:
                t = heads[child]
            elif n.endswith("Hand"):
                # 손 꼬리 = 가운데 손가락 끝 (MPFB hand 뼈는 손목 토막) — 043 손 길이·접촉점 의미 유지
                f = "middle_03_" + ("l" if n.startswith("Left") else "r")
                t = P(self.H[f] + self.R[f] @ (self.mpfb_tail[f] - self.mpfb_head[f]))
            else:
                t = P(self.H[mb] + self.R[mb] @ (self.mpfb_tail[mb] - self.mpfb_head[mb]))
            out[n] = (heads[n], t)
        # 좌우 대칭 강제 (x 부호만 다르게) — 메시 비대칭 잡음이 클립에 들어가지 않게
        for n in OUR_NAMES:
            if n.startswith("Left"):
                r = "Right" + n[4:]
                hl, tl = out[n]
                hr, tr = out[r]
                h = (np.abs(hl) + np.abs(hr)) / 2
                t = (np.abs(tl) + np.abs(tr)) / 2
                h[1], t[1] = (hl[1] + hr[1]) / 2, (tl[1] + tr[1]) / 2
                h[2], t[2] = (hl[2] + hr[2]) / 2, (tl[2] + tr[2]) / 2
                out[n] = (h * np.array([-1, 1, 1]), t * np.array([-1, 1, 1]))
                out[r] = (h.copy(), t.copy())
            elif not n.startswith("Right"):
                h, t = out[n]
                out[n] = (h * np.array([0, 1, 1]), t * np.array([0, 1, 1]))
        return out

    def _our_weights(self):
        """정점 × 043 뼈 가중치 (B=22)."""
        Wo = np.zeros((self.n, len(OUR_NAMES)))
        oi = {n: i for i, n in enumerate(OUR_NAMES)}
        for bi, b in enumerate(self.mpfb_bones):
            if b in BONE_MAP:
                Wo[:, oi[BONE_MAP[b]]] += self.W[:, bi]
            else:  # Root 등 → Hips
                Wo[:, oi["Hips"]] += self.W[:, bi]
        Wo /= Wo.sum(axis=1, keepdims=True)
        return Wo

    # -- 델타
    def _to_ref(self, raw):
        return (self._apply(raw) + self.offset) * self.scale

    def macro_delta(self, over):
        obj = _human(over)
        _macro_files(obj)
        raw = _evaluated(obj)
        _remove(obj)
        return self._apply_delta(raw - self._raw_base) * self.scale

    def target_delta(self, names, weight=1.0):
        """타깃 파일 이름 목록(합산) → 델타 (선수 좌표, 재정렬·스케일)."""
        HS, TS, OS = _services()
        obj = _human()
        for nm in names:
            p = TS.target_full_path(nm)
            if p is None or os.path.basename(p)[:-len(".target.gz")] != nm:
                _remove(obj)
                raise ValueError(f"MPFB 타깃 없음: {nm}")
            _note(p)
            TS.load_target(obj, p, weight=weight)
        raw = _evaluated(obj)
        _remove(obj)
        return self._apply_delta(raw - self._raw_base) * self.scale
