"""경기장 인자 검증 (specs/044 FR-10·12·61). Blender 없이 돈다."""
import unittest

import _path  # noqa: F401
from stadium import params


def make(**kw):
    base = dict(capacity=20000, tiers=2, roof="partial", seat_colors="#1E5AA8,#F2F2F2", lights="towers", boards="on", seed=1)
    base.update(kw)
    return params.StadiumParams.from_args(**base)


class ParamValidation(unittest.TestCase):
    def test_default_ok(self):
        p = make()
        self.assertEqual(p.capacity, 20000)
        self.assertEqual(p.seat_colors, ((0x1E, 0x5A, 0xA8), (0xF2, 0xF2, 0xF2)))
        self.assertEqual(p.lights, "towers")
        self.assertFalse(p.lights_switched)

    def test_invalid(self):
        for kw in (dict(tiers=0), dict(tiers=4), dict(capacity=-1), dict(capacity=12345),
                   dict(seat_colors="red,#FFFFFF"), dict(seat_colors="#GGGGGG,#FFFFFF"), dict(seat_colors="#FFF,#000"),
                   dict(seat_colors="#FFFFFF"), dict(roof="dome"), dict(lights="laser"), dict(boards="maybe"), dict(seed=-1),
                   dict(roof="none", lights="roofline")):
            with self.assertRaises(params.ParamError, msg=str(kw)):
                make(**kw)

    def test_full_roof_switches_towers(self):
        p = make(roof="full", lights="towers")
        self.assertEqual(p.lights, "roofline")
        self.assertTrue(p.lights_switched)

    def test_presets(self):
        for name in ("min", "default", "max"):
            p = params.preset(name)
            self.assertIsInstance(p, params.StadiumParams)
        self.assertEqual(params.preset("min").tiers, 1)
        self.assertEqual(params.preset("max").tiers, 3)
        self.assertEqual(params.preset("max").roof, "full")

    def test_args_roundtrip(self):
        p = params.preset("default")
        self.assertEqual(params.StadiumParams.from_args(**p.as_args()), p)


if __name__ == "__main__":
    unittest.main()
