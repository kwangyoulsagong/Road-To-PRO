"""얼굴 데이터 테스트 (specs/056 FR-20~24·AC-04 원본 쪽). gen_player 산출물 player.face.json을 본다.

Unity 쪽 FaceLandmarks(C#)가 같은 파일로 팀 22명 최소 쌍 거리를 계산한다(AC-04). 여기서는 형식·개수·결정론만.
"""
import unittest

import _path  # noqa: F401
from test_player import generated, HAIR  # noqa: F401


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

    def test_beards_4(self):
        self.assertEqual([b["id"] for b in self.face()["beards"]], ["none", "stubble", "short", "full"])

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


if __name__ == "__main__":
    unittest.main()
