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
# (id, 두께 m). 056 4종 순서 유지 + 062 FR-66 신규 3종(콧수염·염소수염·진한 스텁블)은 뒤에 (Unity 수염 인덱스 호환)
BEARDS = [("none", 0.0), ("stubble", 0.0006), ("short", 0.0022), ("full", 0.0055),
          ("mustache", 0.0030), ("goatee", 0.0040), ("heavy_stubble", 0.0012)]
# 062: 수염 종류 → 영역 마스크 (beard_masks). 7종 = 영역 4 × 굵기 — 텍스처는 beard_mask0.png RGBA(full·mustache·goatee·heavy)
BEARD_REGION = {"stubble": "full", "short": "full", "full": "full", "mustache": "mustache", "goatee": "goatee",
                "heavy_stubble": "heavy"}
BEARD_CHANNELS = ("full", "mustache", "goatee", "heavy")

# 062 FR-70: 표정 4종(+ 무표정 = 키 0). MPFB CC0 표정 단위 타깃(FACS 계열) 조합. LOD0·클로즈업 머리에만(FR-71).
# 시선은 셰이프가 아니라 눈 뼈가 없어 다루지 않는다(실망 = 눈꺼풀을 조금 내려 시선이 아래로 가는 인상).
EXPRESSIONS = [
    ("Expr_cheer", [(["mouth-open"], 0.85), (["mouth-corner-puller"], 0.35), (["eyebrows-left-up", "eyebrows-right-up"], 0.8),
                    (["eye-left-opened-up", "eye-right-opened-up"], 0.4)]),
    ("Expr_smile", [(["mouth-corner-puller"], 0.8), (["mouth-elevation"], 0.15), (["eye-left-slit", "eye-right-slit"], 0.3)]),
    ("Expr_disappoint", [(["eyebrows-left-inner-up", "eyebrows-right-inner-up"], 0.8), (["mouth-depression"], 0.7),
                         (["eye-left-closure", "eye-right-closure"], 0.2)]),
    ("Expr_focus", [(["mouth-compression"], 0.6), (["mouth-retraction"], 0.4), (["eyebrows-left-down", "eyebrows-right-down"], 0.6),
                    (["eye-left-slit", "eye-right-slit"], 0.4)]),
]
EYE_GROUPS = ("helper-l-eye", "helper-r-eye")

