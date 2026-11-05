"""달리기·킥 형태 테스트 (specs/055 AC-01~09). 기준 범위는 Data/anim/055-gait-standards.json, 원형 표는 055-run-styles.json.

뼈대만으로 클립을 만들어 지표를 재는 `gait_lab.py`를 백그라운드 Blender로 한 번 돌린다(약 6~20 s).
뼈대는 설치된 산출물(Assets/_Project/Presentation/Players/Generated/player.layout.json) — 전체 생성기와 같은 뼈대.
실행: `python3 -m unittest test_gait` (Art/Blender/tests에서) 또는 `farm.py art --spec 055`.
"""
import json
import os
import subprocess
import tempfile
import unittest

import _path

BLENDER = os.environ.get("BLENDER", "/Applications/Blender.app/Contents/MacOS/Blender")
LAB = os.path.join(_path.SCRIPTS, "gait_lab.py")
LAYOUT = os.environ.get("GAIT_LAYOUT") or os.path.join(_path.REPO, "Assets", "_Project", "Presentation", "Players", "Generated",
                                                       "player.layout.json")
STD_PATH = os.path.join(_path.REPO, "Data", "anim", "055-gait-standards.json")
STY_PATH = os.path.join(_path.REPO, "Assets", "_Project", "Presentation", "Players", "Data", "055-run-styles.json")  # Unity TextAsset과 같은 파일
GAITS = ("walk", "jog", "run", "sprint")
DIRS = ("f", "fr", "r", "br", "b", "bl", "l", "fl")
_cache = {}


def lab(extra=()):
    key = tuple(extra)
    if key not in _cache:
        out = os.path.join(tempfile.mkdtemp(prefix="gait-"), "m.json")
        cmd = [BLENDER, "--background", "--factory-startup", "--python-exit-code", "1", "--python", LAB, "--",
               "--layout", LAYOUT, "--out", out] + list(extra)
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=900)
        if p.returncode != 0:
            raise AssertionError("gait_lab 실패:\n" + (p.stdout + p.stderr)[-3000:])
        with open(out, encoding="utf-8") as f:
            _cache[key] = json.load(f)
    return _cache[key]


def std():
    with open(STD_PATH, encoding="utf-8") as f:
        return json.load(f)


def styles():
    with open(STY_PATH, encoding="utf-8") as f:
        return json.load(f)


def within(v, rng, tol=1e-6):
    vs = v if isinstance(v, list) else [v]
    return all(x is not None and rng[0] - tol <= x <= rng[1] + tol for x in vs)


class GaitCase(unittest.TestCase):
    def check(self, cid, m, rngs, keys):
        bad = []
        for k in keys:
            r = rngs.get(k)
            if r is None:
                continue
            if isinstance(r, bool):
                if r and not m.get(k):
                    bad.append(f"{k}={m.get(k)} (기대 True)")
                continue
            if not within(m.get(k), r):
                bad.append(f"{k}={m.get(k)} 범위 {r}")
        self.assertFalse(bad, f"{cid}: " + "; ".join(bad))

    @property
    def M(self):
        return lab()["clips"]

    def clip(self, cid):
        self.assertIn(cid, self.M, f"클립 {cid} 없음")
        return self.M[cid]


class TestMetricsUnit(unittest.TestCase):
    def test_metrics_unit(self):
        r = lab(("--selftest",))
        for k, (got, want) in r.items():
            tol = 1.5 if k == "neckExt20" else 0.05  # 머리 젖힘은 골반 요(10°)가 함께 있어 시상면 투영이 약간 줄어든다
            self.assertAlmostEqual(got, want, delta=tol, msg=k)


class TestTimeStructure(GaitCase):
    """AC-01 · FR-01·02"""

    def test_time_structure(self):
        S = std()["gaits"]
        for g in GAITS:
            self.check(f"{g}_f", self.clip(f"{g}_f"), S[g], ("cadence", "stepLength", "duty", "contactTime", "flightTime", "pelvisBob"))


