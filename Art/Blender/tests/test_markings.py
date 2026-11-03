"""피치 마킹 정의 (specs/044 FR-02·03, AC-01). Blender 없이 돈다.

규칙(스펙 수정 2026-11-03): 경계선(터치라인·골라인)은 경계 중심 ±0.06 m, 포스트 중심 = 골라인 중심.
내부 라인은 자기가 둘러싼 구역 안에 포함 — 치수는 라인 바깥 가장자리까지.
"""
import math
import unittest

import _path  # noqa: F401
from stadium import markings

TOL = 0.01
HW = 0.06  # 라인 반폭


def seg(m, name):
    for s in m["lines"]:
        if s.get("name") == name:
            return s
    raise AssertionError("라인 없음: " + name)


class MarkingDimensions(unittest.TestCase):
    def setUp(self):
        self.m = markings.pitch_markings()

    def near(self, a, b, what, tol=TOL):
        self.assertLessEqual(abs(a - b), tol, f"{what}: 기대 {b}, 실제 {a}")

    def test_boundary_is_105_by_68_line_centers(self):
        self.near(seg(self.m, "Touch_N")["a"][1], 34.0, "북쪽 터치라인 중심")
        self.near(seg(self.m, "Touch_S")["a"][1], -34.0, "남쪽 터치라인 중심")
        self.near(seg(self.m, "Goal_L")["a"][0], -52.5, "왼쪽 골라인 중심")
        self.near(seg(self.m, "Goal_R")["a"][0], 52.5, "오른쪽 골라인 중심")
        self.assertEqual(self.m["lineWidth"], 0.12)
        self.assertEqual((self.m["length"], self.m["width"]), (105.0, 68.0))
        # 모서리가 비지 않게 경계선은 바깥 가장자리까지 덮는다
        t = seg(self.m, "Touch_N")
        self.near(min(t["a"][0], t["b"][0]), -52.5 - HW, "터치라인 왼쪽 끝")
        self.near(max(t["a"][0], t["b"][0]), 52.5 + HW, "터치라인 오른쪽 끝")

    def test_halfway_line_centered(self):
        h = seg(self.m, "Halfway")
        self.near(h["a"][0], 0.0, "하프라인")
        self.near(abs(h["a"][1] - h["b"][1]), 68.0, "하프라인 길이(터치라인 중심까지)")

    def test_center_circle_and_mark(self):
        c = seg(self.m, "CenterCircle")
        self.near(c["r"] + HW, 9.15, "센터서클 바깥 반지름")
        self.near(c["to"] - c["from"], 360.0, "센터서클 전체")
        spot = [s for s in self.m["spots"] if s["name"] == "CenterMark"][0]
        self.assertEqual(spot["c"], [0.0, 0.0])

    def test_penalty_area_left(self):
        front = seg(self.m, "PenaltyArea_L_Front")
        self.near(front["a"][0] + HW - (-52.5), 16.5, "페널티 에어리어 깊이(바깥 가장자리)")
        side = seg(self.m, "PenaltyArea_L_SideN")
        self.near((side["a"][1] + HW) * 2, 40.32, "페널티 에어리어 폭(바깥 가장자리)")
        self.near(min(side["a"][0], side["b"][0]), -52.5, "옆선은 골라인 중심에서 시작")
        self.near(max(side["a"][0], side["b"][0]), -52.5 + 16.5, "옆선은 앞선 바깥 가장자리까지")

    def test_goal_area_left(self):
        front = seg(self.m, "GoalArea_L_Front")
        self.near(front["a"][0] + HW - (-52.5), 5.5, "골 에어리어 깊이")
        side = seg(self.m, "GoalArea_L_SideN")
        self.near((side["a"][1] + HW) * 2, 18.32, "골 에어리어 폭")

    def test_penalty_mark_and_arc(self):
        spot = [s for s in self.m["spots"] if s["name"] == "PenaltyMark_L"][0]
        self.near(spot["c"][0] - (-52.5), 11.0, "페널티 마크 거리")
        arc = seg(self.m, "PenaltyArc_L")
        self.near(arc["r"] + HW, 9.15, "페널티 아크 바깥 반지름")
        # 아크 양 끝은 페널티 에어리어 바깥 가장자리 위
        for ang in (arc["from"], arc["to"]):
            x = arc["c"][0] + arc["r"] * math.cos(math.radians(ang))
            self.near(x, -52.5 + 16.5, "아크 끝 x", tol=0.02)
        self.assertLess(arc["from"], 0)
        self.assertGreater(arc["to"], 0)

    def test_corner_arcs(self):
        for name, c in (("Corner_SW", (-52.5, -34.0)), ("Corner_NW", (-52.5, 34.0)), ("Corner_SE", (52.5, -34.0)), ("Corner_NE", (52.5, 34.0))):
            a = seg(self.m, name)
            self.assertEqual(a["c"], list(c))
            self.near(a["r"] + HW, 1.0, name + " 바깥 반지름")
            self.near(a["to"] - a["from"], 90.0, name + " 사분원")
            mid = math.radians((a["from"] + a["to"]) / 2)
            # 사분원 가운데가 피치 안쪽을 향한다
            self.assertLess(abs(c[0] + math.cos(mid)), abs(c[0]))
            self.assertLess(abs(c[1] + math.sin(mid)), abs(c[1]))

    def test_goal_posts_on_goal_line(self):
        g = self.m["goal"]
        self.assertEqual((g["innerWidth"], g["innerHeight"], g["postRadius"]), (7.32, 2.44, 0.06))
        self.near(g["postCenterOffsetY"], 7.32 / 2 + 0.06, "포스트 중심 y")

    def test_mirror_symmetry(self):
        by = {s["name"]: s for s in self.m["lines"]}
        for name, s in by.items():
            if "_L" not in name:
                continue
            r = by[name.replace("_L", "_R")]
            if s["kind"] == "segment":
                xs = sorted([s["a"][0], s["b"][0]])
                rx = sorted([-r["a"][0], -r["b"][0]])
                for a, b in zip(xs, rx):
                    self.near(a, b, name + " 대칭 x")
            else:
                self.near(s["c"][0], -r["c"][0], name + " 대칭 중심")
                self.near((s["to"] - s["from"]), (r["to"] - r["from"]), name + " 대칭 각")

    def test_stripes_default(self):
        self.assertEqual(self.m["stripes"], {"pattern": "straight", "width": 5.25})


if __name__ == "__main__":
    unittest.main()
