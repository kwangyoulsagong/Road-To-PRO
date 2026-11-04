"""생성 검사 (specs/043 AC-02·03·09, FR-31·32). 실패 항목이 있으면 gen_player가 종료 코드 3.

- 예산(AC-03): LOD0 6k~10k, LOD1 2k~3k, LOD2 ≤ 1k tris, 머티리얼 ≤ 3, 텍스처 ≤ 2048.
- 극단 체형(AC-02): 키 최소·최대(루트 균등 스케일 160/180·200/180) × 체중 최소(Thin)·최대(Heavy+Muscular) 4조합에서
  발 최저점 높이 0(오차 ≤ 2 cm), 붙어 있지 않은 부위 쌍(body.CHECK_PAIRS) 사이 삼각형 교차 0. 긴 머리도 함께 본다.
- 클립(FR-31·32): 목록 분류 전부, 접촉 프레임 범위·접촉점 높이가 선언한 공 높이 범위 안.
- 걸음 접지 미끄러짐(AC-09 원본 쪽): 로코모션 클립 접지 구간 볼(발가락 뿌리) 이동 ≤ 5 mm.
"""
import math

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


def budgets(meshes, textures):
    out = {"ok": True, "lods": {}, "textures": textures}
    for lod, m in sorted(meshes.items()):
        tris = m.triangles
        lo, hi = TRI_BUDGET[lod]
        mats = len({f[1] for f in m.faces})
        ok = lo <= tris <= hi and mats <= MATERIAL_BUDGET
        out["lods"][str(lod)] = {"triangles": tris, "materials": mats, "ok": ok}
        out["ok"] &= ok
        for f in m.faces:
            for u in f[2]:
                if not (-1e-6 <= u[0] <= 1 + 1e-6 and -1e-6 <= u[1] <= 1 + 1e-6):
                    out["ok"] = False
                    out["uvOutOfRange"] = True
    for name, (w, h) in textures.items():
        if w > TEXTURE_MAX or h > TEXTURE_MAX:
            out["ok"] = False
    return out


def _blend(base, keys, weights):
    out = []
    for i, p in enumerate(base):
        x, y, z = p
        for k, w in weights.items():
            q = keys[k][i]
            x += (q[0] - p[0]) * w
            y += (q[1] - p[1]) * w
            z += (q[2] - p[2]) * w
        out.append((x, y, z))
    return out


def _part_bvh(m, verts, part):
    polys = [f[0] for f in m.faces if f[3] == part]
    used = sorted({i for p in polys for i in p})
    remap = {i: j for j, i in enumerate(used)}
    return BVHTree.FromPolygons([verts[i] for i in used], [[remap[i] for i in p] for p in polys], all_triangles=False)


EXTREMES = {
    "short_light": (HEIGHT_RANGE_CM[0], {"Thin": 1.0}),
    "short_heavy": (HEIGHT_RANGE_CM[0], {"Heavy": 1.0, "Muscular": 1.0}),
    "tall_light": (HEIGHT_RANGE_CM[1], {"Thin": 1.0, "Hair_Long": 1.0}),
    "tall_heavy": (HEIGHT_RANGE_CM[1], {"Heavy": 1.0, "Muscular": 1.0, "Hair_Dreads": 1.0}),
}


def extremes(lod=0):
    m = B.build(lod)
    keys = B.shape_keys(lod)
    out = {"ok": True, "cases": {}}
    foot_idx = [i for i, p in enumerate(m.vpart) if p.startswith("foot_")]
    for name, (height, weights) in EXTREMES.items():
        scale = height / (S.REF_HEIGHT * 100.0)
        verts = [(x * scale, y * scale, z * scale) for x, y, z in _blend(m.verts, keys, weights)]
        foot_min = min(verts[i][1] for i in foot_idx)
        all_min = min(v[1] for v in verts)
        hits = {}
        for a, b in B.CHECK_PAIRS:
            n = len(_part_bvh(m, verts, a).overlap(_part_bvh(m, verts, b)))
            if n:
                hits[f"{a}×{b}"] = n
        ok = abs(foot_min) <= 0.02 and all_min >= -1e-6 and not hits
        out["cases"][name] = {"footMin": foot_min, "allMin": all_min, "intersections": hits, "ok": ok}
        out["ok"] &= ok
    # 헤어 스타일마다 머리카락이 몸통·귀를 뚫지 않는지(기본 체형)
    for style in B.HAIR_STYLES:
        verts = _blend(m.verts, keys, {style: 1.0})
        hits = {}
        for a, b in (("hair", "torso"), ("hair", "ear_L"), ("hair", "ear_R")):
            n = len(_part_bvh(m, verts, a).overlap(_part_bvh(m, verts, b)))
            if n:
                hits[f"{a}×{b}"] = n
        out["cases"]["hair_" + style] = {"intersections": hits, "ok": not hits, "footMin": 0.0, "allMin": 0.0}
        out["ok"] &= not hits
    return out


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