class TestPelvisPhase(GaitCase):
    """AC-02 · FR-03: jog 이상 모든 클립(방향·원형 포함) 골반 최저점 접지 40~60%, 최고점 공중 구간."""

    def test_pelvis_phase(self):
        n = 0
        for cid, m in sorted(self.M.items()):
            if m.get("gait") not in ("jog", "run", "sprint") or m.get("football"):
                continue
            n += 1
            self.assertTrue(within(m["pelvisMinInStance"], [0.40, 0.60]), f"{cid}: 최저점 {m['pelvisMinInStance']}")
            self.assertTrue(m["pelvisMaxInFlight"], f"{cid}: 골반 최고점이 공중 구간 밖")
        self.assertGreaterEqual(n, 17)


class TestLegAngles(GaitCase):
    """AC-03 · FR-10~13"""

    def test_leg_angles(self):
        S = std()["gaits"]
        for g in ("jog", "run", "sprint"):
            m = dict(self.clip(f"{g}_f"))
            m["kneeTouchdown"] = [m["kneeTouchdown"], m["kneeTouchdownMax"]]
            m["kneeToeOff"] = [m["kneeToeOff"], m["kneeToeOffMax"]]
            self.check(f"{g}_f", m, S[g], ("landingDist", "kneeTouchdown", "kneeStanceMax", "kneeToeOff", "kneeSwingMax",
                                          "hipSwingMax", "swingFootClear", "kneeLeadsFoot"))

    def test_swing_continuity(self):
        sc = std()["swingContinuity"]
        for cid, m in sorted(self.M.items()):
            lim = sc["ankleAccelNormWalk"] if m.get("gait") == "walk" else sc["ankleAccelNorm"]
            if m.get("gait") in GAITS:
                self.assertTrue(within(m["ankleAccelNorm"], lim), f"{cid}: 발목 가속 정규화 {m['ankleAccelNorm']:.1f} > {lim[1]}")


class TestTouchdownEntry(GaitCase):
    """AC-10(043 AC-09 회귀) 앞당김: 30 fps 키 보간 포즈에서 착지 직전 볼이 3 cm 아래로 들어온 뒤 착지 점까지 미끄러짐 ≤ 1.5 cm [제안]
    (Unity 판정 3 cm에 여유). 착지 쪽 발 경로 섞기가 짧으면 마지막 프레임에 발이 아직 앞으로 가며 바닥에 닿는다."""

    def test_touchdown_entry(self):
        lim = std()["swingContinuity"]["tdEntrySlide"]
        for cid, m in sorted(self.M.items()):
            if "tdEntrySlide" in m:
                self.assertTrue(within(m["tdEntrySlide"], lim), f"{cid}: 착지 진입 미끄러짐 {m['tdEntrySlide'] * 100:.1f} cm")


class TestTrunkHead(GaitCase):
    """AC-04 · FR-20~23"""

    def test_trunk_head(self):
        S = std()["gaits"]
        for g in GAITS:
            m = self.clip(f"{g}_f")
            self.check(f"{g}_f", m, S[g], ("trunkLean", "neckExtMax", "headForwardMax", "pelvisYaw", "thoraxPelvisRel",
                                          "pelvisObliquity", "trunkRoll"))
            if g != "walk":
                self.assertTrue(m["thoraxAnti"], f"{g}_f: 흉곽이 골반과 반대로 돌지 않는다")


class TestArms(GaitCase):
    """AC-05 · FR-30~33"""

    def test_arms(self):
        S = std()["gaits"]
        for g in GAITS:
            m = dict(self.clip(f"{g}_f"))
            m["shoulderRom"] = [m["shoulderRom"], m["shoulderRomMax"]]
            self.check(f"{g}_f", m, S[g], ("armLegPhaseErr", "elbowMean", "elbowMin", "elbowMax", "elbowRange", "shoulderRom",
                                          "armAbduction", "handMidline", "handBackBehindHip"))

    def test_no_fixed_elbow(self):
        for cid, m in sorted(self.M.items()):
            if m.get("football") in ("start", "stop"):
                continue  # 비루프 출발·감속은 걸음 지표(FR-40·41)로 본다
            self.assertGreaterEqual(m["elbowRange"], 3.0, f"{cid}: 팔꿈치 각도가 주기 내내 고정")