# ---------------------------------------------------------------- 062 FR-60~62 바탕 머리 (T25 시안)
# 일반 얼굴 유형 8개 = MPFB CC0 인종 매크로 비율(머리·목에만, 몸 비율은 그대로) + CC0 얼굴 타깃 조합. 특정 인물·사진 입력 없음(FR-61).
# 방향은 공개 얼굴 인체 측정 연구의 집단 평균 경향(Farkas 2005 국제 비교 등, 기획서 리서치 2.2a)만 따르고 값은 중간 세기로 둔다 —
# 같은 지역 안 개인차는 기존 얼굴 30축(FaceGenerator)이 낸다. (id, 지역 태그, 인종 매크로 비율, [(타깃들, 가중치)])
# 지역 7개(FR-62): east_asia · southeast_asia · south_asia · west_asia_north_africa · west_central_africa · europe · latin_america
BASE_HEADS = [
    ("Base_0", "east_asia", {"asian": 1.0, "caucasian": 0.0, "african": 0.0}, [
        (["l-eye-epicanthus-in", "r-eye-epicanthus-in"], 0.9), (["l-eye-eyefold-down", "r-eye-eyefold-down"], 0.8),
        (["l-cheek-bones-incr", "r-cheek-bones-incr"], 0.8), (["nose-hump-decr"], 0.6), (["nose-scale-depth-decr"], 0.6),
        (["head-scale-horiz-incr"], 0.5), (["head-back-scale-depth-decr"], 0.5), (["forehead-nubian-decr"], 0.4),
        (["chin-width-incr"], 0.3)]),
    ("Base_1", "southeast_asia", {"asian": 0.6, "caucasian": 0.15, "african": 0.25}, [
        (["l-eye-epicanthus-in", "r-eye-epicanthus-in"], 0.4), (["l-eye-eyefold-down", "r-eye-eyefold-down"], 0.4),
        (["l-eye-scale-incr", "r-eye-scale-incr"], 0.3), (["nose-nostrils-width-incr"], 0.8), (["nose-scale-horiz-incr"], 0.5),
        (["nose-scale-vert-decr"], 0.5), (["nose-scale-depth-decr"], 0.5), (["mouth-upperlip-volume-incr"], 0.6),
        (["mouth-lowerlip-volume-incr"], 0.6), (["head-scale-vert-decr"], 0.4), (["head-round"], 0.6), (["chin-height-decr"], 0.5),
        (["l-cheek-bones-incr", "r-cheek-bones-incr"], 0.4)]),
    ("Base_2", "south_asia", {"asian": 0.2, "caucasian": 0.6, "african": 0.2}, [
        (["l-eye-scale-incr", "r-eye-scale-incr"], 0.8), (["nose-scale-vert-incr"], 0.5), (["nose-point-down"], 0.8),
        (["nose-nostrils-width-incr"], 0.4), (["mouth-lowerlip-volume-incr"], 0.6), (["mouth-upperlip-volume-incr"], 0.3),
        (["head-oval"], 1.0), (["chin-height-incr"], 0.4), (["l-cheek-volume-decr", "r-cheek-volume-decr"], 0.4),
        (["forehead-scale-vert-incr"], 0.4), (["eyebrows-angle-down"], 0.4), (["l-eye-bag-incr", "r-eye-bag-incr"], 0.4)]),
    ("Base_3", "west_asia_north_africa", {"asian": 0.0, "caucasian": 0.8, "african": 0.2}, [
        (["nose-hump-incr"], 0.9), (["nose-scale-vert-incr"], 0.6), (["nose-curve-convex"], 0.7), (["nose-point-down"], 0.3),
        (["forehead-nubian-incr"], 0.7), (["eyebrows-trans-down"], 0.5), (["head-rectangular"], 0.6), (["head-scale-depth-incr"], 0.4),
        (["chin-prominent-incr"], 0.5)]),
    ("Base_4", "west_central_africa", {"asian": 0.0, "caucasian": 0.0, "african": 1.0}, [
        (["nose-scale-horiz-incr"], 0.5), (["nose-nostrils-width-incr"], 0.5), (["nose-scale-depth-decr"], 0.3),
        (["mouth-upperlip-volume-incr"], 0.6), (["mouth-lowerlip-volume-incr"], 0.6), (["chin-prognathism-incr"], 0.3),
        (["head-scale-depth-incr"], 0.2), (["l-cheek-bones-incr", "r-cheek-bones-incr"], 0.3), (["forehead-trans-forward"], 0.3)]),
    ("Base_5", "europe", {"asian": 0.0, "caucasian": 1.0, "african": 0.0}, [
        (["nose-scale-depth-incr"], 0.6), (["nose-point-width-decr"], 0.5), (["nose-scale-horiz-decr"], 0.4), (["head-square"], 0.7),
        (["chin-prominent-incr"], 0.6), (["chin-width-incr"], 0.4), (["forehead-nubian-incr"], 0.5), (["head-scale-horiz-incr"], 0.3),
        (["mouth-upperlip-volume-decr"], 0.5), (["mouth-lowerlip-volume-decr"], 0.4), (["l-cheek-volume-decr", "r-cheek-volume-decr"], 0.2)]),
    ("Base_6", "europe", {"asian": 0.0, "caucasian": 0.85, "african": 0.15}, [
        (["head-scale-horiz-decr"], 0.5), (["head-scale-vert-incr"], 0.4), (["chin-width-decr"], 0.6), (["chin-height-incr"], 0.5),
        (["nose-scale-vert-incr"], 0.5), (["nose-hump-incr"], 0.4), (["l-cheek-volume-decr", "r-cheek-volume-decr"], 0.5),
        (["l-cheek-bones-incr", "r-cheek-bones-incr"], 0.4)]),
    ("Base_7", "latin_america", {"asian": 0.35, "caucasian": 0.45, "african": 0.2}, [
        (["l-cheek-bones-incr", "r-cheek-bones-incr"], 1.0), (["head-round"], 0.8), (["head-scale-horiz-incr"], 0.6),
        (["nose-scale-horiz-incr"], 0.5), (["nose-hump-incr"], 0.7), (["nose-scale-depth-incr"], 0.3),
        (["mouth-upperlip-volume-incr"], 0.4), (["chin-width-incr"], 0.7), (["chin-height-decr"], 0.4), (["forehead-scale-vert-decr"], 0.4),
        (["l-eye-eyefold-down", "r-eye-eyefold-down"], 0.3)]),
]
REGIONS = ("east_asia", "southeast_asia", "south_asia", "west_asia_north_africa", "west_central_africa", "europe", "latin_america")


