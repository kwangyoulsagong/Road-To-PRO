"""생성 검사 (specs/043 AC-02·03·09, FR-31·32 + specs/056 AC-01~03, FR-12·41·50·60·61). 실패 항목이 있으면 gen_player가 종료 코드 3.
056: 몸 메시는 body.Mesh(numpy). 포즈는 pose.fk 결과로 직접 선형 블렌드 스키닝(Blender 평가 없이 — 빠르고 결정론).

- 예산(AC-03): LOD0 6k~10k, LOD1 2k~3k, LOD2 ≤ 1k tris, 머티리얼 ≤ 3, 텍스처 ≤ 2048.
- 극단 체형(AC-02): 키 최소·최대(루트 균등 스케일 160/180·200/180) × 체중 최소(Thin)·최대(Heavy+Muscular) 4조합에서
  발 최저점 높이 0(오차 ≤ 2 cm), 붙어 있지 않은 부위 쌍(body.CHECK_PAIRS) 사이 삼각형 교차 0. 긴 머리도 함께 본다.
- 클립(FR-31·32): 목록 분류 전부, 접촉 프레임 범위·접촉점 높이가 선언한 공 높이 범위 안.
- 걸음 접지 미끄러짐(AC-09 원본 쪽): 로코모션 클립 접지 구간 볼(발가락 뿌리) 이동 ≤ 5 mm.
"""
import math

import numpy as np

from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import body as B
from . import pose as PO
from . import skeleton as S

TRI_BUDGET = {0: (6000, 10000), 1: (2000, 3000), 2: (0, 1000)}
MATERIAL_BUDGET = 3
TEXTURE_MAX = 2048
HEIGHT_RANGE_CM = (160.0, 200.0)

REQUIRED_GROUPS = {
    "locomotion": ["idle", "walk_f", "jog_f", "sprint_f", "decel_stop", "quick_stop", "turn_l90", "turn_r90", "turn_180"],
    "control": ["trap_foot_r", "trap_chest", "trap_thigh_r"],
    "dribble": ["dribble_touch_r"],
    "pass": ["pass_inside_r", "pass_outside_r", "pass_lofted_r"],
    "cross": ["cross_r"],
    "shot": ["shot_instep_r", "shot_curl_r", "shot_volley_r"],
    "header": ["header_jump", "header_stand"],
    "tackle": ["tackle_stand_r", "tackle_slide_r"],
    "duel": ["shoulder_duel"],
    "fall": ["fall_n", "fall_e", "fall_s", "fall_w", "hit_stumble"],
    "keeper": ["gk_ready", "gk_dive_l", "gk_dive_r"],
    "emotion": ["celebrate_arms", "dejected"],
    "restart": ["throw_in"],
    "referee": ["ref_whistle", "ref_card", "ref_advantage"],
    "flow": ["enter_jog_on", "exit_walk_off"],
}
DIRS8 = ("f", "fr", "r", "br", "b", "bl", "l", "fl")


def _tris(m, pred=None):
    return sum(len(f[0]) - 2 for f in m.faces if pred is None or pred(f))


HEAD_MIN = 2000
CLOSEUP_MAX = 6000
HAIR_LOD0_MAX = 1350  # 057 plan T01: 몸 LOD0 + 헤어(셸 + 카드 + 부속) ≤ 10k


