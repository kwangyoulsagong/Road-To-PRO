"""경기장 공용 치수 (specs/044). bpy를 쓰지 않는다. 좌표는 경기장 좌표(x 길이, y 폭, z 위)."""
import json
import os

from . import markings

RUNOFF = 5.0                 # FR-04 (승인)
BOARD_GAP = 0.1              # 런오프 경계 → 광고판 앞면
BOARD_H, BOARD_T = 0.9, 0.12
STAND_GAP = 3.0              # 런오프 경계 → 관중석 앞면
ROW_DEPTH, ROW_RISE = 0.8, 0.4
SEAT_PITCH = 0.5             # 좌석 간격(줄 방향)
FRONT_WALL = 1.2             # 1층 앞 벽 높이
TIER_FASCIA = 2.5            # 위층 앞 벽(아래층 맨 뒤 위로)
ROOF_CLEAR = 6.0             # 맨 위 줄 → 지붕 높이
ROOF_OVERHANG = 4.0          # 관중석 앞면보다 앞으로
ROOF_T = 0.6
TOWER_OFFSET = 10.0          # 관중석 모서리 → 조명탑
TOWER_H = 45.0
SECTIONS = ("S", "N", "W", "E")   # S = 메인 스탠드(카메라), E = 원정 응원석
AWAY_SECTION = "E"

HALF_L = markings.LENGTH / 2
HALF_W = markings.WIDTH / 2
RUN_X = HALF_L + RUNOFF
RUN_Y = HALF_W + RUNOFF
STAND_X = RUN_X + STAND_GAP  # W·E 관중석 앞면 x
STAND_Y = RUN_Y + STAND_GAP  # S·N 관중석 앞면 y


def unity(p):
    """경기장 (x, y, z) → layout.json Unity 좌표 [X, Y, Z] = [x, z, y]."""
    return [p[0], p[2], p[1]]


def unity_box(lo, hi):
    return {"min": unity(lo), "max": unity(hi)}


def _repo_root():
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.normpath(os.path.join(here, "..", "..", "..", ".."))


def ball_tuning():
    """004 공 물리 파라미터 원본(Data/tuning/ball.json) — 골대 반지름·골망 깊이를 시뮬과 같게 (AC-16)."""
    path = os.path.join(_repo_root(), "Data", "tuning", "ball.json")
    with open(path, encoding="utf-8") as f:
        rows = {d["id"]: d["default"] for d in json.load(f)["definitions"]}
    return {"postRadius": float(rows["ball.goal.postRadius"]), "netDepth": float(rows["ball.net.depth"])}


def section_frame(section):
    """관중석 한 면: (앞면 중심선 위치 함수 정보) → (바깥 방향 단위벡터, 길이 방향 단위벡터, 앞면 거리, 반길이)."""
    if section == "S":
        return (0.0, -1.0), (1.0, 0.0), STAND_Y, STAND_X
    if section == "N":
        return (0.0, 1.0), (-1.0, 0.0), STAND_Y, STAND_X
    if section == "W":
        return (-1.0, 0.0), (0.0, -1.0), STAND_X, STAND_Y
    return (1.0, 0.0), (0.0, 1.0), STAND_X, STAND_Y
