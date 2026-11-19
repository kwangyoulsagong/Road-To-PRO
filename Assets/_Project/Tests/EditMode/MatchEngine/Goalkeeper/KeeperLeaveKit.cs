using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/073 모델 테스트 공용: 팀 0이 x = 0 골을 지킨다(+x 공격), GK = 슬롯 0, 상대 = 슬롯 11~21.</summary>
    public static class KeeperLeaveKit
    {
        public static readonly Vec2 Goal = new Vec2(0f, 34f);
        public static GoalkeeperParams P => GoalkeeperParams.Defaults;

        public static KeeperProfile Profile(float sweeping = 50f, float command = 50f, float oneOnOne = 50f, float jumping = 50f)
        {
            var v = KeeperAttributes.InternalFromDisplay(10.5f);
            return KeeperProfile.From(P, new KeeperAttributes(v, v, v, v, oneOnOne, command, sweeping, v, jumping, v, v, 1.88f));
        }

        public static ActionWorld World(Vec2 gk)
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 0, gk);
            return w;
        }

        /// <summary>공이 from에서 골 쪽(−x)으로 speed m/s로 굴러가는 경로 샘플 (0.1 s 간격, 3 s).</summary>
        public static int StraightPath(Vec2 from, Vec2 vel, float[] times, Vec3[] pos)
        {
            var n = 0;
            for (int i = 0; i < times.Length; i++)
            {
                var t = (i + 1) * 0.1f;
                times[n] = t;
                pos[n] = new Vec3(from.X + vel.X * t, from.Y + vel.Y * t, 0.11f);
                n++;
            }
            return n;
        }
    }
}
