"""062 손 (specs/062 FR-10~13): 손 포즈 표 · 손가락 굽힘 회전 · 손 지표 (오목점·손가락 간격·포즈 관통).

손 포즈 = 손가락 마디별 굽힘 각(도). Unity `HandPose`가 같은 표(`player.layout.json` "handPoses")를 읽어
LateUpdate에서 손가락 뼈를 돌린다. 굽힘 축은 레스트 자세에서 정한다: 손바닥 법선 n(손등 → 손바닥), 손가락 방향 d →
축 = d × n (양의 각이 손가락 끝을 손바닥 쪽으로 보낸다). 엄지는 손바닥을 가로질러 접히도록 손 방향 축 회전을 더한다.
"""
import math

import numpy as np
from mathutils import Quaternion, Vector

from . import mpfb_src as MS
from . import skeleton as S

# 마디별 굽힘 (Proximal, Intermediate, Distal) 도. 엄지 마지막 값 = 손바닥 가로지르기(맞섬) 회전.
HAND_POSES = {
    "open": {"Thumb": (0, 5, 5, 0), "Index": (3, 4, 3), "Middle": (3, 4, 3), "Ring": (4, 5, 3), "Little": (5, 6, 4)},
    "relax": {"Thumb": (5, 10, 8, 10), "Index": (15, 20, 10), "Middle": (18, 22, 10), "Ring": (20, 22, 10),
              "Little": (22, 22, 10)},   # 달리기·걷기: 느슨하게 굽힌 손 (반쯤 쥔 손은 극단 체형에서 허벅지에 닿는다)
    "half": {"Thumb": (10, 20, 15, 20), "Index": (35, 45, 25), "Middle": (40, 50, 25), "Ring": (45, 50, 25),
             "Little": (50, 50, 25)},
    "fist": {"Thumb": (12, 20, 15, 25), "Index": (60, 68, 32), "Middle": (68, 75, 34), "Ring": (68, 75, 40),
             "Little": (62, 68, 30)},
}
# 손가락 벌림(도, 손바닥 평면 안에서 가운데 손가락에서 멀어지는 쪽 +) — 편 손에서만
SPREAD = {"open": {"Thumb": 0, "Index": 4, "Middle": 0, "Ring": -4, "Little": -8}}


# 클립 → 손 포즈 (062 FR-11). 앞에서부터 처음 맞는 규칙. (접두사 또는 그룹, 포즈)
HAND_RULES = (
    ("gk_punch", "fist"), ("celebrate_knee", "fist"),
    ("sprint_l", "open"), ("shot_curl_r", "open"), ("run_f_r6", "open"), ("shot_power_r", "open"),  # 팔 궤적이 허벅지 1~2 cm 옆 — 굽히면 닿는다 (T09·T10 관통 측정)
    ("gk_throw", "half"), ("gk_roll", "half"), ("gk_goalkick", "half"), ("gk_punt", "half"), ("gk_kick", "half"),
    ("gk_getup", "open"), ("group:keeper", "open"),          # 잡기·막기·펼치기·대기 = 편 손
    ("celebrate_arms", "open"), ("dejected", "open"), ("throw_in", "open"), ("ref_advantage", "open"),
    ("group:fall", "open"),
    ("idle", "open"), ("walk", "open"), ("exit_walk", "open"),
    ("group:locomotion", "relax"), ("group:flow", "relax"), ("group:duel", "relax"),
    ("group:", "half"),                                      # 나머지(달리기·킥·심판 휘슬·카드 등) = 반쯤 쥔 손
)


def hand_pose_for(clip_id, group):
    for key, pose in HAND_RULES:
        if key.startswith("group:"):
            g = key[len("group:"):]
            if g == "" or g == group:
                return pose
        elif clip_id.startswith(key):
            return pose
    return "half"


def _v(x):
    return Vector(tuple(float(c) for c in x))


def palm_frame(side):
    """→ (손 방향 d, 손바닥 법선 n(손등 → 손바닥), 가로 a(엄지 쪽 → 새끼 쪽)) 레스트 선수 좌표."""
    h, t = _v(S.HEAD[side + "Hand"]), _v(S.TAIL[side + "Hand"])
    d = (t - h).normalized()
    a = (_v(S.HEAD[side + "LittleProximal"]) - _v(S.HEAD[side + "IndexProximal"]))
    a = (a - d * a.dot(d)).normalized()
    n = d.cross(a).normalized()
    # A-포즈에서 손바닥은 몸 안쪽(정중선 쪽)을 본다: 오른손(+X)이면 n.x < 0
    sx = 1.0 if side == "Right" else -1.0
    if n.x * sx > 0:
        n = -n
    return d, n, a


