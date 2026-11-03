"""경기장 생성기 통합 테스트 (specs/044 AC-01·03~09·15·16). 백그라운드 Blender를 실제로 돌린다.

실행: `python3 Tools/devicefarm/farm.py art --spec 044` (리포트) 또는 `python3 -m unittest test_stadium` (Art/Blender/tests에서).
인자 세트별 산출물은 환경 변수 FARM_ART_OUT 폴더(없으면 임시 폴더)에 `<세트>/stadium.*`로 남는다 — 리포트의 미리보기 격자 입력.
"""
import hashlib
import json
import math
import os
import shutil
import struct
import subprocess
import tempfile
import time
import unittest

import _path
from stadium import markings, params

BLENDER = os.environ.get("BLENDER", "/Applications/Blender.app/Contents/MacOS/Blender")
GEN = os.path.join(_path.SCRIPTS, "gen_stadium.py")
REPO = os.path.normpath(os.path.join(_path.SCRIPTS, "..", "..", ".."))
PREVIEWS = ("main", "goal", "top")

_OUT_ROOT = os.environ.get("FARM_ART_OUT") or tempfile.mkdtemp(prefix="stadium-test-")
_cache = {}


def _cli(p_args):
    a = []
    for k, v in p_args.items():
        a += ["--" + k.replace("_", "-"), str(v)]
    return a


def run_gen(out_dir, extra, preview=True):
    """gen_stadium.py 실행 → (종료 코드, 표준 출력+오류, 걸린 초)."""
    os.makedirs(out_dir, exist_ok=True)
    cmd = [BLENDER, "--background", "--factory-startup", "--python-exit-code", "1", "--python", GEN, "--",
           "--out", os.path.join(out_dir, "stadium.glb")] + extra + ([] if preview else ["--no-preview"])
    t0 = time.time()
    p = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
    return p.returncode, p.stdout + p.stderr, time.time() - t0


class Gen:
    def __init__(self, out_dir, code, log, seconds):
        self.dir, self.code, self.log, self.seconds = out_dir, code, log, seconds

    def path(self, suffix):
        return os.path.join(self.dir, "stadium" + suffix)

    @property
    def layout(self):
        with open(self.path(".layout.json"), encoding="utf-8") as f:
            return json.load(f)

    def sha(self, suffix):
        with open(self.path(suffix), "rb") as f:
            return hashlib.sha256(f.read()).hexdigest()

    def glb_json(self):
        with open(self.path(".glb"), "rb") as f:
            data = f.read()
        magic, _, _ = struct.unpack_from("<III", data, 0)
        assert magic == 0x46546C67, "glb 헤더 아님"
        ln, kind = struct.unpack_from("<II", data, 12)
        assert kind == 0x4E4F534A
        return json.loads(data[20:20 + ln])


def generated(name, preview=True, **override):
    """인자 세트 하나를 한 번만 생성해 공유한다."""
    key = (name, preview, tuple(sorted(override.items())))
    if key not in _cache:
        args = dict(params.PRESETS[name.split("#")[0]])
        args.update(override)
        out = os.path.join(_OUT_ROOT, name.replace("#", "-") + ("" if preview else "-nopreview"))
        shutil.rmtree(out, ignore_errors=True)
        code, log, sec = run_gen(out, _cli(args), preview)
        if code != 0:
            raise AssertionError(f"생성 실패 ({name}, 종료 {code}):\n{log[-3000:]}")
        _cache[key] = Gen(out, code, log, sec)
    return _cache[key]


def ball_tuning(id_):
    with open(os.path.join(REPO, "Data", "tuning", "ball.json"), encoding="utf-8") as f:
        for row in json.load(f)["definitions"]:
            if row["id"] == id_:
                return row["default"]
    raise KeyError(id_)


SETS = ("min", "default", "max")


