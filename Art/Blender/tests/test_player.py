"""선수 생성기 통합 테스트 (specs/043 AC-02·03·09, FR-31·32·45). 백그라운드 Blender를 실제로 돌린다.

실행: `python3 Tools/devicefarm/farm.py art --spec 043` (리포트) 또는 `python3 -m unittest test_player` (Art/Blender/tests에서).
산출물은 FARM_ART_OUT 폴더(없으면 임시 폴더)의 `<세트>/player.*`로 남는다 — 리포트 미리보기 격자 입력.
"""
import hashlib
import json
import os
import shutil
import struct
import subprocess
import tempfile
import time
import unittest

import _path  # noqa: F401

BLENDER = os.environ.get("BLENDER", "/Applications/Blender.app/Contents/MacOS/Blender")
GEN = os.path.join(_path.SCRIPTS, "gen_player.py")
_OUT_ROOT = os.environ.get("FARM_ART_OUT") or tempfile.mkdtemp(prefix="player-test-")
_cache = {}


def run_gen(out_dir, extra, preview=True):
    cmd = [BLENDER, "--background", "--factory-startup", "--python-exit-code", "1", "--python", GEN, "--",
           "--out", out_dir] + extra + ([] if preview else ["--no-preview"])
    t0 = time.time()
    p = subprocess.run(cmd, capture_output=True, text=True, timeout=900)
    return p.returncode, p.stdout + p.stderr, time.time() - t0


class Gen:
    def __init__(self, out_dir, log, seconds):
        self.dir, self.log, self.seconds = out_dir, log, seconds

    def path(self, name):
        return os.path.join(self.dir, name)

    def json(self, name):
        with open(self.path(name), encoding="utf-8") as f:
            return json.load(f)

    @property
    def layout(self):
        return self.json("player.layout.json")

    @property
    def clips(self):
        return self.json("player.clips.json")["clips"]

    def sha(self, name):
        with open(self.path(name), "rb") as f:
            return hashlib.sha256(f.read()).hexdigest()

    def files(self):
        out = []
        for root, _, fs in os.walk(self.dir):
            out += [os.path.relpath(os.path.join(root, f), self.dir) for f in fs]
        return sorted(out)

    def glb_json(self):
        with open(self.path("player.glb"), "rb") as f:
            data = f.read()
        magic = struct.unpack_from("<I", data, 0)[0]
        assert magic == 0x46546C67, "glb 헤더 아님"
        ln, kind = struct.unpack_from("<II", data, 12)
        assert kind == 0x4E4F534A
        return json.loads(data[20:20 + ln])


def generated(name, preview=True, extra=()):
    key = (name, preview, tuple(extra))
    if key not in _cache:
        out = os.path.join(_OUT_ROOT, name + ("" if preview else "-nopreview"))
        shutil.rmtree(out, ignore_errors=True)
        code, log, sec = run_gen(out, list(extra), preview)
        if code != 0:
            raise AssertionError(f"생성 실패 ({name}, 종료 {code}):\n{log[-3000:]}")
        _cache[key] = Gen(out, log, sec)
    return _cache[key]


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24])


class Budgets_Lod_Materials_Textures(unittest.TestCase):
    """AC-03: LOD0 ≤ 10k(≥ 6k), LOD1 ≤ 3k(≥ 2k), LOD2 ≤ 1k tris, 머티리얼 ≤ 3, 텍스처 ≤ 2K."""

    def test_layout_summary(self):
        b = generated("default").layout["summary"]["budgets"]
        self.assertTrue(b["ok"], b)
        self.assertLessEqual(b["lods"]["0"]["triangles"], 10_000)
        self.assertGreaterEqual(b["lods"]["0"]["triangles"], 6_000)
        self.assertLessEqual(b["lods"]["1"]["triangles"], 3_000)
        self.assertGreaterEqual(b["lods"]["1"]["triangles"], 2_000)
        self.assertLessEqual(b["lods"]["2"]["triangles"], 1_000)

    def test_glb(self):
        gj = generated("default").glb_json()
        self.assertLessEqual(len(gj.get("materials", [])), 3)
        self.assertEqual(gj.get("images", []), [], "텍스처는 glb에 넣지 않는다(Unity 쪽 PNG)")
        names = sorted(m["name"] for m in gj["meshes"])
        self.assertEqual(names, ["Player_LOD0", "Player_LOD1", "Player_LOD2"])
        self.assertEqual(len(gj.get("skins", [])), 1, "LOD 3개가 뼈대 하나를 공유")
        for m in gj["meshes"]:
            for prim in m["primitives"]:
                self.assertEqual(len(prim.get("targets", [])), 12, f"{m['name']} 셰이프키 12개(체형 3 + 헤어 9)")
            self.assertTrue(all(w == 0 for w in m.get("weights", [0])), f"{m['name']} 셰이프키 기본 가중치 0")

    def test_textures(self):
        g = generated("default")
        tex = [f for f in g.files() if f.startswith("Textures/")]
        self.assertGreaterEqual(len(tex), 8)
        for f in tex:
            w, h = png_size(g.path(f))
            self.assertLessEqual(max(w, h), 2048, f)


class ExtremeBodies_FootGround_NoSelfIntersect(unittest.TestCase):
    """AC-02: 키 최소·최대 × 체중 최소·최대 4조합에서 발 접지 오차 ≤ 2 cm, 부위 간 관통 0."""

    def test_extremes(self):
        e = generated("default").layout["summary"]["extremes"]
        body_cases = sorted(k for k in e["cases"] if not k.startswith("hair_"))
        self.assertEqual(body_cases, ["short_heavy", "short_light", "tall_heavy", "tall_light"])
        self.assertEqual(len([k for k in e["cases"] if k.startswith("hair_")]), 9, "헤어 9종 관통 검사")
        for name, c in e["cases"].items():
            self.assertLessEqual(abs(c["footMin"]), 0.02, name)
            self.assertEqual(c["intersections"], {}, name)
        self.assertTrue(e["ok"])


