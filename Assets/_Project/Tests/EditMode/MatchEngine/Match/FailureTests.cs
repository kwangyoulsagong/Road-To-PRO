using System.Threading.Tasks;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-19 (FR-72): 단계 NaN → 그 경기만 실패 결과, 원인 틱·단계 기록, 같은 배치의 다른 경기는 정상.</summary>
    public class FailureTests
    {
        sealed class NaNSteering : StatelessStage
        {
            public override StageId Id => StageId.SteeringTargets;
            public override void Execute(MatchContext ctx)
            {
                if (ctx.Tick == 300) ctx.Kinematics.SetCommand(4, SteeringCommand.Seek(new Vec2(float.NaN, 3f)));
            }
        }

        [Test]
        public void NaNInStage_FailedResult_OthersOk()
        {
            var bad = Sim(MatchTestKit.ReferenceSeed, new MatchSimOptions
            {
                Stages = () => { var st = MatchSim.DefaultStages(); st[6] = new NaNSteering(); return st; },
            });
            var good = Sim(MatchTestKit.ReferenceSeed);
            var t1 = Task.Run(() => bad.RunToEnd());
            var t2 = Task.Run(() => good.RunToEnd());
            Task.WaitAll(t1, t2);
            var r = t1.Result;
            Assert.That(r.Reason, Is.EqualTo(EndReason.Failed));
            Assert.That(r.FailedTick, Is.EqualTo(300L));
            Assert.That(r.FailedStage, Is.EqualTo((int)StageId.SteeringTargets));
            StringAssert.Contains("NaN", r.FailureMessage);
            var last = r.EventLog[r.EventLog.Length - 1];
            Assert.That(last.Kind, Is.EqualTo(SimEventKind.MatchFailed));
            Assert.That(last.A, Is.EqualTo((int)StageId.SteeringTargets));
            Assert.That(r.Validate(), Is.Empty);

            Assert.That(t2.Result.Reason, Is.EqualTo(EndReason.Normal));
            Assert.That(t2.Result.EventLogHash, Is.EqualTo(Reference().Result.EventLogHash), "다른 경기는 영향 없음");
        }
    }
}