def head_mask(R):
    """바탕 머리 델타를 거는 영역: 머리 + 목 절반 가중치, 목 아래로 부드럽게 0 (몸 비율·칼라 이음매는 그대로)."""
    hi = MS.OUR_NAMES.index("Head")
    ni = MS.OUR_NAMES.index("Neck")
    w = R.weights[:, hi] + 0.5 * R.weights[:, ni]
    return np.clip((w - 0.25) / 0.5, 0.0, 1.0)


def base_head_deltas(R):
    """{Base_n: (V,3) 델타} — 인종 매크로 비율(기준 1/3씩에서 바꾼 만큼, 머리 영역만) + 얼굴 타깃 합. 062 T25 시안: 아직 셰이프 키로
    내보내지 않는다(T27 확정 때 register에 넣는다)."""
    k = head_mask(R)[:, None]
    core = k[:, 0] > 0.95
    out = {}
    for bid, _region, race, targets in BASE_HEADS:
        # 인종 매크로는 키·목 길이도 바꿔 머리 전체를 옮긴다 → 머리 평균 이동을 빼고(목 쪽으로 서서히 0) 형태 차이만 남긴다
        m = R.macro_delta({"race": race})
        m = (m - m[core].mean(axis=0)) * k
        d = m
        for names, w in targets:
            d = d + R.target_delta(names) * w
        out[bid] = d
    return out


def key_names():
    out = []
    for a, _, _, _ in AXES:
        out += [f"Face_{a}_pos", f"Face_{a}_neg"]
    return out + [b[0] for b in BASE_HEADS] + [e[0] for e in EXPRESSIONS]   # 062: 랜드마크 deltas에 바탕 머리·표정도


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
    """수염 자리 0~1 (정점 위치 p, 선수 좌표). 랜드마크 기준 비율(눈 중심 c, 눈 사이 d)로 정한다.
    057 FR-24: 아래 경계 = 턱 밑 1~2 cm(울대 위)에서 끝나고 옆으로 갈수록 턱선을 따라 올라간다. 입은 입술만 비우고
    콧수염·입꼬리·턱이 이어진다. 경계는 넓게 흐린다(칼로 자른 선 금지). 밀도는 턱·콧수염이 짙고 볼이 옅다."""
    c, d = eye_frame(R)
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    rel_y = (y - c[1]) / d
    rel_x = np.abs(x - c[0]) / d
    front = np.clip((z - (c[2] - 1.7 * d)) / (0.25 * d), 0, 1)
    sx = np.clip(rel_x - 0.45, 0, 2)
    # 위 경계(볼 선): 광대 아래, 옆으로 갈수록 낮게 — 0.35 폭으로 흐림
    top = -0.95 - 0.30 * sx
    m = np.clip((top - rel_y) / 0.35, 0, 1)
    # 아래 경계(목 선): 가운데 −2.55, 옆으로 갈수록 턱선을 따라 올라감 — 0.30 폭으로 흐림
    bottom = -2.55 + 0.75 * sx ** 1.3
    m = m * np.clip((rel_y - bottom) / 0.30, 0, 1)
    # 입술만 비운다 (부드러운 타원) — 콧수염·입꼬리·턱이 이어지게
    lip = ((rel_x / 0.40) ** 2 + ((rel_y + 1.50) / 0.10) ** 2)
    m = m * np.clip((lip - 1.0) / 0.6, 0, 1)
    # 밀도: 턱·콧수염 짙게, 볼 옅게
    dens = 0.55 + 0.45 * np.exp(-(sx / 0.55) ** 2)
    m = m * dens
    # 귀 앞까지, 뒷목 제외
    m = m * front * np.clip((1.30 - rel_x) / 0.25, 0, 1)
    return m


