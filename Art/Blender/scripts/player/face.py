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
import os

import numpy as np

from . import mpfb_src as MS
from . import skeleton as S

# 088 FR-51 (T26): 머리·목 = 시안 MPFB 사람과 같은 체형 매크로 (사용자 승인 시안 groom_v2 human()). 몸은 062 체형 유지.
PROTO_MACRO = {"muscle": 0.85, "weight": 0.55, "proportions": 0.7, "height": 0.6}
# 088 FR-51 [제안]: 다양성은 인종 비율·피부 세트·톤으로 — 062 지역 얼굴 타깃 조합·무작위 얼굴 축은 시안 얼굴을 바꾸지 않을 만큼만 남긴다
FACE_TARGET_GAIN = 0.32  # 바탕 머리의 지역 얼굴 타깃 조합 배율 (062 실루엣 쌍 ≥ 0.02 — 2026-11-23 사용자 "기준 완화", 0.3은 0.0198)
AXIS_GAIN = 0.8          # 무작위 얼굴 축 범위 배율 (팀 안 구별 기준 MinDistance는 평균 쌍 거리의 25 %라 함께 바뀐다)
                         # 088 FR-61 (2026-11-24 사용자 "잘생긴·못생긴·평범남 다 있게"): T26 0.35 → 0.8 [제안]
