using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.Tests.EditMode.Presentation.Highlight;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/086 FR-02·AC-02 (Q2 ⓐ): GK 손 처리 기준은 "접촉 순간 공이 자기 박스 안"(GK 잡기 판정, AerialSystem 222행) 하나다.
    /// 084 T15b ②: 시드 20261349 원정 GK(엔티티 11, 193.7 cm)가 착지점은 박스 안·접촉은 박스 밖이라 손 기준(머리 최저 0)으로
    /// 공 1.29 m에서 헤더를 실행 → 하이라이트 머리–공 24 cm. 실행된 헤더는 언제나 필드 기준 머리 최저(키 × headK − headLow) 이상이어야 한다.
    /// 플랜의 "착지 박스 안·접촉 박스 밖 GK" 고정 상황은 그 경기(20261349)로 대신했다(086 T04 결과 줄).
    /// </summary>
    public class AerialKeeperHandsTests
    {
        static ActionParams P => ActionTestKit.P;

        /// <summary>20261349(084 T15b ② 위반 시드) + 082 고정 시드 일부. 진단과 같은 경기를 공유한다.</summary>
        static readonly ulong[] Seeds = { 20261349UL, 20261247UL, 20261220UL };

        /// <summary>1 서브스텝 낙하 여유 (082 AerialHeadReachTests와 같은 5 cm).</summary>
        const float Slack = 0.05f;

        static float HeightOf(MatchContext ctx, int playerId) => ctx.Actions.World.HeightCm[ctx.Squad.SlotOf(playerId)];

        static bool IsExecuted(int outcome) =>
            outcome == (int)HeaderOutcome.Shot || outcome == (int)HeaderOutcome.Pass || outcome == (int)HeaderOutcome.Clear;

        [Test]
        public void KeeperLandingInBox_ContactOutside_HeaderNeedsFieldHeadLow()
        {
            LiveContactAudit.Prewarm(Seeds);
            var failures = new List<string>();
            int headers = 0, keeperHeaders = 0;
            foreach (var seed in Seeds)
            {
                var sim = LiveContactAudit.Played90(seed);
                var ctx = sim.Context;
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    var e = log[i];
                    if (e.Kind != SimEventKind.Header || !IsExecuted(e.A)) continue;
                    headers++;
                    var slot = ctx.Squad.SlotOf(e.PlayerId);
                    if (Squad.IsKeeperSlot(slot)) keeperHeaders++;
                    var low = AerialModel.HeadLow(P, HeightOf(ctx, e.PlayerId));
                    if (e.Z < low - Slack)
                        failures.Add($"{seed} 틱 {e.Tick} 선수 {e.PlayerId}(슬롯 {slot}{(Squad.IsKeeperSlot(slot) ? " GK" : "")}): 공 {e.Z:0.00} m < 머리 최저 {low:0.00} m");
                }
            }
            TestContext.WriteLine($"헤더 {headers} (GK {keeperHeaders})");
            Assert.That(headers, Is.GreaterThan(0), "헤더가 있어야 한다");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>박스 안 접촉 GK는 지금처럼 잡기(Keeper) — 헤더로 실행하지 않는다 (011 손 처리 경로 불변).</summary>
        [Test]
        public void KeeperContactInBox_StillKeeperOutcome()
        {
            LiveContactAudit.Prewarm(Seeds);
            var failures = new List<string>();
            foreach (var seed in Seeds)
            {
                var sim = LiveContactAudit.Played90(seed);
                var ctx = sim.Context;
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    var e = log[i];
                    if (e.Kind != SimEventKind.Header || !IsExecuted(e.A)) continue;
                    var slot = ctx.Squad.SlotOf(e.PlayerId);
                    if (!Squad.IsKeeperSlot(slot)) continue;
                    var ball = new Vec3(e.X * ctx.Pitch.LengthM, e.Y * ctx.Pitch.WidthM, e.Z);
                    if (PhysicsStage.InOwnBox(ctx, Squad.TeamOfSlot(slot), ball))
                        failures.Add($"{seed} 틱 {e.Tick}: 박스 안 GK가 헤더 실행");
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }
    }
}
