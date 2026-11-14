using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 온볼 결정자: 버릇 쌍둥이(AC-05 고정 픽스처 대체), 기술 쌍둥이(AC-06), 의도(AC-07), 다이빙(AC-09), 결정론.</summary>
    public class OnBallDeciderTests
    {
        const int N = 1000;

        /// <summary>측면 1v1: 윙어(슬롯 6) 70·58, 정면 3 m에 수비(슬롯 15). 박스 안 동료 9.</summary>
        static OnBallFixture Wing(PlayStyle style = null, float dribbling = 50f)
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(70f, 58f));
            f.Place(15, new Vec2(73f, 58f));
            f.Place(9, new Vec2(94f, 36f));
            f.GiveBall(6);
            if (style != null) f.Style.SetStyle(6, style);
            f.W.Tech[6] = TechnicalAttributes.Average.WithOnBall(dribbling: dribbling);
            return f;
        }

        /// <summary>크로스 구역: 윙어 90·60, 박스 안 동료 9·10.</summary>
        static OnBallFixture CrossScene(PlayStyle style = null)
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(90f, 60f));
            f.Place(9, new Vec2(95f, 34f));
            f.Place(10, new Vec2(97f, 40f));
            f.GiveBall(6);
            if (style != null) f.Style.SetStyle(6, style);
            return f;
        }

        /// <summary>정면 중거리 24 m (81·34) — 동료 9·10은 박스 근처.</summary>
        static OnBallFixture LongShotScene(PlayStyle style = null)
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(81f, 34f));
            f.Place(9, new Vec2(92f, 26f));
            f.Place(10, new Vec2(92f, 44f));
            f.GiveBall(6);
            if (style != null) f.Style.SetStyle(6, style);
            return f;
        }

        /// <summary>박스 모서리 (88·44, 약 19 m), 골문 앞 6 m에 동료 9 (99·32) — 슈팅과 도움 패스가 비슷한 장면.</summary>
        static OnBallFixture BoxScene(PlayStyle style = null)
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(86f, 50f)); // 2026-11-14 specs/071: 071 xG에서 (88, 44)는 기본 슈팅 89 %로 포화 → 버릇 차이가 보이는 박스 모서리 밖
            f.Place(9, new Vec2(99f, 32f));
            f.GiveBall(6);
            if (style != null) f.Style.SetStyle(6, style);
            return f;
        }

        static int Shots(int[] c) => c[(int)OnBallItem.Shoot];

        [Test]
        public void Twin_DribbleAddict_MoreDribble_FewerPasses_AC05()
        {
            var a = Wing().Distribution(6, 101, N);
            var b = Wing(PlayStyle.Neutral().WithHabit(Habit.DribbleAddict, 1)).Distribution(6, 101, N);
            TestContext.WriteLine($"드리블 A {a[(int)OnBallItem.Dribble]} B {b[(int)OnBallItem.Dribble]}, 패스 A {OnBallFixture.Passes(a)} B {OnBallFixture.Passes(b)}");
            Assert.Greater(a[(int)OnBallItem.Dribble], 0);
            Assert.GreaterOrEqual(b[(int)OnBallItem.Dribble], 1.4f * a[(int)OnBallItem.Dribble]);
            Assert.Less(OnBallFixture.Passes(b), OnBallFixture.Passes(a));
        }

        [Test]
        public void Twin_CrossMachine_MoreCrosses_AC05()
        {
            var a = CrossScene().Distribution(6, 102, N);
            var b = CrossScene(PlayStyle.Neutral().WithHabit(Habit.CrossMachine, 1)).Distribution(6, 102, N);
            TestContext.WriteLine($"크로스 A {a[(int)OnBallItem.Cross]} B {b[(int)OnBallItem.Cross]}");
            Assert.Greater(Stat.Z(b[(int)OnBallItem.Cross], N, a[(int)OnBallItem.Cross], N), Stat.Z01);
        }

        [Test]
        public void Twin_LongShotSpammer_MoreLongShots_AC05()
        {
            var a = LongShotScene().Distribution(6, 103, N);
            var b = LongShotScene(PlayStyle.Neutral().WithHabit(Habit.LongShotSpammer, 1)).Distribution(6, 103, N);
            TestContext.WriteLine($"중거리 A {Shots(a)} B {Shots(b)}");
            Assert.Greater(Stat.Z(Shots(b), N, Shots(a), N), Stat.Z01);
        }

        [Test]
        public void Twin_AltruistEgoist_ShotDirection_AC05()
        {
            var a = BoxScene().Distribution(6, 104, N);
            var alt = BoxScene(PlayStyle.Neutral().WithHabit(Habit.Altruist, 1)).Distribution(6, 104, N);
            var ego = BoxScene(PlayStyle.Neutral().WithHabit(Habit.Egoist, 1)).Distribution(6, 104, N);
            TestContext.WriteLine($"슈팅 기본 {Shots(a)} 이타 {Shots(alt)} 이기 {Shots(ego)}, 패스 기본 {OnBallFixture.Passes(a)} 이타 {OnBallFixture.Passes(alt)} 이기 {OnBallFixture.Passes(ego)}");
            Assert.Greater(Stat.Z(Shots(a), N, Shots(alt), N), Stat.Z01, "이타적: 슈팅 감소");
            Assert.Greater(Stat.Z(Shots(ego), N, Shots(a), N), Stat.Z01, "이기주의: 슈팅 증가");
            Assert.Less(OnBallFixture.Passes(ego), OnBallFixture.Passes(a));
        }

        [Test]
        public void Twin_Dribbling1vs20_MoreDribbleAndSuccess_AC06()
        {
            const int N = 3000; // 돌파 판정 7 m(T11b) 뒤 선택 빈도가 낮아 표본을 늘린다
            int pick1 = 0, ok1 = 0, pick20 = 0, ok20 = 0;
            var roll = new SeedRng(77);
            Wing(dribbling: 0f).Distribution(6, 106, N, r => { if (r.Item == OnBallItem.Dribble) { pick1++; if (roll.NextFloat() < r.P) ok1++; } });
            roll = new SeedRng(77);
            Wing(dribbling: 100f).Distribution(6, 106, N, r => { if (r.Item == OnBallItem.Dribble) { pick20++; if (roll.NextFloat() < r.P) ok20++; } });
            TestContext.WriteLine($"드리블 선택 1: {pick1}(성공 {ok1}) 20: {pick20}(성공 {ok20})");
            Assert.Greater(Stat.Z(pick20, N, pick1, N), Stat.Z01, "Dribbling 20이 더 자주 고른다 (070 대체 성공 확률 경유)");
            Assert.Greater(pick1, 0);
            Assert.Greater(Stat.Z(ok20, pick20, ok1, pick1), Stat.Z01, "성공률도 높다");
        }

        /// <summary>컷백: 캐리어 101·50 (바이라인 근처), 동료 X = 슬롯 9 (93·36).</summary>
        static OnBallFixture Cutback(bool intent)
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(101f, 50f));
            f.Place(9, new Vec2(93f, 36f));
            f.Place(10, new Vec2(90f, 28f));
            f.GiveBall(6);
            f.W.Mind[6] = DecisionAttributes.Average.With(teamwork: 100f, vision: 100f);
            if (intent) f.W.Intent[6] = new IntentPacket { Has = true, StepKey = 7, Role = 0, Kind = Consideration.Pass, NextPassTarget = 9, InWindow = true, TargetSlot = -1 };
            return f;
        }

        [Test]
        public void Intent_Cutback_PassToXMoreOften_AC07()
        {
            int with = 0, without = 0;
            Cutback(true).Distribution(6, 107, N, r => { if (OnBallItems.IsPass(r.Item) && r.Target == 9) with++; });
            Cutback(false).Distribution(6, 107, N, r => { if (OnBallItems.IsPass(r.Item) && r.Target == 9) without++; });
            TestContext.WriteLine($"X 패스 의도 {with} 없음 {without}");
            Assert.Greater(Stat.Z(with, N, without, N), Stat.Z01);
        }

        [Test]
        public void Intent_ShotFarAboveThetaOpp_ShotsRelease_AC07()
        {
            var f = new OnBallFixture();
            f.OnlyOwnerOf(6);
            f.Place(6, new Vec2(99f, 34f));
            f.Place(9, new Vec2(80f, 34f));
            for (int o = 12; o < 22; o++) f.W.Active[o] = false; // 골문 앞이 열림 (GK만)
            f.GiveBall(6);
            f.W.Mind[6] = DecisionAttributes.Average.With(teamwork: 100f, vision: 100f);
            f.W.Intent[6] = new IntentPacket { Has = true, StepKey = 3, Role = 0, Kind = Consideration.Pass, NextPassTarget = 9, InWindow = true, TargetSlot = -1 };
            int shots = 0, shotRelease = 0, xPass = 0, xRelease = 0;
            f.Distribution(6, 108, N, r =>
            {
                if (r.Source == DecisionSource.IntentIgnored) return; // 준수 굴림 실패(Teamwork 100이어도 최대 0.98) → 의도 없음
                if (r.Item == OnBallItem.Shoot) { shots++; if (r.ReleaseIntent) shotRelease++; }
                if (OnBallItems.IsPass(r.Item) && r.Target == 9) { xPass++; if (r.ReleaseIntent) xRelease++; }
            });
            TestContext.WriteLine($"슈팅 {shots} (해제 {shotRelease}), X 패스 {xPass} (해제 {xRelease})");
            Assert.Greater(shots, 0, "의도보다 θ_opp 이상 좋은 슈팅은 선택될 수 있다");
            Assert.AreEqual(shots, shotRelease, "선택마다 해제 통보 1건");
            Assert.AreEqual(0, xRelease);
        }

        [Test]
        public void Twin_Diver_MoreDivesInBox_OneFoulValueEach_AC09()
        {
            OnBallFixture Scene(int lvl)
            {
                var f = new OnBallFixture();
                f.Place(6, new Vec2(95f, 30f));
                f.Place(15, new Vec2(96.2f, 30f), new Vec2(-2f, 0f));
                f.GiveBall(6);
                if (lvl > 0) f.Style.SetStyle(6, PlayStyle.Neutral().WithHabit(Habit.Diver, lvl));
                return f;
            }
            int a = 0, b = 0, bad = 0;
            Scene(0).Distribution(6, 109, N, r => { if (r.Item == OnBallItem.Dive) { a++; if (!(r.FoulValue > 0f)) bad++; } });
            Scene(1).Distribution(6, 109, N, r => { if (r.Item == OnBallItem.Dive) { b++; if (!(r.FoulValue > 0f)) bad++; } });
            TestContext.WriteLine($"넘어지기 기본 {a} 다이버 {b}");
            Assert.Greater(Stat.Z(b, N, a, N), Stat.Z01);
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void SameSeed_SameDecisions_Determinism()
        {
            var x = Wing().Distribution(6, 555, 200);
            var y = Wing().Distribution(6, 555, 200);
            CollectionAssert.AreEqual(x, y);
        }
    }
}
