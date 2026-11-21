"""얼굴 데이터 테스트 (specs/056 FR-20~24·AC-04 원본 쪽). gen_player 산출물 player.face.json을 본다.

Unity 쪽 FaceLandmarks(C#)가 같은 파일로 팀 22명 최소 쌍 거리를 계산한다(AC-04). 여기서는 형식·개수·결정론만.
"""
import unittest

import _path  # noqa: F401
from test_player import generated, HAIR  # noqa: F401

# ---------------------------------------------------------------- specs/062 얼굴 · 수염 · 표정 (T26, AC-07·08)
# T27 생성기가 채우는 약속:
#   player.face.json
#     baseHeads   : [{"id": "Base_n", "region": 지역}] — 랜드마크 deltas에 Base_n 키가 들어간다(Unity 바탕 머리 혼합, FR-61)
#     beards      : 7종 (BEARDS_062 순서)
#     expressions : [{"id": "Expr_*"}] 4종 (무표정 = 키 0)
#   summary["face062"]
#     baseHeads   : {"count", "regions": [...], "landmarkMinPair", "landmark056Min", "silhouetteMinPair"}
#                   landmarkMinPair = 바탕 머리끼리 랜드마크 거리 벡터 RMS 차(FaceLandmarks.Distance와 같은 식)의 최솟값,
#                   landmark056Min  = 056 AC-04 기준 = 무작위 얼굴 400명(30축, 축마다 두 균등 합 × 범위 — FaceGenerator와 같은 분포)
#                                     평균 쌍 거리 × 0.25 (FaceLandmarks.MinFraction),
#                   silhouetteMinPair = 바탕 머리 쌍별 정면·옆 머리 실루엣 1 − IoU(두 방향 중 큰 값)의 최솟값
#     beards      : {종류: {"coreHf", "lowNeck", "mouthCorner", "cheek"}} — 057 FR-24 지표(skin.beard_metrics)를 종류별 마스크로
#     expressions : {"lod": {"0": [...], "1": [...], "2": [...]}, "closeup": [...],
#                    "penetration": {키: 입술·눈꺼풀·치아·안구 교차 면 쌍 수},
#                    "mouthInterior": {키: 입 벌린 틈 중 입 안(치아·혀·입안 캡)이 보이는 비율}}
BEARDS_062 = ("none", "stubble", "short", "full", "mustache", "goatee", "heavy_stubble")
FULL_BEARDS = ("stubble", "short", "full", "heavy_stubble")      # 턱·볼·입 둘레 — 입꼬리 기준 적용
LOCAL_BEARDS = ("mustache", "goatee")                            # 콧수염·염소수염 — 볼 밀도 ≤ 0.01 (FR-66)
EXPRESSIONS_062 = ("Expr_cheer", "Expr_smile", "Expr_disappoint", "Expr_focus")   # FR-70: + 무표정(기본)
REGIONS_062 = ("east_asia", "southeast_asia", "south_asia", "west_asia_north_africa", "west_central_africa", "europe", "latin_america")


class FaceJson(unittest.TestCase):
    def face(self):
        return generated("default").json("player.face.json")

    def test_schema(self):
        f = self.face()
        self.assertEqual(f["schemaVersion"], 1)
        for k in ("targets", "beards", "landmarks"):
            self.assertIn(k, f)

    def test_targets_at_least_20(self):
        t = self.face()["targets"]
        self.assertGreaterEqual(len(t), 20)
        for x in t:
            self.assertTrue(x["id"].startswith("Face_"), x)
            self.assertLess(x["min"], x["max"], x)
            self.assertTrue(-1.0 <= x["min"] and x["max"] <= 1.0, x)

    def test_beards_order(self):
        # 056 4종 순서 유지 + 062 FR-66 신규 3종 뒤에 (Unity FaceShape.Beard 인덱스 호환)
        self.assertEqual([b["id"] for b in self.face()["beards"]], list(BEARDS_062))

    def test_landmarks(self):
        f = self.face()
        lm = f["landmarks"]
        self.assertGreaterEqual(len(lm), 24)
        names = {x["name"] for x in lm}
        for need in ("browRidgeL", "browRidgeR", "cheekboneL", "cheekboneR", "noseBridge", "noseTip", "alarL", "alarR",
                     "philtrum", "upperLip", "lowerLip", "chin", "jawL", "jawR", "earTopL", "earTopR"):
            self.assertIn(need, names)
        ids = {t["id"] for t in f["targets"]}
        for x in lm:
            self.assertEqual(len(x["base"]), 3)
            for k, d in x["deltas"].items():
                self.assertIn(k, ids)
                self.assertEqual(len(d), 3)

    def test_eyes_covered_by_lids(self):
        e = generated("default").layout["summary"]["eyes"]
        self.assertTrue(e["ok"], e)
        self.assertEqual(e["exposedOutsideLids"], 0)