class Gait_StanceFootFixed(unittest.TestCase):
    """AC-09(원본 쪽): 로코모션 클립 접지 구간 발 이동 ≤ 5 mm (시뮬 속도 = 클립 speed일 때)."""

    def test_slide(self):
        s = generated("default").layout["summary"]["gaitSlide"]
        self.assertGreaterEqual(s["clips"], 25)
        self.assertLessEqual(s["worst"], 0.005, s)


class ClipList_CoversFr31(unittest.TestCase):
    """FR-31: 프로토타입 클립 약 80개, 분류 전부(로코모션 8방향, 넘어짐 8방향 포함)."""

    def test_clip_length_matches_tags(self):
        """glb 애니메이션 길이 = 태그 frames / 30 (앞에 정지 프레임이 붙지 않는다)."""
        g = generated("default")
        gj = g.glb_json()
        acc = gj["accessors"]
        tags = {t["id"]: t for t in g.clips}
        for a in gj["animations"]:
            inp = acc[a["samplers"][0]["input"]]
            self.assertAlmostEqual(inp["max"][0] - inp["min"][0], tags[a["name"]]["frames"] / 30.0, delta=1e-4, msg=a["name"])
            self.assertAlmostEqual(inp["min"][0], 0.0, delta=1e-6, msg=a["name"])

    def test_list(self):
        g = generated("default")
        c = g.layout["summary"]["clips"]
        self.assertTrue(c["ok"], c)
        self.assertGreaterEqual(len(g.clips), 75)
        anims = sorted(a["name"] for a in g.glb_json()["animations"])
        self.assertEqual(anims, sorted(t["id"] for t in g.clips), "glb 애니메이션 = 클립 목록")


class ClipTags_Complete(unittest.TestCase):
    """FR-32·45: 클립 태그(접촉 프레임·부위·접근 각도·공 높이·속도·루트 이동), 접촉점이 공 높이 범위 안, 지면 관통 0."""

    def test_tags(self):
        g = generated("default")
        for t in g.clips:
            for k in ("id", "group", "frames", "loop"):
                self.assertIn(k, t, t.get("id"))
            if t["group"] == "locomotion" and t["loop"]:
                self.assertIn("speed", t, t["id"])
                self.assertIn("stance", t, t["id"])
            c = t.get("contact")
            if c:
                for k in ("approach", "ballHeight", "ballSpeed", "root"):
                    self.assertIn(k, t, t["id"])
                self.assertIn(c["part"], ("LeftFoot", "RightFoot", "Head", "Chest", "Thigh", "Hand"), t["id"])
                self.assertTrue(0 <= c["frame"] <= t["frames"], t["id"])
                lo, hi = t["ballHeight"]
                self.assertTrue(lo - 1e-6 <= c["point"][1] <= hi + 1e-6, f"{t['id']} 접촉 높이 {c['point'][1]}")
                for k in ("bone", "toward", "t", "up", "fwd", "side", "basis"):
                    self.assertIn(k, c["rule"], t["id"])
        self.assertTrue(g.layout["summary"]["ground"]["ok"], g.layout["summary"]["ground"])

    def test_ground_ball_contacts_at_ball_center(self):
        for t in generated("default").clips:
            c = t.get("contact")
            if c and t["ballHeight"] == [0.0, 0.3] and c["part"].endswith("Foot"):
                self.assertAlmostEqual(c["point"][1], 0.11, delta=0.012, msg=t["id"])


class SameArgs_SameBytes(unittest.TestCase):
    """결정론: 같은 인자 2회 → glb·JSON·텍스처·미리보기 바이트 동일."""

    def test_two_runs(self):
        a = generated("default")
        b = generated("default2")
        self.assertEqual(a.files(), b.files())
        for f in a.files():
            self.assertEqual(a.sha(f), b.sha(f), "다름: " + f)


class SeedChange_ChangesTextures_KeepsMesh(unittest.TestCase):
    """시드는 텍스처 노이즈만 바꾼다(메시·클립 동일)."""

    def test_seed(self):
        a = generated("default", preview=False)
        b = generated("seed7", preview=False, extra=("--seed", "7"))
        self.assertNotEqual(a.sha("Textures/body.png"), b.sha("Textures/body.png"))
        self.assertEqual(a.sha("player.glb"), b.sha("player.glb"))
        self.assertEqual(a.sha("player.clips.json"), b.sha("player.clips.json"))


class InvalidArgs(unittest.TestCase):
    """인자 오류 → 종료 코드 2, 메시지, 출력 없음."""

    def _bad(self, tag, extra):
        out = os.path.join(_OUT_ROOT, "invalid-" + tag)
        shutil.rmtree(out, ignore_errors=True)
        code, log, _ = run_gen(out, extra, preview=False)
        self.assertEqual(code, 2, f"{tag}: {log[-500:]}")
        self.assertIn("[player] 인자 오류", log, tag)
        self.assertFalse(os.path.exists(out) and os.listdir(out), f"{tag}: 출력이 남았다")

    def test_bad_lod(self):
        self._bad("lod", ["--lods", "0,3"])

    def test_bad_seed(self):
        self._bad("seed", ["--seed", "-1"])

    def test_unknown_clip(self):
        self._bad("clip", ["--clips", "moonwalk"])


class GenerationTime(unittest.TestCase):
    """생성 시간: 미리보기 제외 60초 이내."""

    def test_time(self):
        g = generated("default", preview=False)
        self.assertLessEqual(g.seconds, 60.0, f"{g.seconds:.1f}s")