class TestDirections(GaitCase):
    """AC-07 · FR-44: 옆·뒤 방향은 팔 반대 위상·팔꿈치 변동·머리 정렬·골반 진동 위상."""

    def test_directions(self):
        D = std()["directions"]
        S = std()["gaits"]
        for g in ("walk", "jog", "sprint"):
            for dn in DIRS[1:]:
                cid = f"{g}_{dn}"
                m = self.clip(cid)
                r = dict(S[g])
                lo, hi = r["elbowRange"]
                r["elbowRange"] = [lo * D["elbowRangeScale"], hi]
                items = [k for k in D["items"] if k != "pelvisMinInStance" or g != "walk"]
                if m["hipFlexRom"] < D["phaseMinHipRom"]:
                    items.remove("armLegPhaseErr")  # 사이드 셔플: 엉덩이 굴곡이 거의 없어 팔-다리 위상 기준이 적용되지 않는다
                self.check(cid, m, r, items)


class TestFootballGaits(GaitCase):
    """AC-06 · FR-40~42"""

    def _base_step(self, v):
        """같은 속도 공 없는 보법 보폭: 정면 보법 사이를 속도로 선형 보간."""
        pts = sorted((self.clip(f"{g}_f")["speed"], self.clip(f"{g}_f")["stepLength"]) for g in GAITS)
        for (v0, s0), (v1, s1) in zip(pts, pts[1:]):
            if v <= v1:
                t = max(0.0, (v - v0) / (v1 - v0))
                return s0 + (s1 - s0) * t
        return pts[-1][1]

    def _base_pelvis(self, v):
        pts = sorted((self.clip(f"{g}_f")["speed"], self.clip(f"{g}_f")["pelvisMean"]) for g in GAITS)
        for (v0, s0), (v1, s1) in zip(pts, pts[1:]):
            if v <= v1:
                t = max(0.0, (v - v0) / (v1 - v0))
                return s0 + (s1 - s0) * t
        return pts[-1][1]

    def test_start(self):
        F = std()["football"]["start"]
        m = self.clip("start_f")
        leans = m["stepLeans"]
        self.assertTrue(within(leans[0], F["firstStepLean"]), f"첫 걸음 경사 {leans[0]}")
        self.assertTrue(all(b < a for a, b in zip(leans, leans[1:])), f"걸음별 경사 단조 감소 아님 {leans}")
        self.assertTrue(within(len(leans), F["steps"]), f"걸음 수 {len(leans)}")
        self.assertTrue(within(m["firstStepRatio"], F["firstStepRatio"]), f"첫 걸음 보폭 비율 {m['firstStepRatio']}")
        self.assertTrue(within(m["shoulderRom"], F["shoulderRom"]), f"어깨 가동 {m['shoulderRom']}")

    def test_stop(self):
        F = std()["football"]["stop"]
        m = self.clip("stop_f")
        self.assertTrue(within(m["trunkLeanMin"], F["trunkLean"]), f"감속 몸통 경사 {m['trunkLeanMin']}")
        self.assertTrue(within(m["pelvisDrop"], F["pelvisDrop"]), f"골반 하강 {m['pelvisDrop']}")
        self.assertTrue(within(len(m["stepLeans"]), F["steps"]), f"제동 걸음 수 {len(m['stepLeans'])}")
        self.assertTrue(m["heelFirst"], "뒤꿈치 먼저 착지 아님")

    def test_dribble(self):
        F = std()["football"]["dribble"]
        for v in F["speeds"]:
            cid = f"dribble_run_{int(round(v * 10))}"
            m = self.clip(cid)
            self.assertAlmostEqual(m["speed"], v, delta=1e-3)
            short = 1.0 - m["stepLength"] / self._base_step(v)
            self.assertTrue(within(short, F["stepShorter"]), f"{cid}: 보폭 감소 {short:.3f}")
            low = self._base_pelvis(v) - m["pelvisMean"]
            self.assertTrue(within(low, F["pelvisLower"]), f"{cid}: 무게중심 낮춤 {low:.3f}")


