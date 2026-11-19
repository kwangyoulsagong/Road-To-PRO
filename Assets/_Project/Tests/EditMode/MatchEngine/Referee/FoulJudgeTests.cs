using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-07·23 (FR-21·22·26, IFAB Law 12): 진짜 판정 표 20행(P1 계수), 반복 반칙, 실경기 태클 파울률.</summary>
    public class FoulJudgeTests
    {
        const int N = EventCodes.DecisionNone, F = EventCodes.DecisionFoul, Y = EventCodes.DecisionYellow, R = EventCodes.DecisionRed;

        static ContactDetail Slide(float i, float s, bool master)
        {
            var c = Contact(i, studs: s, sliding: true);
            if (master) c.HabitBits = ContactDetail.HabitSlideMaster;
            return c;
        }

        [Test]
        public void Law12_TrueDecision_Table20()
        {
            // 012 T29 보정 계수 (wS 0.08, 임계 0.40 · 0.55 · 0.80) 기준 기대값
            var rows = new (ContactDetail c, int expect, string why)[]
            {
                (Contact(0.3f, ballFirst: 1f), N, "공 먼저 + 낮은 강도"),
                (Contact(0.5f), N, "강도 0.5만"),
                (Contact(0.8f), N, "강도만 0.8 (점수 0.32)"),
                (Contact(0.8f, behind: 1f), F, "강도 + 뒤에서"),
                (Contact(1f, behind: 1f, studs: 1f, height: 1.2f, late: 1f), R, "뒤에서 + 발바닥 + 고강도 + 높음 + 늦음 → 퇴장"),
                (Contact(0.95f, behind: 0.5f, studs: 0.5f, late: 0.5f), Y, "중간 강도 무모 → 경고"),
                (Contact(0.6f, behind: 0.5f, late: 0.5f), N, "부주의 미만 (0.39)"),
                (Contact(0.6f, studs: 1f), N, "발바닥 + 중간 강도 (0.32)"),
                (Contact(0.7f, behind: 0.5f, studs: 1f), F, "발바닥 + 뒤에서"),
                (Contact(0.7f, behind: 0.5f, studs: 1f, ballFirst: 0.5f), N, "공 반쯤 먼저 → 한 단계 낮음"),
                (Contact(1f, height: 2.4f), F, "높은 접촉"),
                (Contact(0.5f, height: 0.6f), N, "중간 높이 약한 접촉"),
                (Contact(0.2f, late: 1f), N, "늦었지만 약함"),
                (Contact(1f, behind: 1f, late: 1f), Y, "뒤에서 늦은 강한 태클"),
                (Contact(1f, behind: 1f, studs: 1f, late: 1f, ballFirst: 1f), N, "공 먼저면 위험해도 파울 아님"),
                (Slide(0.8f, 0.5f, true), F, "슬라이딩 장인 ×1.4 (0.504)"),
                (Slide(0.8f, 0.5f, false), N, "같은 태클, 버릇 없음 (0.36)"),
                (Contact(0f), N, "강도 0"),
                (Contact(0.9f, behind: 1f, studs: 1f), Y, "강하지만 과도한 힘 미만"),
                (Contact(1f, behind: 1f, studs: 1f, height: 1.2f, late: 0.5f), Y, "과도한 힘 직전 (0.78)"),
            };
            Assert.AreEqual(20, rows.Length);
            for (int i = 0; i < rows.Length; i++)
                Assert.AreEqual(rows[i].expect, FoulJudge.TrueDecision(P, in rows[i].c), $"행 {i + 1}: {rows[i].why} (점수 {FoulJudge.Score(P, in rows[i].c):F3})");
        }

        [Test]
        public void Law12_RepeatFouls_LowerYellowThreshold()
        {
            var avg = RefereeProfile.LeagueAverage;
            var first = FoulJudge.ForReferee(P, avg, 0);
            var third = FoulJudge.ForReferee(P, avg, 2);
            Assert.AreEqual(P.YellowThreshold, first.Yellow, 1e-5f);
            Assert.AreEqual(P.YellowThreshold - P.RepeatFoulDrop, third.Yellow, 1e-5f, "3번째 반칙부터 경고 임계 −0.15");
            var strict = FoulJudge.ForReferee(P, avg.With(strictness: 1f), 0);
            var lenient = FoulJudge.ForReferee(P, avg.With(strictness: 0f), 0);
            Assert.Less(strict.Yellow, lenient.Yellow, "엄격 심판 경고 임계 낮음");
            Assert.Less(strict.Red, lenient.Red);
        }

        // ── AC-23: 실경기 태클 접촉 (평균 능력치 전술 경기 16경기) → 리그 평균 심판 인지 10,000건 ──

        static List<ContactDetail> _harvest;

        static List<ContactDetail> Harvest()
        {
            if (_harvest != null) return _harvest;
            const int matches = 16;
            ShapeTestKit.Setup(seed: 1UL);
            var per = new List<ContactDetail>[matches];
            System.Threading.Tasks.Parallel.For(0, matches, m =>
            {
                var sim = new MatchSim(ShapeTestKit.Setup(seed: 7400UL + (ulong)m), ShapeTestKit.TuningWithShape());
                sim.RunToEnd();
                var log = sim.Context.Actions.Details.Contacts;
                var list = new List<ContactDetail>();
                for (int i = 0; i < log.Count; i++) if (log.IsAvailable(i) && log[i].Kind == ContactKind.Tackle) list.Add(log[i]);
                per[m] = list;
            });
            _harvest = new List<ContactDetail>();
            foreach (var l in per) _harvest.AddRange(l);
            return _harvest;
        }

        static void Rates(out double standing, out double sliding, out double behind, out double front, out int nStand, out int nSlide)
        {
            var cs = Harvest();
            var rng = new SeedRng(1223);
            var r = RefereeProfile.LeagueAverage;
            var th = FoulJudge.ForReferee(P, r, 0);
            int st = 0, stF = 0, sl = 0, slF = 0, be = 0, beF = 0, fr = 0, frF = 0;
            for (int k = 0; k < 10000; k++)
            {
                var c = cs[(int)(rng.NextFloat() * cs.Count) % cs.Count];
                var score = FoulJudge.Score(P, in c);
                var perceived = Perception.Perceive(P, r, score, 0.8f, false, 0f, rng);
                var foul = FoulJudge.Decide(perceived, th) != EventCodes.DecisionNone;
                if (c.Sliding) { sl++; if (foul) slF++; }
                else { st++; if (foul) stF++; }
                if (c.FromBehind > 0.5f) { be++; if (foul) beF++; } else { fr++; if (foul) frF++; }
            }
            standing = st > 0 ? (double)stF / st : 0; sliding = sl > 0 ? (double)slF / sl : 0;
            behind = be > 0 ? (double)beF / be : 0; front = fr > 0 ? (double)frF / fr : 0;
            nStand = st; nSlide = sl;
            TestContext.WriteLine($"태클 접촉 {cs.Count}개 · 표본 10,000: 스탠딩 {standing:P1}({st}) · 슬라이딩 {sliding:P1}({sl}) · 뒤에서 {behind:P1} · 정면 {front:P1}");
        }

        [Test, Category("Slow")]
        public void StandingTackle_10000_Rate10to14()
        {
            Rates(out var st, out _, out _, out _, out var n, out _);
            Assert.Greater(n, 1000);
            Assert.That(st, Is.InRange(0.10, 0.14), "스탠딩 태클 파울률 10~14 % (FR-96)");
        }

        [Test, Category("Slow")]
        public void Sliding_AtLeast2x()
        {
            Rates(out var st, out var sl, out _, out _, out _, out var n);
            Assert.Greater(n, 100);
            Assert.GreaterOrEqual(sl, st * 2, "슬라이딩 ≥ 스탠딩 × 2");
        }

        [Test, Category("Slow")]
        public void FromBehind_Higher()
        {
            Rates(out _, out _, out var be, out var fr, out _, out _);
            Assert.Greater(be, fr, "뒤에서 > 정면");
        }
    }
}
