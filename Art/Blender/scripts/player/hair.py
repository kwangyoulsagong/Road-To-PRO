"""머리카락 35종 (specs/056 FR-40·41, specs/057 FR-10~14·30~33, specs/062 FR-40~45·50~54).
0~29는 057 순서·이름 그대로, 30~34는 062 신규(가르마 투블록·쉼표머리 펌·볼륨 곱슬 장발·직선 콘로우·지그재그 콘로우).

062 재작성 — 057의 "두피 법선으로 일정하게 민 셸"이 헬멧의 원인이었다. 스타일마다 매개변수 표(STYLE_SPEC)로:
  · 헤어라인: 바탕 4형(일자·M자·둥근·V자 이마) + 구레나룻·뒷목선 형 + 앞머리(이마로 내려오는 선) — 8개 제어각에서 선형 보간
  · 두께장: 옆 → 정수리 전이(높이 h0~h1), 앞머리·뒤통수 추가, 아래로 늘어지는 드레이프, 결 변조(곱슬·스파이크·줄·물결·가르마)
    + **계열 상한 강제**: 셸 정점의 정수리 위 높이 ≤ FR-40 상한 × 두개골 높이, 짧은 계열은 옆 폭도(귀 위 띠) — 헬멧 금지
  · 밀도장: 페이드·투블록·언더컷은 옆·뒤가 아래(피부 비침)에서 위로 단조 증가 (FR-42), 버즈는 전체가 약간 비친다
  · 컬러 포인트 마스크: 위(top)·띠(strip)·끝(tips) — 0 = 뿌리·옆 색, 1 = 위·끝 색 (FR-54, 셰이더 두 색 혼합은 T24)
  · 결 계열: 직모·곱슬·코일·브레이드·드레드 5종 — parts 아틀라스 머리카락 칸의 타일(hairtex.py)을 고른다 (FR-43)
정점 데이터: m.hairdata (V,2) = (밀도, 컬러 포인트) → glb 두 번째 UV(TEXCOORD_1, "HairData"). 셰이더 디더·두 색은 T24.

057에서 이어받은 것: 셸 위 가장자리 **가닥 카드**(LOD0만, UV u + 2), 묶음·포니테일·꼬리는 머리 뼈에 고정한 부속 형상(타원체·관),
가장자리 턱(셸 끝 → 두피 안쪽), 머리 뼈 가중치 1, 두피 묶음점의 얼굴·머리·HideHead 델타. 삭발(9)은 형상 없음.
"""
import math

import numpy as np

from . import mpfb_src as MS
from . import skeleton as S


def _sp(fam, group, strand, cap, line, **kw):
    d = dict(fam=fam, group=group, strand=strand, cap=cap, line=line,
             nape="square", burn="normal", fringe=None,
             T=8.0, S=4.0, h0=0.6, h1=1.3, Bk=0.0, F=0.0, Fw=0.6, D=0.0, Du=-0.3,
             mod=None, amp=0.0, part=None, dens=None, base=1.0, cp=None, taper=0.25,
             neck=False, card="short", attach=(), zig=False, shell=None)
    d.update(kw)
    return d