# 088 FR-59 (2026-11-24 사용자 "동양인 얼굴이 여자 얼굴 같아"): 바탕 머리 8개 공통 남성 성인 골격 타깃 [제안] — 지역별로 다르게 주지 않는다
MALE_BASE = [
    (["chin-prominent-incr"], 0.35), (["chin-width-incr"], 0.3), (["forehead-nubian-incr"], 0.4),
    (["eyebrows-trans-down"], 0.3), (["l-cheek-volume-decr", "r-cheek-volume-decr"], 0.3),
]
# 088 T30d: 바탕 머리별 추가 남성화(배율 1) [제안] — 사용자 2026-11-24 "흑인 서양인은 ㄱㅊ아, 저 동아시안이 문제인거야" → 동아시아만.
# 시안 렌더(MPFB asian 0.7 + 아래 조합 0.6배)로 고른 방향: 턱뼈·턱 폭, 볼 살·입술 두께 줄임, 코 폭
BASE_MASC = {
    "Base_0": [(["chin-bones-incr"], 0.3), (["chin-width-incr"], 0.2), (["l-cheek-volume-decr", "r-cheek-volume-decr"], 0.2),
               (["mouth-upperlip-volume-decr"], 0.3), (["mouth-lowerlip-volume-decr"], 0.2), (["nose-scale-horiz-incr"], 0.15),
               (["eyebrows-angle-down"], 0.2)],
}

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
AXES = [(a, neg, pos, round(rng * AXIS_GAIN, 4)) for a, neg, pos, rng in AXES]   # 088 T26
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
    ("Base_0", "east_asia", {"asian": 0.7, "caucasian": 0.3, "african": 0.0}, [   # 088 T26 시안 asian 0.9 → T30d 0.7 (MPFB asian 매크로가 앳되고 중성적 — 사용자 "동양인 얼굴이 여자 얼굴 같아")
        # 088 T30d: 몽고주름·눈꺼풀 내림 타깃 삭제(사용자 2026-11-24 "눈이 인종 비하 하는 거 같음") — 지역 전원에 같은 눈매를 박는 고정관념 과장.
        # 눈매는 인종 매크로의 자연스러운 정도만, 사람마다의 눈 크기·쌍꺼풀은 개인 얼굴 축(모든 지역 같은 범위)이 맡는다
        (["l-cheek-bones-incr", "r-cheek-bones-incr"], 0.8), (["nose-hump-decr"], 0.6), (["nose-scale-depth-decr"], 0.6),
        (["head-scale-horiz-incr"], 0.5), (["head-back-scale-depth-decr"], 0.5), (["forehead-nubian-decr"], 0.4),
        (["chin-width-incr"], 0.3)]),
    ("Base_1", "southeast_asia", {"asian": 0.6, "caucasian": 0.15, "african": 0.25}, [
        (["l-eye-scale-incr", "r-eye-scale-incr"], 0.3), (["nose-nostrils-width-incr"], 0.8),   # 088 T30d: 몽고주름·눈꺼풀 내림 삭제 (["nose-scale-horiz-incr"], 0.5),
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
    ("Base_4", "west_central_africa", {"asian": 0.0, "caucasian": 0.05, "african": 0.95}, [   # 088 T26: 시안 dark 세트 비율
        (["nose-scale-horiz-incr"], 0.5), (["nose-nostrils-width-incr"], 0.5), (["nose-scale-depth-decr"], 0.3),
        (["mouth-upperlip-volume-incr"], 0.6), (["mouth-lowerlip-volume-incr"], 0.6), (["chin-prognathism-incr"], 0.3),
        (["head-scale-depth-incr"], 0.2), (["l-cheek-bones-incr", "r-cheek-bones-incr"], 0.3), (["forehead-trans-forward"], 0.3)]),
    ("Base_5", "europe", {"asian": 0.1, "caucasian": 0.9, "african": 0.0}, [   # 088 T26: 시안 light 세트 비율
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
        (["mouth-upperlip-volume-incr"], 0.4), (["chin-width-incr"], 0.7), (["chin-height-decr"], 0.4), (["forehead-scale-vert-decr"], 0.4)]),   # 088 T30d: 눈꺼풀 내림 삭제
]
REGIONS = ("east_asia", "southeast_asia", "south_asia", "west_asia_north_africa", "west_central_africa", "europe", "latin_america")


def head_mask(R):
    """바탕 머리 델타를 거는 영역: 머리 + 목 절반 가중치, 목 아래로 부드럽게 0 (몸 비율·칼라 이음매는 그대로)."""
    hi = MS.OUR_NAMES.index("Head")
    ni = MS.OUR_NAMES.index("Neck")
    w = R.weights[:, hi] + 0.5 * R.weights[:, ni]
    return np.clip((w - 0.25) / 0.5, 0.0, 1.0)


def proto_head(R):
    """088 FR-51 (T26): 시안 체형 매크로 머리 모양 → (델타 (V,3), 균등 배율 s). 머리 영역만(목 쪽으로 서서히 0), 평균 이동을 빼고
    균등 배율(시안 키 매크로 0.6이라 머리가 ≈ 7.5 % 작다)을 나눠 기준 머리 크기에서 모양만 옮긴다 — 머리/키 비율(FR-12)은 그대로.
    R.verts에 이미 걸렸으면(body.reference) 같은 값을 돌려준다."""
    if getattr(R, "_proto_head", None) is not None:
        return R._proto_head
    # 영역 = 머리 뼈 가중치만 (목은 062 그대로) — head_mask(목 절반 포함)로 걸면 시안 체형이 목을 굵혀 GK 다이빙에서 목×위팔이 겹쳤다
    hw = R.weights[:, MS.OUR_NAMES.index("Head")]
    k = np.clip((hw - 0.35) / 0.5, 0.0, 1.0)
    core = k > 0.95
    body = np.array(R.groups["body"])
    hb = body[k[body] > 0.95]
    m = R.macro_delta(PROTO_MACRO)
    m = m - m[core].mean(axis=0)
    base = R.verts
    c = base[hb].mean(axis=0)
    e0 = base[hb].max(axis=0) - base[hb].min(axis=0)
    e1 = (base[hb] + m[hb]).max(axis=0) - (base[hb] + m[hb]).min(axis=0)
    s = float(np.mean(e1 / e0))
    shaped = c + (base + m - c) / s                    # 시안 모양, 기준 크기
    d = (shaped - base) * k[:, None]
    R._proto_head = (d, s)
    return R._proto_head


def base_head_deltas(R):
    """{Base_n: (V,3) 델타} — 인종 매크로 비율(기준 1/3씩에서 바꾼 만큼, 머리 영역만) + 얼굴 타깃 합.
    088 T26: 인종 델타는 시안 체형 매크로 위에서 계산하고 시안 머리와 같은 배율로 나눈다, 지역 얼굴 타깃은 FACE_TARGET_GAIN배."""
    k = head_mask(R)[:, None]
    core = k[:, 0] > 0.95
    _d, s = proto_head(R)
    p0 = R.macro_delta(PROTO_MACRO)
    out = {}
    for bid, _region, race, targets in BASE_HEADS:
        # 인종 매크로는 키·목 길이도 바꿔 머리 전체를 옮긴다 → 머리 평균 이동을 빼고(목 쪽으로 서서히 0) 형태 차이만 남긴다
        m = (R.macro_delta(dict(PROTO_MACRO, race=race)) - p0) / s
        m = (m - m[core].mean(axis=0)) * k
        d = m
        for names, w in targets:
            d = d + R.target_delta(names) * (w * FACE_TARGET_GAIN)
        for names, w in MALE_BASE + BASE_MASC.get(bid, []):   # 088 FR-59: 모든 바탕 머리에 같은 세기 (혼합 가중치 합 1이라 선수마다 그대로 1배) + 바탕별 추가
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


MOUTH_062 = -1.50   # 062 바탕 머리의 입선 높이 (눈 중심 아래, d 단위) — 아래 수염 경계 상수는 이 머리 기준으로 정했다
LIP_AX, LIP_AY = 0.47, 0.17   # 088 T30b: 입술 구멍 반지름 (d) — lips 그룹 실측(입꼬리 rx 0.47, 윗입술 −1.06 ~ 아랫입술 −1.40) [제안]


def mouth_line(R):
    """088 T30b: 입선 높이 ry (눈 중심 아래, d 단위) = MakeHuman lips 그룹 입꼬리(가로 끝 15 %) 평균.
    T26에서 시안 머리 모양을 넣어 입이 062 기준(−1.50)보다 올라갔다(−1.22) — 수염 마스크의 입 둘레가 따라오게 한다."""
    v = getattr(R, "_mouth_line", None)
    if v is None:
        c, d = eye_frame(R)
        P = R.verts[np.asarray(R.groups["lips"])]
        rx = np.abs(P[:, 0] - c[0]) / d
        ry = (P[:, 1] - c[1]) / d
        v = float(ry[rx > 0.85 * rx.max()].mean())
        R._mouth_line = v
    return v


def _lip_hole(R, rx, ry):
    """입술 타원 정규화 반지름² — 1 안쪽이 입술."""
    return (rx / LIP_AX) ** 2 + ((ry - mouth_line(R)) / LIP_AY) ** 2


MUSTACHE_TOP = 0.40   # 088 T30b: 콧수염 위 경계 = 입선 + 0.40 d (코 밑, 윗입술 −1.06보다 0.24 d 위) [제안]


def _beard_top(R, rx, sx, cheek_top):
    """수염 위 경계와 흐림 폭: 볼은 cheek_top − 0.30·sx(폭 0.35), 가운데는 입선 + MUSTACHE_TOP에서 옆으로 내려가며(폭 0.12) 볼 선과 만난다."""
    top = np.maximum(cheek_top - 0.30 * sx, mouth_line(R) + MUSTACHE_TOP - 0.6 * rx ** 2)
    w = 0.12 + 0.23 * np.clip(rx / 0.6, 0, 1)
    return top, w


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
    # 088 T30b: 가운데(콧수염 자리)는 코 밑까지 — 입을 따라 올라간 윗입술 위를 덮는다(062 고정 −0.95면 시안 머리에서 콧수염 자리가 빈다)
    top, w = _beard_top(R, rel_x, sx, -0.95)
    m = np.clip((top - rel_y) / w, 0, 1)
    # 아래 경계(목 선): 가운데 −2.55, 옆으로 갈수록 턱선을 따라 올라감 — 0.30 폭으로 흐림
    bottom = -2.55 + 0.75 * sx ** 1.3
    m = m * np.clip((rel_y - bottom) / 0.30, 0, 1)
    # 입술만 비운다 (부드러운 타원) — 콧수염·입꼬리·턱이 이어지게
    lip = _lip_hole(R, rel_x, rel_y)   # 088 T30b: 입 위치 실측(이전 062 고정 −1.50·반높이 0.10 → 시안 머리에서 윗입술이 덮였다)
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


def _soft_ellipse(rx, ry, cx, cy, ax, ay, w):
    """타원 안 1 → 밖 0, 경계는 정규화 반지름 1 ± w에서 부드럽게 (088 T28 수염 영역 — 직선 경계 없음)."""
    e = np.sqrt(((rx - cx) / ax) ** 2 + ((ry - cy) / ay) ** 2)
    return _ramp(1.0 + w - e, 2.0 * w)


def beard_masks(R, p):
    """062 FR-66 수염 영역 4개 {full, mustache, goatee, heavy} (0~1). full = 057 beard_mask 그대로(턱·볼·입 둘레),
    heavy = full을 볼 위로 조금 넓힌 진한 스텁블, mustache = 윗입술을 따라 휘는 띠(입꼬리에서 내려오며 가늘어짐), goatee = 턱 타원 + 콧수염 + 입꼬리 타원(088 T28: 직선 경계 사각형 → 곡선·타원).
    경계는 057처럼 넓게 흐린다. 콧수염·염소수염은 볼(입꼬리 바깥 0.6d 넘게)에 번지지 않는다(볼 밀도 ≤ 0.01)."""
    c, d = eye_frame(R)
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ry = (y - c[1]) / d
    rx = np.abs(x - c[0]) / d
    front = np.clip((z - (c[2] - 1.7 * d)) / (0.25 * d), 0, 1)
    full = beard_mask(R, p)
    # 진한 스텁블: 볼 위 경계를 0.15d 올리고 밀도를 고르게 (턱·볼 차이 작게)
    sx = np.clip(rx - 0.45, 0, 2)
    top, w = _beard_top(R, rx, sx, -0.80)
    heavy = np.clip((top - ry) / np.minimum(w, 0.30), 0, 1) * np.clip((ry - (-2.55 + 0.75 * sx ** 1.3)) / 0.30, 0, 1)
    lip = _lip_hole(R, rx, ry)
    dy = mouth_line(R) - MOUTH_062   # 088 T30b: 콧수염·염소수염은 입을 따라 옮긴다
    heavy = heavy * np.clip((lip - 1.0) / 0.6, 0, 1) * front * np.clip((1.30 - rx) / 0.25, 0, 1) * 0.9
    # 088 T28: 가로·세로 경사를 곱한 사각형(062)은 턱·입 둘레에 네모 블록으로 보였다(시안 비교) → 곡선 띠·타원 + 부드러운 합집합.
    # 콧수염: 윗입술 위를 따라 휘는 띠(가운데 ry −1.30, 입꼬리 쪽으로 내려와 −1.55), 가운데 두께 ±0.17d → 가로 0.62d에서 가늘어져 끝난다
    yc = -1.22 + dy - 0.45 * rx ** 2 - 0.25 * _ramp(rx - 0.35, 0.20)
    half = 0.24 * np.sqrt(np.clip(1.0 - (rx / 0.62) ** 2, 0, 1)) + 0.02
    must = _ramp(half - np.abs(ry - yc), 0.30)
    must = must * np.clip((lip - 1.0) / 0.8, 0, 1) * front
    # 염소수염: 턱 타원(가운데 ry −2.0, 반지름 0.44d × 0.42d) + 입꼬리 세로 타원(콧수염과 턱을 잇는다) + 콧수염, 경계는 반지름의 ±30 %로 흐림
    chin = _soft_ellipse(rx, ry, 0.0, -2.00 + dy, 0.44, 0.42, 0.40)
    side = _soft_ellipse(rx, ry, 0.45, -1.68 + dy, 0.12, 0.26, 0.45)
    goat = (1.0 - (1.0 - chin) * (1.0 - side) * (1.0 - must)) * np.clip((lip - 1.0) / 0.8, 0, 1) * front
    # 088 T30b: LOD0·클로즈업은 원본 머리를 이음 평면(body._head_seam)에서 감량 몸에 잇는다(T27). 평면 아래 감량 몸의 큰 삼각형 위로
    # 수염이 넘어가면 UV 보간이 거칠어 목 경계가 계단 블록으로 보였다 → 평면 위 BEARD_SEAM_CLEAR에서 0이 되게 흐린다
    from . import body as B
    pt, nrm = B._head_seam()
    above = (p - pt) @ nrm
    seam = _ramp(above - BEARD_SEAM_CLEAR, BEARD_SEAM_FADE)
    return {"full": full * seam, "mustache": must * seam, "goatee": goat * seam, "heavy": heavy * seam}


BEARD_SEAM_CLEAR = 0.004   # 088 T30b: 수염이 0이 되는 이음 평면 위 높이 (m) [제안]
BEARD_SEAM_FADE = 0.015    # 그 위로 1로 올라가는 폭 (m) [제안]


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
TEETH_REGION = (0.0, 0.5, 0.25, 0.75)  # 088 T30k: parts 치아 칸 — MakeHuman teeth_base 텍스처(R 밝기, G 잇몸, A 거칠기). 눈 칸 아래 빈 열
def eyes_covered(R, tree_tri, key_sets):
    """안구 정점이 피부 밖으로 나오는 수 (정면 광선으로 피부 앞에 있으면 노출). key_sets = [{키: 가중치}] 극값 조합."""
    from mathutils.bvhtree import BVHTree
    from mathutils import Vector
    tree, tri = tree_tri
    from . import mhassets as MH
    px = MH.Proxy(MH.EYES)                      # 088: 고해상도 눈 프록시 정점
    worst = 0
    cases = 0
    eye_z = [c[2] for c in eye_centers(R)]
    for ks in key_sets:
        V = R.verts.copy()
        for k, w in ks.items():
            V = V + R.extra_deltas[k] * w
        t2 = BVHTree.FromPolygons([tuple(p) for p in V], [tuple(t) for t in tri], all_triangles=True)
        bad = 0
        Pe = px.fit(V)
        T_, TU_ = px.triangles()
        ball = np.unique(T_[[not (px.uv[tu].mean(axis=0)[0] > 0.9 and px.uv[tu].mean(axis=0)[1] < 0.15) for tu in TU_]])
        for p in _shrunk_eyes(R, Pe)[ball]:                    # 088: 안구 층만 (각막 층 제외 — body._append_eyes와 같은 규칙)
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


# ---------------------------------------------------------------- 088 MakeHuman 눈썹·속눈썹 → 피부 UV 마스크 (FR-02)
# Mindfront 눈썹(가닥 형상 ≈ 3만 tris)·속눈썹(≈ 2.5만 tris)은 예산(클로즈업 머리 ≤ 6k)에 못 들어간다 → 프록시를 기준 몸에 맞춘 뒤
# 가닥 점을 가장 가까운 피부로 투영해 피부 UV 덮임 마스크로 굽는다(R·G·B = 눈썹 02·04·09, A = 속눈썹 선). 셰이더가 머리색으로 칠한다.

def _skin_tris(R):
    body = set(R.groups["body"])
    tris, tuv = [], []
    for f, fu in zip(R.faces, R.face_uvs):
        if not all(v in body for v in f):
            continue
        for k in range(1, len(f) - 1):
            tris.append((f[0], f[k], f[k + 1]))
            tuv.append((fu[0], fu[k], fu[k + 1]))
    return np.array(tris, dtype=np.int64), np.array(tuv, dtype=np.float64)


def _splat(R, points, size, tree, tris, tuv, max_d=0.012):
    from mathutils import Vector
    acc = np.zeros((size, size))
    for p in points:
        loc, _, idx, d = tree.find_nearest(Vector(tuple(p)), max_d)
        if loc is None:
            continue
        a, b, c = R.verts[tris[idx]]
        v0, v1, v2 = b - a, c - a, np.array(loc) - a
        d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
        d20, d21 = v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        if abs(den) < 1e-18:
            continue
        w1 = (d11 * d20 - d01 * d21) / den
        w2 = (d00 * d21 - d01 * d20) / den
        uv = (1 - w1 - w2) * tuv[idx, 0] + w1 * tuv[idx, 1] + w2 * tuv[idx, 2]
        x = int(min(max(uv[0] * size, 0), size - 1))
        y = int(min(max((1 - uv[1]) * size, 0), size - 1))
        acc[y, x] += 1.0
    return acc


def brow_masks(R, size):
    """→ (S,S,4) 0~1: R·G·B 눈썹 02·04·09 덮임, A 속눈썹 선. 2배 해상도로 찍고 흐린 뒤 줄인다(결정론)."""
    from mathutils.bvhtree import BVHTree
    from . import mhassets as MH
    from . import skin as SK
    tris, tuv = _skin_tris(R)
    tree = BVHTree.FromPolygons([tuple(p) for p in R.verts], [tuple(t) for t in tris], all_triangles=True)
    big = size * 2
    out = np.zeros((size, size, 4))
    for ch, rel in enumerate(MH.EYEBROWS + (MH.EYELASHES,)):
        px = MH.Proxy(rel)
        P = px.fit(R.verts)
        T, _ = px.triangles()
        pts = np.vstack([P, P[T].mean(axis=1)])
        acc = _splat(R, pts, big, tree, tris, tuv)
        acc = SK._box(acc, 1)
        nz = acc[acc > 0]
        k = float(np.percentile(nz, 40)) if len(nz) else 1.0      # 088 T23: 더 진하게 (40 백분위 기준 × 3)
        m = 1.0 - np.exp(-acc / max(k, 1e-6) * 3.0)
        out[..., ch] = m.reshape(size, 2, size, 2).mean(axis=(1, 3))
    return out


def brow_valid(R, size):
    from . import skin as SK
    return SK._raster(R, size)[1]


def mh_face_summary(R):
    """088 AC-01 요약: 피부 세트·눈썹·속눈썹·눈 출처, 지역 → 톤 단계 (PlayerLook 톤 표와 같은 값)."""
    from . import mhassets as MH
    return {"ok": True, "skinSets": list(MH.SKINS), "eyebrows": [os.path.basename(e).split(".")[0] for e in MH.EYEBROWS],
            "eyelashes": [os.path.basename(MH.EYELASHES).split(".")[0]], "eyes": "high-poly",
            "regionTones": {k: list(v) for k, v in REGION_TONES.items()}, "toneTable": TONE_TABLE}


# 088 Q4: 피부톤 8단계(PlayerLook.SkinTones 0 밝음 ~ 7 어두움) → (MakeHuman 세트, 틴트 RGB 배율). 지역 → 톤은 062 FR-63 표(Appearance.cs).
TONE_TABLE = [["light", [1.06, 1.05, 1.04]], ["light", [1.0, 1.0, 1.0]], ["light", [0.92, 0.88, 0.84]],
              ["asian", [1.03, 1.02, 1.0]], ["asian", [0.95, 0.92, 0.88]], ["asian", [0.80, 0.74, 0.68]],
              ["dark", [1.12, 1.10, 1.08]], ["dark", [0.92, 0.90, 0.88]]]
REGION_TONES = {"east_asia": (3, 4), "southeast_asia": (4, 5), "south_asia": (4, 5, 6), "west_asia_north_africa": (2, 3, 4),
                "west_central_africa": (6, 7), "europe": (0, 1, 2), "latin_america": (2, 4, 5)}


def _shrunk_eyes(R, P):
    """body._append_eyes와 같은 축소 (088 EYE_SHRINK)."""
    from . import body as B
    P = P.copy()
    for ctr in eye_centers(R):
        sel = np.sign(P[:, 0]) == np.sign(ctr[0])
        P[sel] = ctr + (P[sel] - ctr) * B.EYE_SHRINK
        P[sel, 2] -= B.EYE_BACK
    return P
