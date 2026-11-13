using System;
using System.Collections.Generic;
using System.IO;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Match.Stages.StandIn;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>
    /// 006 테스트 공용: 튜닝 3종 로드, 표준 경기 설정(4-4-2, 선발 11 + 벤치 7, 가상 선수 id: 홈 1~18, 원정 101~118).
    /// </summary>
    public static class MatchTestKit
    {
        static string Tuning(string f) => Path.Combine(Directory.GetCurrentDirectory(), "Data", "tuning", f);

        static MatchTuning _tuning;
        static readonly object Gate = new object();

        public static MatchTuning Tuning()
        {
            lock (Gate)
            {
                return _tuning ??= new MatchTuning(
                    TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Tuning("ball.json"))),
                    TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Tuning("kinematics.json"))),
                    TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Tuning("match.json"))),
                    goalkeeper: Goalkeeper(), decision: DecisionTuning(), action: ActionTuning());
            }
        }

        /// <summary>골키퍼 튜닝 기본값 (specs/011).</summary>
        public static TuningBundle DecisionTuning() => TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Tuning("decision.json")));

        /// <summary>액션 모델 튜닝 기본값 (specs/008).</summary>
        public static TuningBundle ActionTuning() => TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Tuning("action.json")));

        public static TuningBundle Goalkeeper() => TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Tuning("goalkeeper.json")));

        public static TuningRegistry MatchRegistry() => TuningLoader.LoadDefinitionsFile(Tuning("match.json"));

        /// <summary>match.json 일부 값을 바꾼 튜닝.</summary>
        public static MatchTuning TuningWith(string matchValuesJson)
        {
            var t = Tuning();
            return new MatchTuning(t.Ball, t.Kinematics, TuningLoader.ParseBundle(MatchRegistry(), matchValuesJson), goalkeeper: t.Goalkeeper, decision: t.Decision, action: t.Action);
        }

        public static readonly Vec2[] Formation442 =
        {
            new Vec2(0.04f, 0.50f),
            new Vec2(0.22f, 0.15f), new Vec2(0.20f, 0.38f), new Vec2(0.20f, 0.62f), new Vec2(0.22f, 0.85f),
            new Vec2(0.45f, 0.15f), new Vec2(0.42f, 0.38f), new Vec2(0.42f, 0.62f), new Vec2(0.45f, 0.85f),
            new Vec2(0.66f, 0.40f), new Vec2(0.66f, 0.60f),
        };

        public static TeamSetup Team(int baseId, SeedRng rng, int bench = 7)
        {
            var n = 11 + bench;
            var players = new MatchPlayer[n];
            for (int i = 0; i < n; i++)
            {
                var a = new KinematicAttributes(rng.Range(30f, 90f), rng.Range(30f, 90f), rng.Range(30f, 90f), rng.Range(30f, 90f),
                    rng.Range(30f, 90f), rng.Range(30f, 90f), rng.Range(30f, 90f));
                players[i] = new MatchPlayer(baseId + i, a);
            }
            var starters = new int[11];
            for (int i = 0; i < 11; i++) starters[i] = baseId + i;
            var benchIds = new int[bench];
            for (int i = 0; i < bench; i++) benchIds[i] = baseId + 11 + i;
            return new TeamSetup
            {
                TeamId = baseId,
                Players = players,
                Starters = starters,
                Bench = benchIds,
                CaptainId = baseId + 5,
                Formation = (Vec2[])Formation442.Clone(),
            };
        }

        public static MatchSetup Setup(ulong seed = 20261103UL, CompetitionRules rules = null, int bench = 7)
        {
            var rng = new SeedRng(seed ^ 0x5EEDUL);
            return new MatchSetup
            {
                Seed = seed,
                Home = Team(1, rng, bench),
                Away = Team(101, rng, bench),
                Rules = rules ?? CompetitionRules.Default(),
            };
        }

        public static MatchSim Sim(ulong seed = 20261103UL, MatchSimOptions options = null, CompetitionRules rules = null, MatchTuning tuning = null) =>
            new MatchSim(Setup(seed, rules), tuning ?? Tuning(), options);

        public static int Count(MatchSim sim, SimEventKind kind)
        {
            int c = 0;
            for (int i = 0; i < sim.Log.Count; i++) if (sim.Log[i].Kind == kind) c++;
            return c;
        }

        public static int FirstIndex(MatchSim sim, SimEventKind kind, int from = 0)
        {
            for (int i = from; i < sim.Log.Count; i++) if (sim.Log[i].Kind == kind) return i;
            return -1;
        }

        public static MatchSimOptions WithCheckpoints(CheckpointMode mode) => new MatchSimOptions { Checkpoints = mode };

        public static string Values(params (string id, float v)[] kv)
        {
            var parts = new List<string>();
            foreach (var (id, v) in kv) parts.Add("{\"id\":\"" + id + "\",\"value\":" + v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
            return "{\"version\":1,\"values\":[" + string.Join(",", parts) + "]}";
        }

        /// <summary>골 세리머니·재시작 지연 0 (정지 시간이 주입한 것만 남는다).</summary>
        public static MatchTuning NoDelayTuning() => TuningWith(Values(("standin.goalCelebration", 0f), ("standin.restartDelay", 0f), ("standin.subDelay", 0f)));

        // ── 참조 경기 (여러 테스트가 공유, 결정론이라 한 번만 돌린다) ──

        static MatchSim _reference;
        static readonly Dictionary<long, ulong> _referenceFrames = new Dictionary<long, ulong>();
        public const ulong ReferenceSeed = 20261103UL;

        /// <summary>표준 경기를 체크포인트 All로 끝까지 돌린 것. 프레임 해시(인덱스 → 해시)도 모은다.</summary>
        public static MatchSim Reference()
        {
            lock (Gate)
            {
                if (_reference != null) return _reference;
                var sim = Sim(ReferenceSeed, WithCheckpoints(CheckpointMode.All));
                sim.Recorder.Sink = new FrameHashSink(_referenceFrames);
                _referenceFrames[0] = sim.Recorder.InitialFrame.Hash();
                sim.RunToEnd();
                sim.Recorder.Sink = null;
                _reference = sim;
                return sim;
            }
        }

        public static IReadOnlyDictionary<long, ulong> ReferenceFrames { get { Reference(); return _referenceFrames; } }

        sealed class FrameHashSink : IReplayFrameSink
        {
            readonly Dictionary<long, ulong> _d;
            public FrameHashSink(Dictionary<long, ulong> d) => _d = d;
            public void OnFrame(ReplayFrame f) => _d[f.Index] = f.Hash();
        }

        // ── 스크립트 경기: 선수는 가만히, 테스트가 틱마다 상태를 조작 ──

        /// <summary>단계 5 자리에서 틱마다 테스트 동작을 부르는 단계 (결정 없음 — 선수는 멈춰 있다).</summary>
        public sealed class ScriptedStage : StatelessStage
        {
            readonly Action<MatchContext> _onTick;
            public ScriptedStage(Action<MatchContext> onTick) => _onTick = onTick;
            public override StageId Id => StageId.ScenarioOrUtility;
            public override void Execute(MatchContext ctx) => _onTick?.Invoke(ctx);
        }

        public static IMatchStage[] ScriptedStages(Action<MatchContext> onTick) => new IMatchStage[]
        {
            new NoOpStage(StageId.TeamTactics), new NoOpStage(StageId.Phase), new HoldShapeStage(), new NoOpStage(StageId.RoleAdjust),
            new NoOpStage(StageId.Tendency), new ScriptedStage(onTick), new SimpleSteeringStage(), new PhysicsStage(), new SimpleRefereeStage(), new PublishStage(),
        };

        /// <summary>
        /// 스크립트 경기. 킥오프 데드볼을 바로 인플레이로 바꾸지 않으면 경기 내내 데드볼이다 (autoInPlay=false).
        /// 골·아웃이 없어 정지 시간은 주입한 것뿐이다.
        /// </summary>
        public static MatchSim Scripted(Action<MatchContext> onTick, CompetitionRules rules = null, MatchSimOptions options = null, ulong seed = 7UL, int bench = 7)
        {
            var o = options ?? new MatchSimOptions();
            o.Stages = () => ScriptedStages(onTick);
            return new MatchSim(Setup(seed, rules, bench), NoDelayTuning(), o);
        }

        public static List<SimEvent> Events(MatchSim sim, SimEventKind kind)
        {
            var l = new List<SimEvent>();
            for (int i = 0; i < sim.Log.Count; i++) if (sim.Log[i].Kind == kind) l.Add(sim.Log[i]);
            return l;
        }

        /// <summary>테스트용 단계 감싸기: 실행 순서를 기록한다.</summary>
        public sealed class RecordingStage : IMatchStage
        {
            readonly IMatchStage _inner;
            readonly List<StageId> _log;
            public RecordingStage(IMatchStage inner, List<StageId> log) { _inner = inner; _log = log; }
            public StageId Id => _inner.Id;
            public void Execute(MatchContext ctx) { _log.Add(_inner.Id); _inner.Execute(ctx); }
            public void WriteState(StateWriter w) => _inner.WriteState(w);
            public void ReadState(StateReader r) => _inner.ReadState(r);
            public void AddTo(ref StableHasher h) => _inner.AddTo(ref h);
        }
    }
}
