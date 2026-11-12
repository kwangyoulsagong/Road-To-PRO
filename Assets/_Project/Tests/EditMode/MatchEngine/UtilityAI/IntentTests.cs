using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Scenarios;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 혼합 구조: 의도 보너스·준수·기회주의·해제·키 모먼트 (AC-10·13·30·31·32).</summary>
    public class IntentTests
    {
        const int Fb = 4, Owner = 8;

        /// <summary>풀백(슬롯 4, 25·58) 뒤에서 MF(슬롯 8, 45·58)가 공을 가진 장면 — 오버래핑 후보가 생긴다.</summary>
        static DecisionFixture OverlapScene(DecisionParams p = null)
        {
            var f = new DecisionFixture(p);
            f.GiveBall(Owner, 0);
            f.W.Mind[Fb] = DecisionAttributes.Average.With(teamwork: 100f);
            return f;
        }

        static IntentPacket OverlapIntent(int key = 1) => new IntentPacket
        {
            Has = true, StepKey = key, Role = 0, Kind = Consideration.Overlap, Point = new Vec2(55f, 64f), InWindow = true, TargetSlot = -1, NextPassTarget = -1,
        };

        [Test]
        public void Bonus_RaisesIntentMatchShare_AC30()
        {
            int Share(DecisionParams p)
            {
                var f = OverlapScene(p);
                f.W.Intent[Fb] = OverlapIntent();
                return f.Distribution(Fb, 50UL)[(int)Consideration.Overlap];
            }
            var with = Share(DecisionParams.Defaults);
            var zero = Share(DecisionFixture.With(("dec.intent.w", 0f)));
            Assert.Greater(Stat.Z(with, 1000, zero, 1000), Stat.Z01, $"보너스 {with} vs 0 {zero}");
        }

        [Test]
        public void Bonus_NotMultipliedByCombined_AC30()
        {
            var f = OverlapScene();
            f.Style.SetStyle(Fb, PlayStyle.WingBack()); // 오버래핑 결합 배수 ≠ 1
            f.W.Intent[Fb] = OverlapIntent();
            f.Decide(Fb, new SeedRng(51UL), true);
            var c = f.Decider.Candidates;
            var ix = c.IntentIndex;
            Assert.GreaterOrEqual(ix, 0);
            float umax = float.MinValue;
            for (int i = 0; i < c.Count; i++) if (c[i].Mult > 0f && c[i].Utility > umax) umax = c[i].Utility;
            var expected = c[ix].Utility / System.Math.Max(System.Math.Abs(umax), f.P.NormEps) + c[ix].Noise + f.P.IntentW * c[ix].Match;
            Assert.AreEqual(expected, f.Decider.Chooser.Score(ix), 1e-6f, "점수 = U/정규화 + 노이즈 + w·m (보너스에 결합 배수 없음)");
            Assert.AreNotEqual(1f, c[ix].Mult);
            Assert.That(c[ix].Match, Is.GreaterThan(0.5f).And.LessThanOrEqualTo(1f), "의도 지점 후보 (배역 앵커 당김 FR-96만큼 1보다 작다)");
        }

        [Test]
        public void Comply_TeamworkTwin_AC31()
        {
            int Complied(float teamwork, int habitLevel)
            {
                var f = OverlapScene();
                f.W.Mind[Fb] = DecisionAttributes.Average.With(teamwork: teamwork);
                if (habitLevel > 0) f.Style.SetStyle(Fb, PlayStyle.Neutral().WithHabit(Habit.LineBreaker, habitLevel)); // 침투 ×2 — 오버래핑 의도와 충돌
                f.W.Intent[Fb] = OverlapIntent();
                var rng = new SeedRng(52UL);
                int n = 0;
                for (int i = 0; i < 1000; i++)
                {
                    f.ResetState(Fb);
                    f.Decide(Fb, rng, true);
                    if (f.State[Fb].Complied) n++;
                }
                return n;
            }
            var hi = Complied(100f, 0);
            var lo = Complied(0f, 0);
            Assert.Greater(Stat.Z(hi, 1000, lo, 1000), Stat.Z01, $"Teamwork 20 {hi} vs 1 {lo}");
            var none = Complied(50f, 0);
            var lv2 = Complied(50f, 2);
            Assert.Greater(Stat.Z(none, 1000, lv2, 1000), Stat.Z01, $"충돌 버릇 레벨 0 {none} vs 2 {lv2}");
        }

        [Test]
        public void Comply_OneRollPerStep_AC31()
        {
            var f = OverlapScene();
            f.W.Intent[Fb] = OverlapIntent(7);
            var rng = new SeedRng(53UL);
            f.Decide(Fb, rng, true);
            var first = f.State[Fb].Complied;
            f.State[Fb].Complied = !first; // 같은 단계면 다시 굴리지 않으므로 값이 유지된다
            for (int k = 0; k < 5; k++) { f.W.Tick++; f.Decide(Fb, rng); }
            Assert.AreEqual(!first, f.State[Fb].Complied, "같은 단계 = 굴림 0회");
            // 같은 단계에서 결정 1회의 RNG 소비 = 집중 저하 1 + 노이즈(정규 2개씩) — 준수 굴림 없음
            var before = new SeedRng(rng.SaveState());
            f.W.Tick += 10; // 최소 유지 시간 밖 → 재추첨 여부는 결과로 확인
            var r = f.Decide(Fb, rng);
            before.NextFloat();
            for (int i = 0; i < f.Decider.Candidates.Count; i += 2) { before.NextFloat(); before.NextFloat(); }
            if (r.Rerolled) before.NextFloat();
            Assert.AreEqual(before.SaveState(), rng.SaveState());
            f.W.Intent[Fb] = OverlapIntent(8);
            f.W.Tick++;
            f.Decide(Fb, rng);
            Assert.AreEqual(8, f.State[Fb].IntentKey, "새 단계면 다시 굴린다");
        }

        [Test]
        public void Opportunistic_BelowTheta_Never_AboveTheta_ReleaseEach_AC32()
        {
            int Outside(DecisionParams p, out int releases, out int opp)
            {
                var f = OverlapScene(p);
                var intent = OverlapIntent();
                intent.Kind = Consideration.StayWide;
                intent.Point = new Vec2(20f, 66f); // 다른 후보와 멀리
                f.W.Intent[Fb] = intent;
                var rng = new SeedRng(54UL);
                int outside = 0;
                releases = 0;
                opp = 0;
                for (int i = 0; i < 1000; i++)
                {
                    f.ResetState(Fb);
                    f.State[Fb].IntentKey = intent.StepKey;
                    f.State[Fb].Complied = true;
                    var r = f.Decide(Fb, rng, true);
                    if (r.Kind != intent.Kind) outside++;
                    if (r.ReleaseIntent) releases++;
                    if (r.Source == DecisionSource.Opportunistic) opp++;
                }
                return outside;
            }
            Assert.AreEqual(0, Outside(DecisionFixture.With(("dec.intent.thetaOpp", 3f)), out var rel0, out _), "θ_opp 미만 → 의도 밖 0회");
            Assert.AreEqual(0, rel0);
            var outside = Outside(DecisionFixture.With(("dec.intent.thetaOpp", 0f)), out var rel, out var oppCount);
            Assert.Greater(outside, 0, "θ_opp 이상이면 선택될 수 있다");
            Assert.AreEqual(outside, rel, "의도 밖 선택마다 해제 통보 정확히 1건");
            Assert.AreEqual(outside, oppCount);
        }

        [Test]
        public void Release_SameTick_BonusZero_OthersKept_AC10()
        {
            var f = OverlapScene();
            f.W.Intent[Fb] = OverlapIntent(1);
            f.W.Intent[9] = new IntentPacket { Has = true, StepKey = 2, Kind = Consideration.RunInBehind, Point = new Vec2(88f, 30f), InWindow = true, TargetSlot = -1, NextPassTarget = -1 };
            f.W.Mind[9] = DecisionAttributes.Average.With(teamwork: 100f);
            var rng = new SeedRng(55UL);
            f.Decide(Fb, rng, true);
            f.Decide(9, rng, true);
            f.W.Tick++;
            f.W.Intent[Fb] = IntentPacket.None; // 해제 (배역 종료·붕괴)
            var r = f.Decide(Fb, rng, true);   // 해제 = 즉시 재결정 트리거
            Assert.AreEqual(DecisionSource.General, r.Source);
            Assert.AreEqual(-1, f.State[Fb].IntentKey);
            for (int i = 0; i < f.Decider.Candidates.Count; i++) Assert.AreEqual(0f, f.Decider.Candidates[i].Match, "보너스 0");
            Assert.IsTrue(f.W.Intent[9].Has, "해제 범위 밖 배역은 유지");
            var r9 = f.Decide(9, rng);
            Assert.AreNotEqual(DecisionSource.General, r9.Source);
        }

        [Test]
        public void Intent_OffPitchTarget_ClampedAndMatched()
        {
            var f = OverlapScene();
            var intent = OverlapIntent();
            intent.Point = new Vec2(130f, 90f);
            f.W.Intent[Fb] = intent;
            f.State[Fb].IntentKey = intent.StepKey;
            f.State[Fb].Complied = true;
            f.Decide(Fb, new SeedRng(56UL), true);
            var c = f.Decider.Candidates[f.Decider.Candidates.IntentIndex];
            Assert.LessOrEqual(c.Point.X, f.W.PitchL);
            Assert.LessOrEqual(c.Point.Y, f.W.PitchW);
        }

        [Test]
        public void Intent_Ignored_SourceFlagAndNoBonus()
        {
            var f = OverlapScene();
            f.W.Intent[Fb] = OverlapIntent();
            f.State[Fb].IntentKey = 1;
            f.State[Fb].Complied = false;
            var r = f.Decide(Fb, new SeedRng(57UL), true);
            Assert.AreEqual(DecisionSource.IntentIgnored, r.Source);
            Assert.IsFalse(r.ReleaseIntent);
            for (int i = 0; i < f.Decider.Candidates.Count; i++) Assert.AreEqual(0f, f.Decider.Candidates[i].Match);
        }

        [Test]
        public void KeyMoment_TakeOnceOnlyForThatPlayer_AC13()
        {
            var km = new KeyMomentOverride();
            km.Set(9, Consideration.DropToReceive, new Vec2(60f, 30f));
            Assert.IsFalse(km.TryTake(10, out _, out _));
            Assert.IsTrue(km.TryTake(9, out var k, out var p));
            Assert.AreEqual(Consideration.DropToReceive, k);
            Assert.AreEqual(new Vec2(60f, 30f), p);
            Assert.IsFalse(km.TryTake(9, out _, out _), "다음 결정 1회만");
        }

        [Test]
        public void VerbMapping_CoversRunsAndDefence()
        {
            Assert.AreEqual(Consideration.RunInBehind, ScenarioIntentAdapter.KindOf(ScenarioVerb.RunInBehind, VerbClass.Move, false));
            Assert.AreEqual(Consideration.Overlap, ScenarioIntentAdapter.KindOf(ScenarioVerb.Overlap, VerbClass.Move, false));
            Assert.AreEqual(Consideration.BoxCrash, ScenarioIntentAdapter.KindOf(ScenarioVerb.NearPostRun, VerbClass.Move, false));
            Assert.AreEqual(Consideration.Press, ScenarioIntentAdapter.KindOf(ScenarioVerb.Engage, VerbClass.Defend, true));
            Assert.AreEqual(Consideration.Mark, ScenarioIntentAdapter.KindOf(ScenarioVerb.TightMark, VerbClass.Defend, true));
            Assert.AreEqual(Consideration.Support, ScenarioIntentAdapter.KindOf(ScenarioVerb.CheckOut, VerbClass.Move, false));
        }
    }
}