# 062 FR-51 분류표 (계열 group: bald·short·fade·medium·curly·braid·long), 057 family(분포 규칙 FR-52용 short·medium·long),
# 결 strand, FR-40 상한 등급 cap(short ≤ 0.06·medium ≤ 0.15·volume ≤ 0.35 — test_hair.py와 같은 표), 두께 mm, 높이 = 눈 기준 d 단위.
#   dens = (맨 아래 밀도, 시작 높이, 끝 높이, 모양) — 모양 "fade"(부드러운 단조) / "block"(투블록·언더컷 계단)
STYLE_SPEC = {
    "Hair_Buzz": _sp("short", "short", "straight", "short", "straight", T=2.8, S=2.6, base=0.8, card="tiny"),
    "Hair_Short": _sp("short", "short", "straight", "short", "round", nape="round", T=9.0, S=4.0, h0=0.9, h1=1.5),
    "Hair_Long": _sp("long", "long", "straight", "medium", "straight", nape="neck", burn="long", T=14.0, S=12.0, h0=0.0,
                     h1=1.2, D=24.0, Du=-0.2, taper=0.7, neck=True, card="long"),
    "Hair_Curly": _sp("medium", "curly", "curl", "medium", "round", nape="long", T=24.0, S=18.0, h0=0.2, h1=1.3,
                      mod="curl", amp=7.0, card="curly"),
    "Hair_Bun": _sp("long", "braid", "straight", "volume", "straight", nape="long", T=6.5, S=6.0, card="tight",
                    attach=(("knot", (0.0, 0.62, -0.79), (0.040, 0.034, 0.036)),)),
    "Hair_Fade": _sp("short", "fade", "straight", "short", "straight", nape="high", T=10.0, S=1.2, h0=0.7, h1=1.15,
                     dens=(0.03, -0.1, 0.95, "fade"), cp="top", card="tiny"),
    "Hair_Afro": _sp("medium", "curly", "coil", "volume", "round", nape="round", T=62.0, S=48.0, h0=-0.2, h1=1.4,
                     mod="curl", amp=8.0, card="curly"),
    "Hair_Mohawk": _sp("medium", "fade", "straight", "volume", "straight", nape="high", T=58.0, S=1.2, h0=0.4, h1=1.2,
                       mod="strip", dens=(0.04, -0.4, 0.4, "block"), cp="strip", card="tiny"),
    "Hair_Dreads": _sp("long", "braid", "dread", "medium", "M", nape="neck", burn="normal", T=20.0, S=18.0, h0=0.0,
                       D=14.0, Du=-0.2, mod="rope", amp=4.0, taper=0.7, neck=True, card="long"),
    "Hair_Bald": None,
    "Hair_Crew": _sp("short", "short", "straight", "short", "straight", nape="high", burn="short", T=11.0, S=3.2,
                     h0=1.0, h1=1.5, dens=(0.55, -0.4, 0.6, "fade")),
    "Hair_SidePart": _sp("short", "short", "straight", "short", "peak", nape="taper", burn="long", T=11.0, S=5.0, h0=0.9, h1=1.45,
                         F=6.0, part=0.45),
    "Hair_Undercut": _sp("short", "fade", "straight", "short", "straight", nape="high", T=11.5, S=1.2, h0=0.95, h1=1.1,
                         F=8.0, dens=(0.06, -0.4, 0.95, "block"), cp="top", card="tiny"),
    "Hair_SlickBack": _sp("medium", "medium", "straight", "medium", "M", nape="square", T=12.0, S=9.0, h0=0.4, h1=1.2,
                          Bk=10.0, card="tight"),
    "Hair_Quiff": _sp("short", "medium", "straight", "medium", "straight", nape="high", T=10.0, S=3.5, h0=0.9, h1=1.4,
                      F=42.0, Fw=0.55, card="fringe"),
    "Hair_Curtains": _sp("medium", "medium", "straight", "medium", "straight", nape="long", burn="long",
                         fringe=(0.95, 0.78, 0.45), T=14.0, S=13.0, h0=0.2, part=0.0, card="long"),
    "Hair_TexturedCrop": _sp("short", "short", "straight", "short", "straight", nape="high", fringe=(0.72, 0.74, 0.82),
                             T=9.0, S=2.5, h0=0.9, h1=1.4, mod="crop", amp=2.0, dens=(0.3, -0.4, 0.8, "fade"), card="fringe"),
    "Hair_BuzzLine": _sp("short", "short", "straight", "short", "straight", T=2.8, S=2.6, base=0.8, mod="line", card="tiny"),
    "Hair_HighTop": _sp("medium", "curly", "coil", "volume", "straight", nape="high", T=72.0, S=1.5, h0=0.85, h1=1.05,
                        mod="flat", dens=(0.1, -0.4, 0.85, "fade"), card="tiny"),
    "Hair_Cornrows": _sp("short", "braid", "braid", "short", "round", nape="taper", burn="short", T=5.0, S=4.5, mod="rows",
                         amp=2.5, card="tiny"),
    "Hair_Twists": _sp("medium", "curly", "dread", "medium", "round", nape="round", T=17.0, S=14.0, h0=0.3,
                       mod="rope", amp=4.0, card="curly"),
    "Hair_TopKnot": _sp("long", "braid", "straight", "volume", "straight", nape="high", T=7.0, S=1.2, h0=0.95, h1=1.1,
                        dens=(0.06, -0.4, 0.95, "block"), card="tiny",
                        attach=(("knot", (0.0, 0.85, -0.53), (0.034, 0.030, 0.034)),)),
    "Hair_Ponytail": _sp("long", "braid", "straight", "medium", "straight", nape="long", T=7.0, S=6.0, card="tight",
                         attach=(("knot", (0.0, 0.25, -0.97), (0.020, 0.018, 0.016)),
                                 ("tail", (0.0, 0.25, -0.97), (0.095, 0.022, 0.009, (0.0, -0.88, -0.47))))),
    "Hair_MediumWave": _sp("medium", "medium", "curl", "medium", "round", nape="long", burn="long", T=17.0, S=16.0,
                           h0=0.1, mod="wave", amp=5.0, card="long"),
    "Hair_FrenchCrop": _sp("short", "short", "straight", "short", "straight", nape="high", fringe=(0.88, 0.88, 0.9),
                           T=7.0, S=1.5, h0=0.9, h1=1.3, dens=(0.05, -0.4, 0.75, "fade"), card="fringe"),
    "Hair_IvyLeague": _sp("short", "short", "straight", "short", "M", nape="taper", T=7.0, S=3.0, h0=1.0, h1=1.5,
                          part=0.45, dens=(0.6, -0.4, 0.6, "fade")),
    "Hair_Spiky": _sp("short", "short", "straight", "short", "M", burn="short", T=7.0, S=3.5, h0=1.0, h1=1.5, mod="spike",
                      amp=5.0, card="tiny"),
    "Hair_CurlyTop": _sp("short", "curly", "coil", "medium", "round", nape="high", T=24.0, S=1.2, h0=0.6, h1=1.15,
                         mod="curl", amp=6.0, dens=(0.04, -0.1, 0.9, "fade"), cp="top", card="tiny"),
    "Hair_BoxBraids": _sp("long", "braid", "braid", "medium", "round", nape="neck", burn="long", T=16.0, S=15.0, h0=0.0,
                          D=20.0, Du=-0.2, mod="rows", amp=3.0, cp="tips", taper=0.7, neck=True, card="long"),
    "Hair_HalfUp": _sp("long", "long", "straight", "medium", "M", nape="long", burn="normal", T=12.0, S=12.0,
                       h0=0.0, D=10.0, Du=-0.2, taper=0.7, card="long",
                       attach=(("knot", (0.0, 0.55, -0.83), (0.026, 0.024, 0.024)),)),
    # ---- 062 신규 5종 (FR-52)
    "Hair_PartedTwoBlock": _sp("short", "fade", "straight", "short", "round", nape="high", fringe=(1.0, 1.0, 0.95),
                               T=11.5, S=1.2, h0=0.55, h1=0.7, part=0.4, dens=(0.06, -0.4, 0.55, "block"), cp="top"),
    "Hair_CommaPerm": _sp("medium", "medium", "curl", "medium", "round", nape="round", fringe=(0.55, 0.68, 0.95),
                          T=16.0, S=8.0, h0=0.5, h1=1.2, F=4.0, mod="comma", amp=12.0, card="fringe"),
    "Hair_VolumeCurlyLong": _sp("long", "long", "curl", "volume", "round", nape="neck", burn="long", T=40.0, S=38.0,
                                h0=0.0, Bk=-22.0, D=22.0, Du=0.0, mod="curl", amp=8.0, taper=0.8, neck=True, card="curly"),  # 뒷목 아래는 얇게 (고개 젖힘 관통)
    "Hair_StraightCornrows": _sp("short", "braid", "braid", "short", "straight", T=3.6, S=3.4, mod="rows", amp=1.4,
                                 card="tiny", attach=tuple(
                                     ("tail", (x, -0.05, -1.0), (0.085, 0.0075, 0.004, (0.12 * x, -0.9, -0.42)), (3, 4))
                                     for x in (-0.45, -0.27, -0.09, 0.09, 0.27, 0.45))),
    "Hair_ZigzagCornrows": _sp("short", "braid", "braid", "short", "peak", nape="high", T=3.6, S=1.2, h0=0.55, h1=0.8,
                               mod="rows", amp=1.4, dens=(0.08, -0.4, 0.6, "fade"), zig=True, card="tiny"),
}
STYLES = tuple(STYLE_SPEC)
STYLE_TABLE = tuple((n, STYLE_SPEC[n]["fam"] if STYLE_SPEC[n] else "short") for n in STYLES)  # 057 호환 (이름, 분포 계열)
FAMILY = dict(STYLE_TABLE)
GROUP = {n: (STYLE_SPEC[n]["group"] if STYLE_SPEC[n] else "bald") for n in STYLES}
STRAND = {n: (STYLE_SPEC[n]["strand"] if STYLE_SPEC[n] else None) for n in STYLES}
MESH_STYLES = tuple(s for s in STYLES if STYLE_SPEC[s] is not None)
COLOR_POINT = tuple(s for s in MESH_STYLES if STYLE_SPEC[s]["cp"])
FADE = ("Hair_Fade", "Hair_Undercut", "Hair_PartedTwoBlock", "Hair_Mohawk", "Hair_CurlyTop")  # FR-42 판정 대상 (옆·뒤 그라데이션)
# FR-44: 무늬만 다른 변형 (실루엣 쌍 판정 제외, 2026-11-21 사용자 결정)
VARIANT_PAIRS = {("Hair_Buzz", "Hair_BuzzLine")}
CAP = {"short": 0.06, "medium": 0.15, "volume": 0.35}   # FR-40 볼륨 비율 상한
SIDE_CAP = 0.05                                          # FR-40 짧은 계열 옆 폭
MIN_T = 0.0016                                           # 셸 최소 두께 (m) — 헤어라인 가장자리 포함
CAP_MARGIN = 0.0025                                      # 상한 강제 여유 (m) — 감량·LOD0 두개골 차이 몫

