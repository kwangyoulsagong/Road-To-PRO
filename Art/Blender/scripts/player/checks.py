"""생성 검사 (specs/043 AC-02·03·09, FR-31·32 + specs/056 AC-01~03, FR-12·41·50·60·61). 실패 항목이 있으면 gen_player가 종료 코드 3.
056: 몸 메시는 body.Mesh(numpy). 포즈는 pose.fk 결과로 직접 선형 블렌드 스키닝(Blender 평가 없이 — 빠르고 결정론).

- 예산(AC-03, 062 FR-81): LOD0 6k~11k, LOD1 2k~3k, LOD2 ≤ 1k tris, 머티리얼 ≤ 3, 텍스처 ≤ 2048.
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
from . import mpfb_src as MS
from . import pose as PO
from . import skeleton as S

TRI_BUDGET = {0: (6000, 11000), 1: (2000, 3000), 2: (0, 1000)}  # 062: LOD0 상한 11k (2026-11-20 사용자 결정, CLAUDE.md)
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
    "keeper": ["gk_ready"] + [f"gk_dive_{h}_{r}_{s}" for h in ("low", "mid", "high") for r in ("short", "long") for s in "lr"],
    "emotion": ["celebrate_arms", "dejected"],
    "restart": ["throw_in"],
    "referee": ["ref_whistle", "ref_card", "ref_advantage"],
    "flow": ["enter_jog_on", "exit_walk_off"],
}
DIRS8 = ("f", "fr", "r", "br", "b", "bl", "l", "fl")


def _tris(m, pred=None):
    return sum(len(f[0]) - 2 for f in m.faces if pred is None or pred(f))


HEAD_MIN = 2000
HAND_BALL_SLACK = 0.30  # 080: 손목(Hand 본 머리) → 손바닥 → 공 중심 (손 ≈ 0.10 + 손바닥 앞 0.06 + 공 반지름 0.11 + 여유)
CLOSEUP_MAX = 6000
HAIR_LOD0_MAX = 1900  # 088 플랜: 헤어 몫 (셸 ≤ 1,300 + 카드), 몸 포함 LOD0 ≤ 11k
# 062 FR-92·83 (plan '데이터' 텍스처 표): 파일별 최대 해상도. 표에 없는 텍스처는 실패(예산 밖 맵이 몰래 늘지 않게).
# 선수별 텍스처는 키트 합성(팀당 1회)뿐이고 나머지는 공유. 메모리 판정(≤ 160 MB)은 G단계 062-model-perf 실측, 여기선 추정치 기록.
TEXTURE_RES = {
    # 088 FR-01·04: MakeHuman 피부 3세트 2K(LOD0·클로즈업 공유) + 공유 노멀 2K + 눈썹 마스크 1K
    **{f"skin_{k}.png": 2048 for k in ("light", "asian", "dark")}, "skin_normal.png": 2048, "brow_mask.png": 1024,
    "parts.png": 1024,
    "body_detail_n.png": 2048, "body_detail_ao.png": 2048,                      # 몸 디테일 2K (Medium·High)
    "kit_detail_n.png": 2048, "kit_detail_ao.png": 2048,                        # 키트 디테일 2K
    "kit_region.png": 1024, "beard_mask0.png": 1024, "skin_thickness.png": 512,
    "kit_normal.png": 512, "kit_weave.png": 256, "kit_mask_suit.png": 1024,
    **{f"kit_mask_{p}.png": 1024 for p in ("plain", "stripes", "hoops", "halves", "sash", "yoke")},
}


def texture_budget(textures):
    """FR-92: 파일별 해상도 ≤ 표, 표에 없는 파일 0. 메모리 추정 = RGBA8 비압축 × 밉 4/3 (Unity 임포트 Uncompressed 규칙)."""
    over, unknown, mb = {}, [], 0.0
    for name, (w, h) in sorted(textures.items()):
        lim = TEXTURE_RES.get(name)
        if lim is None:
            unknown.append(name)
        elif w > lim or h > lim:
            over[name] = [w, h, lim]
        mb += w * h * 4 * 4 / 3 / (1 << 20)
    return {"ok": not over and not unknown, "over": over, "unknown": unknown, "estimatedMB": round(mb, 1)}


def budgets(meshes, parts, textures):
    """LOD 삼각형 = 몸 + 가장 무거운 헤어 조각 (FR-60). 머리 몫 = Head 관절 위 면(몸 LOD0)."""
    from . import hairstyles as H
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
    # 062 FR-81: 스타일마다 몸 + 그 헤어 조각이 LOD 예산 안(가장 무거운 헤어만 보던 것을 스타일별로)
    style_tot = {}
    # 057 스타일별: 헤어 삼각형(LOD별), 카드 삼각형 — 클로즈업 머리는 LOD0 헤어 조각을 그대로 쓴다(056 구조)
    out["hair"] = {}
    for st in H.MESH_STYLES:
        rec = {"tris": {}, "cards": {}, "closeup": hc}
        for lod in sorted(meshes):
            hm = parts[f"{st}_LOD{lod}"]
            rec["tris"][str(lod)] = hm.triangles
            rec["cards"][str(lod)] = _tris(hm, lambda f: f[3] == "hair_card")
        rec["cards"]["closeup"] = rec["cards"].get("0", 0)
        ok = rec["tris"].get("0", 0) <= HAIR_LOD0_MAX and (0 not in meshes or 0 <= rec["cards"]["0"] <= H.card_max())   # 088: 봉투 셸이 실루엣을 맡아 카드 0도 허용
        ok &= all(rec["cards"][str(l)] == 0 for l in sorted(meshes) if l > 0)
        tot = {str(l): meshes[l].triangles + rec["tris"][str(l)] for l in sorted(meshes)}
        rec["lodTotals"] = tot
        ok &= all(TRI_BUDGET[l][0] <= tot[str(l)] <= TRI_BUDGET[l][1] for l in sorted(meshes))
        style_tot[st] = tot
        rec["ok"] = ok
        out["hair"][st] = rec
        out["ok"] &= ok
    for name, (w, h) in textures.items():
        if w > TEXTURE_MAX or h > TEXTURE_MAX:
            out["ok"] = False
    if style_tot:
        out["styleMax"] = {l: max(t[l] for t in style_tot.values()) for l in next(iter(style_tot.values()))}
    out["textureBudget"] = texture_budget(textures)
    out["ok"] &= out["textureBudget"]["ok"]
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
    for bi, n in enumerate(S.NAMES[:m.W.shape[1]]):  # 헤어·머리 조각은 보조 뼈 열이 없다 (062)
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
    "short_heavy": (HEIGHT_RANGE_CM[0], {"Heavy": 1.0, "Muscular": 1.0, "Broad": 1.0, "Limb": 1.0}, "Hair_Bun"),
    "tall_light": (HEIGHT_RANGE_CM[1], {"Thin": 1.0}, "Hair_Long"),
    "tall_heavy": (HEIGHT_RANGE_CM[1], {"Heavy": 1.0, "Muscular": 1.0, "Broad": 1.0, "Limb": 1.0}, "Hair_Dreads"),  # 062: 근육 최대
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
    nb = len(MS.OUR_NAMES)  # 062: 보조 뼈 열 제외 — 밑단이 Kit_*로 잡히면 몸 뼈 거리(hops)가 1 늘어 이웃 판정이 바뀐다
    return [S.NAMES[int(np.argmax(m.W[list(f), :nb].sum(axis=0)))] for f in faces]


def _overlaps(m, P, fa, fb, da, db):
    """두 면 집합의 교차 중 서로 떨어진 부위(ADJACENT_HOPS 초과) 쌍 수."""
    if not fa or not fb:
        return 0
    n = 0
    for i, j in _bvh(P, fa).overlap(_bvh(P, fb)):
        if _hops(da[i], db[j]) > ADJACENT_HOPS:
            n += 1
            OVERLAP_PAIRS[(da[i], db[j])] = OVERLAP_PAIRS.get((da[i], db[j]), 0) + 1
    return n


OVERLAP_PAIRS = {}  # 진단: 마지막 검사에서 겹친 (주 뼈, 주 뼈) 쌍 수


def extremes(meshes, parts, lod=0):
    """극단 체형 4종(레스트): 축구화 바닥 접지 ≤ 2 cm, 바닥 아래 0, 헤어 10종 × 머리 아닌 몸 교차 0."""
    from . import hairstyles as H
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


HANDQ = {}
CLIP_HITS = {}


def penetration(clips, meshes, parts, lod=0):
    """056 AC-03: 클립 × 극단 4 × 4프레임 — 피부×유니폼, 머리카락×머리 아닌 몸, 뒤집힌 삼각형."""
    from . import hairstyles as H
    from . import hands as HD
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
            hand_q = HANDQ.setdefault(HD.hand_pose_for(c.id, c.group), HD.pose_rotations(HD.hand_pose_for(c.id, c.group)))
            for f in _sample_frames(c):
                pz = c.sample(f / 30.0)
                for k, qv in hand_q.items():  # 062: 런타임과 같이 클립의 손 포즈를 손가락에 건다
                    pz.local.setdefault(k, qv)
                rw, head, _ = PO.fk(pz)
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
                if n1:
                    CLIP_HITS[c.id] = CLIP_HITS.get(c.id, 0) + n1
                if (n1 or n2 or n3) and len(worst) < 16:
                    worst.append({"case": name, "clip": c.id, "frame": f, "skinKit": n1, "hairBody": n2, "flipped": n3})
    nst = sum(len(v) for v in assign.values())
    return {"overlapPairs": {f"{a}×{b}": v for (a, b), v in sorted(OVERLAP_PAIRS.items())}, "clipHits": dict(CLIP_HITS),
            "ok": sk == 0 and hb == 0 and fl == 0, "poses": poses, "skinKit": sk, "hairBody": hb, "flipped": fl,
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


def _hull_perimeter(P2):
    pts = sorted(set(map(tuple, np.round(P2, 6))))
    if len(pts) < 3:
        return 0.0
    def cr(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for q in pts:
        while len(lo) >= 2 and cr(lo[-2], lo[-1], q) <= 0:
            lo.pop()
        lo.append(q)
    for q in reversed(pts):
        while len(up) >= 2 and cr(up[-2], up[-1], q) <= 0:
            up.pop()
        up.append(q)
    h = np.array(lo[:-1] + up[:-1])
    return float(np.linalg.norm(h - np.roll(h, 1, axis=0), axis=1).sum())


LIMB_SPAN = {"UpperArm": (0.40, 0.80), "LowerArm": (0.08, 0.45)}  # 뼈 길이 비율 구간 — 위팔은 ANSUR 위팔 둘레처럼 이두 높이(삼각근 아래), 아래팔은 팔꿈치 아래 굵은 곳


def limb_girth(R, V, bone, slices=10):
    """뼈 축에 수직인 단면(구간을 slices개로 나눔)의 볼록 둘레 최댓값 (m), 좌우 평균. 단면 = 그 뼈가 주 뼈인 몸 정점."""
    body = np.zeros(R.n, dtype=bool)
    body[R.groups["body"]] = True
    dom = np.argmax(R.weights, axis=1)
    out = []
    for side in ("Left", "Right"):
        name = side + bone
        h, t = (np.array(x) for x in R.joints[name])
        ax = t - h
        ln = float(np.linalg.norm(ax))
        ax /= ln
        sel = body & (dom == MS.OUR_NAMES.index(name))
        P = V[sel] - h
        tt = P @ ax / ln
        u = np.cross(ax, [0.0, 0.0, 1.0] if abs(ax[2]) < 0.9 else [1.0, 0.0, 0.0])
        u /= np.linalg.norm(u)
        w = np.cross(ax, u)
        a0, a1 = LIMB_SPAN[bone]
        best = 0.0
        for k in range(slices):
            lo = a0 + (a1 - a0) * k / slices
            m = (tt >= lo) & (tt < lo + (a1 - a0) / slices)
            if m.sum() >= 6:
                best = max(best, _hull_perimeter(np.stack([P[m] @ u, P[m] @ w], axis=1)))
        out.append(best)
    return float(np.mean(out))


SLIVER_M2 = 2e-6  # 062: 2 mm² 미만 퇴화 조각(056 감량이 남긴 칼라 쪽 조각, 클로즈업에서 ≈ 1~2 px)은 뒤집힘 판정에서 따로 센다


def body_shapes(R, m0, samples=200, seed=62):
    """062 AC-01: 체형 5축 무작위 200명 + 극단 4종 × 근육(Muscular·Broad·Limb) 0/1.
    팔 둘레/키는 기준 고해상도 몸(R, 키 1.80 m, 균등 스케일이라 비율 불변), 같은 표본의 3축(Broad·Limb 0) 평균 = 056·057 분포.
    뒤집힌 삼각형(기준 면 법선과 반대)·찢어짐(모서리 길이 ×2.5 초과)은 실제로 그리는 LOD0 메시(m0, 몸+키트)에서."""
    rng = np.random.default_rng(seed)
    keys = sorted(R.body_deltas)
    W = []
    for _ in range(samples):
        u = float(rng.uniform(0, 1))
        W.append({"Thin": max(0.0, 1 - 2 * u), "Heavy": max(0.0, 2 * u - 1), "Muscular": float(rng.uniform(0, 1)),
                  "Broad": float(rng.uniform(0, 1)), "Limb": float(rng.uniform(0, 1))})
    for _, w, _ in EXTREMES.values():
        base = {k: v for k, v in w.items() if k in ("Thin", "Heavy")}
        for mu in (0.0, 1.0):
            W.append(dict(base, Muscular=mu, Broad=mu, Limb=mu))
    under = set(getattr(m0, "under", []))  # 062 T17a: 밑깔개는 셸 밑이라 보이지 않는다 (가라앉힌 피부가 체형 키에서 접힐 수 있다)
    sel_f = [(f, part) for fi, (f, _mat, _uv, part, _) in enumerate(m0.faces) if fi not in under and all(m0.vpart[v] not in ("hair", "eye") for v in f)]
    tris = np.array([(f[0], f[k], f[k + 1]) for f, _ in sel_f for k in range(1, len(f) - 1)])
    tpart = [part for f, part in sel_f for k in range(1, len(f) - 1)]
    def fn(V):
        return np.cross(V[tris[:, 1]] - V[tris[:, 0]], V[tris[:, 2]] - V[tris[:, 0]])
    n0 = fn(m0.verts)
    e = np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]])
    l0 = np.linalg.norm(m0.verts[e[:, 0]] - m0.verts[e[:, 1]], axis=1)
    ua, fa, ua_base, flipped, torn, flip_base, flip_parts, slivers = [], [], [], 0, 0, 0, {}, 0
    area0 = np.linalg.norm(n0, axis=1) * 0.5
    for w in W:
        V = R.verts + sum(R.body_deltas[k] * w.get(k, 0.0) for k in keys)
        ua.append(limb_girth(R, V, "UpperArm") / S.REF_HEIGHT)
        fa.append(limb_girth(R, V, "LowerArm") / S.REF_HEIGHT)
        Vb = R.verts + sum(R.body_deltas[k] * w.get(k, 0.0) for k in keys if k not in ("Broad", "Limb"))
        ua_base.append(limb_girth(R, Vb, "UpperArm") / S.REF_HEIGHT)
        V0 = _blend(m0, w)
        inv = (fn(V0) * n0).sum(axis=1) < 0
        fl = inv & (area0 >= SLIVER_M2)
        slivers += int((inv & (area0 < SLIVER_M2)).sum())
        flipped += int(fl.sum())
        a0 = area0
        for ti in np.flatnonzero(fl):
            k2 = f"{tpart[ti]}|{MS.OUR_NAMES[int(np.argmax(m0.W[tris[ti][0], :len(MS.OUR_NAMES)]))]}|{a0[ti]*1e6:.2f}mm2|{tuple(int(x) for x in tris[ti])}|{ {k: round(v, 2) for k, v in w.items() if v} }"
            flip_parts[k2] = flip_parts.get(k2, 0) + 1
        Vb0 = _blend(m0, {k: v for k, v in w.items() if k not in ("Broad", "Limb")})
        flip_base += int((((fn(Vb0) * n0).sum(axis=1) < 0) & (area0 >= SLIVER_M2)).sum())
        r = np.linalg.norm(V0[e[:, 0]] - V0[e[:, 1]], axis=1) / np.maximum(l0, 1e-9)
        torn += int((r > 2.5).sum())
    out = {"keys": keys, "samples": len(W),
           "upperArmRatio": [round(min(ua), 4), round(max(ua), 4)], "forearmRatio": [round(min(fa), 4), round(max(fa), 4)],
           "upperArmMean": round(float(np.mean(ua)), 4), "upperArmMeanBase": round(float(np.mean(ua_base)), 4),
           "flipped": flipped, "torn": torn, "flippedBase": flip_base, "flipParts": flip_parts,
           "sliverFlips": slivers, "sliverM2": SLIVER_M2}
    out["ok"] = (out["upperArmRatio"][0] >= 0.17 and out["upperArmRatio"][1] <= 0.24 and out["forearmRatio"][0] >= 0.155
                 and out["forearmRatio"][1] <= 0.20 and out["upperArmMean"] >= out["upperArmMeanBase"]
                 and flipped == 0 and torn == 0)
    return out


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
        if c["part"] == "Hand":
            # 080 FR-04: 손 접촉 클립은 어깨·팔 길이 태그가 있고, 접촉점(공 중심)이 어깨에서 팔 길이 + 손·공 여유 안
            if "shoulders" not in c or "armLength" not in c:
                bad.append(f"{t['id']}: 손 접촉 클립에 shoulders·armLength 없음")
            else:
                d = min(math.dist(sh, c["point"]) for sh in c["shoulders"])
                if d > c["armLength"] + HAND_BALL_SLACK:
                    bad.append(f"{t['id']}: 손 접촉점이 어깨에서 {d:.3f} m (팔 {c['armLength']:.3f} + {HAND_BALL_SLACK})")
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
                if n.endswith(("Proximal", "Intermediate", "Distal")):
                    continue  # 062: 손가락은 손 기준이 맡는다 (클립 생성 지면 보정과 같은 규칙)
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
    under = set(getattr(m, "under", []))  # 062 T17a: 밑깔개는 절단선이 아니다 (경계 정점만 공유하고 끝은 셸 밑에 열려 있다)
    for fi, (f, mat, uvs, part, _) in enumerate(m.faces):
        if part == "skin" and fi not in under:
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
    # 088: 부위 순서(코·볼 > 이마…)는 057 절차 모공 규칙 — MakeHuman diffuse 결은 이마가 가장 많아 판정에서 뺀다(범위·거칠기·수염은 유지)
    ok &= all(abs(metrics[k]["rough"] - v) <= 0.02 for k, v in SKIN_056_ROUGH.items())
    bd = metrics["beard"]  # 057 FR-24
    ok &= bd["coreHf"] <= 0.05 and bd["lowNeck"] <= 0.01 and bd["mouthCorner"] >= 0.3
    out["ok"] = bool(ok)
    return out


KIT_MOTION_CLIPS = ("sprint", "run", "turn", "cut", "jump", "celebr", "header", "stop")  # FR-21 대상 (질주·방향 전환·점프·세리머니)


def _kit_corner(k):
    """상자 꼭짓점 k(0..7) → 보조 뼈별 오프셋 (선수 레스트 좌표)."""
    out = {}
    for n in S.KIT_NAMES:
        _, lo, hi = S.KIT_BOX[n]
        ax = S.kit_axes(n)
        c = [hi[a] if (k >> a) & 1 else lo[a] for a in range(3)]
        out[n] = Vector(tuple(sum(c[a] * ax[a][i] for a in range(3)) for i in range(3)))
    return out


BASE_HEM_CLIPS = {}
KIT_FRACTIONS = (1 / 3, 2 / 3, 1.0)  # 상자 꼭짓점 × 비율


KITPAIRS = {}


def kit_motion(clips, meshes, lod=0):
    """062 FR-21 (T15): 보조 뼈를 이동 상자 꼭짓점 8개(고관절 게이트 적용)로 밀었을 때 새로 생기는 관통 0.
    극단 체형 4 × 대상 클립 × 4프레임. 짝: 밑단 띠 천(보조 뼈 가중치 > 0, 턱 제외) × 피부 전체, 상의 밑단 띠 × 보이는 반바지.
    관통 = (꼭짓점 오프셋 교차 수) − (오프셋 0 교차 수) > 0 — 오프셋과 무관한 기존 접촉(056 penetration 몫)은 세지 않는다."""
    from . import hands as HD
    m = meshes[lod]
    nb = len(MS.OUR_NAMES)
    kitw = m.W[:, nb:].sum(axis=1) if m.W.shape[1] > nb else np.zeros(len(m.verts))
    band = [f[0] for f in m.faces if f[1] == B.KIT and not f[3].endswith("_rim") and kitw[list(f[0])].max() > 1e-6]
    hem = [f[0] for f in m.faces if f[3] == "shirt" and not f[3].endswith("_rim") and kitw[list(f[0])].max() > 1e-6]
    skin = [f[0] for f in m.faces if f[3] == "skin"]
    # 반바지 윗부분은 셔츠 밑단 아래 겹침 구조(가려짐)라 레스트에서도 교차한다 → 셔츠 밑단보다 3 cm 넘게 아래(보이는 허벅지 쪽)만
    hem_y = {pl[0]: pl for pl in B.cut_planes()}["hem"][1][1]
    shorts = [f[0] for f in m.faces if f[3] == "shorts" and m.verts[list(f[0]), 1].max() < hem_y - 0.03]
    # 062 T17a·c: 셔츠 밑 반바지 밑깔개(허리, 밑단 위 3.5 cm)도 짝에 넣는다 — 들리거나 벌어진 셔츠 밑단이 밑깔개를 뚫으면 안 된다.
    # (T17c 전에는 셔츠 밑단이 허벅지를 안 따라가 오프셋 0에서도 고리 전체가 교차했다.) PLAYER_KITPAIRS=1|base: 교차 면 쌍 위치 출력
    import os
    n_vis = len(shorts)
    shorts += [m.faces[i][0] for i in getattr(m, "under", []) if m.faces[i][3] == "shorts"]
    sel = [c for c in clips if any(k in c.id for k in KIT_MOTION_CLIPS)] or list(clips)
    # 꼭짓점만 보면 안 된다: 관통이 이동량에 단조롭지 않다(T15: 반바지 1.0 cm 관통, 1.5 cm 통과) — 런타임은 0~최대를 연속으로 지난다
    corners = [{n: o * fr for n, o in _kit_corner(k).items()} for fr in KIT_FRACTIONS for k in range(8)]
    import os
    diag = os.environ.get("PLAYER_KITDIAG")  # 조정용: 뼈 하나 × 축 하나씩 (끝값) — 요약 "diag"
    if diag:
        corners = []
        for n in S.KIT_NAMES:
            _, lo, hi = S.KIT_BOX[n]
            ax = S.kit_axes(n)
            for a in range(3):
                for v in (lo[a], hi[a]):
                    for fr in KIT_FRACTIONS:
                        corners.append({n: Vector(tuple(v * fr * ax[a][i] for i in range(3)))})
        labels = [f"{n}:{a}:{s_}:{fr:.2f}" for n in S.KIT_NAMES for a in range(3) for s_ in ("lo", "hi") for fr in KIT_FRACTIONS]
        per = {}
    poses, new_hits, worst = 0, 0, []
    base_pose = base_skin = base_hem = 0
    by_corner = {}
    BASE_HEM_CLIPS.clear()
    fresh_total = [0]
    for name, (height, weights, _style) in EXTREMES.items():
        V = _blend(m, weights)
        for c in sel:
            hp = HD.hand_pose_for(c.id, c.group)
            hand_q = HANDQ.setdefault(hp, HD.pose_rotations(hp))
            for f in _sample_frames(c):
                pz = c.sample(f / 30.0)
                for k, qv in hand_q.items():
                    pz.local.setdefault(k, qv)
                rw, head, tail = PO.fk(pz)
                fl = [S.hip_flex(rw["Hips"], (tail[sd + "UpperLeg"] - head[sd + "UpperLeg"]).normalized()) for sd in ("Left", "Right")]

                def hits(off):
                    hd = dict(head)
                    if off:
                        for n, o in off.items():
                            hd[n] = head[n] + rw[S.PARENT[n]] @ (o * S.kit_gate(n, fl[0], fl[1]))  # 런타임과 같은 게이트
                    P = _skin(m, V, rw, hd)
                    tb = _bvh(P, band)
                    a = len(tb.overlap(_bvh(P, skin))) if band and skin else 0
                    if hem and shorts:
                        ov = _bvh(P, hem).overlap(_bvh(P, shorts))
                        b = len(ov)
                        if os.environ.get("PLAYER_KITPAIRS") and (off or os.environ.get("PLAYER_KITPAIRS") == "base"):   # 조정용: 교차 면 쌍 위치 (레스트 좌표 y·z, 밑깔개 여부)
                            for i, j in ov:
                                hc, sc = m.verts[list(hem[i])].mean(axis=0), m.verts[list(shorts[j])].mean(axis=0)
                                k = (round(float(hc[1] - hem_y), 2), round(float(hc[2]), 2), round(float(sc[1] - hem_y), 2), round(float(sc[2]), 2), j >= n_vis)
                                KITPAIRS[k] = KITPAIRS.get(k, 0) + 1
                    else:
                        b = 0
                    return a, b

                a0, b0 = hits(None)
                base_pose += 1
                if a0:
                    base_skin += 1
                if b0:
                    base_hem += 1
                    BASE_HEM_CLIPS[c.id] = BASE_HEM_CLIPS.get(c.id, 0) + 1
                fresh = 0
                for k, off in enumerate(corners):
                    a, b = hits(off)
                    poses += 1
                    d = max(a - a0, 0) + max(b - b0, 0)
                    if (a and not a0) or (b and not b0):
                        fresh += 1
                    if d and diag:
                        per[labels[k]] = per.get(labels[k], 0) + d
                        per["case:" + name] = per.get("case:" + name, 0) + d
                        per["clip:" + c.id] = per.get("clip:" + c.id, 0) + d
                    if d:
                        new_hits += d
                        key = f"{k % 8}@{KIT_FRACTIONS[k // 8]:.2f}|skin{int(a > a0)}hem{int(b > b0)}" if not diag else labels[k]
                        by_corner[key] = by_corner.get(key, 0) + d
                        if len(worst) < 16:
                            worst.append({"case": name, "clip": c.id, "frame": f, "corner": k, "kitSkin": a - a0, "hemShorts": b - b0})
                fresh_total[0] += fresh
    if os.environ.get("PLAYER_KITPAIRS"):
        print("KITPAIRS (hem dy, hem z, shorts dy, shorts z, under):", sorted(KITPAIRS.items(), key=lambda x: -x[1])[:25])
    return {"ok": new_hits == 0, "penetration": new_hits, "poses": poses, "clips": len(sel),
            "bandFaces": len(band), "basePoses": base_pose, "baseKitSkin": base_skin, "baseHemShorts": base_hem,
            "baseHemClips": dict(BASE_HEM_CLIPS), "freshContacts": fresh_total[0], "byCorner": by_corner, "worst": worst, **({"diag": per} if diag else {})}


def kit_cover(meshes):
    """062 T17a: 밑단 접선 이동(상자 크기 × 밑단 끝 몫, 런타임 3 cm 상한)이 밑깔개 폭 − 5 mm 안이어야 구멍이 안 보인다.
    접선 = 상의 밑단은 위(y), 소매·반바지는 뼈 방향 축. 바깥으로 벌어지는 몫은 아래 밑깔개가 보일 뿐이라 세지 않는다.
    + LOD마다 밑깔개 면이 있는지(보조 뼈가 있는 밑단만)."""
    cases = {}
    for n in S.KIT_NAMES:
        frame, lo, hi = S.KIT_BOX[n]
        if n.startswith("Kit_Hem"):
            travel, key = max(abs(lo[1]), abs(hi[1])), "hem"
        else:
            travel, key = max(abs(lo[0]), abs(hi[0])), ("sleeve" if "Sleeve" in n else "short")
        travel = min(travel, 0.03) * B.KIT_SHARE
        cases[n] = {"travel": round(travel, 4), "cover": B.UNDER[key], "ok": travel + 0.005 <= B.UNDER[key]}
    faces = {str(l): len(getattr(m, "under", [])) for l, m in sorted(meshes.items())}
    need = {str(l): bool(B.UNDER_LODS.get(l)) for l in sorted(meshes)}
    lod_ok = all((faces[k] > 0) == need[k] for k in faces)
    return {"ok": all(c["ok"] for c in cases.values()) and lod_ok, "bones": cases, "underFaces": faces}


# 062 FR-91 부위 → 디테일 → 기록 키 (heightfield.FEAT·skin.FEAT, 높이 m / 얼굴은 높이장 단위·밝기 배율)
BAKED_KEYS = {
    "body": {"muscleEdges": "body.muscleEdges", "boneOutline": "body.boneOutline", "tendons": "body.tendons"},
    "hand": {"knuckleCreases": "hand.knuckleCreases", "palmCreases": "hand.palmCreases", "nailEdge": "hand.nailEdge"},
    "face": {"pores": "face.pores", "fineWrinkles": "face.fineWrinkles", "lipGrain": "face.lipGrain", "eyelidCreases": "face.eyelidCreases"},
    "shirt": {"folds": "shirt.folds", "seams": "shirt.seams", "weave": "shirt.weave", "collarRib": "shirt.collarRib"},
    "shorts": {"folds": "shorts.folds", "sideSeam": "shorts.sideSeam", "hemFold": "shorts.hemFold"},
    "sock": {"ribbing": "sock.ribbing", "ankleFolds": "sock.ankleFolds"},
    "boot": {"panelSeams": "boot.panelSeams", "soleStuds": "boot.soleStuds", "heelCounter": "boot.heelCounter"},
}
BAKED_MIN_M = 1e-5      # 구운 높이 기여 하한 (0.01 mm)
BAKED_MIN_FACE = 1e-3   # 얼굴 높이장·밝기 기여 하한


def baked_maps(hf_feat, skin_feat):
    """062 AC-04 summary["bakedMaps"]: FR-91 부위별 디테일이 구운 지도에 실제로 기여했는지(최대 |기여| > 하한).
    정강이 보호대 윤곽은 셸 형상(body._kit_extra)이 만든 음영이라 정강이 앞 점에서 띄움을 잰다. 인쇄 두 항목은 런타임 합성
    (KitCompositor·셰이더)이라 여기서 판정하지 않는다 → 062-body-closeup 육안 (T20)."""
    feat = dict(hf_feat)
    feat.update(skin_feat)
    parts, vals = {}, {}
    for part, items in BAKED_KEYS.items():
        parts[part] = {}
        for name, key in items.items():
            v = float(feat.get(key, 0.0))
            vals[key] = round(v, 6)
            parts[part][name] = v > (BAKED_MIN_FACE if part == "face" else BAKED_MIN_M)
    # 정강이 보호대: 왼 정강이 가운데 앞면 점의 셸 추가 띄움
    planes = {pl[0]: pl for pl in B.cut_planes()}
    knee_y, ank_y = S.HEAD["LeftLowerLeg"][1], S.HEAD["LeftFoot"][1]
    w = np.zeros(len(MS.OUR_NAMES))
    w[MS.OUR_NAMES.index("LeftLowerLeg")] = 1.0
    hx = S.HEAD["LeftLowerLeg"][0]
    p = np.array([hx, 0.5 * ((knee_y - 0.09) + (ank_y + 0.07)), S.HEAD["LeftLowerLeg"][2] + 0.04])
    shin = float(B._kit_extra(p, "sock", w, np.array([0.0, 0.0, 1.0]), planes))
    vals["sock.shinGuard"] = round(shin, 6)
    parts["sock"]["shinGuard"] = shin > 0.001
    vals["skin.thickness"] = round(float(skin_feat.get("skin.thickness", 0.0)), 6)
    vals["skin.sweat"] = round(float(skin_feat.get("skin.sweat", 0.0)), 6)
    ok = all(all(d.values()) for d in parts.values()) and vals["skin.thickness"] > 0.5 and vals["skin.sweat"] > 0.5
    # 모든 텍스처는 이 생성기 함수가 그린다(외부 이미지 입력 없음) — 외부 원천은 licenses 검사가 따로 본다
    return {"ok": bool(ok), "parts": parts, "values": vals, "procedural": True}



# ---------------------------------------------------------------- 062 AC-07·08 얼굴 · 수염 · 표정 (T27)

def _lm_dist_vec(P):
    n = len(P)
    iu = np.triu_indices(n, 1)
    D = np.linalg.norm(P[:, None, :] - P[None, :, :], axis=2)
    return D[iu]


def _lm_distance(a, b):
    """Unity FaceLandmarks.Distance와 같은 식: 거리 벡터 차의 RMS."""
    return float(np.sqrt(np.mean((a - b) ** 2)))


def face_base_heads(R, face_json):
    """바탕 머리 랜드마크 최소 쌍 vs 056 AC-04 기준(무작위 얼굴 400명 평균 쌍 거리 × 0.25 — FaceGenerator 분포·FaceLandmarks 정의)."""
    from . import face as F
    lms = face_json["landmarks"]
    base = np.array([l["base"] for l in lms])

    def pos(weights):
        P = base.copy()
        for i, l in enumerate(lms):
            for k, w in weights.items():
                if k in l["deltas"]:
                    P[i] += np.array(l["deltas"][k]) * w
        return P

    rng = np.random.default_rng(56)  # 통계량이라 Unity와 같은 난수일 필요 없다 (분포만 같게)
    vs = []
    for _ in range(400):
        w = {}
        for a, _, _, r in F.AXES:
            x = r * (rng.random() + rng.random() - 1.0)
            w[f"Face_{a}_{'pos' if x >= 0 else 'neg'}"] = abs(x)
        vs.append(_lm_dist_vec(pos(w)))
    n = len(vs)
    mean = np.mean([_lm_distance(vs[i], vs[(i + 1) % n]) for i in range(n)] +
                   [_lm_distance(vs[i], vs[(i + n // 2) % n]) for i in range(n)])
    ids = [b[0] for b in F.BASE_HEADS]
    bv = {b: _lm_dist_vec(pos({b: 1.0})) for b in ids}
    pairs = sorted((_lm_distance(bv[a], bv[b]), a, b) for i, a in enumerate(ids) for b in ids[i + 1:])
    # 실루엣: 머리 영역(head_mask > 0.02) 정면·옆 마스크, 쌍별 두 방향 중 큰 1 − IoU
    from . import visual_metrics as VM
    k = F.head_mask(R)
    keep = set(v for v in R.groups["body"] if k[v] > 0.02)
    tris = []
    for f in R.faces:
        if all(v in keep for v in f):
            tris += [(f[0], f[j], f[j + 1]) for j in range(1, len(f) - 1)]
    c, d = F.eye_frame(R)
    lo = np.array([-0.2, c[1] - 0.25])
    masks = {}
    for b in ids:
        P = R.verts + R.extra_deltas[b]
        masks[b] = [VM._tri_mask(np.stack([P[:, 0], P[:, 1]], 1), tris, lo, 0.4),
                    VM._tri_mask(np.stack([P[:, 2], P[:, 1]], 1), tris, lo + np.array([-0.05, 0.0]), 0.4)]
    sil = []
    for i, a in enumerate(ids):
        for b in ids[i + 1:]:
            dmax = 0.0
            for va, vb in zip(masks[a], masks[b]):
                u = np.logical_or(va, vb).sum()
                dmax = max(dmax, 1.0 - np.logical_and(va, vb).sum() / u if u else 0.0)
            sil.append((dmax, a, b))
    sil.sort()
    return {"count": len(ids), "regions": sorted({b[1] for b in F.BASE_HEADS}),
            "landmarkMinPair": round(pairs[0][0], 6), "landmarkPair": list(pairs[0][1:]), "landmark056Min": round(float(mean) * 0.25, 6),
            "landmarkMean056": round(float(mean), 6), "silhouetteMinPair": round(sil[0][0], 4), "silhouettePair": list(sil[0][1:])}


def _region_faces(m, part_pred, box=None, centers=None, radius=None):
    out = []
    for f, _mat, _uv, part, _ in m.faces:
        if not part_pred(part):
            continue
        cpos = m.verts[list(f)].mean(axis=0)
        if box is not None and not (np.all(cpos >= box[0]) and np.all(cpos <= box[1])):
            continue
        if centers is not None and min(np.linalg.norm(cpos - cc) for cc in centers) > radius:
            continue
        out.append(f)
    return out


def face_expressions(R, mm, closeup):
    """표정 키가 있는 LOD, 최대치(1)에서 안구·치아·혀가 피부(눈꺼풀·입술)를 뚫고 나온 정점 수(LOD0 + 클로즈업, eyes_covered와 같은
    정면 광선 판정 — 면 교차 쌍은 안구가 머리 안에 박힌 기준 메시 구조상 무표정에서도 수백 개라 쓰지 않는다), 환호 입 안 보임 비율."""
    from . import face as F
    keys = [e[0] for e in F.EXPRESSIONS]
    out = {"lod": {str(l): sorted(k for k in m.deltas if k.startswith("Expr_") and float(np.abs(m.deltas[k]).max()) > 1e-6)
                   for l, m in sorted(mm.items())},
           "closeup": sorted(k for k in closeup.deltas if k.startswith("Expr_") and float(np.abs(closeup.deltas[k]).max()) > 1e-6),
           "penetration": {k: 0 for k in keys}, "penetrationRest": {}, "mouthInterior": {}}
    lips = R.verts[R.groups["lips"]]
    mbox = (lips.min(axis=0) - np.array([0.006, 0.006, 0.02]), lips.max(axis=0) + np.array([0.006, 0.006, 0.004]))
    eyes = [R.verts[R.groups[g]].mean(axis=0) for g in F.EYE_GROUPS]
    for name, m in (("lod0", mm[min(mm)]), ("closeup", closeup)):
        out["penetrationRest"][name] = _poke_through(m, m.verts, eyes)
        for k in keys:
            if k in m.deltas:
                out["penetration"][k] += _poke_through(m, m.verts + m.deltas[k], eyes)
    m = mm[min(mm)]
    lip_f = _region_faces(m, lambda p: p == "skin", box=mbox)
    for k in keys:
        P = m.verts + m.deltas.get(k, 0.0)
        L = P[sorted({v for f in lip_f for v in f})]
        out["mouthInterior"][k] = _mouth_interior(m, P, (L.min(axis=0) - 0.002, L.max(axis=0) + 0.002))
    return out


def _poke_through(m, P, eye_centers):
    """안구(앞 반구)·치아·혀 정점 중 정면에서 처음 맞는 피부가 그 점 바로 뒤(0.5~6 mm)에 있는 수 = 피부를 뚫고 나옴.
    입 틈·눈 틈으로 보이는 경우는 광선이 머리 안쪽 깊은 곳(입안 캡·머리 뒤)에 맞아 세지 않는다 (face.eyes_covered와 같은 규칙)."""
    from mathutils import Vector
    skin = [f for f, _mat, _uv, part, _ in m.faces if part == "skin"]
    tree = _bvh(P, skin)
    bad = 0
    for i, part in enumerate(m.vpart):
        if part not in ("eye", "mouth_teeth_upper", "mouth_teeth_lower", "mouth_tongue", "mouth_cap"):
            continue
        p = P[i]
        if part == "eye" and p[2] < min(eye_centers, key=lambda c: abs(c[0] - p[0]))[2]:
            continue
        loc, _n, _idx, _d = tree.ray_cast(Vector((p[0], p[1], p[2] + 0.5)), Vector((0, 0, -1)))
        if loc is not None and 0.0005 < p[2] - loc[2] < 0.006:
            bad += 1
    # 실루엣: 정면·좌우 45°·좌우 90°·아래에서 입 안 부품 정점이 머리 실루엣 밖(1 px 여유)에 나오면 볼·턱을 뚫고 나온 것
    from . import visual_metrics as VM
    mouth = [i for i, part in enumerate(m.vpart) if part.startswith("mouth_")]
    if mouth:
        hc = P[mouth].mean(axis=0)
        tris = []
        for f in skin:
            tris += [(f[0], f[k], f[k + 1]) for k in range(1, len(f) - 1)]
        span = 0.12
        for yaw, pitch in ((0, 0), (45, 0), (-45, 0), (90, 0), (-90, 0), (0, -60)):
            ry, rp = math.radians(yaw), math.radians(pitch)
            X = P - hc
            x1 = X[:, 0] * math.cos(ry) + X[:, 2] * math.sin(ry)
            z1 = -X[:, 0] * math.sin(ry) + X[:, 2] * math.cos(ry)
            y2 = X[:, 1] * math.cos(rp) - z1 * math.sin(rp)
            P2 = np.stack([x1, y2], axis=1)
            lo = np.array([-span / 2, -span / 2])
            mask = VM._tri_mask(P2, tris, lo, span)
            mask = mask | np.roll(mask, 1, 0) | np.roll(mask, -1, 0) | np.roll(mask, 1, 1) | np.roll(mask, -1, 1)
            q = ((P2[mouth] - lo) / span * (VM.SIL - 1)).astype(int)
            inside = (q >= 0).all(axis=1) & (q < VM.SIL).all(axis=1)
            bad += int((~inside).sum()) + int((~mask[q[inside, 1], q[inside, 0]]).sum())
    return bad


def _mouth_interior(m, P, mbox, res=0.00025):
    """정면 직교 래스터(표정을 건 입술 상자): 가장 가까운 면 = 피부(1) / 입 안(3) / 없음(0).
    입 틈 = 열마다 위·아래 피부(입술) 사이의 피부 아닌 픽셀. 반환 = 틈 중 입 안이 보이는 비율(틈이 없으면 0)."""
    x0, y0 = mbox[0][0], mbox[0][1]
    W = int((mbox[1][0] - x0) / res) + 1
    H = int((mbox[1][1] - y0) / res) + 1
    depth = np.full((H, W), -1e9)
    lab = np.zeros((H, W), dtype=np.int8)
    for f, _mat, _uv, part, _ in m.faces:
        q = P[list(f)]
        cpos = q.mean(axis=0)
        if not (np.all(cpos[:2] >= mbox[0][:2] - 0.01) and np.all(cpos[:2] <= mbox[1][:2] + 0.01)) or cpos[2] < mbox[0][2] - 0.04:
            continue
        if part.startswith("mouth_"):
            L = 3
        elif part in ("skin", "head"):
            L = 1
        else:
            continue
        for j in range(1, len(f) - 1):
            a, b, c = q[0], q[j], q[j + 1]
            xs = np.array([a[0], b[0], c[0]]); ys = np.array([a[1], b[1], c[1]])
            i0, i1 = int(max((xs.min() - x0) / res, 0)), int(min((xs.max() - x0) / res + 1, W))
            j0, j1 = int(max((ys.min() - y0) / res, 0)), int(min((ys.max() - y0) / res + 1, H))
            if i1 <= i0 or j1 <= j0:
                continue
            gx, gy = np.meshgrid(x0 + (np.arange(i0, i1) + 0.5) * res, y0 + (np.arange(j0, j1) + 0.5) * res)
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-14:
                continue
            l0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / den
            l1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / den
            l2 = 1 - l0 - l1
            ins = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
            z = l0 * a[2] + l1 * b[2] + l2 * c[2]
            sub = depth[j0:j1, i0:i1]
            sl = lab[j0:j1, i0:i1]
            upd = ins & (z > sub)
            sub[upd] = z[upd]
            sl[upd] = L
    skin = lab == 1
    gap = np.zeros_like(skin)
    for i in range(W):
        idx = np.flatnonzero(skin[:, i])
        if len(idx) < 2:
            continue
        for a_, b_ in zip(idx[:-1], idx[1:]):
            if b_ - a_ > 2:
                gap[a_ + 1:b_, i] = True
    n = int(gap.sum())
    if n < 20:
        return 0.0
    return round(float((lab[gap] == 3).sum()) / n, 4)


def face062(R, mm, closeup, face_json, skin_metrics):
    """summary["face062"] (test_face.py 약속)."""
    return {"ok": True,  # 기록용 — 판정은 test_face
            "baseHeads": face_base_heads(R, face_json),
            "beards": skin_metrics.get("beards", {}),
            "expressions": face_expressions(R, mm, closeup)}
