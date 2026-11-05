"""클립 형태 지표 빠른 측정 (specs/055 FR-70). MPFB·메시 없이 뼈대만으로 클립을 만들어 지표를 낸다(수 초).

실행: Blender --background --factory-startup --python gait_lab.py -- --layout <player.layout.json> --out <metrics.json> [--clips id,id]
뼈대는 생성 산출물(player.layout.json skeleton.bones)에서 읽는다 — 전체 생성기(gen_player)와 같은 뼈대.
출력: {"clips": {id: 지표}, "kicks": {id: 지표}}.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--layout", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--clips", default="all")
    ap.add_argument("--selftest", action="store_true", help="합성 포즈로 지표 정의를 검사 (test_gait.test_metrics_unit)")
    a = ap.parse_args(argv)
    from player import skeleton
    with open(a.layout, encoding="utf-8") as f:
        lay = json.load(f)
    skeleton.configure({b["name"]: (b["head"], b["tail"]) for b in lay["skeleton"]["bones"]})
    from player import gait_metrics as GM
    if a.selftest:
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(selftest(), f, indent=1, sort_keys=True)
        return
    from player.clips import all_clips
    want = None if a.clips == "all" else set(a.clips.split(","))
    out = {"clips": {}, "kicks": {}}
    for c in all_clips():
        if want is not None and c.id not in want:
            continue
        if c.group == "locomotion" and c.loop and c.tags.get("gait") not in (None, "idle"):
            out["clips"][c.id] = GM.locomotion(c)
        elif c.tags.get("contact") and c.tags["contact"]["part"] in ("RightFoot", "LeftFoot") and c.tags.get("kind"):
            out["kicks"][c.id] = GM.kick(c)
        elif hasattr(GM, "football") and c.tags.get("football"):
            out["clips"][c.id] = GM.football(c)

    def clean(x):
        if isinstance(x, dict):
            return {k: clean(v) for k, v in x.items() if k not in ("hipFlex",)}
        if isinstance(x, (list, tuple)):
            return [clean(v) for v in x]
        if isinstance(x, float):
            return round(x, 5)
        return x
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(clean(out), f, ensure_ascii=False, indent=1, sort_keys=True)
    print(f"[gait_lab] {len(out['clips'])} loco, {len(out['kicks'])} kicks → {a.out}")


def selftest():
    """합성 포즈 → 지표 차이. 기대값과 함께 돌려준다 (판정은 테스트가)."""
    import math
    from mathutils import Vector
    from player import gait_metrics as GM
    from player import pose as PO
    from player.clips import poses as P
    from player.clips.base import Clip
    d = Vector((0.0, 0.0, 1.0))

    def H(**kw):
        k = P.merged(P.STAND, **kw)
        return PO.fk(P.to_pose(k))

    def lean(f):
        return GM.trunk_lean(f[1], d)

    def thigh(f, n="Left"):
        h = f[1]
        return GM._sag_angle(h[n + "LowerLeg"] - h[n + "UpperLeg"], d, -(h["Neck"] - h["Hips"]))

    def knee(f, n="Left"):
        h = f[1]
        return GM.flexion(h[n + "UpperLeg"], h[n + "LowerLeg"], h[n + "Foot"])

    def elbow(f, n="Left"):
        h = f[1]
        return GM.flexion(h[n + "UpperArm"], h[n + "LowerArm"], h[n + "Hand"])

    r = {}
    base = H()
    r["lean15"] = (lean(H(hrot=(15.0, 0.0, 0.0))) - lean(base), 15.0)
    leg0 = H(legL=(0.0, 0.0, 0.0, 0.0, 0.0))
    r["hipFlex40"] = (thigh(H(legL=(40.0, 0.0, 0.0, 0.0, 0.0))) - thigh(leg0), 40.0)
    r["knee60"] = (knee(H(legL=(0.0, 60.0, 0.0, 0.0, 0.0))) - knee(leg0), 60.0)
    # 굴곡 = 기하 각: 키 포즈 숫자(팔꿈치 키는 A-포즈에서 선수 X축 회전이라 실제 굴곡과 다르다)가 아니라 점으로 검사
    a = math.radians(70.0)
    r["flexion70"] = (GM.flexion(Vector((0, 0, 0)), Vector((1, 0, 0)), Vector((1 + math.cos(a), math.sin(a), 0))), 70.0)

    # 합성 루프 클립: 머리 젖힘 20°·골반 요 ±10°·팔 스윙 ±30°(팔꿈치 고정 90°)
    def sample(t):
        ph = t / 1.0
        c = math.cos(2 * math.pi * ph)
        k = P.merged(P.STAND, head=(-20.0 * max(0.0, c), 0.0), hrot=(0.0, 10.0 * c, 0.0),
                     armL=(20.0, 30.0 * c, 90.0), armR=(20.0, -30.0 * c, 90.0))
        return P.to_pose(k)
    clip = Clip("synthetic", "locomotion", 30, sample, loop=True, gait="jog", speed=1.0, dir=0,
                stance={"Left": [0.0, 0.3], "Right": [0.5, 0.8]}, cycle=30)
    m = GM.locomotion(clip)
    r["neckExt20"] = (m["neckExtMax"], 20.0)
    r["pelvisYaw10"] = (m["pelvisYaw"], 10.0)
    r["elbowRange0"] = (m["elbowRange"], 0.0)
    r["cadence2"] = (m["cadence"], 2.0)
    r["duty03"] = (m["duty"], 0.3)
    return r


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    try:
        main(argv)
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