HAIR_TRIS = {0: 800, 1: 230, 2: 70}  # 셸 감량 목표 (헤어 LOD0 합계 ≤ 1,350)
HAIRLINE_SMOOTH = 12  # 헤어라인 고리 평활 반복
REGION = (0.25, 0.0, 1.0, 0.75)   # parts 아틀라스 머리카락 칸 (u0, v0, u1, v1)
CARD_REGION = (0.25, 0.75, 1.0, 1.0)  # parts 아틀라스 가닥 카드 칸 — 카드 UV는 u + 2 (셰이더가 알파 클립 대상으로 구분, 057 FR-10·12)
CARD_MAX = 400       # LOD0 카드 삼각형 상한 (057 plan T01)
HAIR_LOD0_MAX = 1320  # 헤어 LOD0 합계 목표 (checks 상한 1,350)
CARD_TILE = 0.04     # 카드 텍스처 가로 한 칸 = 4 cm

# 062 FR-43: 결 계열 타일 — REGION 안 2열 × 3행 (6번째 칸은 예비, 직모 복사). hairtex.py와 같은 표.
STRANDS = ("straight", "curl", "coil", "braid", "dread")
TILE_COLS, TILE_ROWS = 2, 3
TILE_PAD = 4 / 1024  # 타일 테두리 여유 (밉맵 번짐)


def tile_rect(strand):
    """결 계열 → parts 아틀라스 안 (u0, v0, u1, v1)."""
    i = STRANDS.index(strand)
    c, r = i % TILE_COLS, i // TILE_COLS
    u0, v0, u1, v1 = REGION
    w, h = (u1 - u0) / TILE_COLS, (v1 - v0) / TILE_ROWS
    return (u0 + c * w + TILE_PAD, v1 - (r + 1) * h + TILE_PAD, u0 + (c + 1) * w - TILE_PAD, v1 - r * h - TILE_PAD)


HEAD_AXIS_Z = [0.0]  # 머리 수직축 z (머리 정점 앞뒤 가운데) — build에서 정한다


def layout_info(style):
    """player.layout.json hairStyles[] 062 필드: FR-51 계열, 결, 헤어라인 형, 컬러 포인트 마스크 종류, 페이드 여부."""
    sp = STYLE_SPEC[style]
    if sp is None:
        return {"group": "bald", "strand": None, "hairline": None, "colorPoint": None, "fade": False}
    return {"group": sp["group"], "strand": sp["strand"], "hairline": "fringe" if sp["fringe"] else sp["line"],
            "nape": sp["nape"], "colorPoint": sp["cp"], "fade": style in FADE}


def _phi(p, c):
    """머리 수직축 둘레 각: 0 = 정면, π = 뒤 (좌우 대칭이라 절댓값). 축은 눈 중심이 아니라 머리 앞뒤 가운데."""
    return abs(math.atan2(p[0] - c[0], p[2] - HEAD_AXIS_Z[0]))


def _ss(x, a, b):
    """smoothstep (a → b), a == b면 계단."""
    if b <= a:
        return 1.0 if x >= a else 0.0
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


# FR-41: 헤어라인 제어각 (0 정면 → π 뒤) — 이마 가운데·이마 옆·관자 모서리·구레나룻·귀 위·귀 뒤·뒷목 옆·뒷목 가운데
LINE_PHI = (0.0, 0.25, 0.55, 1.05, 1.35, 1.9, 2.6, math.pi)
LINE_FRONT = {  # 바탕 4형 (이마 가운데, 이마 옆, 관자 모서리)
    "straight": (1.15, 1.15, 1.05),
    "M": (1.20, 1.30, 1.50),      # 관자가 들어간 M자
    "round": (1.22, 1.14, 0.90),  # 모서리가 둥글게 내려온다
    "peak": (1.02, 1.24, 1.10),   # 가운데가 내려온 V자 이마
}
LINE_BURN = {"normal": (0.15, -0.35), "short": (0.45, 0.0), "long": (-0.25, -0.80)}   # 구레나룻, 귀 위
LINE_NAPE = {  # 귀 뒤, 뒷목 옆, 뒷목 가운데
    "square": (-0.55, -1.15, -1.20), "round": (-0.50, -0.95, -1.05), "taper": (-0.55, -1.05, -1.50),
    "high": (-0.35, -0.60, -0.70), "long": (-1.00, -1.55, -1.75),
    "neck": (-1.35, -1.60, -1.62),   # 장발: 목 뒤·옆까지 (−1.65 d 아래는 목 아래·윗가슴 피부와 관통 — T22 진단)
}


def hairline(style, phi, d=None):
    """헤어라인 높이 (눈 중심 기준, d 단위). phi 0 정면 → π 뒤."""
    sp = STYLE_SPEC[style]
    front = sp["fringe"] or LINE_FRONT[sp["line"]]
    pts = tuple(front) + LINE_BURN[sp["burn"]] + LINE_NAPE[sp["nape"]]
    for k in range(len(LINE_PHI) - 1):
        a0, a1 = LINE_PHI[k], LINE_PHI[k + 1]
        if phi <= a1:
            t = (phi - a0) / (a1 - a0)
            return pts[k] + (pts[k + 1] - pts[k]) * t
    return pts[-1]


def _geo(p, c, d):
    phi = _phi(p, c)
    up = (p[1] - c[1]) / d
    px = (p[0] - c[0]) / d
    return phi, up, px, abs(px)


