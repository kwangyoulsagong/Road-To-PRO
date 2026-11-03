"""farm art (specs/044 T06): Blender 테스트 수집·미리보기 격자."""
import os
import tempfile
import textwrap
import unittest

from farm import art, cli


class ArtCollect(unittest.TestCase):
    def test_collects_pass_fail_skip(self):
        with tempfile.TemporaryDirectory() as d:
            src = os.path.join(d, "src")
            os.makedirs(src)
            with open(os.path.join(src, "test_fake_art.py"), "w") as f:
                f.write(textwrap.dedent('''
                    import unittest
                    class T(unittest.TestCase):
                        def test_ok(self): pass
                        def test_bad(self): self.assertEqual(1, 2, "숫자 다름")
                        @unittest.skip("이유")
                        def test_skip(self): pass
                '''))
            r = art.run_tests(os.path.join(d, "out"), pattern="test_fake_art.py", start_dir=src)
            self.assertEqual((r.total, r.passed, r.failed, r.skipped), (3, 1, 1, 1))
            self.assertFalse(r.ok)
            bad = r.failures()[0]
            self.assertEqual(bad["name"], "T.test_bad")
            self.assertIn("숫자 다름", bad["message"])
            self.assertEqual(os.environ["FARM_ART_OUT"], os.path.join(d, "out"))

    def test_previews_grid_and_copy(self):
        with tempfile.TemporaryDirectory() as d:
            out = os.path.join(d, "out")
            for s in ("min", "default"):
                os.makedirs(os.path.join(out, s))
                for v in ("main", "top"):
                    open(os.path.join(out, s, f"stadium.preview-{v}.png"), "wb").write(b"x")
            found = art.previews(out)
            self.assertEqual(sorted(found), ["default", "min"])
            self.assertEqual(sorted(found["min"]), ["main", "top"])
            ev = os.path.join(d, "ev")
            copied = art.copy_previews(found, ev)
            self.assertTrue(os.path.exists(os.path.join(ev, "blender", "min", "preview-main.png")))
            html = art.preview_grid_html(copied, ev)
            self.assertIn("blender/min/preview-main.png", html)
            self.assertEqual(art.preview_grid_html({}, ev), "")

    def test_cli_has_art(self):
        p = cli.build_parser()
        a = p.parse_args(["art", "--spec", "044"])
        self.assertEqual((a.command, a.spec), ("art", "044"))


if __name__ == "__main__":
    unittest.main()


class ArtMerge(unittest.TestCase):
    """specs/044 T18a: Blender 증거 ↔ AC, logic·matrix 리포트 합치기."""

    def test_ac_evidence_from_docstrings(self):
        with tempfile.TemporaryDirectory() as d:
            src = os.path.join(d, "src")
            os.makedirs(src)
            with open(os.path.join(src, "test_doc_art.py"), "w") as f:
                f.write(textwrap.dedent('''
                    import unittest
                    class A(unittest.TestCase):
                        """AC-03: 같은 인자."""
                        def test_a(self): pass
                    class B(unittest.TestCase):
                        """AC-05, AC-03"""
                        def test_b(self): self.fail("x")
                '''))
            r = art.run_tests(os.path.join(d, "out"), pattern="test_doc_art.py", start_dir=src)
            ev = art.ac_evidence(r)
            self.assertEqual(sorted(ev), ["AC-03", "AC-05"])
            self.assertFalse(ev["AC-03"][0]["ok"])
            self.assertIn("1/2", ev["AC-03"][0]["label"])

    def test_merge_art_replaces_no_determinism(self):
        import json
        from farm import commands
        with tempfile.TemporaryDirectory() as d:
            with open(os.path.join(d, "art.json"), "w") as f:
                json.dump({"result": {"ok": True, "total": 2, "passed": 2, "failed": 0}, "png": "art.png",
                           "acEvidence": {"AC-03": [{"label": "Blender 생성 테스트 1/1 통과", "ok": True}]}}, f)
            ev = {"acEvidence": {"AC-03": [{"label": "결정론 테스트 없음", "ok": False}], "AC-01": [{"label": "EditMode", "ok": True}]}}
            commands.merge_art(ev, d)
            self.assertEqual(ev["acEvidence"]["AC-03"], [{"label": "Blender 생성 테스트 1/1 통과", "ok": True}])
            self.assertTrue(ev["art"]["ok"])

    def test_merge_previous_logic_into_matrix(self):
        import json
        from farm import commands
        with tempfile.TemporaryDirectory() as d:
            with open(os.path.join(d, "run.json"), "w") as f:
                json.dump({"spec": {"id": "044"}, "logic": {"ok": True}, "art": {"ok": True},
                           "acEvidence": {"AC-01": [{"label": "EditMode", "ok": True}]}}, f)
            ev = {"acEvidence": {"AC-10": [{"label": "e2e", "ok": True}]}}
            commands.merge_previous(ev, d, "044")
            self.assertEqual(sorted(ev["acEvidence"]), ["AC-01", "AC-10"])
            self.assertTrue(ev["logic"]["ok"])
            ev2 = {"acEvidence": {}}
            commands.merge_previous(ev2, d, "045")
            self.assertNotIn("logic", ev2)