class SpecDimensions(unittest.TestCase):
    """AC-01: 피치·골대·라인 규격."""

    def test_lines_match_marking_definition(self):
        lay = generated("default").layout
        ref = markings.pitch_markings()
        self.assertEqual(lay["pitch"]["length"], 105.0)
        self.assertEqual(lay["pitch"]["width"], 68.0)
        self.assertEqual(lay["pitch"]["lineWidth"], 0.12)
        got = {l["name"]: l for l in lay["pitch"]["lines"]}
        for l in ref["lines"]:
            g = got[l["name"]]
            for k in ("a", "b", "c"):
                if k in l:
                    for u, v in zip(l[k], g[k]):
                        self.assertAlmostEqual(u, v, delta=0.001, msg=f"{l['name']}.{k}")
            for k in ("r", "from", "to"):
                if k in l:
                    self.assertAlmostEqual(l[k], g[k], delta=0.001, msg=f"{l['name']}.{k}")

    def test_measured_goal_mesh(self):
        for s in SETS:
            m = generated(s).layout["summary"]["measured"]
            for side, gx in (("Goal_L", -52.5), ("Goal_R", 52.5)):
                g = m[side]
                self.assertAlmostEqual(g["innerWidth"], 7.32, delta=0.005, msg=f"{s} {side} 안쪽 폭")
                self.assertAlmostEqual(g["innerHeight"], 2.44, delta=0.005, msg=f"{s} {side} 안쪽 높이")
                self.assertAlmostEqual(g["postCenterX"], gx, delta=0.005, msg=f"{s} {side} 포스트 중심 = 골라인 중심")
                self.assertAlmostEqual(g["postDiameter"], 0.12, delta=0.005, msg=f"{s} {side} 포스트 지름")

    def test_pitch_plane_covers_runoff(self):
        m = generated("default").layout["summary"]["measured"]["Pitch"]
        self.assertLessEqual(m["min"][0], -52.5 - 5.0 + 0.01)
        self.assertGreaterEqual(m["max"][0], 52.5 + 5.0 - 0.01)
        self.assertLessEqual(m["min"][2], -34.0 - 5.0 + 0.01)
        self.assertGreaterEqual(m["max"][2], 34.0 + 5.0 - 0.01)
        self.assertAlmostEqual(m["max"][1], 0.0, delta=0.001, msg="피치 면 높이 0")

    def test_corner_flags(self):
        flags = generated("default").layout["summary"]["measured"]["CornerFlags"]
        self.assertEqual(len(flags), 4)
        for f in flags:
            self.assertGreaterEqual(f["height"], 1.5)


class SameArgs_SameBytes(unittest.TestCase):
    """AC-03: 같은 인자 2회 → glb·json·미리보기 3장 바이트 동일."""

    def test_two_runs_identical(self):
        a = generated("default")
        b = generated("default#2")
        for suf in [".glb", ".layout.json"] + [f".preview-{p}.png" for p in PREVIEWS]:
            self.assertEqual(a.sha(suf), b.sha(suf), "다름: " + suf)


class SeedChange_ChangesHash_KeepsSpec(unittest.TestCase):
    """AC-04."""

    def test_seed(self):
        a = generated("default")
        b = generated("default#seed", seed=7)
        self.assertNotEqual(a.sha(".glb"), b.sha(".glb"))
        self.assertTrue(b.layout["summary"]["checks"]["spec"]["ok"])


class Budgets_Min_Default_Max(unittest.TestCase):
    """AC-05: 삼각형 ≤ 100k, 머티리얼 ≤ 12, 텍스처 ≤ 2K, 규격 검사 통과."""

    def test_budgets(self):
        for s in SETS:
            g = generated(s)
            summ = g.layout["summary"]
            self.assertLessEqual(summ["triangles"]["total"], 100_000, s)
            self.assertLessEqual(summ["materials"], 12, s)
            self.assertTrue(summ["checks"]["spec"]["ok"], f"{s}: {summ['checks']['spec']}")
            gj = g.glb_json()
            self.assertLessEqual(len(gj.get("materials", [])), 12, s)
            for img in gj.get("images", []):
                self.fail(f"{s}: glb에 텍스처가 들어 있다 ({img.get('name')}) — 텍스처는 Unity 쪽")
            self.assertLessEqual(os.path.getsize(g.path(".glb")), 20 * 1024 * 1024, f"{s}: glb 20 MB 초과")
            self.assertIn("triangles", g.log)

    def test_full_roof_switches_light_towers(self):
        lay = generated("max").layout
        self.assertEqual(lay["lights"]["mode"], "roofline")
        self.assertTrue(lay["lights"]["switchedFromTowers"])
        self.assertTrue(lay["summary"]["notes"], "전환 사실이 요약에 기록돼야 한다")
        self.assertEqual(generated("min").layout["lights"]["mode"], "towers")


class InvalidArgs(unittest.TestCase):
    """AC-06: 범위 밖 인자 → 0이 아닌 종료 코드, 메시지, 출력 파일 없음."""

    def _bad(self, tag, **override):
        args = dict(params.PRESETS["default"])
        args.update(override)
        out = os.path.join(_OUT_ROOT, "invalid-" + tag)
        shutil.rmtree(out, ignore_errors=True)
        code, log, _ = run_gen(out, _cli(args))
        self.assertNotEqual(code, 0, tag)
        self.assertIn("[stadium] 인자 오류", log, tag)
        self.assertEqual([f for f in os.listdir(out)] if os.path.isdir(out) else [], [], f"{tag}: 출력이 남았다")

    def test_tiers_zero(self):
        self._bad("tiers0", tiers=0)

    def test_tiers_four(self):
        self._bad("tiers4", tiers=4)

    def test_negative_capacity(self):
        self._bad("negcap", capacity=-20000)

    def test_bad_color(self):
        self._bad("color", seat_colors="red,#FFFFFF")


class Visibility_And_Runoff_AllSets(unittest.TestCase):
    """AC-07: 메인 카메라 거치 위치에서 피치 네 모서리 가시, 런오프 침범 0."""

    def test_sets(self):
        for s in SETS:
            c = generated(s).layout["summary"]["checks"]
            self.assertEqual(c["visibility"]["blockedCorners"], [], s)
            self.assertEqual(len(c["visibility"]["corners"]), 4, s)
            self.assertEqual(c["runoff"]["intrusions"], [], s)
            cam = generated(s).layout["cameras"]["mainMount"]
            self.assertLess(cam["min"][2], -34.0, f"{s}: 메인 카메라는 메인 스탠드(남쪽, −Z) 쪽")


