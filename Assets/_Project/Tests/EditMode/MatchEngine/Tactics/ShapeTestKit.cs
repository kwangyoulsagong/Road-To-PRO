using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    /// <summary>009 테스트 공용: 튜닝·포메이션 로드, 정렬된 팀 프레임 생성.</summary>
    public static class ShapeTestKit
    {
        public const float PitchL = 105f, PitchW = 68f;
        static ShapeData _data;
        static readonly object Gate = new object();

        static string Root => Directory.GetCurrentDirectory();
        public static string FormationsJson => File.ReadAllText(Path.Combine(Root, "Data", "tactics", "formations.json"));

        public static ShapeData Data()
        {
            lock (Gate)
            {
                return _data ??= new ShapeData(
                    TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(Root, "Data", "tuning", "shape.json"))),
                    FormationCatalog.Parse(FormationsJson));
            }
        }

        public static ShapeParams Params() => ShapeParams.Create(Data().Tuning, PitchSpec.Default);
        public static FormationCatalog Catalog => Data().Formations;

        public static TeamShapeEngine Engine(string formation = "4-4-2", int team = 0, ShapeInputProviders inputs = null) =>
            new TeamShapeEngine(Params(), Catalog, inputs ?? new ShapeInputProviders(), team, Catalog.IndexOf(formation));

        /// <summary>전원 활성, 위치는 슬롯 번호로 흩어 놓은 프레임.</summary>
        public static TeamFrame Frame(TeamPhase phase, Vec2 ball, long tick = 0)
        {
            var f = new TeamFrame { Tick = tick, Period = 0, Phase = phase, Block = TeamPhase.DefMid, Ball = ball, OppOffsideLine = 0.5f };
            for (int i = 0; i < TeamFrame.N; i++)
            {
                f.OwnActive[i] = f.OppActive[i] = true;
                f.Own[i] = new Vec2(0.1f + 0.06f * i, 0.1f + 0.07f * i);
                f.Opp[i] = new Vec2(0.9f - 0.06f * i, 0.9f - 0.07f * i);
            }
            return f;
        }

        public static TeamInstructions Ins(float lineHeight = 0.5f)
        {
            var i = TeamInstructions.Default;
            i.LineHeight = lineHeight;
            return i;
        }

        /// <summary>형태 수렴까지 틱을 돌린다 (오차 끔, 보간 끝남).</summary>
        public static void Settle(TeamShapeEngine e, TeamFrame f, in TeamInstructions ins, int ticks = 40, bool errors = false)
        {
            var rng = new SeedRng(77UL);
            var s = PressSignals.None;
            for (int t = 0; t < ticks; t++)
            {
                f.Tick++;
                e.Step(f, in ins, null, in s, rng, null, errors);
            }
        }

        public static MatchTuning TuningWithShape()
        {
            var t = MatchTestKit.Tuning();
            return new MatchTuning(t.Ball, t.Kinematics, t.Match, Data(), goalkeeper: t.Goalkeeper);
        }

        public static MatchSetup Setup(string home = "4-3-3", string away = "4-4-2", ulong seed = 20261109UL)
        {
            var s = MatchTestKit.Setup(seed);
            s.Home.Tactics = new TeamTactics { FormationId = home };
            s.Away.Tactics = new TeamTactics { FormationId = away };
            return s;
        }

        public static MatchSim Sim(MatchSimOptions o = null, MatchSetup setup = null) => new MatchSim(setup ?? Setup(), TuningWithShape(), o);
    }
}
