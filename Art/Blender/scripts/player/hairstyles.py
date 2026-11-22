"""헤어 스타일 표 (specs/088 FR-14·17 — 062 hair.py의 절차적 셸 생성을 지우고 표만 남김).

순서·이름 0~34는 057·062 그대로(Unity PlayerLook.HairMeshes·AppearanceGenerator와 같다), 9 = 삭발(형상 없음).
모든 형상은 그루밍 v2 시안을 굽는다(groom.bake). 계열(group)·분포 계열(family)·컬러 포인트(cp)·헤어라인 정보는 062 FR-51 분류표 값.
결(strand)은 그루밍 스타일 정의(groom.styles)가 정한다 — 공유 결 아틀라스 타일(직모·웨이브·곱슬·코일·브레이드·드레드).
"""
import math

# (이름, 분포 계열 family, 분류표 계열 group, 컬러 포인트, 헤어라인 형, 뒷목선, 앞머리) — 062 FR-51 (hair.STYLE_SPEC에서 옮김)
_TABLE = (
    ("Hair_Buzz", "short", "short", None, "straight", "square", False),
    ("Hair_Short", "short", "short", None, "round", "round", False),
    ("Hair_Long", "long", "long", None, "straight", "neck", False),
    ("Hair_Curly", "medium", "curly", None, "round", "long", False),
    ("Hair_Bun", "long", "braid", None, "straight", "long", False),
    ("Hair_Fade", "short", "fade", "top", "straight", "high", False),
    ("Hair_Afro", "medium", "curly", None, "round", "round", False),
    ("Hair_Mohawk", "medium", "fade", "strip", "straight", "high", False),
    ("Hair_Dreads", "long", "braid", None, "M", "neck", False),
    ("Hair_Bald", "short", "bald", None, None, None, False),
    ("Hair_Crew", "short", "short", None, "straight", "high", False),
    ("Hair_SidePart", "short", "short", None, "peak", "taper", False),
    ("Hair_Undercut", "short", "fade", "top", "straight", "high", False),
    ("Hair_SlickBack", "medium", "medium", None, "M", "square", False),
    ("Hair_Quiff", "short", "medium", None, "straight", "high", False),
    ("Hair_Curtains", "medium", "medium", None, "straight", "long", True),
    ("Hair_TexturedCrop", "short", "short", None, "straight", "high", True),
    ("Hair_BuzzLine", "short", "short", None, "straight", "square", False),
    ("Hair_HighTop", "medium", "curly", None, "straight", "high", False),
    ("Hair_Cornrows", "short", "braid", None, "round", "taper", False),
    ("Hair_Twists", "medium", "curly", None, "round", "round", False),
    ("Hair_TopKnot", "long", "braid", None, "straight", "high", False),
    ("Hair_Ponytail", "long", "braid", None, "straight", "long", False),
    ("Hair_MediumWave", "medium", "medium", None, "round", "long", False),
    ("Hair_FrenchCrop", "short", "short", None, "straight", "high", True),
    ("Hair_IvyLeague", "short", "short", None, "M", "taper", False),
    ("Hair_Spiky", "short", "short", None, "M", "square", False),
    ("Hair_CurlyTop", "short", "curly", "top", "round", "high", False),
    ("Hair_BoxBraids", "long", "braid", "tips", "round", "neck", False),
    ("Hair_HalfUp", "long", "long", None, "M", "long", False),
    ("Hair_PartedTwoBlock", "short", "fade", "top", "round", "high", True),
    ("Hair_CommaPerm", "medium", "medium", None, "round", "round", True),
    ("Hair_VolumeCurlyLong", "long", "long", None, "round", "neck", False),
    ("Hair_StraightCornrows", "short", "braid", None, "straight", "square", False),
    ("Hair_ZigzagCornrows", "short", "braid", None, "peak", "high", False),
)
STYLES = tuple(r[0] for r in _TABLE)
FAMILY = {r[0]: r[1] for r in _TABLE}
GROUP = {r[0]: r[2] for r in _TABLE}
_CP = {r[0]: r[3] for r in _TABLE}
_LINE = {r[0]: (r[4], r[5], r[6]) for r in _TABLE}
STYLE_TABLE = tuple((r[0], r[1]) for r in _TABLE)
MESH_STYLES = tuple(s for s in STYLES if s != "Hair_Bald")
COLOR_POINT = tuple(s for s in MESH_STYLES if _CP[s])
FADE = ("Hair_Fade", "Hair_Undercut", "Hair_PartedTwoBlock", "Hair_Mohawk", "Hair_CurlyTop")  # FR-42 판정 대상
VARIANT_PAIRS = {("Hair_Buzz", "Hair_BuzzLine")}
CAP = {"short": 0.06, "medium": 0.15, "volume": 0.35}   # 062 FR-40 볼륨 비율 상한
HEAD_AXIS_Z = [0.0]


def strand(style):
    from .groom import styles as GS
    return GS.STYLES[style]["strand"] if style in GS.STYLES else None


STRAND = type("StrandMap", (), {"__getitem__": lambda self, s: strand(s)})()


def cp(style):
    from .groom import styles as GS
    return GS.STYLES.get(style, {}).get("cp", _CP.get(style)) if style in GS.STYLES else _CP.get(style)


def card_max():
    from .groom import bake
    return bake.CARD_TRIS[0]


def build(R, style, lod):
    """→ body.Mesh: 그루밍 시안을 굽는다 (088 FR-10)."""
    from .groom import bake as GB
    from . import face as F
    from . import mpfb_src as MS
    hi = MS.OUR_NAMES.index("Head")
    hz = R.verts[[v for v in R.groups["body"] if R.weights[v, hi] >= 0.5], 2]
    HEAD_AXIS_Z[0] = float((hz.min() + hz.max()) / 2)
    return GB.build(R, style, lod, GB.summary(style, R))


def layout_info(style):
    """player.layout.json hairStyles[] 062 필드 (group·strand·hairline·nape·colorPoint·fade)."""
    if style == "Hair_Bald":
        return {"group": "bald", "strand": None, "hairline": None, "colorPoint": None, "fade": False}
    line, nape, fringe = _LINE[style]
    return {"group": GROUP[style], "strand": strand(style), "hairline": "fringe" if fringe else line, "nape": nape,
            "colorPoint": cp(style), "fade": style in FADE}


def _phi(p, c):
    """머리 수직축 둘레 각: 0 = 정면, π = 뒤 (좌우 대칭이라 절댓값). 축은 머리 앞뒤 가운데 (build에서 정한다)."""
    return abs(math.atan2(p[0] - c[0], p[2] - HEAD_AXIS_Z[0]))
