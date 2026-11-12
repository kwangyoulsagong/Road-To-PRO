using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 오프볼 후보·점수·유지·쌍둥이 비교 (AC-09·12·20·22·26·27·28, FR-03·04·15·16·19·19b).</summary>
    public class OffBallDeciderTests
    {
        const int Fw = 9, Mf = 7, AwayPresser = 20, AwayMf = 17;

        static KinematicAttributes Kin(float pace, float accel, float ant = 55f) =>
            new KinematicAttributes(pace, accel, 55f, 55f, 55f, ant, 55f);

        // ── 후보 생성 ──

        [Test]
        public void Candidates_AttackSet_NoDefensiveKinds_CapRespected()
        {
            var f = new DecisionFixture();
            f.Decide(Fw, new SeedRng(1UL), true);
            var c = f.Decider.Candidates;
            Assert.LessOrEqual(c.Count, (int)f.P.CandMax);
            Assert.AreEqual(Consideration.HoldShape, c[0].Kind);
            for (int i = 0; i < c.Count; i++)
            {
                var k = c[i].Kind;
                Assert.IsFalse(k == Consideration.Press || k == Consideration.Mark || k == Consideration.Cover, k.ToString());
            }
        }

        [Test]
        public void Candidates_DefenceSet_NoAttackKinds_PressOnlyForDesignated()
        {
            var f = new DecisionFixture();
            f.Decide(AwayPresser, new SeedRng(2UL), true);
            var c = f.Decider.Candidates;
            var press = false;
            for (int i = 0; i < c.Count; i++)
            {
                Assert.IsFalse(c[i].Kind == Consideration.RunInBehind || c[i].Kind == Consideration.Overlap, c[i].Kind.ToString());
                if (c[i].Kind == Consideration.Press) { press = true; Assert.AreEqual(6, c[i].Target, "압박 대상 = 공 소유자"); }
            }
            Assert.IsTrue(press, "지정 1순위는 압박 후보");
            f.Decide(AwayMf, new SeedRng(2UL), true);
            for (int i = 0; i < f.Decider.Candidates.Count; i++) Assert.AreNotEqual(Consideration.Press, f.Decider.Candidates[i].Kind, "지정 밖은 압박 후보 없음 (게겐프레싱 창 밖)");
        }

        [Test]
        public void Candidates_Gegenpress_NonDesignatedNearGetPress()
        {
            var f = new DecisionFixture();
            f.W.GegenActive[1] = true;
            f.Decide(AwayMf, new SeedRng(3UL), true); // 66·12 → 공 45·26 거리 25 m: 반경 15 m 밖
            var near = false;
            for (int i = 0; i < f.Decider.Candidates.Count; i++) near |= f.Decider.Candidates[i].Kind == Consideration.Press;
            Assert.IsFalse(near, "반경 밖");
            f.Move(AwayMf, new Vec2(52f, 20f));
            f.Decide(AwayMf, new SeedRng(3UL), true);
            for (int i = 0; i < f.Decider.Candidates.Count; i++) near |= f.Decider.Candidates[i].Kind == Consideration.Press;
            Assert.IsTrue(near, "창 안·반경 안이면 지정 밖도 압박 후보");
        }

        [Test]
        public void Candidates_LooseBall_NoOwner()
        {
            var f = new DecisionFixture();
            f.W.Owner = -1;
            f.W.Ball = new Vec2(55f, 30f);
            f.Refresh();
            f.Decide(Fw, new SeedRng(4UL), true);
            var loose = false;
            for (int i = 0; i < f.Decider.Candidates.Count; i++) loose |= f.Decider.Candidates[i].Kind == Consideration.LooseBall;
            Assert.IsTrue(loose);
        }

        [Test]
        public void Candidates_MarkAndCover_GoalSide_FR16_FR19b()
        {
            var f = new DecisionFixture();
            const int def = 13; // 원정 수비 80·42
            f.W.MarkTarget[def] = 10; // 홈 FW 62·40
            f.Decide(def, new SeedRng(5UL), true);
            var c = f.Decider.Candidates;
            var ownGoal = f.W.OwnGoal(1);
            bool mark = false, cover = false;
            for (int i = 0; i < c.Count; i++)
            {
                if (c[i].Kind == Consideration.Mark)
                {
                    mark = true;
                    var opp = f.W.Pos[10];
                    var d = (c[i].Point - opp).Length();
                    Assert.That(d, Is.InRange(1f - 1e-3f, 2f + 1e-3f), "상대–골 선 위 1~2 m");
                    Assert.Less((c[i].Point - ownGoal).Length(), (opp - ownGoal).Length(), "골 쪽");
                }
                if (c[i].Kind == Consideration.Cover) { cover = true; Assert.Less((c[i].Point - ownGoal).Length(), (f.W.Ball - ownGoal).Length()); }
            }
            Assert.IsTrue(mark && cover);
        }

        [Test]
        public void Candidates_Support_WithinRange_FR19()
        {
            var f = new DecisionFixture();
            f.Decide(Mf, new SeedRng(6UL), true);
            var c = f.Decider.Candidates;
            for (int i = 0; i < c.Count; i++)
                if (c[i].Kind == Consideration.Support)
                {
                    var d = (c[i].Point - f.W.Pos[6]).Length();
                    Assert.That(d, Is.InRange(f.P.SupportMin * 0.7f, f.P.SupportMax * 1.1f), "지원 거리 (앵커 당김 반영)");
                    return;
                }
            Assert.Fail("지원 후보 없음");
        }

        // ── 유지 · RNG (AC-09·20) ──

        [Test]
        public void Hold_WithinMinTime_NoSoftmaxRng_AC09()
        {
            var f = new DecisionFixture();
            var rng = new SeedRng(7UL);
            f.Decide(Fw, rng); // 첫 결정 (재추첨)
            f.W.Tick += 1;
            var before = new SeedRng(rng.SaveState());
            var r = f.Decide(Fw, rng);
            Assert.IsFalse(r.Rerolled, "최소 유지 시간 안");
            before.NextFloat(); // 집중 저하 굴림
            for (int i = 0; i < f.Decider.Candidates.Count; i += 2) { before.NextFloat(); before.NextFloat(); } // 노이즈 표본 (후보 2개당 균등 2개)
            Assert.AreEqual(before.SaveState(), rng.SaveState(), "재추첨 안 하면 softmax 난수 소비 없음");
        }

        [Test]
        public void Hold_CloseCandidates_AtMostOneSwitchIn2s_TargetStep2m_AC20()
        {
            var f = new DecisionFixture();
            var rng = new SeedRng(8UL);
            int switches = 0;
            var prevKind = f.Decide(Mf, rng).Kind;
            var prevPoint = f.State[Mf].Point;
            for (int k = 1; k <= 20; k++)
            {
                f.W.Tick += 1;
                f.W.Anchor[Mf] = f.W.Anchor[Mf] + new Vec2(0.3f, 0f); // 앵커가 천천히 움직임
                var r = f.Decide(Mf, rng);
                if (r.Kind != prevKind) switches++;
                else Assert.LessOrEqual((f.State[Mf].Point - prevPoint).Length(), f.P.TargetMaxStep + 1e-4f, $"tick {k}");
                prevKind = r.Kind;
                prevPoint = f.State[Mf].Point;
            }
            Assert.LessOrEqual(switches, 1);
        }

        [Test]
        public void Trigger_Rerolls()
        {
            var f = new DecisionFixture();
            var rng = new SeedRng(9UL);
            f.Decide(Fw, rng);
            f.W.Tick += 1;
            Assert.IsTrue(f.Decide(Fw, rng, true).Rerolled);
        }

        // ── 공 몰림 (AC-12) ──

        [Test]
        public void Crowding_FourthPlayerLessBallward_AC12()
        {
            int Ballward(DecisionParams p)
            {
                var f = new DecisionFixture(p);
                f.W.Owner = -1;
                f.W.Ball = new Vec2(50f, 34f);
                f.Move(5, new Vec2(48f, 32f)); f.Move(6, new Vec2(52f, 36f)); f.Move(8, new Vec2(50f, 30f)); // 이미 3명
                f.Move(Mf, new Vec2(56f, 38f));
                var d = f.Distribution(Mf, 12UL, 1000);
                return d[(int)Consideration.LooseBall] + d[(int)Consideration.Support];
            }
            var with = Ballward(DecisionParams.Defaults);
            var without = Ballward(DecisionFixture.With(("dec.crowd.penalty", 1f)));
            Assert.Greater(Stat.Z(without, 1000, with, 1000), Stat.Z01, $"억제 {with} vs 없음 {without}");
        }

        [Test]
        public void Crowding_YieldOscillationEndsIn2s_AC12()
        {
            var f = new DecisionFixture();
            f.W.Owner = -1;
            f.W.Ball = new Vec2(50f, 34f);
            f.Move(5, new Vec2(46f, 34f));
            f.Move(Mf, new Vec2(54f, 34f));
            var rng = new SeedRng(13UL);
            var last = new Consideration[2];
            int lateSwitches = 0;
            for (int k = 0; k < 30; k++)
            {
                var a = f.Decide(5, rng).Kind;
                var b = f.Decide(Mf, rng).Kind;
                if (k >= 20) { if (a != last[0]) lateSwitches++; if (b != last[1]) lateSwitches++; }
                last[0] = a; last[1] = b;
                f.W.Tick += 1;
            }
            Assert.AreEqual(0, lateSwitches, "2 s 뒤에는 번갈아 바꾸지 않는다");
        }

        // ── 침투 타이밍 (AC-22) ──

        [Test]
        public void Run_OffBall20_EarlierAndLessOffside_AC22()
        {
            void Measure(float offBall, out double meanStart, out int offside, out int started)
            {
                var f = new DecisionFixture();
                var ant = offBall;
                f.SetKin(Fw, Kin(60f, 60f, ant));
                f.W.Mind[Fw] = DecisionAttributes.Average.With(offBall: offBall);
                var place = new SeedRng(77UL);
                var rng = new SeedRng(78UL);
                long sum = 0;
                offside = 0;
                started = 0;
                for (int trial = 0; trial < 1000; trial++)
                {
                    var x = 77f + 5f * place.NextFloat();
                    f.Move(Fw, new Vec2(x, 30f));
                    f.ResetState(Fw);
                    f.GiveBall(6, 0);
                    for (int k = 0; k < 25; k++)
                    {
                        f.W.Tick = k;
                        if (f.Decide(Fw, rng).Kind != Consideration.RunInBehind) continue;
                        sum += k;
                        started++;
                        if (x > f.W.OffsideLineX[1]) offside++;
                        break;
                    }
                }
                meanStart = started > 0 ? (double)sum / started : 99;
            }
            Measure(100f, out var hiStart, out var hiOff, out var hiN);
            Measure(0f, out var loStart, out var loOff, out var loN);
            Assert.Greater(hiN, 100, "침투 표본");
            Assert.Greater(loN, 100);
            Assert.Less(hiStart, loStart, $"출발 틱 평균 OffBall20 {hiStart:F2} vs OffBall1 {loStart:F2}");
            Assert.Greater(Stat.Z(loOff, loN, hiOff, hiN), Stat.Z01, $"오프사이드 비율 OffBall1 {loOff}/{loN} vs OffBall20 {hiOff}/{hiN}");
        }

        // ── 쌍둥이 (AC-26·27·28) ──

        [Test]
        public void Twin_PaceAccel_RunInBehindMore_AC26()
        {
            int Runs(float pace)
            {
                var f = new DecisionFixture();
                f.SetKin(Fw, Kin(pace, pace));
                f.Move(Fw, new Vec2(74f, 30f));
                return f.Distribution(Fw, 26UL)[(int)Consideration.RunInBehind];
            }
            var fast = Runs(95f);
            var slow = Runs(20f);
            Assert.Greater(Stat.Z(fast, 1000, slow, 1000), Stat.Z01, $"빠름 {fast} vs 느림 {slow}");
        }

        [Test]
        public void Twin_Tendency_RunsDirectionPress_DistributionDiffers_AC27()
        {
            int[] Dist(int s, Action<PlayStyle> set, ulong seed, Action<DecisionFixture> scene = null)
            {
                var f = new DecisionFixture();
                scene?.Invoke(f);
                var st = PlayStyle.Neutral();
                set(st);
                f.Style.SetStyle(s, st);
                return f.Distribution(s, seed);
            }
            Assert.IsTrue(Stat.ChiSquareDiffers(Dist(Fw, st => st.Runs = 10f, 27UL), Dist(Fw, st => st.Runs = 90f, 27UL), out var c1, out _), $"침투 χ² {c1:F1}");
            Assert.IsTrue(Stat.ChiSquareDiffers(Dist(8, st => st.Direction = 10f, 28UL), Dist(8, st => st.Direction = 90f, 28UL), out var c2, out _), $"방향 χ² {c2:F1}");
            Assert.IsTrue(Stat.ChiSquareDiffers(Dist(AwayPresser, st => st.PressAggression = 10f, 29UL), Dist(AwayPresser, st => st.PressAggression = 90f, 29UL), out var c3, out _), $"압박 χ² {c3:F1}");
        }

        [Test]
        public void Twin_LineBreaker_RunInBehindMore_AC27()
        {
            int Runs(int level)
            {
                var f = new DecisionFixture();
                f.Style.SetStyle(Fw, PlayStyle.Neutral().WithHabit(Habit.LineBreaker, level));
                return f.Distribution(Fw, 30UL)[(int)Consideration.RunInBehind];
            }
            var lb = Runs(1);
            var none = Runs(0);
            Assert.Greater(Stat.Z(lb, 1000, none, 1000), Stat.Z01, $"라인 브레이커 {lb} vs 없음 {none}");
        }

        [Test]
        public void Twin_Temperament_PressMore_AC28()
        {
            int Press(float temper)
            {
                var f = new DecisionFixture();
                f.W.Mind[AwayPresser] = DecisionAttributes.Average.With(temperament: temper);
                var d = f.Distribution(AwayPresser, 31UL);
                return d[(int)Consideration.Press] + d[(int)Consideration.CoverShadow];
            }
            var hot = Press(95f);
            var calm = Press(5f);
            Assert.Greater(Stat.Z(hot, 1000, calm, 1000), Stat.Z01, $"다혈질 {hot} vs 침착 {calm}");
            Assert.Greater(OffBallScorer.TackleAttemptScale(DecisionParams.Defaults, 95f), OffBallScorer.TackleAttemptScale(DecisionParams.Defaults, 5f), "태클 시도 효용");
        }

        [Test]
        public void Twin_WorkRatePro_RecoverySprintMore_AC28()
        {
            void Count(float v, out int recovery, out int sprint)
            {
                var f = new DecisionFixture();
                const int s = 19; // 원정 FW 52·30 → 공 45·26보다 앞(원정 −x 공격)
                f.Move(s, new Vec2(38f, 30f), false); // 공보다 7 m 자기 골 반대쪽
                f.W.C[s] = 0.2f;
                f.Refresh();
                f.W.Mind[s] = DecisionAttributes.Average.With(workRate: v, professionalism: v);
                recovery = 0;
                sprint = 0;
                var rng = new SeedRng(32UL);
                for (int i = 0; i < 1000; i++)
                {
                    f.ResetState(s);
                    f.State[s].Intensity = Intensity.Run;
                    var r = f.Decide(s, rng, true);
                    if (r.Kind == Consideration.RecoveryRun) recovery++;
                    if (r.Intensity == Intensity.Sprint) sprint++;
                }
            }
            Count(95f, out var rHi, out var sHi);
            Count(5f, out var rLo, out var sLo);
            Assert.Greater(Stat.Z(rHi, 1000, rLo, 1000), Stat.Z01, $"회복 달리기 {rHi} vs {rLo}");
            Assert.Greater(Stat.Z(sHi, 1000, sLo, 1000), Stat.Z01, $"질주 {sHi} vs {sLo}");
        }

        [Test]
        public void Role_AnchorVsTrequartista_RoamDistance_AC28()
        {
            double Roam(PlayStyle st)
            {
                var f = new DecisionFixture();
                f.Style.SetStyle(Mf, st);
                var rng = new SeedRng(33UL);
                double sum = 0;
                for (int i = 0; i < 1000; i++)
                {
                    f.ResetState(Mf);
                    var r = f.Decide(Mf, rng, true);
                    sum += (r.Point - f.W.Anchor[Mf]).Length();
                }
                return sum / 1000;
            }
            var anchor = Roam(PlayStyle.Anchor());
            var treq = Roam(PlayStyle.Trequartista());
            Assert.Less(anchor, treq, $"앵커 {anchor:F2} m vs 트레콸르티스타 {treq:F2} m");
        }

        // ── 경계 ──

        [Test]
        public void Attributes_Extremes_NoNaN()
        {
            foreach (var v in new[] { 0f, 100f })
            {
                var f = new DecisionFixture();
                f.W.Mind[Fw] = new DecisionAttributes(v, v, v, v, v, v, v, v, v, v, v);
                f.SetKin(Fw, new KinematicAttributes(v, v, v, v, v, v, v));
                f.W.C[Fw] = 1f;
                f.Refresh();
                var r = f.Decide(Fw, new SeedRng(34UL), true);
                Assert.IsFalse(float.IsNaN(r.Point.X) || float.IsNaN(r.Point.Y) || float.IsNaN(r.MaxSpeed));
                Assert.GreaterOrEqual(r.MaxSpeed, 0f);
            }
        }

        [Test]
        public void SpaceQueries_OnlyForRunTarget_FR04()
        {
            var f = new DecisionFixture();
            f.Decide(Fw, new SeedRng(35UL), true);
            Assert.LessOrEqual(f.Space.QueryCount, 1, "피치 컨트롤 질의는 침투 목표점에만");
        }

        [Test]
        public void LooseBall_PassReceiverPriority_FR41()
        {
            var f = new DecisionFixture();
            f.W.Owner = -1;
            f.W.Ball = new Vec2(50f, 30f);
            f.W.PassTarget = 10; // 홈 FW 62·40 (더 가까운 동료가 있어도)
            f.Refresh();
            f.Decide(10, new SeedRng(70UL), true);
            float g10 = -1f;
            for (int i = 0; i < f.Decider.Candidates.Count; i++) if (f.Decider.Candidates[i].Kind == Consideration.LooseBall) g10 = f.Decider.Candidates[i].Gain;
            Assert.AreEqual(f.P.WLoose * f.P.PassReceiveBoost, g10, 1e-5f, "받는 선수 = 순위 무관 우선권");
            f.Decide(6, new SeedRng(71UL), true); // 공 바로 옆 동료
            float g6 = -1f;
            for (int i = 0; i < f.Decider.Candidates.Count; i++) if (f.Decider.Candidates[i].Kind == Consideration.LooseBall) g6 = f.Decider.Candidates[i].Gain;
            Assert.LessOrEqual(g6, f.P.WLoose * f.P.PassMateDamp + 1e-5f, "동료에게 가는 패스는 덜 쫓는다");
        }
    }
}
