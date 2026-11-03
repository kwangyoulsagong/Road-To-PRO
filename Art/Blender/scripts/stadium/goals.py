"""골대·골망과 충돌 형상 (specs/044 FR-03·27·64). 포스트 중심 = 골라인 중심 = 피치 경계 (004 GoalFrame)."""
from . import geometry, markings
from .layout import HALF_L, unity, unity_box

SEGMENTS = 16
NET_TOP_EXTRA = 0.05


def build(mats, tuning):
    r = tuning["postRadius"]
    depth = tuning["netDepth"]
    w, h = markings.GOAL_INNER_WIDTH, markings.GOAL_INNER_HEIGHT
    zc = w / 2 + r                   # 포스트 중심 (폭 방향)
    top = h + 2 * r                  # 크로스바 윗면
    data = []
    for side, name in ((-1, "Goal_L"), (1, "Goal_R")):
        x = side * HALF_L
        parent = geometry.empty(name)
        fr = geometry.MeshBuilder()
        posts = []
        for sy in (-1, 1):
            a, b = (x, sy * zc, 0.0), (x, sy * zc, top)
            fr.cylinder(a, b, r, SEGMENTS, mats["Metal"])
            posts.append({"a": unity(a), "b": unity(b), "r": r})
        ca, cb = (x, -zc - r, h + r), (x, zc + r, h + r)
        fr.cylinder(ca, cb, r, SEGMENTS, mats["Metal"])
        fr.build(f"GoalFrame_{name[-1]}", parent=parent)

        nx = x + side * depth
        nt = h + r + NET_TOP_EXTRA
        nw = zc
        net = geometry.MeshBuilder()
        m = mats["Net"]
        net.quad((nx, -nw, 0), (nx, nw, 0), (nx, nw, nt), (nx, -nw, nt), m, (-side, 0, 0))          # 뒤
        net.quad((x, -nw, nt), (nx, -nw, nt), (nx, nw, nt), (x, nw, nt), m, (0, 0, -1))             # 위
        net.quad((x, -nw, 0), (nx, -nw, 0), (nx, -nw, nt), (x, -nw, nt), m, (0, 1, 0))              # 옆
        net.quad((x, nw, 0), (nx, nw, 0), (nx, nw, nt), (x, nw, nt), m, (0, -1, 0))
        net.build(f"Net_{name[-1]}", parent=parent)
        lo = (min(x, nx), -nw, 0.0)
        hi = (max(x, nx), nw, nt)
        data.append({"side": side, "name": name, "posts": posts,
                     "crossbar": {"a": unity(ca), "b": unity(cb), "r": r},
                     "net": unity_box(lo, hi)})
    return data