class Face062(unittest.TestCase):
    """062 AC-07·08: 바탕 머리·지역 얼굴형, 수염 7종, 표정 4종(+ 무표정)."""

    def s(self):
        return generated("default").layout["summary"]["face062"]

    def test_base_heads(self):
        """FR-60~62·64: 바탕 머리 6~10개(8), 지역 7개 전부, 랜드마크 최소 쌍 ≥ 056 기준 × 2, 실루엣 차이 ≥ 0.05."""
        f = generated("default").json("player.face.json")
        bh = f["baseHeads"]
        self.assertTrue(6 <= len(bh) <= 10, bh)
        self.assertEqual([b["id"] for b in bh], [f"Base_{i}" for i in range(len(bh))])
        self.assertTrue(set(REGIONS_062) <= {b["region"] for b in bh}, bh)
        ids = {b["id"] for b in bh}
        for lm in f["landmarks"]:
            self.assertTrue(ids & set(lm["deltas"]), f"{lm['name']} 바탕 머리 델타 없음")
        s = self.s()["baseHeads"]
        self.assertEqual(s["count"], len(bh))
        self.assertGreater(s["landmark056Min"], 0.0)
        self.assertGreaterEqual(s["landmarkMinPair"], 2.0 * s["landmark056Min"], s)
        self.assertGreaterEqual(s["silhouetteMinPair"], 0.05, s)

    def test_beards_seven(self):
        """FR-66: 수염 7종, 057 FR-24 기준(점 노이즈 ≤ 0.05·울대 아래 ≤ 0.01·입 둘레 종류 입꼬리 ≥ 0.3), 콧수염·염소수염 볼 ≤ 0.01."""
        b = self.s()["beards"]
        self.assertEqual(sorted(b), sorted(BEARDS_062[1:]))
        for k, m in b.items():
            self.assertLessEqual(m["coreHf"], 0.05, f"{k} {m}")
            self.assertLessEqual(m["lowNeck"], 0.01, f"{k} {m}")
            if k in FULL_BEARDS:
                self.assertGreaterEqual(m["mouthCorner"], 0.3, f"{k} {m}")
            if k in LOCAL_BEARDS:
                self.assertLessEqual(m["cheek"], 0.01, f"{k} {m}")

    def test_expressions(self):
        """FR-70~72: 표정 4종(+ 무표정)이 LOD0·클로즈업 머리에 있고 LOD1·LOD2엔 없다, 최대치에서 입술·눈꺼풀·치아·안구 관통 0,
        입 벌린 표정(환호)에서 입 안이 보인다(틈의 ≥ 90 %)."""
        f = generated("default").json("player.face.json")
        self.assertEqual([e["id"] for e in f["expressions"]], list(EXPRESSIONS_062))
        e = self.s()["expressions"]
        self.assertEqual(sorted(e["lod"]["0"]), sorted(EXPRESSIONS_062))
        self.assertEqual(sorted(e["closeup"]), sorted(EXPRESSIONS_062))
        self.assertEqual(e["lod"]["1"], [])
        self.assertEqual(e["lod"]["2"], [])
        for k in EXPRESSIONS_062:
            self.assertEqual(e["penetration"][k], 0, f"{k} {e['penetration']}")
        self.assertGreaterEqual(e["mouthInterior"]["Expr_cheer"], 0.9, e["mouthInterior"])


if __name__ == "__main__":
    unittest.main()