class TestRunStyles(GaitCase):
    """AC-08 · FR-50·52: 원형 sprint(lo·hi)·run 클립이 원형 표대로, FR-01~33 공통 항목은 지킨다."""

    def test_run_styles(self):
        Y = styles()
        S = std()["gaits"]
        jit = Y["jitter"]
        base_rom = self.clip("sprint_f")["shoulderRom"]
        common_all = ("elbowMean", "elbowMin", "elbowMax", "elbowRange", "armLegPhaseErr", "neckExtMax", "headForwardMax",
                      "pelvisMinInStance", "handMidline")
        for sid, st in sorted(Y["styles"].items()):
            # 스펙 FR-50 표가 몸통 ≥ 20°로 명시한 예외(GK): 머리가 몸통과 한 줄이어도 어깨선보다 목 길이 × sin(경사)만큼 앞 →
            # 머리 앞 이동 6 cm(FR-21, 보통 경사 기준)는 적용하지 않는다. 목 신전 ≤ 10°는 그대로 본다.
            common = tuple(k for k in common_all if not (k == "headForwardMax" and st["trunkLean"][0] > 15.0))
            run = self.clip(f"run_f_{sid}")
            self.check(f"run_f_{sid}", run, S["run"], common)
            variants = ("lo", "hi") if sid != "gk" else (None,)
            ms = []
            for var in variants:
                cid = f"sprint_f_{sid}" + (f"_{var}" if var else "")
                m = self.clip(cid)
                ms.append(m)
                self.check(cid, m, S["sprint"], common)
                wide = {k: [st[k][0] * (1 - jit), st[k][1] * (1 + jit)] for k in ("sprintCadence", "sprintStep", "topSpeed") if st.get(k)}
                if "sprintCadence" in wide:
                    self.assertTrue(within(m["cadence"], wide["sprintCadence"]), f"{cid}: 빈도 {m['cadence']:.2f} {wide['sprintCadence']}")
                if "sprintStep" in wide:
                    self.assertTrue(within(m["stepLength"], wide["sprintStep"]), f"{cid}: 보폭 {m['stepLength']:.2f} {wide['sprintStep']}")
                self.assertTrue(within(m["speed"], wide["topSpeed"]), f"{cid}: 속도 {m['speed']}")
                self.assertTrue(within(m["trunkLean"], st["trunkLean"], tol=0.5), f"{cid}: 몸통 {m['trunkLean']:.1f} {st['trunkLean']}")
                ab = st.get("armAbduction", S["sprint"]["armAbduction"])
                self.assertTrue(within(m["armAbduction"], ab), f"{cid}: 외전 {m['armAbduction']:.1f} {ab}")
                if st.get("shoulderRomScale"):
                    ratio = m["shoulderRom"] / base_rom
                    lo, hi = st["shoulderRomScale"]
                    self.assertTrue(within(ratio, [lo - jit, hi + jit]), f"{cid}: 어깨 가동 비율 {ratio:.2f}")
                elif sid != "gk":
                    self.assertTrue(within(m["shoulderRom"], S["sprint"]["shoulderRom"]), f"{cid}: 어깨 가동 {m['shoulderRom']:.0f}")
            if len(ms) == 2:
                # 30 fps 정수 주기라 sprint 빈도는 4.00/4.29/4.62/5.00 Hz로만 나온다 → lo·hi 두 극단(이웃 주기 프레임)의 빈도 구간이
                # 원형 표 빈도 구간과 겹치면 선수별 블렌드 가중치로 표 안 빈도를 낼 수 있다. 보폭 중간값은 표 ± 흔들기 안.
                c_lo, c_hi = sorted((ms[0]["cadence"], ms[1]["cadence"]))
                lo, hi = st["sprintCadence"]
                self.assertTrue(c_lo <= hi + 1e-6 and c_hi >= lo - 1e-6, f"{sid}: 빈도 구간 [{c_lo:.2f}, {c_hi:.2f}]가 표 {st['sprintCadence']}와 안 겹친다")
                mid_s = (ms[0]["stepLength"] + ms[1]["stepLength"]) / 2
                self.assertTrue(within(mid_s, [st["sprintStep"][0] * (1 - jit), st["sprintStep"][1] * (1 + jit)]), f"{sid}: 중간 보폭 {mid_s:.2f}")
                self.assertNotAlmostEqual(ms[0]["cadence"], ms[1]["cadence"], places=2, msg=f"{sid}: lo·hi가 같다")

    def test_styles_distinct(self):
        """원형끼리 구분: 빈도·보폭·몸통·어깨 가동 중 적어도 하나가 서로 다르다(중간값 기준 3% 또는 2° 이상)."""
        Y = styles()
        mids = {}
        for sid in Y["styles"]:
            ms = [self.clip(f"sprint_f_{sid}" + s) for s in (("_lo", "_hi") if sid != "gk" else ("",))]
            mids[sid] = {k: sum(m[k] for m in ms) / len(ms) for k in ("cadence", "stepLength", "trunkLean", "shoulderRom", "armAbduction")}
        ids = sorted(mids)
        for i, a in enumerate(ids):
            for b in ids[i + 1:]:
                A, B = mids[a], mids[b]
                diff = (abs(A["cadence"] / B["cadence"] - 1) >= 0.03 or abs(A["stepLength"] / B["stepLength"] - 1) >= 0.03
                        or abs(A["trunkLean"] - B["trunkLean"]) >= 2 or abs(A["shoulderRom"] - B["shoulderRom"]) >= 4
                        or abs(A["armAbduction"] - B["armAbduction"]) >= 2)
                self.assertTrue(diff, f"원형 {a}·{b} 구분 안 됨: {A} / {B}")


