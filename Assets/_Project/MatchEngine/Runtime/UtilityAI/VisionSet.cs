using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 패스 후보 시야 집합 (specs/072 FR-02, 리서치 03 § B·04 "시야 모델"): 반경 r = r0 + r1·s 안 동료를 거리순으로 k = round(k0 + k1·s)명 (s = Vision 0~1, 동점 = 슬롯 순).
    /// 시야각(몸 방향 기준, [대체] Vision 보간) 밖 동료는 지각 지연만큼 과거 위치로 본다. 008 패스 평가는 이 집합만 평가한다. 할당 0.
    /// </summary>
    public sealed class VisionSet
    {
        public const int Capacity = DecisionWorld.PerTeam - 1;

        readonly int[] _slot = new int[Capacity];
        readonly float[] _d2 = new float[Capacity];
        readonly Vec2[] _seen = new Vec2[Capacity];
        readonly bool[] _inAngle = new bool[Capacity];

        public int Count { get; private set; }
        public float Radius { get; private set; }
        public int K { get; private set; }
        /// <summary>시야각 (도, 전체 각).</summary>
        public float AngleDeg { get; private set; }
        /// <summary>몸 방향 (단위 벡터).</summary>
        public Vec2 Facing { get; private set; }

        public int Slot(int i) => _slot[i];
        public Vec2 Seen(int i) => _seen[i];
        public bool InAngle(int i) => _inAngle[i];

        public bool Contains(int slot)
        {
            for (int i = 0; i < Count; i++) if (_slot[i] == slot) return true;
            return false;
        }

        /// <summary>반경·후보 수 (FR-02 식).</summary>
        public static void Size(DecisionParams p, float visionN, out float radius, out int k)
        {
            var s = visionN < 0f ? 0f : (visionN > 1f ? 1f : visionN);
            radius = p.OnBallVisionR0 + p.OnBallVisionR1 * s;
            k = (int)(p.OnBallVisionK0 + p.OnBallVisionK1 * s + 0.5f);
            if (k > Capacity) k = Capacity;
            if (k < 0) k = 0;
        }

        /// <summary>몸 방향: 속도 0.5 m/s 넘으면 속도 방향, 아니면 공격 방향.</summary>
        public static Vec2 FacingOf(DecisionWorld w, int s)
        {
            var v = w.Vel[s];
            var len = v.Length();
            if (len > 0.5f) return v / len;
            return new Vec2(w.AttackDir[DecisionWorld.TeamOf(s)], 0f);
        }

        public void Build(DecisionParams p, DecisionWorld w, int s)
        {
            var vis = DecisionAttributes.N(w.Mind[s].Vision);
            Size(p, vis, out var r, out var k);
            Radius = r;
            K = k;
            AngleDeg = p.OnBallVisionAngleMin + (p.OnBallVisionAngleMax - p.OnBallVisionAngleMin) * vis;
            var facing = FacingOf(w, s);
            Facing = facing;
            var cosHalf = AngleDeg >= 360f ? -2f : DMath.Cos(AngleDeg * 0.5f * (DMath.Pi / 180f));
            var me = w.Pos[s];
            var t = DecisionWorld.TeamOf(s);
            var r2 = r * r;
            var n = 0;
            for (int o = t * DecisionWorld.PerTeam; o < (t + 1) * DecisionWorld.PerTeam; o++)
            {
                if (o == s || !w.Active[o]) continue;
                var d2 = (w.Pos[o] - me).LengthSq();
                if (d2 > r2) continue;
                // 거리 오름차순 삽입 (동점은 먼저 온 = 낮은 슬롯 유지), k명만
                int j;
                if (n < k) j = n++;
                else
                {
                    if (k == 0 || d2 >= _d2[k - 1]) continue;
                    j = k - 1;
                }
                while (j > 0 && _d2[j - 1] > d2) { _d2[j] = _d2[j - 1]; _slot[j] = _slot[j - 1]; j--; }
                _d2[j] = d2;
                _slot[j] = o;
            }
            Count = n;
            for (int i = 0; i < n; i++)
            {
                var o = _slot[i];
                var rel = w.Pos[o] - me;
                var len = DMath.Sqrt(_d2[i]);
                var inAngle = len < 1e-4f || (rel.X * facing.X + rel.Y * facing.Y) >= cosHalf * len;
                _inAngle[i] = inAngle;
                _seen[i] = inAngle ? w.Pos[o] : w.Perceived(o, p.OnBallVisionDelay);
            }
        }
    }
}