def thickness(style, p, c, d, top, n=None, split=False):
    """두께 (m). p 두피 위치, c 눈 중심, top 정수리 y, n 두피 법선 (평평한 윗면 변조용).
    split=True → (바탕, 결 변조) — 바탕만 평활하고 변조는 평활 뒤에 더한다(곱슬·로프·줄이 평활에 지워지지 않게)."""
    sp = STYLE_SPEC[style]
    phi, up, px, side = _geo(p, c, d)
    wt = _ss(up, sp["h0"], sp["h1"])
    t = sp["S"] + (sp["T"] - sp["S"]) * wt
    t += sp["Bk"] * _ss(phi, 1.9, 2.7) * (1 - wt)
    if sp["F"]:
        t += sp["F"] * math.exp(-(phi / sp["Fw"]) ** 2) * _ss(up, 0.8, 1.3)
    if sp["D"]:
        t += sp["D"] * min(max((sp["Du"] - up) / 1.6, 0.0), 1.0) * _ss(phi, 0.9, 1.4)
    mod, a = sp["mod"], sp["amp"]
    t0 = t
    if mod == "curl":
        t += a * math.sin(p[0] * 210) * math.sin(p[1] * 190) * math.sin(p[2] * 230)
    elif mod == "rope":
        t += a * math.sin(p[0] * 160 + p[2] * 140)
    elif mod == "wave":
        t += a * math.sin(p[1] * 120 + p[0] * 60)
    elif mod == "rows":
        t += a * abs(math.sin(p[0] * 95))
    elif mod == "crop":
        t += a * math.sin(p[0] * 230) * math.sin(p[2] * 210) * wt
    elif mod == "spike":
        t += a * max(0.0, math.sin(p[0] * 170) * math.sin(p[2] * 170)) ** 2 * wt * 2.0
    elif mod == "strip":  # 모히칸: 가운데 띠만 높다
        strip = min(max((0.40 - side) / 0.14, 0.0), 1.0)
        t = sp["S"] + (sp["T"] - sp["S"]) * strip * _ss(up, sp["h0"], sp["h1"])
    elif mod == "flat":  # 하이톱: 윗면이 평평한 상자 (윗면 높이 = 정수리 + T)
        box = min(max((0.80 - side) / 0.12, 0.0), 1.0) * wt
        ny = float(n[1]) if n is not None else 1.0
        flat = min(max(0.0, (top + sp["T"] / 1000.0 - p[1]) / max(ny, 0.5)) * 1000.0, sp["T"])
        t = sp["S"] + (flat - sp["S"]) * box * _ss(ny, 0.25, 0.7)  # 옆을 향한 면은 밀지 않는다 (상자 옆면이 수직)
    elif mod == "comma":  # 쉼표머리: 이마 한쪽으로 말려 내려오는 컬 + 잔 곱슬
        t += 3.0 * math.sin(p[0] * 210) * math.sin(p[1] * 190) * math.sin(p[2] * 230)
        t += a * math.exp(-((px - 0.35) / 0.35) ** 2) * math.exp(-((up - 0.85) / 0.25) ** 2) * (phi < 1.0)
    elif mod == "line":  # 버즈+라인: 옆머리에 면도 선
        if side > 0.55 and abs(up - (0.35 + 0.15 * math.cos(phi * 2.0))) < 0.05:
            t = 0.8
    if sp["part"] is not None and up > 0.8 and abs(px - sp["part"]) < 0.06:
        t *= 0.45  # 가르마 홈
    if split and mod in ("curl", "rope", "wave", "rows", "crop", "spike", "comma"):
        return t0 / 1000.0, (t - t0) / 1000.0
    if split:
        return max(t, 0.6) / 1000.0, 0.0
    return max(t, 0.6) / 1000.0


def density(style, p, c, d):
    """밀도 (0 피부 비침 → 1 꽉 참). FR-42: 페이드 계열은 옆·뒤 아래 → 위 단조 증가."""
    sp = STYLE_SPEC[style]
    phi, up, px, side = _geo(p, c, d)
    v = sp["base"]
    if sp["mod"] == "line" and side > 0.55 and abs(up - (0.35 + 0.15 * math.cos(phi * 2.0))) < 0.05:
        v = 0.15
    if sp["dens"]:
        lo, a, b, kind = sp["dens"]
        if sp["mod"] == "strip":  # 모히칸: 띠 밖은 전부 밀었다
            s = min(max((0.40 - side) / 0.10, 0.0), 1.0) * _ss(up, sp["h0"], sp["h0"] + 0.25)
            return lo + (1 - lo) * s
        if kind == "block":
            s = _ss(up, b - 0.06, b + 0.04)
        else:
            s = _ss(up, a, b) if b > a else 1.0
        s = s + (1 - s) * (1 - _ss(phi, 0.55, 0.95))  # 페이드는 옆·뒤만 — 이마 앞머리는 꽉 찬다
        v = lo + (v - lo) * s
    return v


def color_point(style, p, c, d):
    """FR-54 두 색 마스크: 0 = 뿌리·옆(자연색), 1 = 위·끝(탈색·염색). 대상 아닌 스타일은 0."""
    sp = STYLE_SPEC[style]
    phi, up, px, side = _geo(p, c, d)
    cp = sp["cp"]
    if cp == "top":
        lo = sp["dens"][2] if sp["dens"] else sp["h1"]
        return _ss(up, lo + 0.02, lo + 0.10)
    if cp == "strip":
        return _ss(0.36 - side, 0.0, 0.04) * _ss(up, sp["h0"] + 0.2, sp["h0"] + 0.3)
    if cp == "tips":   # 브레이드 끝 쪽 탈색
        return 1.0 - _ss(up, -0.55, -0.45)
    return 0.0


def _caps(style, R, c, d):
    """FR-40 상한 강제용 두개골 기준 (visual_metrics.hair_metrics와 같은 정의: 머리 뼈 주 가중치 정점)."""
    hi = MS.OUR_NAMES.index("Head")
    idx = [v for v in R.groups["body"] if int(np.argmax(R.weights[v])) == hi]
    sk = R.verts[idx]
    top, chin = float(sk[:, 1].max()), float(sk[:, 1].min())
    cx = 0.5 * float(sk[:, 0].max() + sk[:, 0].min())
    ear = chin + 0.6 * (top - chin)
    half = float(np.abs(sk[sk[:, 1] >= ear, 0] - cx).max())
    sp = STYLE_SPEC[style]
    ymax = top + CAP[sp["cap"]] * (top - chin) - CAP_MARGIN
    xmax = half + SIDE_CAP * 2 * half - CAP_MARGIN if sp["cap"] == "short" else None
    return ymax, xmax, ear, cx


