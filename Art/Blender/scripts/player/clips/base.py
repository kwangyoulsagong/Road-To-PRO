"""클립 공통 (specs/043 FR-32).

Clip: id, group, frames(키 개수 − 1 = 길이 프레임, 30 fps), loop, sample(t초) → Pose, 태그.
접촉 규칙 contact = {"frame", "part", "rule": {...}} — rule로 계산한 점은 **접촉 순간 공 중심이 있어야 할 위치**다.
rule: bone, toward(없으면 bone 머리), t, up, fwd, side, basis("foot" = bone→toward 수평 방향, "body" = 골반 정면)
Unity(ClipLibrary)가 같은 규칙을 본 위치만으로 계산한다 — 본 축 규칙에 기대지 않는다.
"""
import math

from mathutils import Vector

FPS = 30
BALL_R = 0.11


class Clip:
    def __init__(self, id_, group, frames, sample, loop=False, **tags):
        self.id, self.group, self.frames, self.sample, self.loop = id_, group, frames, sample, loop
        self.tags = tags

    @property
    def length(self):
        return self.frames / FPS


def smooth(x):
    x = 0.0 if x < 0 else 1.0 if x > 1 else x
    return x * x * (3 - 2 * x)


def smoother(x):
    x = 0.0 if x < 0 else 1.0 if x > 1 else x
    return x * x * x * (x * (x * 6 - 15) + 10)


def facing(head):
    r = head["RightUpperLeg"] - head["LeftUpperLeg"]
    f = r.cross(Vector((0, 1, 0)))
    f.y = 0
    return f.normalized()


def contact_point(rule, head):
    p0 = head[rule["bone"]]
    p1 = head[rule["toward"]] if rule.get("toward") else p0
    p = p0.lerp(p1, rule.get("t", 0.0))
    up = Vector((0, 1, 0))
    if rule.get("basis", "foot") == "foot" and rule.get("toward"):
        f = p1 - p0
        f.y = 0
        f = f.normalized() if f.length > 1e-6 else facing(head)
    else:
        f = facing(head)
    side = up.cross(f)  # f = +Z → side = +X (오른쪽)
    return p + up * rule.get("up", 0.0) + f * rule.get("fwd", 0.0) + side * rule.get("side", 0.0)


# 자주 쓰는 접촉 규칙
def instep(side):
    n = "Left" if side < 0 else "Right"
    return {"bone": n + "Foot", "toward": n + "Toes", "t": 0.7, "up": 0.03, "fwd": 0.10, "side": 0.0, "basis": "foot"}


def inside(side):
    n = "Left" if side < 0 else "Right"
    # 안쪽 = 왼발이면 발 방향의 오른쪽, 오른발이면 왼쪽
    return {"bone": n + "Foot", "toward": n + "Toes", "t": 0.35, "up": 0.03, "fwd": 0.0, "side": -side * (0.045 + BALL_R), "basis": "foot"}


def outside(side):
    n = "Left" if side < 0 else "Right"
    return {"bone": n + "Foot", "toward": n + "Toes", "t": 0.6, "up": 0.03, "fwd": 0.0, "side": side * (0.045 + BALL_R), "basis": "foot"}


def sole(side):
    n = "Left" if side < 0 else "Right"
    return {"bone": n + "Foot", "toward": n + "Toes", "t": 1.0, "up": 0.0, "fwd": 0.06 + BALL_R, "side": 0.0, "basis": "foot"}


HEAD = {"bone": "Head", "toward": "Neck", "t": -0.9, "up": 0.0, "fwd": 0.10 + BALL_R, "side": 0.0, "basis": "body"}
CHEST = {"bone": "UpperChest", "toward": "Neck", "t": 0.2, "up": 0.0, "fwd": 0.12 + BALL_R, "side": 0.0, "basis": "body"}


def thigh(side):
    n = "Left" if side < 0 else "Right"
    return {"bone": n + "UpperLeg", "toward": n + "LowerLeg", "t": 0.6, "up": 0.06 + BALL_R, "fwd": 0.0, "side": 0.0, "basis": "body"}


HANDS = {"bone": "LeftHand", "toward": "RightHand", "t": 0.5, "up": 0.0, "fwd": 0.06 + BALL_R, "side": 0.0, "basis": "body"}


def hand(side):
    n = "Left" if side < 0 else "Right"
    return {"bone": n + "Hand", "toward": n + "LowerArm", "t": -0.6, "up": 0.0, "fwd": 0.0, "side": 0.0, "basis": "body"}
