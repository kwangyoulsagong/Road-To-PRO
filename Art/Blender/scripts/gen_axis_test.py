"""축·스케일 검증 에셋 (specs/044 FR-21, AC-02).

만드는 것:
- `UnitCube`: 1 m 큐브, 중심 원점.
- `Mark_Length` / `Mark_Width` / `Mark_Up`: 경기장 좌표 +x(길이)·+y(폭)·+z(위) 2 m 지점의 0.2 m 표식 큐브.
  Unity에서 각각 +X·+Z·+Y에 있어야 한다(AC-02). 좌표 규칙은 stadium/geometry.py.
- `Goal`: 규격 골대 1개(x = 10 m, 안쪽 폭 7.32 m, 높이 2.44 m, 포스트 지름 0.12 m). 폭 방향은 경기장 +y.

인자: --out <glb 경로>
폴리곤 예산: 수백 tris.
"""
import argparse
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from stadium import export, geometry  # noqa: E402

GOAL_WIDTH = 7.32
GOAL_HEIGHT = 2.44
POST_R = 0.06


def build():
    mat = geometry.material("AxisTest", (0.8, 0.8, 0.8, 1.0))
    geometry.box("UnitCube", (0.0, 0.0, 0.0), (1.0, 1.0, 1.0), mat)
    for name, pos in (("Mark_Length", (2.0, 0.0, 0.0)), ("Mark_Width", (0.0, 2.0, 0.0)), ("Mark_Up", (0.0, 0.0, 2.0))):
        geometry.box(name, pos, (0.2, 0.2, 0.2), mat)
    half = GOAL_WIDTH / 2 + POST_R
    goal = geometry.empty("Goal")
    geometry.cylinder("Post_A", (10.0, -half, 0.0), (10.0, -half, GOAL_HEIGHT + 2 * POST_R), POST_R, 12, mat, parent=goal)
    geometry.cylinder("Post_B", (10.0, half, 0.0), (10.0, half, GOAL_HEIGHT + 2 * POST_R), POST_R, 12, mat, parent=goal)
    geometry.cylinder("Crossbar", (10.0, -half - POST_R, GOAL_HEIGHT + POST_R), (10.0, half + POST_R, GOAL_HEIGHT + POST_R), POST_R, 12, mat, parent=goal)


def main(argv):
    ap = argparse.ArgumentParser(prog="gen_axis_test")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    geometry.reset_scene()
    build()
    export.write_glb(a.out)
    print(f"[axis-test] 삼각형 {geometry.triangle_count()} → {a.out}")


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
