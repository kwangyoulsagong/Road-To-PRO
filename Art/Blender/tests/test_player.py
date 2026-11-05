"""선수 생성기 통합 테스트 (specs/043 AC-02·03·09, FR-31·32·45 + specs/056 AC-01·02·03·11). 백그라운드 Blender를 실제로 돌린다.
MPFB2 확장(2.0.18)이 설치되어 있어야 한다 (specs/056 FR-03).

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


def run_gen(out_dir, extra, preview=True, env=None):
    cmd = [BLENDER, "--background", "--factory-startup", "--python-exit-code", "1", "--python", GEN, "--",
           "--out", out_dir] + extra + ([] if preview else ["--no-preview"])
    t0 = time.time()
    p = subprocess.run(cmd, capture_output=True, text=True, timeout=1800, env=env)
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


# specs/057 FR-30: 30종. 0~9는 056 순서 그대로(기준 이미지·기존 시드 해석), 삭발(9)은 형상 없음.
HAIR = ("Hair_Buzz", "Hair_Short", "Hair_Long", "Hair_Curly", "Hair_Bun", "Hair_Fade", "Hair_Afro", "Hair_Mohawk", "Hair_Dreads",
        "Hair_Bald",
        "Hair_Crew", "Hair_SidePart", "Hair_Undercut", "Hair_SlickBack", "Hair_Quiff", "Hair_Curtains", "Hair_TexturedCrop",
        "Hair_BuzzLine", "Hair_HighTop", "Hair_Cornrows", "Hair_Twists", "Hair_TopKnot", "Hair_Ponytail", "Hair_MediumWave",
        "Hair_FrenchCrop", "Hair_IvyLeague", "Hair_Spiky", "Hair_CurlyTop", "Hair_BoxBraids", "Hair_HalfUp")
MESH_HAIR = tuple(h for h in HAIR if h != "Hair_Bald")
FAMILY = {"short": {0, 1, 5, 9, 10, 11, 12, 14, 16, 17, 19, 24, 25, 26, 27}, "medium": {3, 6, 7, 13, 15, 18, 20, 23},
          "long": {2, 4, 8, 21, 22, 28, 29}}
# 056 기준 (specs/057 plan '구현 중 바뀐 점' T01): 클로즈업 피부 얼굴 노멀 고주파 RMS, 부위별 거칠기 평균
SKIN_056_NORMAL_HF = 0.070367
SKIN_056_ROUGH = {"nose": 0.591938, "cheek": 0.67973, "forehead": 0.566366, "eyeArea": 0.678763, "lips": 0.521222}


class Budgets_Lod_Materials_Textures(unittest.TestCase):
    """043 AC-03 + 056 AC-02·FR-60·61: LOD0 6k~10k(머리 ≥ 2.0k), LOD1 2k~3k, LOD2 ≤ 1k tris (가장 무거운 헤어 포함),
    클로즈업 머리 ≤ 6k, 머티리얼 ≤ 3, 텍스처 ≤ 2K."""

    def test_layout_summary(self):
        b = generated("default").layout["summary"]["budgets"]
        self.assertTrue(b["ok"], b)
        self.assertLessEqual(b["lods"]["0"]["triangles"], 10_000)
        self.assertGreaterEqual(b["lods"]["0"]["triangles"], 6_000)
        self.assertGreaterEqual(b["lods"]["0"]["headTriangles"], 2_000)
        self.assertLessEqual(b["lods"]["1"]["triangles"], 3_000)
        self.assertGreaterEqual(b["lods"]["1"]["triangles"], 2_000)
        self.assertLessEqual(b["lods"]["2"]["triangles"], 1_000)
        self.assertLessEqual(b["headCloseup"]["triangles"], 6_000)

    def test_hair_styles_budget_and_cards(self):
        """057 AC-03·FR-10·11·40: 스타일마다 몸 + 헤어 LOD0 6k~10k, LOD1 2k~3k, LOD2 ≤ 1k, 클로즈업 머리 메시 ≤ 6k(056 기준).
        카드는 LOD0(클로즈업 머리는 LOD0 헤어 조각을 쓴다)에만, 삭발 제외 전부. LOD1·LOD2엔 없다. 헤어 LOD0 ≤ 1,350 (plan T01)."""
        g = generated("default")
        b = g.layout["summary"]["budgets"]
        hs = b["hair"]
        self.assertEqual(sorted(hs), sorted(MESH_HAIR))
        for st, h in hs.items():
            self.assertLessEqual(h["tris"]["0"], 1_350, st)
            self.assertGreater(h["cards"]["0"], 0, f"{st} LOD0 카드 없음")
            self.assertLessEqual(h["cards"]["0"], 400, st)
            self.assertEqual(h["cards"]["1"], 0, st)
            self.assertEqual(h["cards"]["2"], 0, st)
            self.assertGreater(h["cards"]["closeup"], 0, st)
            self.assertLessEqual(h["closeup"], 6_000, st)
            for lod, (lo, hi) in {"0": (6_000, 10_000), "1": (2_000, 3_000), "2": (0, 1_000)}.items():
                t = b["lods"][lod]["body"] + h["tris"][lod]
                self.assertTrue(lo <= t <= hi, f"{st} LOD{lod} {t}")

    def test_hair_style_order_and_family(self):
        """057 FR-30·31: 30종 순서(0~9 = 056), 계열 표."""
        hl = generated("default").layout["hairStyles"]
        self.assertEqual([h["name"] for h in hl], list(HAIR))
        for fam, idx in FAMILY.items():
            for i in idx:
                self.assertEqual(hl[i]["family"], fam, hl[i]["name"])
        self.assertIsNone(hl[9]["meshes"])
        for i, h in enumerate(hl):
            if i != 9:
                self.assertEqual(h["meshes"], [f"{h['name']}_LOD{l}" for l in (0, 1, 2)])

    def test_glb(self):
        gj = generated("default").glb_json()
        self.assertLessEqual(len(gj.get("materials", [])), 3)
        self.assertEqual(gj.get("images", []), [], "텍스처는 glb에 넣지 않는다(Unity 쪽 PNG)")
        names = sorted(m["name"] for m in gj["meshes"])
        want = ["Head_Closeup", "Player_LOD0", "Player_LOD1", "Player_LOD2"]
        want += [f"{h}_LOD{l}" for h in MESH_HAIR for l in (0, 1, 2)]  # 삭발은 형상 없음
        self.assertEqual(names, sorted(want))
        self.assertEqual(len(gj.get("skins", [])), 1, "모든 메시가 뼈대 하나를 공유")
        for m in gj["meshes"]:
            keys = m.get("extras", {}).get("targetNames", [])
            if m["name"].startswith("Player_"):
                for k in ("Thin", "Muscular", "Heavy", "HideHead"):
                    self.assertIn(k, keys, m["name"])
                self.assertGreaterEqual(len([k for k in keys if k.startswith("Face_")]), 20, m["name"])
                self.assertGreaterEqual(len([k for k in keys if k.startswith("Beard_")]), 3, m["name"])
            self.assertTrue(all(w == 0 for w in m.get("weights", [0])), f"{m['name']} 셰이프키 기본 가중치 0")

    def test_textures(self):
        g = generated("default")
        tex = [f for f in g.files() if f.startswith("Textures/")]
        self.assertGreaterEqual(len(tex), 10)
        for f in tex:
            w, h = png_size(g.path(f))
            self.assertLessEqual(max(w, h), 2048, f)
        for f in ("Textures/skin.png", "Textures/skin_normal.png", "Textures/parts.png", "Textures/skin_closeup.png"):
            self.assertIn(f, tex)
        self.assertEqual(png_size(g.path("Textures/skin.png")), (1024, 1024))
        self.assertEqual(png_size(g.path("Textures/skin_closeup.png")), (2048, 2048))


class Mpfb_Reference_Licenses(unittest.TestCase):
    """056 AC-01·FR-01~03·10~12: MPFB 고정 버전, 쓴 파일 = assets.json(sha256) ⊆ CC0, 기준 키 1.80 m, 비율 200명."""

    def test_reference_height_180(self):
        r = generated("default").layout["summary"]["reference"]
        self.assertAlmostEqual(r["heightM"], 1.80, delta=0.005)

    def test_assets_all_cc0_in_licenses(self):
        g = generated("default")
        lic = g.layout["summary"]["licenses"]
        self.assertTrue(lic["ok"], lic)
        mp = g.layout["generator"]["mpfb"]
        self.assertEqual(mp["version"], "2.0.18")
        with open(os.path.join(_path.REPO, "Art", "ThirdParty", "MPFB2", "assets.json"), encoding="utf-8") as f:
            pinned = {a["path"]: a["sha256"] for a in json.load(f)["files"]}
        self.assertTrue(mp["files"], "쓴 MPFB 파일 목록이 비었다")
        for a in mp["files"]:
            self.assertEqual(pinned.get(a["path"]), a["sha256"], a["path"])
        with open(os.path.join(_path.REPO, "Art", "LICENSES.csv"), encoding="utf-8") as f:
            rows = [r for r in f.read().splitlines() if r.startswith("MPFB2")]
        self.assertTrue(rows and all("CC0" in r for r in rows), rows)

    def test_missing_mpfb_exit4(self):
        out = os.path.join(_OUT_ROOT, "no-mpfb")
        shutil.rmtree(out, ignore_errors=True)
        env = dict(os.environ, PLAYER_MPFB_MODULE="bl_ext.nowhere.mpfb_missing")
        code, log, _ = run_gen(out, [], preview=False, env=env)
        self.assertEqual(code, 4, log[-800:])
        self.assertIn("extension install mpfb", log)
        self.assertFalse(os.path.exists(out) and os.listdir(out))

    def test_proportions_200_samples(self):
        p = generated("default").layout["summary"]["proportions"]
        self.assertEqual(p["samples"], 200)
        self.assertLessEqual(p["heightErrCmMax"], 1.0)
        self.assertGreaterEqual(p["headRatioMin"], 1 / 8.0 - 1e-4)
        self.assertLessEqual(p["headRatioMax"], 1 / 7.5 + 1e-4)
        self.assertTrue(p["ok"])


class Penetration_ClipsExtremes(unittest.TestCase):
    """056 AC-03: 클립 82 × 극단 체형 4 × 클립당 4프레임 — 피부×유니폼, 머리카락×몸, 뒤집힌 삼각형 0."""

    def test_no_penetration_extremes(self):
        p = generated("default").layout["summary"]["penetration"]
        self.assertGreaterEqual(p["poses"], 82 * 4 * 4)
        self.assertEqual(p["skinKit"], 0, p)
        self.assertEqual(p["hairBody"], 0, p)
        self.assertEqual(p["flipped"], 0, p)
        self.assertTrue(p["ok"])

    def test_all_hair_styles_with_cards(self):
        """057 AC-02·FR-13·33: 헤어 29조각(삭발 제외, 셸 + 카드) 전부 클립 82 × 4프레임에서 머리카락×몸·유니폼 0."""
        p = generated("default").layout["summary"]["penetration"]
        self.assertEqual(p["hairStyles"], len(MESH_HAIR), p)
        self.assertGreaterEqual(p["hairPoses"], len(MESH_HAIR) * 82 * 4)
        self.assertEqual(p["hairBody"], 0, p)


class KitBoundary_Smooth(unittest.TestCase):
    """057 AC-01·FR-01·02: LOD0 유니폼 경계 고리(칼라·소매 2·밑단·반바지 2·양말 2·축구화 2)가 닫힌 고리 하나씩이고
    평면 투영 이웃 꺾임 각 칼라 ≤ 20° · 나머지 ≤ 40°, 계단 0, 평면 이탈 ≤ 3 mm. LOD1은 닫힌 고리·이탈만 (plan T01·T04)."""

    def test_lod0_loops(self):
        kb = generated("default").layout["summary"]["kitBoundary"]
        loops = kb["0"]["loops"]
        base = sorted(k.split("#")[0] for k in loops)
        self.assertEqual(base.count("collar"), 1, loops)
        self.assertEqual(base.count("hem"), 1, loops)
        self.assertEqual(len(loops), 10, loops)
        for name, r in loops.items():
            self.assertTrue(r["closed"], name)
            self.assertLessEqual(r["turnMax"], 20.0 if name == "collar" else 40.0, f"{name} {r}")
            self.assertEqual(r["zigzag"], 0, f"{name} {r}")
            self.assertLessEqual(r["devMax"], 0.003, f"{name} {r}")
        self.assertTrue(kb["0"]["ok"])

    def test_lod1_closed(self):
        loops = generated("default").layout["summary"]["kitBoundary"]["1"]["loops"]
        self.assertEqual(len(loops), 10, loops)
        for name, r in loops.items():
            self.assertTrue(r["closed"], name)
            self.assertLessEqual(r["devMax"], 0.003, f"{name} {r}")

    def test_kit_rim_uv_region(self):
        """057 FR-04: 칼라 포인트색 띠(셔츠 v ≥ 0.96)는 칼라 경계를 따라가고(칼라 턱 전부 띠 안) 경계 아래로 번지지 않는다."""
        r = generated("default").layout["summary"]["kitRimUv"]
        self.assertGreater(r["collarRimCorners"], 0, r)
        self.assertEqual(r["missing"], 0, r)
        self.assertEqual(r["bleed"], 0, r)
        self.assertEqual(r["rimWelded"], 0, "턱 면 정점이 셸 정점과 같은 위치 → 굽기 법선 섞임(톱니 음영)")
        self.assertTrue(r["ok"])


class SkinDetail_Closeup(unittest.TestCase):
    """057 AC-05·FR-20·21·23: 클로즈업 피부 고주파(노멀)가 056의 절반 이하이면서 0이 아니고, 코·볼 > 이마·눈가,
    거칠기(정반사) 평균은 056 ± 0.02 (플라스틱 피부 금지 유지)."""

    def test_detail(self):
        s = generated("default").layout["summary"]["skinDetail"]
        face = s["face"]["normalHf"]
        self.assertLessEqual(face, SKIN_056_NORMAL_HF * 0.5, s)
        self.assertGreater(face, 0.002, s)
        self.assertGreater(min(s["nose"]["normalHf"], s["cheek"]["normalHf"]),
                           max(s["forehead"]["normalHf"], s["eyeArea"]["normalHf"], s["lips"]["normalHf"]), s)
        for k, v in SKIN_056_ROUGH.items():
            self.assertAlmostEqual(s[k]["rough"], v, delta=0.02, msg=k)

    def test_beard(self):
        """057 FR-24: 수염 밀도 점 노이즈 ≤ 0.05(056 0.164), 울대 아래 목 ≤ 0.01, 입꼬리 ≥ 0.3(056 0 — 입 둘레 타원 구멍)."""
        b = generated("default").layout["summary"]["skinDetail"]["beard"]
        self.assertLessEqual(b["coreHf"], 0.05, b)
        self.assertLessEqual(b["lowNeck"], 0.01, b)
        self.assertGreaterEqual(b["mouthCorner"], 0.3, b)


class KitUv_SameAs043(unittest.TestCase):
    """056 FR-51: 유니폼 UV 영역·번호/이름 영역이 043과 같다 (Unity PlayerLook 상수 불변)."""

    def test_kit_uv_areas_same_as_043(self):
        uv = generated("default").layout["uv"]
        self.assertEqual(uv["text"], {"backName": [0.60, 0.865, 0.90, 0.925], "backNumber": [0.63, 0.60, 0.87, 0.84],
                                      "frontNumber": [0.29, 0.80, 0.37, 0.88]})
        self.assertEqual(uv["kit"]["shirtBack"], [0.50, 0.50, 1.00, 1.00])


class ExtremeBodies_FootGround_NoSelfIntersect(unittest.TestCase):
    """AC-02: 키 최소·최대 × 체중 최소·최대 4조합에서 발 접지 오차 ≤ 2 cm, 부위 간 관통 0."""

    def test_extremes(self):
        e = generated("default").layout["summary"]["extremes"]
        body_cases = sorted(k for k in e["cases"] if not k.startswith("hair_"))
        self.assertEqual(body_cases, ["short_heavy", "short_light", "tall_heavy", "tall_light"])
        self.assertEqual(len([k for k in e["cases"] if k.startswith("hair_")]), 30, "헤어 30종 관통 검사")
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
        self.assertNotEqual(a.sha("Textures/skin.png"), b.sha("Textures/skin.png"))
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
    """생성 시간: 미리보기 제외 300초 이내 (056: 감량·묶음·관통 검사 1,312포즈 포함, 057: 헤어 30종 · 헤어 관통 29 × 328포즈, FR-42 ≤ 180 s 목표는 기록)."""

    def test_time(self):
        g = generated("default", preview=False)
        self.assertLessEqual(g.seconds, 180.0, f"{g.seconds:.1f}s")  # 057 FR-42
