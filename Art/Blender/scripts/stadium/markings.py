"""피치 마킹 정의 (specs/044 FR-02·03, Law 1 공개 규격). bpy를 쓰지 않는다.

좌표: 경기장 좌표(x = 길이, y = 폭, 미터, 센터마크 원점). 라인은 **중심선**으로 기록하고 폭은 `lineWidth`.
- 경계선(터치라인·골라인)은 경계를 중심으로 ±반폭 (스펙 수정 2026-11-03, 004 GoalFrame과 같은 기준).
- 내부 라인은 자기가 둘러싼 구역 안에 포함 — 규격 치수는 라인 바깥 가장자리까지 잰다.
- 선분은 끝을 둥글리지 않는 직사각형. 모서리가 비지 않게 만나는 선 쪽으로 끝을 늘려 둔다.
- 원호 각도는 도(degree), x축에서 y축 방향(반시계)이 +. `from` < `to`.
"""
import math

LENGTH = 105.0
WIDTH = 68.0
LINE_WIDTH = 0.12
CENTER_CIRCLE_R = 9.15
PENALTY_AREA_DEPTH = 16.5
GOAL_AREA_DEPTH = 5.5
PENALTY_MARK_DIST = 11.0
PENALTY_ARC_R = 9.15
CORNER_ARC_R = 1.0
GOAL_INNER_WIDTH = 7.32
GOAL_INNER_HEIGHT = 2.44
POST_RADIUS = 0.06
SPOT_RADIUS = 0.11
STRIPE_WIDTH = 5.25  # 105 m / 20띠


def _segment(name, a, b):
    return {"kind": "segment", "name": name, "a": [a[0], a[1]], "b": [b[0], b[1]]}


def _arc(name, c, r, frm, to):
    return {"kind": "arc", "name": name, "c": [c[0], c[1]], "r": r, "from": frm, "to": to}


def pitch_markings(length=LENGTH, width=WIDTH, line_width=LINE_WIDTH):
    hl, hwid, h = length / 2, width / 2, line_width / 2
    lines = [
        _segment("Touch_S", (-hl - h, -hwid), (hl + h, -hwid)),
        _segment("Touch_N", (-hl - h, hwid), (hl + h, hwid)),
        _segment("Goal_L", (-hl, -hwid - h), (-hl, hwid + h)),
        _segment("Goal_R", (hl, -hwid - h), (hl, hwid + h)),
        _segment("Halfway", (0.0, -hwid), (0.0, hwid)),
        _arc("CenterCircle", (0.0, 0.0), CENTER_CIRCLE_R - h, -180.0, 180.0),
    ]
    spots = [{"name": "CenterMark", "c": [0.0, 0.0], "r": SPOT_RADIUS}]
    post_inner = GOAL_INNER_WIDTH / 2
    for side, sgn in (("L", -1.0), ("R", 1.0)):
        gx = sgn * hl
        for area, depth, half_outer in (("PenaltyArea", PENALTY_AREA_DEPTH, post_inner + PENALTY_AREA_DEPTH),
                                        ("GoalArea", GOAL_AREA_DEPTH, post_inner + GOAL_AREA_DEPTH)):
            outer_x = gx - sgn * depth          # 앞선 바깥 가장자리 (피치 중앙 쪽)
            front_x = outer_x + sgn * h          # 앞선 중심
            lines.append(_segment(f"{area}_{side}_Front", (front_x, -half_outer), (front_x, half_outer)))
            for ns, ys in (("S", -1.0), ("N", 1.0)):
                y = ys * (half_outer - h)
                lines.append(_segment(f"{area}_{side}_Side{ns}", (gx, y), (outer_x, y)))
        mark_x = gx - sgn * PENALTY_MARK_DIST
        spots.append({"name": f"PenaltyMark_{side}", "c": [mark_x, 0.0], "r": SPOT_RADIUS})
        r = PENALTY_ARC_R - h
        pa_outer = gx - sgn * PENALTY_AREA_DEPTH
        theta = math.degrees(math.acos(abs(pa_outer - mark_x) / r))
        base = 0.0 if sgn < 0 else 180.0
        lines.append(_arc(f"PenaltyArc_{side}", (mark_x, 0.0), r, base - theta, base + theta))
    corner_r = CORNER_ARC_R - h
    for name, cx, cy, frm in (("Corner_SW", -hl, -hwid, 0.0), ("Corner_NW", -hl, hwid, -90.0),
                              ("Corner_SE", hl, -hwid, 90.0), ("Corner_NE", hl, hwid, 180.0)):
        lines.append(_arc(name, (cx, cy), corner_r, frm, frm + 90.0))
    return {
        "length": length,
        "width": width,
        "lineWidth": line_width,
        "lines": lines,
        "spots": spots,
        "stripes": {"pattern": "straight", "width": STRIPE_WIDTH},
        "goal": {"innerWidth": GOAL_INNER_WIDTH, "innerHeight": GOAL_INNER_HEIGHT, "postRadius": POST_RADIUS,
                 "postCenterOffsetY": post_inner + POST_RADIUS},
    }