class TestKicks(unittest.TestCase):
    """AC-09 · FR-60·61"""

    def test_kicks(self):
        K = std()["kicks"]
        Y = styles()["kicks"]
        kicks = lab()["kicks"]
        self.assertGreaterEqual(len(kicks), 13)
        seen = set()
        for cid, m in sorted(kicks.items()):
            bad = []
            if m["ground"]:  # 디딤발·접촉 무릎 기준은 땅볼 킥 실험(Hay 1993 등) — 발리·펀트(공 높이 > 0.3 m)는 대상 아님
                if not within(m["plantBehind"], K["plantBehind"]):
                    bad.append(f"디딤발 뒤 {m['plantBehind']:.3f}")
                if not within(m["plantSide"], K["plantSide"]):
                    bad.append(f"디딤발 옆 {m['plantSide']:.3f}")
                if not within(m["kneeContactKick"], K["kneeContactKick"]):
                    bad.append(f"차는 무릎 {m['kneeContactKick']:.0f}")
                if not within(m["kneeContactPlant"], K["kneeContactPlant"]):
                    bad.append(f"디딤 무릎 {m['kneeContactPlant']:.0f}")
            if not within(m["hipFlexVelPeak"], K["hipFlexVelPeak"]):
                bad.append(f"엉덩이 굴곡 각속도 피크 {m['hipFlexVelPeak']:.2f}")
            ks = m.get("kickStyle")
            cls = Y[ks]["backswing"] if ks else m.get("backswingClass", "big")
            if not within(m["hipExtMax"], K["hipExtBackswing"][cls]):
                bad.append(f"백스윙 엉덩이 신전 {m['hipExtMax']:.0f} ({cls})")
            if ks:
                seen.add(ks)
                st = Y[ks]
                if not within(m["runupSteps"], st["runupSteps"]):
                    bad.append(f"도움닫기 걸음 {m['runupSteps']} {st['runupSteps']}")
                if not within(m["approachAngle"], st["approach"], tol=2.0):
                    bad.append(f"도움닫기 각 {m['approachAngle']:.0f} {st['approach']}")
                if not within(m["followThrough"], K["followThrough"][st["follow"]]):
                    bad.append(f"팔로스루 {m['followThrough']:.0f} ({st['follow']})")
            self.assertFalse(bad, f"{cid}: " + "; ".join(bad))
        self.assertEqual(seen, set(Y), "K1~K8 원형 클립이 모두 있어야 한다")


if __name__ == "__main__":
    unittest.main()
