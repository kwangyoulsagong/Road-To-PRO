"""문서 검사 (specs/088 AC-09): 헤어 스타일 시안 절차 문서가 FR-20의 네 단계와 FR-11 지표를 담는다. Blender 불필요."""
import os
import unittest

import _path


class GroomProcessDoc(unittest.TestCase):
    def test_groom_process_doc(self):
        p = os.path.join(_path.REPO, "docs", "hair-groom-process.html")
        self.assertTrue(os.path.exists(p), p)
        t = open(p, encoding="utf-8").read()
        for word in ("참고 사진", "시안 렌더 비교", "승인", "굽기", "IoU", "ΔE", "가르마", "앞머리"):
            self.assertIn(word, t, word)
        self.assertIn("레포 밖", t)   # 사진은 레포에 넣지 않는다


if __name__ == "__main__":
    unittest.main()