def _scalp_ok(style, R, v, p, c, d, headw, neckw, N):
    sp = STYLE_SPEC[style]
    up = (p[1] - c[1]) / d
    if up <= hairline(style, _phi(p, c)) or N[v][1] <= (-0.9 if sp["neck"] else -0.35):  # 장발은 후두부 아랫면도
        return False
    if headw[v] >= 0.5:
        return True
    # 062 장발: 머리 아래(목 뒤·옆)까지 내려온다 — 머리 + 목 가중치, 목 앞(턱 아래)은 제외
    return sp["neck"] and headw[v] + neckw[v] >= 0.5 and _phi(p, c) > 1.0


def build(R, style, lod):
    """→ body.Mesh (머리카락 조각). 두피를 띄운 셸 + 가장자리 턱 + 부속 형상 (+ LOD0 카드). m.hairdata = (밀도, 컬러 포인트)."""
    from . import body as B
    from . import face as F
    sp = STYLE_SPEC[style]
    c, d = F.eye_frame(R)
    hi = MS.OUR_NAMES.index("Head")
    ni = MS.OUR_NAMES.index("Neck")
    headw, neckw = R.weights[:, hi], R.weights[:, ni]
    ears = set(R.groups.get("ears", []))
    bodyset = set(R.groups["body"])
    top = float(R.verts[R.groups["body"], 1].max())
    N = R.normals
    hz = R.verts[[v for v in R.groups["body"] if headw[v] >= 0.5], 2]
    HEAD_AXIS_Z[0] = float((hz.min() + hz.max()) / 2)
    scalp = np.zeros(R.n, dtype=bool)
    for v in R.groups["body"]:
        if v not in ears and _scalp_ok(style, R, v, R.verts[v], c, d, headw, neckw, N):
            scalp[v] = True
    # 구멍 메우기: 이웃 대부분이 두피인 정점(헤어라인 안쪽 섬·홈)은 두피로
    rnb = _ref_neighbors(R)
    for _ in range(4):  # 헤어라인 안쪽으로 파인 홈(노치)도 — 턱 면이 둘러싸 구멍처럼 보인다
        add = [v for v in R.groups["body"] if not scalp[v] and v not in ears and rnb[v]
               and sum(scalp[u] for u in rnb[v]) >= 0.6 * len(rnb[v])]
        for v in add:
            scalp[v] = True
    faces = [f for f in R.faces if all(scalp[v] for v in f) and all(v in bodyset for v in f)]
    ids = sorted({v for f in faces for v in f})
    remap = {v: i for i, v in enumerate(ids)}
    fl = [tuple(remap[v] for v in f) for f in faces]
    base = R.verts[ids]
    nb = B._neighbors(len(ids), fl)
    edge_cnt = {}
    for f in fl:
        for k in range(len(f)):
            e = (min(f[k], f[(k + 1) % len(f)]), max(f[k], f[(k + 1) % len(f)]))
            edge_cnt[e] = edge_cnt.get(e, 0) + 1
    boundary = sorted({v for e, n in edge_cnt.items() if n == 1 for v in e})
    ring = np.full(len(ids), 99)
    frontier = list(boundary)
    for v in frontier:
        ring[v] = 0
    k = 0
    while frontier:
        k += 1
        nxt = []
        for v in frontier:
            for u in nb[v]:
                if ring[u] > k:
                    ring[u] = k
                    nxt.append(u)
        frontier = nxt
    # 헤어라인 계단 펴기: 가장자리 고리 정점을 고리 이웃 평균으로 옮긴 뒤 머리 표면에 다시 붙인다
    bset = set(boundary)
    bnb = {v: [u for u in nb[v] if u in bset and (min(u, v), max(u, v)) in edge_cnt and edge_cnt[(min(u, v), max(u, v))] == 1]
           for v in boundary}
    base = base.copy()
    head_tree = B._subset_tree(R, [f for f in R.faces if all((headw[v] >= 0.3 or (sp["neck"] and headw[v] + neckw[v] >= 0.3))
                                                             and v in bodyset for v in f)])
    for _ in range(HAIRLINE_SMOOTH):
        nxt = base.copy()
        for v in boundary:
            if len(bnb[v]) == 2:
                nxt[v] = 0.5 * base[v] + 0.25 * (base[bnb[v][0]] + base[bnb[v][1]])
        for v in boundary:
            loc = head_tree[0].find_nearest(tuple(nxt[v]))[0]
            if loc is not None:
                nxt[v] = np.array(loc)
        base = nxt
    taper = np.clip(ring / 4.0, sp["taper"], 1.0)
    Nn = N[ids].copy()
    Nn[:, 1] = np.maximum(Nn[:, 1], -0.15)  # 아래를 향한 두피(후두부 아랫면)도 아래가 아니라 바깥으로 민다 — 접힘·판 방지
    Nn /= np.linalg.norm(Nn, axis=1, keepdims=True)
    TM = np.array([thickness(style, base[i], c, d, top, Nn[i], split=True) for i in range(len(ids))])
    T = TM[:, 0] * taper
    # 띄움 벡터 평활: 두꺼운 머리가 오목한 곳(귀 뒤·턱 옆)에서 접혀 판처럼 튀어나오지 않게 (바탕 두께 중앙값에 비례한 횟수,
    # 모히칸 띠·하이톱 상자처럼 형태 자체가 변조인 스타일은 중앙값이 작아 거의 안 건다)
    disp = Nn * T[:, None]
    for _ in range(int(np.clip(np.median(T) * 1000.0 / 5.0, 0, 8))):
        avg = np.array([disp[nb[i]].mean(axis=0) if nb[i] else disp[i] for i in range(len(ids))])
        disp = 0.5 * disp + 0.5 * avg
    disp += Nn * (TM[:, 1] * taper)[:, None]   # 결 변조 (곱슬·로프·줄·물결·스파이크)
    tn = np.einsum("ij,ij->i", disp, Nn)
    disp += Nn * np.maximum(MIN_T - tn, 0.0)[:, None]  # 감량된 몸 LOD0 피부 꼭짓점이 얇은 가장자리를 뚫지 않게
    # FR-40 상한 강제 (헬멧 금지): 정수리 위 높이, 짧은 계열은 귀 위 옆 폭 — 평활 뒤에 건다
    ymax, xmax, ear, cx = _caps(style, R, c, d)
    for i in range(len(ids)):
        k = 1.0
        if disp[i][1] > 1e-6 and base[i][1] + disp[i][1] > ymax:
            k = min(k, max(ymax - base[i][1], 0.0) / disp[i][1])
        if xmax is not None and base[i][1] + disp[i][1] >= ear - 0.004:
            x0 = abs(base[i][0] - cx)
            dx = disp[i][0] * np.sign(base[i][0] - cx)
            if dx > 1e-6 and x0 + dx > xmax:
                k = min(k, max(xmax - x0, 0.0) / dx)
        disp[i] *= k
        if np.linalg.norm(disp[i]) < 0.0006:
            disp[i] = Nn[i] * 0.0006
    shell = base + disp
    target = HAIR_TRIS[lod] if sp["shell"] is None else sp["shell"][lod]
    co, f2 = B._decimate_faces(shell, fl, min(target, sum(len(f) - 2 for f in fl)))
    fc = np.array([base[list(f)].mean(axis=0) for f in fl])        # 몸 LOD0 정점은 기준 표면 면 위에도 놓인다 → 면 중심도 검사
    fn = np.array([Nn[list(f)].mean(axis=0) for f in fl])
    fn /= np.linalg.norm(fn, axis=1, keepdims=True)
    co = _clear_skin(co, f2, np.vstack([base, fc]), np.vstack([Nn, fn]))
    tree = B._subset_tree(R, faces)
    anchor_tri = np.zeros((len(co), 3), dtype=np.int64)
    anchor_bc = np.zeros((len(co), 3))
    off = np.zeros((len(co), 3))
    for i, p in enumerate(co):
        (t, bc), _ = B._bind_point(R, tree, p)
        anchor_tri[i], anchor_bc[i] = t, bc
        a = (R.verts[t] * bc[:, None]).sum(axis=0)
        off[i] = p - a
    bind = B.Binding.__new__(B.Binding)
    bind.tri, bind.bc = anchor_tri, anchor_bc
    P = bind.apply(R.verts) + off
    W = np.zeros((len(co), len(MS.OUR_NAMES)))
    W[:, hi] = 1.0
    alld = B._all_deltas(R)
    D = {k2: bind.apply(v) for k2, v in sorted(alld.items()) if lod == 0 or not k2.startswith(B.EXPR_PREFIX)}  # 062 FR-71
    m = B.Mesh()
    m.verts, m.W, m.deltas, m.vpart = P, W, D, ["hair"] * len(P)
    m.style = style
    m.hairdata = np.array([_hd(style, P[i] - off[i], c, d) for i in range(len(P))])
    for f in f2:
        m.faces.append((tuple(f), B.BODY, tuple(hair_uv(P[i], c, d, top, style) for i in f), "hair", None))
    _rim(m, f2, off, c, d, top)
    for spec in sp["attach"]:
        _attach(m, spec, lod, c, d, top, head_tree, tree, R)
    if lod == 0:  # 카드는 셸·턱·부속 형상 뒤에 남은 몫만큼 (헤어 LOD0 합계 ≤ HAIR_LOD0_MAX)
        noear = B._subset_tree(R, [f for f in R.faces if all(headw[v] >= 0.3 and v in bodyset and v not in ears for v in f)])
        _cards(m, f2, off, style, c, d, top, noear, tree, R, min(CARD_MAX, HAIR_LOD0_MAX - m.triangles))
    return m