def budgets(meshes, parts, textures):
    """LOD 삼각형 = 몸 + 가장 무거운 헤어 조각 (FR-60). 머리 몫 = Head 관절 위 면(몸 LOD0)."""
    from . import hair as H
    from . import headlod as HL
    out = {"ok": True, "lods": {}, "textures": textures}
    hy = S.HEAD["Head"][1]
    for lod, m in sorted(meshes.items()):
        hair = max(_tris(parts[f"{st}_LOD{lod}"]) for st in H.MESH_STYLES)
        tris = m.triangles + hair
        lo, hi = TRI_BUDGET[lod]
        mats = len({f[1] for f in m.faces})
        head = _tris(m, lambda f: m.verts[list(f[0]), 1].mean() > hy)
        ok = lo <= tris <= hi and mats <= MATERIAL_BUDGET and (lod != 0 or head >= HEAD_MIN)
        out["lods"][str(lod)] = {"triangles": tris, "body": m.triangles, "hairMax": hair, "headTriangles": head,
                                 "materials": mats, "ok": ok}
        out["ok"] &= ok
        for f in m.faces:
            for u in f[2]:
                if not (-1e-6 <= u[0] <= 2 + 1e-6 and -1e-6 <= u[1] <= 1 + 1e-6):
                    out["ok"] = False
                    out["uvOutOfRange"] = True
    hc = parts[HL.NAME].triangles
    out["headCloseup"] = {"triangles": hc, "ok": hc <= CLOSEUP_MAX}
    out["ok"] &= hc <= CLOSEUP_MAX
    # 057 스타일별: 헤어 삼각형(LOD별), 카드 삼각형 — 클로즈업 머리는 LOD0 헤어 조각을 그대로 쓴다(056 구조)
    out["hair"] = {}
    for st in H.MESH_STYLES:
        rec = {"tris": {}, "cards": {}, "closeup": hc}
        for lod in sorted(meshes):
            hm = parts[f"{st}_LOD{lod}"]
            rec["tris"][str(lod)] = hm.triangles
            rec["cards"][str(lod)] = _tris(hm, lambda f: f[3] == "hair_card")
        rec["cards"]["closeup"] = rec["cards"].get("0", 0)
        ok = rec["tris"].get("0", 0) <= HAIR_LOD0_MAX and (0 not in meshes or 0 < rec["cards"]["0"] <= H.CARD_MAX)
        ok &= all(rec["cards"][str(l)] == 0 for l in sorted(meshes) if l > 0)
        rec["ok"] = ok
        out["hair"][st] = rec
        out["ok"] &= ok
    for name, (w, h) in textures.items():
        if w > TEXTURE_MAX or h > TEXTURE_MAX:
            out["ok"] = False
    return out


def _blend(m, weights):
    V = m.verts.copy()
    for k, w in weights.items():
        if k in m.deltas:
            V = V + m.deltas[k] * w
    return V


def _skin(m, V, rw, head):
    """선형 블렌드 스키닝: v' = Σ w_b (head_b + R_b (v − rest_head_b))."""
    out = np.zeros_like(V)
    for bi, n in enumerate(S.NAMES):
        w = m.W[:, bi]
        nz = w > 1e-6
        if not nz.any():
            continue
        R = np.array(rw[n].to_matrix())
        h0 = np.array(S.HEAD[n])
        h1 = np.array(head[n])
        out[nz] += w[nz, None] * ((V[nz] - h0) @ R.T + h1)
    return out


def _bvh(V, faces):
    return BVHTree.FromPolygons([tuple(p) for p in V], [list(f) for f in faces], all_triangles=False)


EXTREMES = {
    "short_light": (HEIGHT_RANGE_CM[0], {"Thin": 1.0}, "Hair_Afro"),
    "short_heavy": (HEIGHT_RANGE_CM[0], {"Heavy": 1.0, "Muscular": 1.0}, "Hair_Bun"),
    "tall_light": (HEIGHT_RANGE_CM[1], {"Thin": 1.0}, "Hair_Long"),
    "tall_heavy": (HEIGHT_RANGE_CM[1], {"Heavy": 1.0, "Muscular": 1.0}, "Hair_Dreads"),
}