class LightmapUv_NoOverlap(unittest.TestCase):
    """AC-08: 정적 구조물 전부 UV2 보유, 겹침 0%."""

    def test_uv2(self):
        for s in SETS:
            g = generated(s)
            self.assertEqual(g.layout["summary"]["checks"]["uv2"]["overlapRatio"], 0.0, s)
            self.assertEqual(g.layout["summary"]["checks"]["uv2"]["missing"], [], s)
            for mesh in g.glb_json()["meshes"]:
                for prim in mesh["primitives"]:
                    self.assertIn("TEXCOORD_1", prim["attributes"], f"{s}: {mesh['name']} UV2 없음")


class Seats_CountAndOnMesh(unittest.TestCase):
    """AC-09: 좌석 수 = 수용 인원 등급, 모든 좌석이 관중석 메시 위(±0.05 m)."""

    def test_sets(self):
        for s in SETS:
            lay = generated(s).layout
            rows = lay["seats"]["rows"]
            total = sum(r["count"] for r in rows)
            self.assertEqual(total, params.PRESETS[s]["capacity"], s)
            self.assertEqual(lay["seats"]["count"], total, s)
            self.assertEqual({r["section"] for r in rows}, {"N", "S", "E", "W"}, s)
            self.assertLessEqual(lay["summary"]["checks"]["seats"]["maxDistance"], 0.05, s)
            self.assertEqual(lay["summary"]["checks"]["seats"]["checked"], total, s)


class GenerationTime_Under60s(unittest.TestCase):
    """AC-15: 기본 인자, 미리보기 제외 60초 이내."""

    def test_time(self):
        g = generated("default", preview=False)
        self.assertLessEqual(g.seconds, 60.0, f"{g.seconds:.1f}s")
        self.assertFalse(os.path.exists(g.path(".preview-main.png")))


class GoalColliders_MatchVisual_AndDeterministic(unittest.TestCase):
    """AC-16: 충돌 형상 = 규격 = 시각 메시(±1 cm) = 004 공 물리 파라미터, 2회 동일."""

    def test_colliders(self):
        lay = generated("default").layout
        r = ball_tuning("ball.goal.postRadius")
        depth = ball_tuning("ball.net.depth")
        self.assertEqual(len(lay["goals"]), 2)
        for g in lay["goals"]:
            side = "Goal_L" if g["side"] < 0 else "Goal_R"
            gx = 52.5 * g["side"]
            posts = g["posts"]
            self.assertEqual(len(posts), 2)
            for p in posts:
                self.assertAlmostEqual(p["r"], r, delta=1e-6)
                self.assertAlmostEqual(p["a"][0], gx, delta=0.01)
            spacing = abs(posts[0]["a"][2] - posts[1]["a"][2]) - 2 * r
            self.assertAlmostEqual(spacing, 7.32, delta=0.01, msg="포스트 안쪽 간격")
            bar = g["crossbar"]
            self.assertAlmostEqual(bar["a"][1] - bar["r"], 2.44, delta=0.01, msg="크로스바 아래면 높이")
            self.assertAlmostEqual(abs(g["net"]["max"][0] - g["net"]["min"][0]), depth, delta=0.01, msg="골망 깊이 = 004")
            self.assertGreaterEqual(depth, 1.5)
            meas = lay["summary"]["measured"][side]
            for p, mc in zip(posts, meas["postCentersZ"]):
                self.assertAlmostEqual(p["a"][2], mc, delta=0.01, msg="형상 ↔ 시각 메시")
        b = generated("default#2").layout
        self.assertEqual(json.dumps(lay["goals"], sort_keys=True), json.dumps(b["goals"], sort_keys=True))


class LayoutContents(unittest.TestCase):
    """FR-24·25·26: 이름 규칙, 좌석·카메라·조명 데이터."""

    def test_names(self):
        names = {n.get("name") for n in generated("default").glb_json()["nodes"]}
        for need in ("Pitch", "Goal_L", "Goal_R", "Stand_N_T1", "Stand_S_T1", "Stand_E_T1", "Stand_W_T1",
                     "Stand_S_T2", "Boards", "Ground", "Bench_Home", "Bench_Away", "Tunnel"):
            self.assertIn(need, names)
        self.assertTrue(any(n.startswith("LightTower_") for n in names))
        self.assertTrue(any(n.startswith("Roof_") for n in names))

    def test_generator_record(self):
        gen = generated("default").layout["generator"]
        self.assertEqual(gen["blender"], "5.1.1")
        self.assertEqual(gen["args"], params.PRESETS["default"])

    def test_cameras_and_lights(self):
        lay = generated("default").layout
        self.assertTrue(lay["cameras"]["noGo"])
        self.assertEqual(len(lay["lights"]["emitters"]), 4)


if __name__ == "__main__":
    unittest.main()