def _clear_skin(co, faces, skin, nrm, gap=0.0012, iters=3):
    """감량된 셸 면이 휜 곳(관자·귀 위)에서 현만큼 안쪽으로 파고들어 피부 정점이 뚫고 나오지 않게, 피부 정점이 셸 아래 gap 이상
    들어가도록 가까운 셸 면의 정점을 바깥으로 민다."""
    from mathutils.bvhtree import BVHTree
    co = co.copy()
    tris = [tuple(f) for f in faces]
    for _ in range(iters):
        tree = BVHTree.FromPolygons([tuple(map(float, p)) for p in co], tris)
        push = np.zeros(len(co))
        for p, n in zip(skin, nrm):
            hit = tree.ray_cast(tuple(map(float, p - n * 0.05)), tuple(map(float, n)), 0.2)
            if hit[0] is None:
                continue
            depth = float(np.dot(np.array(hit[0]) - p, n))   # 피부 → 셸 (법선 방향, 양수 = 셸이 바깥)
            if depth < gap:
                for v in tris[hit[2]]:
                    push[v] = max(push[v], gap - depth)
        if not push.any():
            break
        vn = np.zeros_like(co)
        for f in tris:
            fn = np.cross(co[f[1]] - co[f[0]], co[f[2]] - co[f[0]]) if len(f) == 3 else np.cross(co[f[2]] - co[f[0]], co[f[3]] - co[f[1]])
            for v in f:
                vn[v] += fn
        vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)
        out = co - skin.mean(axis=0)
        vn *= np.where(np.einsum("ij,ij->i", vn, out) < 0, -1.0, 1.0)[:, None]  # 감기와 상관없이 바깥
        co += vn * push[:, None] * 1.2
    return co


_RNB = {}


def _ref_neighbors(R):
    if id(R) not in _RNB:
        from . import body as B
        _RNB.clear()
        _RNB[id(R)] = B._neighbors(R.n, R.faces)
    return _RNB[id(R)]


def _hd(style, p, c, d):
    return (density(style, p, c, d), color_point(style, p, c, d))


def _append(m, P, faces_uv, part, R, tree, hd=None, vpart=None):
    """새 정점 P (k,3)를 두피 최근접 + 오프셋으로 묶어 m에 붙인다(머리 모양 델타를 따라간다). faces_uv: [(정점 지역 인덱스, UV)].
    hd: (k,2) 밀도·컬러 포인트 (없으면 위치로 계산)."""
    from . import body as B
    from . import face as F
    hi = MS.OUR_NAMES.index("Head")
    base = len(m.verts)
    tri = np.zeros((len(P), 3), dtype=np.int64)
    bc = np.zeros((len(P), 3))
    off = np.zeros((len(P), 3))
    for i, p in enumerate(P):
        (t3, w3), _ = B._bind_point(R, tree, p)
        tri[i], bc[i] = t3, w3
        off[i] = p - (R.verts[t3] * w3[:, None]).sum(axis=0)
    bind = B.Binding.__new__(B.Binding)
    bind.tri, bind.bc = tri, bc
    Pn = bind.apply(R.verts) + off
    W = np.zeros((len(P), len(MS.OUR_NAMES)))
    W[:, hi] = 1.0
    alld = B._all_deltas(R)
    if hd is None:
        c, d = F.eye_frame(R)
        hd = np.array([_hd(m.style, Pn[i] - off[i], c, d) for i in range(len(Pn))])
    m.verts = np.vstack([m.verts, Pn])
    m.W = np.vstack([m.W, W])
    m.hairdata = np.vstack([m.hairdata, np.asarray(hd, dtype=float).reshape(-1, 2)])
    for k in m.deltas:
        m.deltas[k] = np.vstack([m.deltas[k], bind.apply(alld[k])])
    m.vpart += [vpart or part] * len(P)
    for f, uv in faces_uv:
        m.faces.append((tuple(base + i for i in f), B.BODY, tuple(uv), part, None))


