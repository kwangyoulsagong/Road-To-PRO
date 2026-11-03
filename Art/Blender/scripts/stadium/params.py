"""경기장 인자 (specs/044 FR-10·12·61). bpy를 쓰지 않는다.

수용 인원 등급 4단계 8천·2만·4만·6만석, 층수 1~3, 지붕 none|partial|full, 좌석 색 #RRGGBB 2개,
조명 towers|roofline(전체 지붕 + towers → roofline 자동 전환, 2026-11-03), 광고판 on|off, 시드 0 이상 정수.
"""
import re
from dataclasses import dataclass

CAPACITY_TIERS = (8000, 20000, 40000, 60000)
ROOFS = ("none", "partial", "full")
LIGHTS = ("towers", "roofline")
BOARDS = ("on", "off")
_HEX = re.compile(r"^#([0-9A-Fa-f]{6})$")


class ParamError(ValueError):
    pass


def _color(s):
    m = _HEX.match(s.strip())
    if not m:
        raise ParamError(f"좌석 색 '{s}': #RRGGBB 형식이어야 한다")
    v = int(m.group(1), 16)
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


@dataclass(frozen=True)
class StadiumParams:
    capacity: int
    tiers: int
    roof: str
    seat_colors: tuple
    lights: str
    lights_switched: bool
    boards: bool
    seed: int
    requested_lights: str

    @staticmethod
    def from_args(capacity, tiers, roof, seat_colors, lights, boards, seed):
        try:
            capacity, tiers, seed = int(capacity), int(tiers), int(seed)
        except (TypeError, ValueError):
            raise ParamError("수용 인원·층수·시드는 정수")
        if capacity not in CAPACITY_TIERS:
            raise ParamError(f"수용 인원 {capacity}: 등급 {CAPACITY_TIERS} 중 하나")
        if not 1 <= tiers <= 3:
            raise ParamError(f"층수 {tiers}: 1~3")
        if roof not in ROOFS:
            raise ParamError(f"지붕 '{roof}': {ROOFS}")
        if lights not in LIGHTS:
            raise ParamError(f"조명 '{lights}': {LIGHTS}")
        if boards not in BOARDS:
            raise ParamError(f"광고판 '{boards}': {BOARDS}")
        if not 0 <= seed <= 2**31 - 1:
            raise ParamError(f"시드 {seed}: 0 ~ 2^31-1")
        parts = seat_colors.split(",") if isinstance(seat_colors, str) else []
        if len(parts) != 2:
            raise ParamError("좌석 색은 '#RRGGBB,#RRGGBB' 2개")
        cols = (_color(parts[0]), _color(parts[1]))
        if lights == "roofline" and roof == "none":
            raise ParamError("지붕 라인 조명은 지붕이 있어야 한다 (roof none + lights roofline)")
        eff = "roofline" if roof == "full" and lights == "towers" else lights
        return StadiumParams(capacity, tiers, roof, cols, eff, eff != lights, boards == "on", seed, lights)

    def as_args(self):
        return dict(capacity=self.capacity, tiers=self.tiers, roof=self.roof,
                    seat_colors=",".join("#%02X%02X%02X" % c for c in self.seat_colors),
                    lights=self.requested_lights, boards="on" if self.boards else "off", seed=self.seed)


PRESETS = {
    "min": dict(capacity=8000, tiers=1, roof="none", seat_colors="#1E5AA8,#F2F2F2", lights="towers", boards="on", seed=1),
    "default": dict(capacity=40000, tiers=2, roof="partial", seat_colors="#1E5AA8,#F2F2F2", lights="towers", boards="on", seed=1),
    "max": dict(capacity=60000, tiers=3, roof="full", seat_colors="#1E5AA8,#F2F2F2", lights="towers", boards="on", seed=1),
}


def preset(name):
    return StadiumParams.from_args(**PRESETS[name])
