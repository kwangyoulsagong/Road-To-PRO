using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/070 고정 상황: 실경기 컨텍스트를 인플레이까지 돌린 뒤 결정 단계 없이 물리 단계(단계 7)만 틱 단위로 돌린다.
    /// 선수 이동은 테스트가 운동 명령을 직접 준다. 결투 시스템은 켠다. 단계 밖 이벤트는 임시 로그로 모은다.
    /// </summary>
    public sealed class DuelFixture
    {
        public readonly MatchSim Sim;
        public MatchContext Ctx => Sim.Context;
        public DuelSystem Duels => Ctx.Actions.Duels;
        public readonly System.Collections.Generic.List<SimEvent> Events = new System.Collections.Generic.List<SimEvent>();
        readonly PhysicsStage _phys = new PhysicsStage();

        public static MatchTuning TuningWithAction(string actionValues)
        {
            var t = MatchTestKit.Tuning();
            if (actionValues == null) return t;
            var reg = TuningLoader.LoadDefinitionsFile(Path.Combine(Directory.GetCurrentDirectory(), "Data", "tuning", "action.json"));
            // {"id": 값, ...} 짧은 형식 → 튜닝 값 파일 형식 {"version":1,"values":[{"id":..,"value":..}]}
            var items = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(actionValues, "\"([^\"]+)\"\\s*:\\s*(-?[0-9.]+)"))
                items.Add($"{{\"id\":\"{m.Groups[1].Value}\",\"value\":{m.Groups[2].Value}}}");
            var json = "{\"version\":1,\"values\":[" + string.Join(",", items) + "]}";
            return new MatchTuning(t.Ball, t.Kinematics, t.Match, goalkeeper: t.Goalkeeper, decision: t.Decision, action: TuningLoader.ParseBundle(reg, json));
        }

        public DuelFixture(ulong seed = 7070UL, string actionValues = null)
        {
            Sim = MatchTestKit.Sim(seed, tuning: TuningWithAction(actionValues));
            while (!Ctx.Flow.InPlay) Sim.StepTick();
            Sim.StepTicks(20);
            Ctx.Publisher.Clear();
            Duels.Enabled = true;
            ClearPitch();
        }

        public void ClearPitch()
        {
            AutoChase = -1;
            for (int s = 0; s < Squad.Slots; s++)
                if (Ctx.IsSlotActive(s))
                {
                    Ctx.Kinematics.Place(s, new Vec2(2f + (s % 11) * 1.5f, s < 11 ? 1f : 67f), new Vec2(1f, 0f));
                    Ctx.Kinematics.SetCommandImmediate(s, SteeringCommand.Idle());
                }
            Ctx.Actions.InvalidateWorld();
        }

        public void Put(int slot, Vec2 pos, Vec2 facing)
        {
            Ctx.Kinematics.Place(slot, pos, facing);
            Ctx.Kinematics.SetCommandImmediate(slot, SteeringCommand.Idle());
            Ctx.Actions.InvalidateWorld();
        }

        public void Seek(int slot, Vec2 target, float maxSpeed = 0f) => Ctx.Kinematics.SetCommandImmediate(slot, SteeringCommand.Seek(target, maxSpeed));

        public void Own(int slot)
        {
            var p = Ctx.SlotState(slot);
            var bp = p.Position + p.Facing * 0.45f;
            Ctx.Ball = BallState.Controlled(slot, new Vec3(bp.X, bp.Y, Ctx.BallParams.Radius));
            Ctx.LastTouchTeam = Squad.TeamOfSlot(slot);
            Ctx.LastTouchSlot = slot;
            Duels.ResetDribbleForTest();
        }

        /// <summary>이 슬롯이 공을 가졌고 이격이 열려 있으면 틱마다 공을 최고속도로 쫓게 한다 (010 결정 단계의 ChaseBall 흉내). 끝나면 원래 목표로.</summary>
        public int AutoChase = -1;
        Vec2 _chaseResume;
        float _chaseResumeSpeed;
        bool _chasing;

        public void SeekAndChase(int slot, Vec2 target, float maxSpeed = 0f)
        {
            AutoChase = slot;
            _chaseResume = target;
            _chaseResumeSpeed = maxSpeed;
            _chasing = false;
            Seek(slot, target, maxSpeed);
        }

        /// <summary>물리 단계 1틱 (서브스텝 10).</summary>
        public void Tick()
        {
            if (AutoChase >= 0)
            {
                if (Duels.GapOpenFor(AutoChase)) { Seek(AutoChase, Duels.ChasePoint(Ctx)); _chasing = true; }
                else if (_chasing) { Seek(AutoChase, _chaseResume, _chaseResumeSpeed); _chasing = false; }
            }
            Ctx.Actions.InvalidateWorld();
            var b = Ctx.Board;
            b.Clear();
            b.Enter(RoadToPro.MatchEngine.Match.Pipeline.StageId.Physics);
            _phys.Execute(Ctx);
            b.Leave();
            Flush();
        }

        /// <summary>마지막 틱에 라인 통과 사건이 있었나.</summary>
        public bool CrossedLastTick()
        {
            var b = Ctx.Board;
            for (int i = 0; i < b.PhysicsCallCount; i++) if (b.PhysicsCallAt(i).Kind == RoadToPro.MatchEngine.Match.Pipeline.PhysicsCallKind.BoundaryCrossed) return true;
            return false;
        }

        public void Ticks(int n) { for (int i = 0; i < n; i++) Tick(); }

        public void Flush()
        {
            var log = new SimEventLog(256);
            Ctx.Publisher.Flush(log, null);
            for (int i = 0; i < log.Count; i++) Events.Add(log[i]);
        }

        public int Count(SimEventKind k)
        {
            var c = 0;
            foreach (var e in Events) if (e.Kind == k) c++;
            return c;
        }
    }
}
