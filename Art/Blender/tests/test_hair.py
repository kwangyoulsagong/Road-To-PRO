"""062 AC-05·06 헤어 재제작 (Blender 몫). 생성 결과는 test_player와 같은 캐시(generated)를 쓴다 — 한 번에 돌리면 생성 1회.

summary["hair062"] 약속 (T22 생성기가 채운다):
  styles      : 35개 이름 (0~29 = 057 순서 그대로, 30~34 신규)
  shape[s]    : {"volume": 셸 볼륨 비율(부속 형상 제외), "side": 옆 폭 비율}  — visual_metrics.hair_metrics 정의
  silhouette  : {"minPair": 1 − IoU 최솟값(삭발 제외, 정면·옆·뒤 최대), "pairsBelow": [[a, b, d], ...]}
                — 마스크 = 보이는 머리카락 영역(FR-44 개정 2026-11-21), 무늬 변형(버즈 / 버즈+라인) 쌍 제외 (hair.VARIANT_PAIRS)
  fade[s]     : 옆머리 세로 띠 5칸 밀도(아래 → 위)
  strandHf    : 클로즈업 머리카락 결 방향 고주파 에너지 (visual_metrics.hair_texture 정의)
  colorPoint[s]: {"a": 뿌리·옆 색 영역 비율, "b": 위·끝 색 영역 비율, "mixed": 두 색이 섞인(0.2~0.8) 정점 비율}
"""
import unittest

import _path  # noqa: F401
from test_player import BASE_062, HAIR, generated

# FR-52: 신규 5종 (인덱스 30~34)
NEW_062 = ("Hair_PartedTwoBlock", "Hair_CommaPerm", "Hair_VolumeCurlyLong", "Hair_StraightCornrows", "Hair_ZigzagCornrows")
HAIR_062 = HAIR + NEW_062

# FR-40 상한: 짧은 ≤ 0.06(옆 폭 ≤ 0.05), 중간 ≤ 0.15, 볼륨 곱슬·아프로·하이톱 ≤ 0.35.
# 계열은 FR-51 분류표. 모히칸·탑노트·번(의도적으로 솟는 형태)은 '볼륨' 등급 [제안] (T21).
SHORT = {"Hair_Buzz", "Hair_Short", "Hair_Fade", "Hair_Crew", "Hair_SidePart", "Hair_Undercut", "Hair_TexturedCrop",
         "Hair_BuzzLine", "Hair_Cornrows", "Hair_FrenchCrop", "Hair_IvyLeague", "Hair_Spiky",
         "Hair_PartedTwoBlock", "Hair_StraightCornrows", "Hair_ZigzagCornrows"}
VOLUME = {"Hair_Afro", "Hair_HighTop", "Hair_VolumeCurlyLong", "Hair_Mohawk", "Hair_TopKnot", "Hair_Bun"}
MEDIUM = set(HAIR_062) - SHORT - VOLUME - {"Hair_Bald"}
CAP = {"short": 0.06, "medium": 0.15, "volume": 0.35}
SIDE_SHORT = 0.05

# FR-42 페이드 계열 (옆·뒤가 아래에서 위로 진해진다), FR-54 컬러 포인트 대상
FADE = ("Hair_Fade", "Hair_Undercut", "Hair_PartedTwoBlock", "Hair_Mohawk", "Hair_CurlyTop")
COLOR_POINT = ("Hair_PartedTwoBlock", "Hair_Undercut", "Hair_Mohawk", "Hair_CurlyTop", "Hair_BoxBraids")


def hair():
    return generated("default").layout["summary"]["hair062"]


class Hair062(unittest.TestCase):

    def test_style_names_0_29_fixed(self):
        h = hair()
        self.assertEqual(tuple(h["styles"]), HAIR_062)                     # 0~29 이름·순서 불변 + 신규 5
        self.assertEqual(len(MEDIUM | SHORT | VOLUME | {"Hair_Bald"}), 35)

    def test_volume_ratio(self):
        shape = hair()["shape"]
        for s in HAIR_062:
            if s == "Hair_Bald":
                continue
            cls = "short" if s in SHORT else "volume" if s in VOLUME else "medium"
            self.assertLessEqual(shape[s]["volume"], CAP[cls], f"{s} ({cls}) {shape[s]}")
            if cls == "short":
                self.assertLessEqual(shape[s]["side"], SIDE_SHORT, f"{s} 옆 폭 {shape[s]}")

    def test_fade_density(self):
        fade = hair()["fade"]
        for s in FADE:
            d = fade[s]
            self.assertEqual(len(d), 5, s)
            for a, b in zip(d, d[1:]):
                self.assertLessEqual(a, b + 1e-6, f"{s} 밀도가 위로 갈수록 늘어야 한다: {d}")
            self.assertLessEqual(d[0], 0.2, f"{s} 맨 아래 칸 {d}")

    def test_strand_hf(self):
        self.assertGreaterEqual(hair()["strandHf"], BASE_062["hairStrandHf"] * 1.5)   # FR-43

    def test_silhouette_pairs(self):
        sil = hair()["silhouette"]
        self.assertGreaterEqual(sil["minPair"], 0.08, sil["pairsBelow"][:10])         # FR-44 (색 변형은 스타일이 아니라 색 조합)

    def test_color_point_regions(self):
        cp = hair()["colorPoint"]
        for s in COLOR_POINT:
            r = cp[s]
            self.assertGreaterEqual(r["a"], 0.1, f"{s} {r}")
            self.assertGreaterEqual(r["b"], 0.1, f"{s} {r}")
            self.assertLessEqual(r["mixed"], 0.2, f"{s} 두 색이 분리돼야 한다 {r}")


if __name__ == "__main__":
    unittest.main()