# 가닥 카드 길이 (앞·옆·뒤, m). 057 FR-10: 삭발 제외 전부 카드가 있다.
CARD_LEN = {
    "tiny": (0.005, 0.005, 0.005), "short": (0.008, 0.009, 0.008), "fringe": (0.014, 0.009, 0.008),
    "long": (0.030, 0.024, 0.016), "curly": (0.012, 0.012, 0.010), "tight": (0.008, 0.007, 0.006),
}
CARD_KIND = {n: STYLE_SPEC[n]["card"] for n in MESH_STYLES}


def _cards(m, faces, off, style, c, d, top, head_tree, tree, R, budget=CARD_MAX):
    """057 FR-10~13: 셸 가장자리 변마다 가닥 카드(두피 접선 방향으로 헤어라인 밖까지, 끝은 피부 위 2 mm). LOD0만.
    카드마다 정점을 따로 둔다(UV 칸 안에서 반복). 삼각형 ≤ CARD_MAX. 카드 밀도 = 뿌리 셸 정점 밀도(페이드 아래 카드는 옅다)."""
    P = m.verts
    edge, opp = {}, {}
    for f in faces:
        k = len(f)
        for i in range(k):
            a, b = f[i], f[(i + 1) % k]
            key = (min(a, b), max(a, b))
            edge.setdefault(key, []).append((a, b))
            opp[key] = P[list(f)].mean(axis=0)
    bnd = sorted(key for key, lst in edge.items() if len(lst) == 1)
    if STYLE_SPEC[style]["neck"]:  # 장발 아래 끝(귀보다 아래)은 늘어진 머리 끝 — 카드가 목 피부를 따라 내려가 고개를 젖히면 윗등에 닿는다
        bnd = [k for k in bnd if (0.5 * (P[k[0]][1] + P[k[1]][1]) - c[1]) / d > -1.0]
    if not bnd:
        return
    segs = 2 if len(bnd) * 4 <= budget else 1
    if len(bnd) * 2 * segs > budget:
        keep = max(budget // (2 * segs), 0)
        step = len(bnd) / max(keep, 1)
        bnd = [bnd[int(i * step)] for i in range(keep)]  # 고르게 솎는다
    lf, ls, lb = CARD_LEN[CARD_KIND[style]]
    u0, v0, u1, v1 = CARD_REGION
    newP, fuv, hd = [], [], []
    for key in bnd:
        a, b = edge[key][0]
        pa, pb = P[a], P[b]
        mid = 0.5 * (pa + pb)
        nrm = off[a] + off[b]
        nrm = nrm / max(np.linalg.norm(nrm), 1e-9)
        g = mid - opp[key]
        g = g - (g @ nrm) * nrm
        g = g / max(np.linalg.norm(g), 1e-9)
        phi = _phi(mid, c)
        L = lf + (ls - lf) * min(phi / 1.2, 1.0) if phi < 1.2 else ls + (lb - ls) * min((phi - 1.2) / 1.6, 1.0)
        rows = []
        bent = False
        for s in range(segs + 1):
            tt = s / segs
            row = []
            for q in (pa, pb):
                x = q + g * L * tt
                if s > 0:
                    loc, sn, _, _ = head_tree[0].find_nearest(tuple(x))
                    if loc is not None:
                        lift = 0.002 + 0.001 * tt
                        x = np.array(loc) + np.array(sn) * lift
                        x = x if (x - q) @ nrm > -0.002 else q + g * L * tt + nrm * lift
                    if (x - q) @ g < 0.5 * L * tt:  # 표면에 붙이며 뒤로 꺾였다(귀 뒤·오목한 곳) → 이 카드는 버린다
                        bent = True
                row.append(x)
            rows.append(row)
        if bent or any((rb - ra) @ (pb - pa) < 0.3 * float((pb - pa) @ (pb - pa)) for ra, rb in rows[1:]):
            continue  # 꺾이거나 끝이 엇갈린(X자) 카드는 버린다
        h = (key[0] * 2654435761 + key[1] * 40503) % 997 / 997.0
        w = min(np.linalg.norm(pb - pa) / CARD_TILE, 0.5)
        ua, ub = h * (1.0 - w), h * (1.0 - w) + w
        base = len(newP)
        for s, (qa, qb) in enumerate(rows):
            newP += [qa, qb]
            hd += [m.hairdata[a], m.hairdata[b]]
        for s in range(segs):
            va, vb = 1.0 - s / segs, 1.0 - (s + 1) / segs
            uv = lambda uu, vv: (2.0 + u0 + (u1 - u0) * uu, v0 + (v1 - v0) * vv)
            i0, i1, i2, i3 = base + 2 * s, base + 2 * s + 1, base + 2 * s + 3, base + 2 * s + 2
            fuv.append(((i1, i0, i3, i2), (uv(ub, va), uv(ua, va), uv(ua, vb), uv(ub, vb))))
    if newP:
        _append(m, np.array(newP), fuv, "hair_card", R, tree, hd=np.array(hd))


ATTACH_SEG = {0: (6, 8), 1: (4, 6), 2: (3, 4)}  # (고리 수, 둘레 분할)


def _attach(m, spec, lod, c, d, top, head_tree, tree, R):
    """057: 묶음(타원체)·포니테일·꼬리(관). 머리 중심에서 dirn으로 두피를 찾아 붙인다. UV는 스타일 결 타일(가닥 = v).
    spec = (종류, 방향, 크기[, (LOD0 고리 수, 둘레 분할)]) — 가는 꼬리 여럿은 분할을 줄인다."""
    kind, dirn, size = spec[:3]
    rings, segs = ATTACH_SEG[lod]
    if len(spec) > 3:
        r0, s0 = spec[3]
        rings, segs = min(rings, r0 if lod == 0 else max(r0 - lod, 2)), min(segs, s0 if lod == 0 else max(s0 - lod, 3))
    hc = np.array([c[0], c[1] + 0.6 * d, HEAD_AXIS_Z[0]])
    dv = np.array(dirn, dtype=float)
    dv /= np.linalg.norm(dv)
    hit = head_tree[0].ray_cast(tuple(hc), tuple(dv))
    if hit[0] is None:
        raise RuntimeError(f"부속 형상 두피를 못 찾음: {dirn}")
    s, sn = np.array(hit[0]), np.array(hit[1])
    if sn @ dv < 0:
        sn = -sn  # 감기 방향과 상관없이 바깥 법선
    pts, fuv = [], []
    u0, v0, u1, v1 = tile_rect(STRAND[m.style])

    def uv(uu, vv):
        return (1.0 + u0 + (u1 - u0) * uu, v0 + (v1 - v0) * vv)

    tipv = []  # 정점별 '끝' 정도 (컬러 포인트 끝 마스크용)
    if kind == "knot":
        rx, ry, rz = size
        cen = s + sn * 0.55 * max(rx, ry, rz)
        ax_up = sn
        ax_x = np.cross(ax_up, [0.0, 0.0, 1.0]) if abs(ax_up[2]) < 0.9 else np.cross(ax_up, [1.0, 0.0, 0.0])
        ax_x /= np.linalg.norm(ax_x)
        ax_z = np.cross(ax_x, ax_up)
        pts.append(cen + ax_up * ry)
        for r in range(1, rings):
            th = math.pi * r / rings
            for k in range(segs):
                ph = 2 * math.pi * k / segs
                pts.append(cen + ax_up * ry * math.cos(th) + ax_x * rx * math.sin(th) * math.cos(ph)
                           + ax_z * rz * math.sin(th) * math.sin(ph))
        pts.append(cen - ax_up * ry)
        tipv = [1.0] * len(pts)
        for k in range(segs):
            k2 = (k + 1) % segs
            fuv.append(((0, 1 + k2, 1 + k), (uv(0.5, 1.0), uv(k2 / segs, 0.8), uv(k / segs, 0.8))))
        for r in range(rings - 2):
            for k in range(segs):
                k2 = (k + 1) % segs
                a, b = 1 + r * segs + k, 1 + r * segs + k2
                cc, dd = 1 + (r + 1) * segs + k2, 1 + (r + 1) * segs + k
                vv = 0.8 - 0.6 * r / max(rings - 2, 1)
                fuv.append(((a, b, cc, dd), (uv(k / segs, vv), uv(k2 / segs, vv), uv(k2 / segs, vv - 0.1), uv(k / segs, vv - 0.1))))
        last = len(pts) - 1
        lr = 1 + (rings - 2) * segs
        for k in range(segs):
            k2 = (k + 1) % segs
            fuv.append(((last, lr + k, lr + k2), (uv(0.5, 0.0), uv(k / segs, 0.1), uv(k2 / segs, 0.1))))
    else:  # tail
        length, r0, r1, dn = size
        dn = np.array(dn, dtype=float)
        dn /= np.linalg.norm(dn)
        start = s + sn * 0.004
        ax_x = np.array([1.0, 0.0, 0.0])
        ax_z = np.cross(dn, ax_x)
        ax_z /= np.linalg.norm(ax_z)
        n_r = max(rings - 2, 1)
        for r in range(n_r + 1):
            tt = r / n_r
            cen = start + dn * length * tt
            rad = r0 + (r1 - r0) * tt
            for k in range(segs):
                ph = 2 * math.pi * k / segs
                pts.append(cen + (ax_x * math.cos(ph) + ax_z * math.sin(ph)) * rad)
                tipv.append(tt)
        pts.append(start + dn * (length + 0.4 * r1))
        tipv.append(1.0)
        for r in range(n_r):
            for k in range(segs):
                k2 = (k + 1) % segs
                a, b = r * segs + k, r * segs + k2
                cc, dd = (r + 1) * segs + k2, (r + 1) * segs + k
                fuv.append(((a, dd, cc, b), (uv(k / segs, 1 - r / n_r), uv(k / segs, 1 - (r + 1) / n_r),
                                             uv(k2 / segs, 1 - (r + 1) / n_r), uv(k2 / segs, 1 - r / n_r))))
        tip = len(pts) - 1
        lr = n_r * segs
        for k in range(segs):
            k2 = (k + 1) % segs
            fuv.append(((lr + k, tip, lr + k2), (uv(k / segs, 0.05), uv(0.5, 0.0), uv(k2 / segs, 0.05))))
    cpk = STYLE_SPEC[m.style]["cp"]
    hd = [(1.0, (1.0 if t >= 0.5 else 0.0) if cpk == "tips" else 0.0) for t in tipv]
    _append(m, np.array(pts), fuv, "hair", R, tree, hd=np.array(hd), vpart="hair_attach")


def _rim(m, faces, off, c, d, top):
    """셸 가장자리 → 두피 안쪽(두께 + 1 mm 아래)으로 띠."""
    from . import body as B
    edge = {}
    for f in faces:
        for k in range(len(f)):
            a, b = f[k], f[(k + 1) % len(f)]
            edge.setdefault((min(a, b), max(a, b)), []).append((a, b))
    new = {}
    P = m.verts
    extra = []
    for key in sorted(edge):
        if len(edge[key]) != 1:
            continue
        a, b = edge[key][0]
        for v in (a, b):
            if v not in new:
                n = off[v] / max(np.linalg.norm(off[v]), 1e-9)
                new[v] = len(P) + len(extra)
                extra.append((v, P[v] - off[v] - n * 0.001))
        m.faces.append(((b, a, new[a], new[b]), B.BODY,
                        tuple(hair_uv(x, c, d, top, m.style) for x in (P[b], P[a], P[a], P[b])), "hair", None))
    if extra:
        src = [v for v, _ in extra]
        m.verts = np.vstack([P, np.array([p for _, p in extra])])
        m.W = np.vstack([m.W, m.W[src]])
        m.hairdata = np.vstack([m.hairdata, m.hairdata[src]])
        for k in m.deltas:
            m.deltas[k] = np.vstack([m.deltas[k], m.deltas[k][src]])
        m.vpart += ["hair_rim"] * len(extra)  # 턱 안쪽 정점 (지표·관통 진단에서 셸과 구분)


def hair_uv(p, c, d, top, style):
    """u = 머리 둘레 각(정면 0 → 뒤 1, 좌우 대칭), v = 정수리 1 → 아래 0. 스타일 결 타일 안 + u 1 오프셋.
    지그재그 콘로우는 u를 v에 따라 지그재그로 민다(브레이드 타일의 곧은 줄 → 지그재그 줄)."""
    phi = _phi(p, c) / math.pi
    v = float(np.clip((p[1] - (c[1] - 2.4 * d)) / (top + 0.08 - (c[1] - 2.4 * d)), 0, 1))
    if STYLE_SPEC[style]["zig"]:
        q = (v * 6.0) % 1.0
        phi = phi + 0.035 * (2 * abs(2 * q - 1) - 1)
    u0, v0, u1, v1 = tile_rect(STRAND[style])
    return (1.0 + u0 + (u1 - u0) * float(np.clip(phi, 0, 1)), v0 + (v1 - v0) * v)