def _ramp(x, w):
    """0 → 1 부드러운 경사 (폭 w, smoothstep) — 수염 경계를 057처럼 흐리게."""
    t = np.clip(x / w, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def beard_masks(R, p):
    """062 FR-66 수염 영역 4개 {full, mustache, goatee, heavy} (0~1). full = 057 beard_mask 그대로(턱·볼·입 둘레),
    heavy = full을 볼 위로 조금 넓힌 진한 스텁블, mustache = 윗입술 위 띠(입꼬리에서 살짝 내려옴), goatee = 턱 + 콧수염 + 입꼬리 연결.
    경계는 057처럼 넓게 흐린다. 콧수염·염소수염은 볼(입꼬리 바깥 0.6d 넘게)에 번지지 않는다(볼 밀도 ≤ 0.01)."""
    c, d = eye_frame(R)
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ry = (y - c[1]) / d
    rx = np.abs(x - c[0]) / d
    front = np.clip((z - (c[2] - 1.7 * d)) / (0.25 * d), 0, 1)
    full = beard_mask(R, p)
    # 진한 스텁블: 볼 위 경계를 0.15d 올리고 밀도를 고르게 (턱·볼 차이 작게)
    sx = np.clip(rx - 0.45, 0, 2)
    top = -0.80 - 0.30 * sx
    heavy = np.clip((top - ry) / 0.30, 0, 1) * np.clip((ry - (-2.55 + 0.75 * sx ** 1.3)) / 0.30, 0, 1)
    lip = ((rx / 0.40) ** 2 + ((ry + 1.50) / 0.10) ** 2)
    heavy = heavy * np.clip((lip - 1.0) / 0.6, 0, 1) * front * np.clip((1.30 - rx) / 0.25, 0, 1) * 0.9
    # 콧수염: 윗입술 위 (ry −1.42 ~ −1.10), 가로 ≤ 0.52d, 입꼬리에서 −1.55까지 살짝 내려온다. 경계 경사 ≥ 0.12d (057 흐린 경계)
    droop = 0.25 * np.clip((rx - 0.30) / 0.20, 0, 1)
    must = (_ramp(ry - (-1.46 - droop), 0.14) * _ramp(-1.06 - ry, 0.14) * _ramp(0.56 - rx, 0.14))
    must = must * np.clip((lip - 1.0) / 0.8, 0, 1) * front
    # 염소수염: 턱(ry −2.40 ~ −1.60, 가로 ≤ 0.46d) + 입꼬리 세로 띠(0.32~0.54d) + 콧수염
    chin = _ramp(ry - (-2.42), 0.16) * _ramp(-1.58 - ry, 0.14) * _ramp(0.48 - rx, 0.14)
    side = _ramp(rx - 0.30, 0.10) * _ramp(0.56 - rx, 0.10) * _ramp(ry - (-1.80), 0.12) * _ramp(-1.36 - ry, 0.12)
    goat = np.maximum(np.maximum(chin, side), must) * np.clip((lip - 1.0) / 0.8, 0, 1) * front
    return {"full": full, "mustache": must, "goatee": goat, "heavy": heavy}


# ---------------------------------------------------------------- 델타 등록

def register(R):
    """R.extra_deltas에 얼굴·수염·HideHead 델타를 넣는다 (기준 정점 전체, 선수 좌표)."""
    out = {}
    for a, neg, pos, rng in AXES:
        out[f"Face_{a}_pos"] = R.target_delta(pos)
        out[f"Face_{a}_neg"] = R.target_delta(neg)
    out.update(base_head_deltas(R))   # 062 FR-60·61 바탕 머리 8개 (T25 승인 v2)
    for eid, units in EXPRESSIONS:    # 062 FR-70
        d = np.zeros_like(R.verts)
        for names, w in units:
            d = d + R.target_delta(names) * w
        out[eid] = d
    N = vertex_normals(R)
    bm = beard_mask(R, R.verts)
    masks = beard_masks(R, R.verts)
    hi = MS.OUR_NAMES.index("Head")
    headw = R.weights[:, hi]
    for bid, th in BEARDS[1:]:
        out[f"Beard_{bid}"] = N * (masks[BEARD_REGION[bid]] * th)[:, None]
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
    return {"schemaVersion": 1, "targets": axes_json(),
            "beards": [{"id": b, "thickness": t, "region": BEARD_REGION.get(b)} for b, t in BEARDS],
            "baseHeads": [{"id": b, "region": r} for b, r, _, _ in BASE_HEADS],   # 062 FR-60~62 (필드 추가만)
            "expressions": [{"id": e} for e, _ in EXPRESSIONS],                   # 062 FR-70
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