def _sets(m):
    """관통 검사 면 집합: 피부(유니폼에서 3 cm 넘게 떨어진 것) / 유니폼(턱 제외) / 머리 아닌 몸."""
    hi = S.NAMES.index("Head")
    ni = S.NAMES.index("Neck")
    pos = m.verts
    kit_faces = [f[0] for f in m.faces if f[1] == B.KIT and not f[3].endswith("_rim")]
    tree = _bvh(pos, kit_faces) if kit_faces else None
    skin, nonhead = [], []
    for f in m.faces:
        idx = list(f[0])
        headish = (m.W[idx, hi] + m.W[idx, ni]).mean() > 0.5
        if f[3] == "skin":
            c = pos[idx].mean(axis=0)
            if tree is None or tree.find_nearest(tuple(c), 0.03)[0] is None:
                skin.append(f[0])
        if not headish and f[3] not in ("eye", "hair"):
            nonhead.append(f[0])
    return skin, kit_faces, nonhead


RIGID = 0.8  # 뒤집힘 실패 판정: 세 정점 모두 같은 뼈 가중치가 이 이상인 면(블렌드로는 뒤집힐 수 없는 면) (관절 주름 면의 선형 스키닝 접힘은 creaseFolds로 따로 기록)


def _flipped(m, V0, V1, faces, rw):
    """포즈 뒤 면 법선이 지배 뼈 회전으로 옮긴 레스트 법선과 반대(내적 < 0)면 뒤집힘. → (단단한 면 뒤집힘, 주름 접힘). numpy 일괄."""
    F = np.asarray(faces, dtype=np.int64)
    n0 = np.cross(V0[F[:, 1]] - V0[F[:, 0]], V0[F[:, 2]] - V0[F[:, 0]])
    n1 = np.cross(V1[F[:, 1]] - V1[F[:, 0]], V1[F[:, 2]] - V1[F[:, 0]])
    w = m.W[F].mean(axis=1)
    dom = np.argmax(w, axis=1)
    vdom = np.argmax(m.W[F], axis=2)          # (f, 3) 정점별 지배 뼈
    vmax = m.W[F].max(axis=2)
    Rm = np.stack([np.array(rw[n].to_matrix()) for n in S.NAMES])
    pred = np.einsum("fij,fj->fi", Rm[dom], n0)
    valid = (np.linalg.norm(n0, axis=1) > 1e-10) & (np.linalg.norm(n1, axis=1) > 1e-10)
    flip = valid & ((n1 * pred).sum(axis=1) < 0)
    rigid = (vdom == dom[:, None]).all(axis=1) & (vmax >= RIGID).all(axis=1)  # 세 정점 모두 같은 뼈가 0.8 이상
    return int((flip & rigid).sum()), int((flip & ~rigid).sum())


def _hops(a, b):
    """뼈대 계층 거리 (같은 뼈 0, 부모-자식 1 …)."""
    def chain(x):
        out = []
        while x:
            out.append(x)
            x = S.PARENT[x]
        return out
    ca, cb = chain(a), chain(b)
    for i, x in enumerate(ca):
        if x in cb:
            return i + cb.index(x)
    return 99


ADJACENT_HOPS = 2  # 043 CHECK_PAIRS와 같은 뜻: 붙어 있는 부위(관절로 이어진 2단계 이내)의 접촉은 관통이 아니다


def _dom(m, faces):
    return [S.NAMES[int(np.argmax(m.W[list(f)].sum(axis=0)))] for f in faces]


def _overlaps(m, P, fa, fb, da, db):
    """두 면 집합의 교차 중 서로 떨어진 부위(ADJACENT_HOPS 초과) 쌍 수."""
    if not fa or not fb:
        return 0
    n = 0
    for i, j in _bvh(P, fa).overlap(_bvh(P, fb)):
        if _hops(da[i], db[j]) > ADJACENT_HOPS:
            n += 1
    return n