def pose_rotations(name, sides=("Left", "Right")):
    """포즈 이름 → {뼈: 레스트 축 기준 local 델타 Quaternion} (pose.Pose.local에 그대로 넣는다)."""
    out = {}
    table = HAND_POSES[name]
    spread = SPREAD.get(name, {})
    for side in sides:
        d, n, a = palm_frame(side)
        for F, _ in MS.FINGERS:
            segs = [side + F + g for g, _ in MS.FINGER_SEGS]
            fd = (_v(S.TAIL[segs[0]]) - _v(S.HEAD[segs[0]])).normalized()
            axis = fd.cross(n).normalized()
            ang = table[F]
            for k, b in enumerate(segs):
                q = Quaternion(axis, math.radians(ang[k]))
                if k == 0 and spread.get(F):
                    q = Quaternion(n, math.radians(spread[F] * (1 if side == "Right" else -1))) @ q
                if F == "Thumb" and k == 0 and len(ang) > 3 and ang[3]:
                    q = Quaternion(d, math.radians(ang[3] * (1 if side == "Right" else -1))) @ q
                out[b] = q
    return out


def layout_json():
    """Unity HandPose용: 포즈 표 + 축 규칙 (각은 도). 축은 Unity가 레스트 뼈대에서 같은 규칙으로 계산한다."""
    return {"poses": {k: {f: list(v) for f, v in t.items()} for k, t in HAND_POSES.items()},
            "spread": {k: dict(v) for k, v in SPREAD.items()},
            "rule": "axis = fingerDir × palmNormal (rest), palmNormal faces body midline in A-pose; "
                    "thumb[3] = rotation about hand dir; spread about palm normal (Right +, Left −)"}


def clips_json():
    """Unity HandPoseTable: 손가락 뼈 30개 순서 + 포즈별 레스트 축(선수 = Unity 좌표) 회전 델타 [x, y, z, w].
    런타임: 뼈 local = restLocal · (restModel⁻¹ · 델타 · restModel) — 손가락은 클립 키가 없어 레스트에서 시작한다."""
    bones = [n for n in S.NAMES[MS.CORE_BONES:]]
    poses = {}
    for name in sorted(HAND_POSES):
        rot = pose_rotations(name)
        poses[name] = [[round(rot[b].x, 6), round(rot[b].y, 6), round(rot[b].z, 6), round(rot[b].w, 6)] for b in bones]
    return {"bones": bones, "poses": poses}


# ---------------------------------------------------------------- 지표

def finger_set():
    return {i for i, n in enumerate(S.NAMES) if i >= MS.CORE_BONES}


def hand_silhouette(m, P, side, px=0.0007):
    """손바닥 법선 방향에서 본 손(손·손가락 주 뼈 면) 정사영 마스크 → (마스크, 손가락 가로 단면 줄)."""
    from .visual_metrics import _tri_mask  # noqa: F401 (같은 래스터 규칙)
    d, n, a = palm_frame(side)
    hi = S.NAMES.index(side + "Hand")
    fi = {i for i in finger_set() if S.NAMES[i].startswith(side)}
    dom = np.argmax(m.W, axis=1)
    faces = [f for f, *_ in m.faces if all(dom[v] == hi or dom[v] in fi for v in f)]
    if not faces:
        return None, None
    D, A = np.array(d), np.array(a)
    h0 = np.array(S.HEAD[side + "Hand"])
    Q = np.stack([(P - h0) @ A, (P - h0) @ D], axis=1)
    lo = Q[sorted({v for f in faces for v in f})].min(axis=0) - 0.005
    hi_ = Q[sorted({v for f in faces for v in f})].max(axis=0) + 0.005
    W, H = int((hi_[0] - lo[0]) / px) + 1, int((hi_[1] - lo[1]) / px) + 1
    img = np.zeros((H, W), dtype=bool)
    q = (Q - lo) / px
    for f in faces:
        for k in range(1, len(f) - 1):
            tri = q[[f[0], f[k], f[k + 1]]]
            x0, x1 = int(max(tri[:, 0].min(), 0)), int(min(tri[:, 0].max() + 1, W))
            y0, y1 = int(max(tri[:, 1].min(), 0)), int(min(tri[:, 1].max() + 1, H))
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            a_, b_, c_ = tri
            den = (b_[1] - c_[1]) * (a_[0] - c_[0]) + (c_[0] - b_[0]) * (a_[1] - c_[1])
            if abs(den) < 1e-12:
                continue
            l0 = ((b_[1] - c_[1]) * (xs - c_[0]) + (c_[0] - b_[0]) * (ys - c_[1])) / den
            l1 = ((c_[1] - a_[1]) * (xs - c_[0]) + (a_[0] - c_[0]) * (ys - c_[1])) / den
            ins = (l0 >= 0) & (l1 >= 0) & (1 - l0 - l1 >= 0)
            img[ys[ins].astype(int), xs[ins].astype(int)] = True
    # 손가락 가로 단면: 가운데 손가락 중간 마디 높이
    mid = (np.array(S.HEAD[side + "MiddleIntermediate"]) - h0) @ D
    row = int((mid - lo[1]) / px)
    return img, img[min(max(row, 0), H - 1)]


