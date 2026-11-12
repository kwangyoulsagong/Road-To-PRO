using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>
    /// 010 고정 장면 픽스처: MatchContext 없이 <see cref="OffBallDecider"/>를 직접 부른다(단계와 같은 경로).
    /// 기본 장면: 홈(0~10)이 +x로 공격, 슬롯 6(MF, 45·26)이 공을 가짐. 원정 수비 라인 x = 80.
    /// </summary>
    public sealed class DecisionFixture
    {
        public readonly DecisionParams P;
        public readonly DecisionWorld W = new DecisionWorld();
        public readonly KinematicsParams Kp;
        public readonly StandInStyleSource Style;
        public readonly StandInSpace Space;
        public readonly OffBallDecider Decider;
        public readonly PlayerDecisionState[] State = new PlayerDecisionState[DecisionWorld.N];

        static readonly Vec2[] Home =
        {
            new Vec2(5f, 34f),
            new Vec2(25f, 10f), new Vec2(25f, 26f), new Vec2(25f, 42f), new Vec2(25f, 58f),
            new Vec2(45f, 10f), new Vec2(45f, 26f), new Vec2(45f, 42f), new Vec2(45f, 58f),
            new Vec2(62f, 28f), new Vec2(62f, 40f),
        };

        static readonly Vec2[] Away =
        {
            new Vec2(100f, 34f),
            new Vec2(80f, 10f), new Vec2(80f, 26f), new Vec2(80f, 42f), new Vec2(80f, 58f),
            new Vec2(66f, 12f), new Vec2(66f, 27f), new Vec2(66f, 41f), new Vec2(66f, 56f),
            new Vec2(52f, 30f), new Vec2(52f, 40f),
        };

        public static DecisionParams With(params (string id, float v)[] kv) =>
            DecisionParams.Create(TuningLoader.ParseBundle(MatchTestKit.DecisionTuning().Registry, MatchTestKit.Values(kv)));

        public DecisionFixture(DecisionParams p = null)
        {
            P = p ?? DecisionParams.Defaults;
            Kp = KinematicsParams.Create(MatchTestKit.Tuning().Kinematics, PitchSpec.Default);
            Style = new StandInStyleSource(DecisionWorld.N, P.TendencyBase);
            Space = new StandInSpace(P);
            Decider = new OffBallDecider(P, Space, Style);
            var avg = KinematicAttributes.Uniform(12);
            for (int i = 0; i < 11; i++)
            {
                W.SetPlayer(i, in avg, Kp, Home[i]);
                W.SetPlayer(11 + i, in avg, Kp, Away[i]);
            }
            W.AttackDir[0] = 1;
            W.AttackDir[1] = -1;
            W.Phase[0] = TeamPhase.Progression;
            W.Phase[1] = TeamPhase.DefMid;
            W.OffsideLineX[0] = 25f; W.DefLineX[0] = 25f;
            W.OffsideLineX[1] = 80f; W.DefLineX[1] = 80f;
            W.Ball = Home[6];
            W.Owner = 6;
            W.Tick = 20;
            W.OwnerSinceTick = 0;
            W.PressIntensity[0] = 0.5f;
            W.PressIntensity[1] = 0.5f;
            W.Presser[3] = 20; // 원정 압박 1순위 = 슬롯 20 (52·30)
            W.Presser[4] = 16;
            for (int i = 0; i < DecisionWorld.N; i++) { State[i] = PlayerDecisionState.Initial; W.Intent[i] = IntentPacket.None; }
            Refresh();
        }

        public void Refresh()
        {
            W.RefreshTti(Kp);
            W.CountNearBall(P.CrowdRadius);
        }

        public void SetKin(int s, in KinematicAttributes a)
        {
            var pos = W.Pos[s];
            var anchor = W.Anchor[s];
            W.SetPlayer(s, in a, Kp, pos, W.Vel[s]);
            W.Anchor[s] = anchor;
            Refresh();
        }

        public void Move(int s, Vec2 pos, bool anchorToo = true)
        {
            W.Pos[s] = pos;
            if (anchorToo) W.Anchor[s] = pos;
            Refresh();
        }

        public void GiveBall(int s, long since = 0)
        {
            W.Owner = s;
            W.Ball = s >= 0 ? W.Pos[s] : W.Ball;
            W.OwnerSinceTick = since;
            Refresh();
        }

        public void ResetState(int s) => State[s] = PlayerDecisionState.Initial;

        /// <summary>결정 1회 = 결정 틱 1개로 본다 (질의 카운터를 단계처럼 틱마다 비운다).</summary>
        public OffBallResult Decide(int s, SeedRng rng, bool trigger = false)
        {
            Space.ResetQueries();
            return Decider.Decide(W, s, ref State[s], rng, trigger);
        }

        /// <summary>새 상태(트리거)로 n회 결정해 종류별 횟수.</summary>
        public int[] Distribution(int s, ulong seed, int n = 1000)
        {
            var counts = new int[Considerations.Count];
            var rng = new SeedRng(seed);
            for (int i = 0; i < n; i++)
            {
                ResetState(s);
                counts[(int)Decide(s, rng, true).Kind]++;
            }
            return counts;
        }
    }

    /// <summary>검정 도우미 (단측 0.01).</summary>
    public static class Stat
    {
        /// <summary>두 비율 z (a/n1 − b/n2).</summary>
        public static double Z(int a, int n1, int b, int n2)
        {
            var p1 = (double)a / n1;
            var p2 = (double)b / n2;
            var p = (double)(a + b) / (n1 + n2);
            var se = Math.Sqrt(p * (1 - p) * (1.0 / n1 + 1.0 / n2));
            return se <= 0 ? (p1 > p2 ? 99 : (p1 < p2 ? -99 : 0)) : (p1 - p2) / se;
        }

        public const double Z01 = 2.326;

        static readonly double[] Crit01 = { 0, 6.635, 9.210, 11.345, 13.277, 15.086, 16.812, 18.475, 20.090, 21.666, 23.209, 24.725, 26.217, 27.688, 29.141, 30.578, 32.000 };

        /// <summary>2 × K 분할표 카이제곱이 0.01 임계값을 넘는가.</summary>
        public static bool ChiSquareDiffers(int[] a, int[] b, out double chi2, out int df)
        {
            chi2 = 0;
            int na = 0, nb = 0, k = 0;
            for (int i = 0; i < a.Length; i++) { na += a[i]; nb += b[i]; }
            for (int i = 0; i < a.Length; i++)
            {
                var col = a[i] + b[i];
                if (col == 0) continue;
                k++;
                var ea = (double)col * na / (na + nb);
                var eb = (double)col * nb / (na + nb);
                chi2 += (a[i] - ea) * (a[i] - ea) / ea + (b[i] - eb) * (b[i] - eb) / eb;
            }
            df = Math.Max(1, k - 1);
            return chi2 > Crit01[Math.Min(df, Crit01.Length - 1)];
        }
    }
}