def extremes(meshes, parts, lod=0):
    """극단 체형 4종(레스트): 축구화 바닥 접지 ≤ 2 cm, 바닥 아래 0, 헤어 10종 × 머리 아닌 몸 교차 0."""
    from . import hair as H
    m = meshes[lod]
    out = {"ok": True, "cases": {}}
    boot = [i for i, p in enumerate(m.vpart) if p == "boot"]
    for name, (height, weights, style) in EXTREMES.items():
        scale = height / (S.REF_HEIGHT * 100.0)
        V = _blend(m, weights) * scale
        foot_min = float(V[boot, 1].min())
        all_min = float(V[:, 1].min())
        ok = abs(foot_min) <= 0.02 and all_min >= -1e-6
        out["cases"][name] = {"footMin": foot_min, "allMin": all_min, "intersections": {}, "ok": ok}
        out["ok"] &= ok
    _, _, nonhead = _sets(m)
    body_tree = _bvh(m.verts, nonhead)
    for style in H.STYLES:
        key = f"{style}_LOD{lod}"
        hits = {}
        if key in parts:
            hm = parts[key]
            n = len(_bvh(hm.verts, [f[0] for f in hm.faces]).overlap(body_tree))
            if n:
                hits["hair×body"] = n
        out["cases"]["hair_" + style] = {"intersections": hits, "ok": not hits, "footMin": 0.0, "allMin": 0.0}
        out["ok"] &= not hits
    return out