def _runs(line):
    r, prev = 0, False
    for x in line:
        if x and not prev:
            r += 1
        prev = bool(x)
    return r


def concavities(img, min_px=6):
    ys, xs = np.nonzero(img)
    pts = np.stack([xs, ys], axis=1)
    from .visual_metrics import convex_fill, label_count
    hull = convex_fill(pts, img.shape)
    return label_count(hull & ~img, min_px)


def finger_penetration(m, P, side):
    """손가락 면 × (손바닥·다른 손가락) 교차 수. 같은 손가락 안·바로 부모 관계(손바닥–첫 마디)는 제외."""
    from .checks import _bvh
    dom = np.argmax(m.W, axis=1)
    hi = S.NAMES.index(side + "Hand")
    fi = sorted(i for i in finger_set() if S.NAMES[i].startswith(side))
    def finger_of(b):
        n = S.NAMES[b][len(side):]
        for F, _ in MS.FINGERS:
            if n.startswith(F):
                return F
        return None
    groups = {}
    for f, *_ in m.faces:
        b = int(np.argmax(m.W[list(f)].sum(axis=0)))
        if b == hi or b in fi:
            groups.setdefault(b, []).append(f)
    hits = 0
    keys = sorted(groups)
    for i, ba in enumerate(keys):
        for bb in keys[i + 1:]:
            fa, fb = finger_of(ba), finger_of(bb)
            if fa is not None and fa == fb:
                continue
            if S.PARENT[S.NAMES[ba]] == S.NAMES[bb] or S.PARENT[S.NAMES[bb]] == S.NAMES[ba]:
                continue
            if fa is not None and fb is not None and S.NAMES[ba].endswith("Proximal") and S.NAMES[bb].endswith("Proximal"):
                continue  # 이웃 손가락 첫 마디끼리는 손바닥 살로 붙어 있다 (물갈퀴)
            ga, gb = groups[ba], groups[bb]
            # 변·정점을 공유하는 이웃 면(같은 메시에서 이어진 면)의 맞닿음은 관통이 아니다
            k = sum(1 for i, j in _bvh(P, ga).overlap(_bvh(P, gb)) if not (set(ga[i]) & set(gb[j])))
            if k:
                hits += k
                PAIRS[f"{S.NAMES[ba]}×{S.NAMES[bb]}"] = PAIRS.get(f"{S.NAMES[ba]}×{S.NAMES[bb]}", 0) + k
    return hits


PAIRS = {}


def hand_metrics(meshes):
    """062 AC-02 손: LOD0 편 손 오목점·손가락 간격, 포즈 3종 관통, LOD1 장갑형(손가락 뼈 가중치 0)."""
    from .checks import _skin
    from . import pose as PO
    m = meshes[min(meshes)]
    out = {"fingerBones": len(finger_set()), "poses": {}}
    concave, gaps = [], []
    for name in HAND_POSES:
        p = PO.Pose()
        p.local.update(pose_rotations(name))
        rw, head, _ = PO.fk(p)
        P = _skin(m, m.verts, rw, head)
        PAIRS.clear()
        pen = sum(finger_penetration(m, P, s) for s in ("Left", "Right"))
        import os
        if os.environ.get("PLAYER_DEBUG_DIR") and name == "fist":
            img, _ = hand_silhouette(m, P, "Right")
            np.save(os.path.join(os.environ["PLAYER_DEBUG_DIR"], "hand_fist.npy"), img)
        out["poses"][name] = {"penetration": int(pen), "pairs": dict(sorted(PAIRS.items(), key=lambda x: -x[1])[:8])}
        if name == "open":
            for s in ("Left", "Right"):
                img, line = hand_silhouette(m, P, s)
                import os
                if os.environ.get("PLAYER_DEBUG_DIR"):
                    np.save(os.path.join(os.environ["PLAYER_DEBUG_DIR"], f"hand_{s}.npy"), img)
                concave.append(concavities(img))
                gaps.append(_runs(line) - 1)
    out["openConcave"] = int(min(concave))
    out["openGapPx"] = int(min(gaps))   # 가운데 마디 높이 가로줄에서 손가락 사이 빈칸 수 (≥ 1 = 틈 있음)
    if 1 in meshes:
        w1 = meshes[1].W[:, MS.CORE_BONES:]
        out["lod1Mitten"] = bool(np.abs(w1).max() < 1e-6)
    else:
        out["lod1Mitten"] = True
    out["ok"] = (out["openConcave"] >= 4 and out["openGapPx"] > 0 and out["lod1Mitten"]
                 and all(v["penetration"] == 0 for v in out["poses"].values()))
    return out