def _sample_frames(c):
    fr = [0, c.frames // 3, (2 * c.frames) // 3, c.frames]
    ct = c.tags.get("contact")
    if ct:
        fr[1] = ct["frame"]
    return fr


def penetration(clips, meshes, parts, lod=0):
    """056 AC-03: 클립 × 극단 4 × 4프레임 — 피부×유니폼, 머리카락×머리 아닌 몸, 뒤집힌 삼각형."""
    from . import hair as H
    m = meshes[lod]
    skin, kit, nonhead = _sets(m)
    ds, dk = _dom(m, skin), _dom(m, kit)
    tri_faces = [f[0] for f in m.faces if len(f[0]) == 3]
    poses, sk, hb, fl, cr, hposes = 0, 0, 0, 0, 0, 0
    worst = []
    hair_worst = {}
    # 057 AC-02: 헤어 29조각(셸 + 카드 + 부속) 전부 — 스타일 i는 극단 체형 (기본 배정 우선, 나머지는 i % 4)에 붙여 클립 × 4프레임
    names = list(EXTREMES)
    assign = {n: [EXTREMES[n][2]] for n in names}
    for i, st in enumerate(H.MESH_STYLES):
        if not any(st in v for v in assign.values()):
            assign[names[i % len(names)]].append(st)
    for name, (height, weights, _style) in EXTREMES.items():
        V = _blend(m, weights)
        hms = [(st, parts[f"{st}_LOD{lod}"]) for st in assign[name] if f"{st}_LOD{lod}" in parts]
        HVs = [(st, hm, _blend(hm, weights), [x[0] for x in hm.faces]) for st, hm in hms]
        for c in clips:
            for f in _sample_frames(c):
                rw, head, _ = PO.fk(c.sample(f / 30.0))
                P = _skin(m, V, rw, head)
                poses += 1
                n1 = _overlaps(m, P, skin, kit, ds, dk)
                n2 = 0
                if HVs:
                    bt = _bvh(P, nonhead)
                    for st, hm, HV, hf in HVs:
                        HP = _skin(hm, HV, rw, head)
                        k = len(_bvh(HP, hf).overlap(bt))
                        hposes += 1
                        if k:
                            n2 += k
                            hair_worst.setdefault(st, {"clip": c.id, "frame": f, "case": name, "hits": k})
                n3, c3 = _flipped(m, V, P, tri_faces, rw)
                sk, hb, fl, cr = sk + n1, hb + n2, fl + n3, cr + c3
                if (n1 or n2 or n3) and len(worst) < 16:
                    worst.append({"case": name, "clip": c.id, "frame": f, "skinKit": n1, "hairBody": n2, "flipped": n3})
    nst = sum(len(v) for v in assign.values())
    return {"ok": sk == 0 and hb == 0 and fl == 0, "poses": poses, "skinKit": sk, "hairBody": hb, "flipped": fl,
            "creaseFolds": cr, "worst": worst, "hairStyles": nst, "hairPoses": hposes, "hairWorst": hair_worst}


def proportions(meshes, samples=200):
    """FR-10·12: 200명(키 160~200 cm 균등, 체형 가중치 무작위 — 시드 고정) 메시 키 오차·머리 높이 비율(정수리~턱끝 / 키)."""
    from . import face as F
    m = meshes[0]
    rng = np.random.default_rng(56)
    body = [i for i, p in enumerate(m.vpart) if p not in ("hair", "eye")]
    R = B.reference()
    lm = {x["name"]: x for x in F.landmarks(R, B._ref_tree(R))}
    chin_y = lm["chin"]["base"][1]
    errs, ratios = [], []
    for _ in range(samples):
        h = float(rng.uniform(HEIGHT_RANGE_CM[0], HEIGHT_RANGE_CM[1]))
        w = {"Thin": float(rng.uniform(0, 1)), "Muscular": float(rng.uniform(0, 1)), "Heavy": float(rng.uniform(0, 1))}
        V = _blend(m, w)
        scale = h / (S.REF_HEIGHT * 100.0)
        top = float(V[body, 1].max())
        errs.append(abs(top * scale * 100.0 - h))
        ratios.append((top - chin_y) / top)
    hmin, hmax = min(ratios), max(ratios)
    ok = max(errs) <= 1.0 and hmin >= 1 / 8.0 - 1e-4 and hmax <= 1 / 7.5 + 1e-4
    return {"ok": ok, "samples": samples, "heightErrCmMax": max(errs), "headRatioMin": hmin, "headRatioMax": hmax}


def licenses(used, assets_json, licenses_csv):
    """056 AC-01: 쓴 MPFB 파일이 모두 assets.json(sha256 고정)에 있고, LICENSES.csv에 MPFB2 CC0 줄이 있다."""
    import json
    import os
    from . import mpfb_src as MS
    out = {"ok": True, "missing": [], "hashMismatch": []}
    pinned = {}
    if os.path.exists(assets_json):
        with open(assets_json, encoding="utf-8") as f:
            pinned = {a["path"]: a["sha256"] for a in json.load(f)["files"]}
    root = MS.data_dir()
    files = []
    for rel in used:
        sha = MS.sha256(os.path.join(root, rel))
        files.append({"path": rel, "sha256": sha})
        if rel not in pinned:
            out["missing"].append(rel)
        elif pinned[rel] != sha:
            out["hashMismatch"].append(rel)
    rows = []
    if os.path.exists(licenses_csv):
        with open(licenses_csv, encoding="utf-8") as f:
            rows = [r for r in f.read().splitlines() if r.startswith("MPFB2")]
    out["csvRows"] = len(rows)
    out["ok"] = not out["missing"] and not out["hashMismatch"] and bool(rows) and all("CC0" in r for r in rows)
    return out, files


def clip_list(tags):
    ids = {t["id"] for t in tags}
    missing = []
    for g, need in REQUIRED_GROUPS.items():
        missing += [n for n in need if n not in ids]
    for gk in ("walk", "jog", "sprint"):
        missing += [f"{gk}_{d}" for d in DIRS8 if f"{gk}_{d}" not in ids]
    missing += [f"fall_{d}" for d in ("n", "ne", "e", "se", "s", "sw", "w", "nw") if f"fall_{d}" not in ids]
    bad = []
    for t in tags:
        c = t.get("contact")
        if not c:
            continue
        if not (0 <= c["frame"] <= t["frames"]):
            bad.append(f"{t['id']}: 접촉 프레임 범위 밖")
        lo, hi = t["ballHeight"]
        if not (lo - 1e-6 <= c["point"][1] <= hi + 1e-6):
            bad.append(f"{t['id']}: 접촉점 높이 {c['point'][1]:.3f} ∉ [{lo}, {hi}]")
    return {"ok": not missing and not bad and len(tags) >= 75, "count": len(tags), "missing": missing, "bad": bad}


def gait_slide(clips):
    worst, per = 0.0, {}
    for c in clips:
        if c.group != "locomotion" or not c.loop or c.tags.get("gait") in (None, "idle"):
            continue
        v = c.tags["speed"]
        d = Vector((math.sin(math.radians(c.tags["dir"])), 0.0, math.cos(math.radians(c.tags["dir"]))))
        T = c.frames / 30.0
        for n in ("Left", "Right"):
            s0, s1 = c.tags["stance"][n]
            span = (s1 - s0) % 1.0
            pts = []
            for i in range(41):
                ph = (s0 + span * i / 40) % 1.0
                t = ph * T
                _, head, _ = PO.fk(c.sample(t))
                tw = t if ph >= s0 - 1e-9 else t + T
                pts.append(head[n + "Toes"] + d * v * tw)
            dev = max((p - pts[0]).length for p in pts)
            per[f"{c.id}.{n}"] = dev
            worst = max(worst, dev)
    # 발끝이 바닥 근처(≤ 3 cm)일 때 월드 수평 속도 — 접지 경계(떼기·딛기)의 끌림 검사 (Unity AC-09 측정 방식과 같은 기준)
    low = 0.0
    for c in clips:
        if c.group != "locomotion" or not c.loop or c.tags.get("gait") in (None, "idle"):
            continue
        v = c.tags["speed"]
        d = Vector((math.sin(math.radians(c.tags["dir"])), 0.0, math.cos(math.radians(c.tags["dir"]))))
        T = c.frames / 30.0
        for n in ("Left", "Right"):
            prev = None
            for i in range(121):
                t = T * i / 120
                _, head, _ = PO.fk(c.sample(t))
                p = head[n + "Toes"] + d * v * t
                if prev is not None and p.y <= 0.03 and prev.y <= 0.03:
                    dv = p - prev
                    low = max(low, math.hypot(dv.x, dv.z) / (T / 120))
                prev = p
    return {"ok": worst <= 0.005 and low <= 0.1, "worst": worst, "lowToeSpeed": low, "clips": len(per) // 2}


# 관절 최저 높이 (FR-45: 지면 관통 없음). 관절 = 본 머리, 값 = 관절 굵기 근사.
GROUND_MIN = {"Toes": 0.0, "Foot": 0.035, "Hand": 0.02, "LowerArm": 0.03, "UpperArm": 0.04, "Head": 0.08}


def _min_for(bone):
    for k, v in GROUND_MIN.items():
        if bone.endswith(k):
            return v
    return 0.045


def ground(clips, step=2):
    """모든 클립의 표본 프레임에서 관절이 바닥 아래로 들어가면 실패."""
    bad = {}
    for c in clips:
        worst = None
        for f in list(range(0, c.frames + 1, step)) + [c.frames]:
            _, head, tail = PO.fk(c.sample(f / 30.0))
            for n, p in head.items():
                d = p.y - _min_for(n)
                if d < -0.005 and (worst is None or d < worst[0]):
                    worst = (d, n, f)
            tip = tail["LeftToes"].y, tail["RightToes"].y
            if min(tip) < -0.005 and (worst is None or min(tip) < worst[0]):
                worst = (min(tip), "ToesTip", f)
        if worst:
            bad[c.id] = {"depth": round(worst[0], 3), "bone": worst[1], "frame": worst[2]}
    return {"ok": not bad, "bad": bad}


# ---------------------------------------------------------------- 057 유니폼 경계 매끈함 (AC-01, FR-01·02)

BOUNDARY_TURN_MAX = {"collar": 20.0}  # 이웃 정점 꺾임 각 상한 (도) — specs/057 FR-01 칼라 20°, 나머지 40° (FR-02)
BOUNDARY_TURN_OTHER = 40.0
ZIGZAG_DEG = 10.0           # 계단: 이웃 꺾임 부호가 번갈아 바뀌고 둘 다 이 이상
BOUNDARY_DEV_MAX = 0.003    # 경계 평면 이탈 상한 (m)


def _loops(edges):
    return B.chain_loops(edges)


def kit_boundary(m, lod=0):
    """턱 면(`*_rim`)의 바깥 변 = 유니폼 경계(보이는 턱 선). 고리마다 가장 가까운 절단 평면을 찾아 평면 투영 꺾임 각(최대·p95)·계단
    수를 재고, 평면 이탈(평균 대비 최대)은 피부 쪽 절단선에서 잰다(피부 없는 경계는 턱 선). → {ok, loops: {평면: {...}}}"""
    planes = B.cut_planes()
    V = np.asarray(m.verts)
    edges = []
    for f, mat, uvs, part, _ in m.faces:
        if part.endswith("_rim"):
            ob, oa = f[0], f[1]
            edges.append((min(oa, ob), max(oa, ob)))
    out = {"ok": True, "lod": lod, "loops": {}}
    if not edges:
        return out
    # 절단선(피부 쪽 경계 = 피부 면에서 한 번만 쓰인 변) — 평면 이탈은 이 선에서 잰다(셸 턱은 띄움 두께만큼 법선으로 떠 있다)
    se = {}
    for f, mat, uvs, part, _ in m.faces:
        if part == "skin":
            for i in range(len(f)):
                a, b = f[i], f[(i + 1) % len(f)]
                se[(min(a, b), max(a, b))] = se.get((min(a, b), max(a, b)), 0) + 1
    cut_loops = [V[lp] for lp, cl in _loops(sorted(e for e, c in se.items() if c == 1)) if cl]
    for loop, closed in _loops(sorted(set(edges))):
        P = V[loop]
        best = min(planes, key=lambda pl: float(np.abs((P - np.array(pl[1])) @ np.array(pl[2])).mean()))
        name, pt, nrm, _ = best
        n = np.array(nrm)
        d = (P - np.array(pt)) @ n
        cen = P.mean(axis=0)
        cands = [C for C in cut_loops if np.linalg.norm(C.mean(axis=0) - cen) < 0.03
                 and abs(float(((C - np.array(pt)) @ n).mean())) < 0.02]
        dd = ((cands[0] - np.array(pt)) @ n) if cands else d  # 피부가 없는 경계(밑단·축구화 목)는 셸 고리
        dev = float(np.abs(dd - dd.mean()).max())
        Q = P - np.outer(d, n)  # 평면 투영
        turns = []
        k = len(Q)
        rng = range(k) if closed else range(1, k - 1)
        for i in rng:
            e0 = Q[i] - Q[i - 1]
            e1 = Q[(i + 1) % k] - Q[i]
            l0, l1 = np.linalg.norm(e0), np.linalg.norm(e1)
            if l0 < 1e-7 or l1 < 1e-7:
                continue
            c = float(np.clip(e0 @ e1 / (l0 * l1), -1.0, 1.0))
            sgn = 1.0 if float(np.cross(e0, e1) @ n) >= 0 else -1.0
            turns.append(sgn * math.degrees(math.acos(c)))
        zig = sum(1 for a, b in zip(turns, turns[1:] + (turns[:1] if closed else []))
                  if a * b < 0 and abs(a) > ZIGZAG_DEG and abs(b) > ZIGZAG_DEG)
        t = sorted(abs(x) for x in turns)
        rec = {"verts": k, "closed": bool(closed), "turnMax": round(t[-1], 2) if t else 0.0,
               "turnP95": round(t[int(0.95 * (len(t) - 1))], 2) if t else 0.0, "devMax": round(dev, 5), "zigzag": zig}
        key = name
        i = 2
        while key in out["loops"]:
            key = f"{name}#{i}"
            i += 1
        out["loops"][key] = rec
        if lod == 0 and (rec["turnMax"] > BOUNDARY_TURN_MAX.get(name, BOUNDARY_TURN_OTHER) or zig):
            out["ok"] = False
        if dev > BOUNDARY_DEV_MAX or not closed:
            out["ok"] = False
    return out


def kit_rim_uv(m):
    """057 FR-04: 칼라 포인트색 띠(셔츠 판 v_local ≥ 0.96 — textures.kit_mask `collar`)가 칼라 경계를 따라간다.
    missing = 칼라 턱(경계 3 cm 안 shirt_rim) 코너인데 띠 밖, bleed = 칼라 평면 아래 3 cm 넘는 셔츠 면 코너인데 띠 안."""
    from . import uv as U
    pl = B.cut_planes()[0]
    V = np.asarray(m.verts)
    fr, bk = U.KIT_REGIONS["shirtFront"], U.KIT_REGIONS["shirtBack"]
    out = {"ok": True, "collarRimCorners": 0, "missing": 0, "bleed": 0}
    for f, mat, uvs, part, _ in m.faces:
        if part not in ("shirt", "shirt_rim"):
            continue
        for vi, (u, v) in zip(f, uvs):
            reg = fr if u < 0.5 else bk
            vl = (v - reg[1]) / (reg[3] - reg[1])
            d = B._side(V[vi], pl)
            if part == "shirt_rim" and d > -0.03:
                out["collarRimCorners"] += 1
                if vl < 0.96 - 1e-6:
                    out["missing"] += 1
            elif part == "shirt" and vl >= 0.96 and d < -0.03:
                out["bleed"] += 1
    # 턱 면 정점이 셸 정점과 같은 위치(굽기 용접 격자 0.01 mm)에 있으면 법선이 섞인다 — 0이어야 한다
    shell = {tuple(np.round(V[v] * 1e5).astype(np.int64)) for f, mat, uvs, part, _ in m.faces
             if mat == B.KIT and not part.endswith("_rim") for v in f}
    out["rimWelded"] = sum(1 for f, mat, uvs, part, _ in m.faces if part.endswith("_rim")
                           for v in f[:2] if tuple(np.round(V[v] * 1e5).astype(np.int64)) in shell)
    out["ok"] = out["missing"] == 0 and out["bleed"] == 0 and out["collarRimCorners"] > 0 and out["rimWelded"] == 0
    return out


SKIN_056_NORMAL_HF = 0.070367  # 056 클로즈업 얼굴 노멀 고주파 RMS (specs/057 plan T01)
SKIN_056_ROUGH = {"nose": 0.591938, "cheek": 0.67973, "forehead": 0.566366, "eyeArea": 0.678763, "lips": 0.521222}


def skin_detail(metrics):
    """057 AC-05: 얼굴 노멀 고주파 ≤ 056 × 0.5 이고 > 0.002, 코·볼 > 이마·눈가·입술, 거칠기 평균 056 ± 0.02."""
    out = dict(metrics)
    face = metrics["face"]["normalHf"]
    ok = face <= SKIN_056_NORMAL_HF * 0.5 and face > 0.002
    ok &= min(metrics["nose"]["normalHf"], metrics["cheek"]["normalHf"]) > max(
        metrics["forehead"]["normalHf"], metrics["eyeArea"]["normalHf"], metrics["lips"]["normalHf"])
    ok &= all(abs(metrics[k]["rough"] - v) <= 0.02 for k, v in SKIN_056_ROUGH.items())
    bd = metrics["beard"]  # 057 FR-24
    ok &= bd["coreHf"] <= 0.05 and bd["lowNeck"] <= 0.01 and bd["mouthCorner"] >= 0.3
    out["ok"] = bool(ok)
    return out
