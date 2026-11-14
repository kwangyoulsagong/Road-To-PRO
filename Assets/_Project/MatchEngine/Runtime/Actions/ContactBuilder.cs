using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 접촉 필드 계산 (specs/070 FR-13·21). 순수 함수, RNG 없음. 0~1 필드는 자르고, 피치 밖 접촉은 득점 기회 스냅샷을 비운다(NaN 없음).
    /// 강도 [대체]: 유효 질량 μ = m1·m2/(m1+m2) (m = massLo + (massHi − massLo)·근력), 강도 = clamp01(상대 속도/relSpeedMax × μ/(massHi/2)).
    /// </summary>
    public static class ContactBuilder
    {
        public const float BoxDepth = 16.5f, BoxHalfWidth = 20.16f, GoalHalfWidth = 3.66f;

        static float C01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f; // NaN → 0

        public static float Mass(ActionParams p, float aStrength) => p.ContactMassLo + (p.ContactMassHi - p.ContactMassLo) * C01(aStrength);

        public static float Intensity(ActionParams p, Vec2 velA, Vec2 velB, float aStrengthA, float aStrengthB)
        {
            var rel = (velA - velB).Length();
            var m1 = Mass(p, aStrengthA);
            var m2 = Mass(p, aStrengthB);
            var mu = m1 * m2 / DMath.Max(m1 + m2, 1e-3f);
            return C01(rel / DMath.Max(p.ContactRelSpeedMax, 1e-3f) * (mu / DMath.Max(p.ContactMassHi * 0.5f, 1e-3f)));
        }

        /// <summary>
        /// 기본 필드를 채운 상세. attacker = 공 쪽, defender = 접촉을 건 쪽. w의 위치·속도·근력, 수비 팀 골문 방향(attackDir of attacker team)을 쓴다.
        /// </summary>
        public static ContactDetail Make(ActionParams p, ActionWorld w, ContactKind kind, long tick, int sub, int attacker, int defender,
            Vec2 at, float ballFirst, float fromBehind, float lateness, bool sliding, float height, float controlLikely)
        {
            var c = new ContactDetail
            {
                Tick = tick, Substep = (sbyte)sub, Kind = kind, Attacker = attacker, Defender = defender,
                BallFirst = C01(ballFirst), FromBehind = C01(fromBehind), Lateness = C01(lateness), Sliding = sliding,
                Height = FloatOk(height) ? (height < 0f ? 0f : height) : 0f,
                X = FloatOk(at.X) ? at.X : 0f, Y = FloatOk(at.Y) ? at.Y : 0f,
            };
            c.StudsUp = sliding ? C01(0.5f + 0.5f * c.FromBehind) : 0f;
            var va = attacker >= 0 && attacker < ActionWorld.N ? w.Vel[attacker] : Vec2.Zero;
            var vd = defender >= 0 && defender < ActionWorld.N ? w.Vel[defender] : Vec2.Zero;
            var sa = attacker >= 0 && attacker < ActionWorld.N ? w.Strength[attacker] : 0.5f;
            var sd = defender >= 0 && defender < ActionWorld.N ? w.Strength[defender] : 0.5f;
            c.Intensity = Intensity(p, va, vd, sa, sd);
            if (defender >= 0 && defender < ActionWorld.N && w.SlideLevel[defender] > 0) c.HabitBits |= ContactDetail.HabitSlideMaster;
            var team = attacker >= 0 ? ActionWorld.TeamOf(attacker) : 0;
            FillDogso(ref c, w, team, at, controlLikely);
            return c;
        }

        /// <summary>득점 기회 저지 스냅샷 (012 판정 입력): 골까지 거리·골문 시야각·공과 골 사이 수비수 수·컨트롤 가능성. 피치 밖이면 비움.</summary>
        public static void FillDogso(ref ContactDetail c, ActionWorld w, int attTeam, Vec2 at, float controlLikely)
        {
            c.DogsoValid = false; c.GoalDist = 0f; c.GoalAngle = 0f; c.DefendersBetween = 0; c.ControlLikely = 0f; c.InBox = false;
            if (!FloatOk(at.X) || !FloatOk(at.Y) || at.X < 0f || at.X > w.PitchL || at.Y < 0f || at.Y > w.PitchW) return;
            var goal = w.OppGoal(attTeam);
            var g = goal - at;
            c.GoalDist = g.Length();
            var a = new Vec2(goal.X - at.X, goal.Y - GoalHalfWidth - at.Y);
            var b = new Vec2(goal.X - at.X, goal.Y + GoalHalfWidth - at.Y);
            c.GoalAngle = DMath.Atan2(DMath.Abs(a.X * b.Y - a.Y * b.X), a.X * b.X + a.Y * b.Y);
            var depth = (goal.X - at.X) * w.AttackDir[attTeam]; // 골까지 남은 x 깊이 (≥ 0)
            c.InBox = depth <= BoxDepth && DMath.Abs(at.Y - goal.Y) <= BoxHalfWidth;
            var def = 1 - attTeam;
            var n = 0;
            for (int s = def * ActionWorld.PerTeam; s < (def + 1) * ActionWorld.PerTeam; s++)
            {
                if (!w.Active[s]) continue;
                var r = w.Pos[s] - at;
                var along = r.X * w.AttackDir[attTeam];
                if (along <= 0f || along > depth) continue;
                // 공→골문 양 포스트 부채꼴 안 (+1 m 여유)
                var t = along / DMath.Max(depth, 1e-3f);
                var halfW = GoalHalfWidth * t + 1f;
                var lineY = at.Y + (goal.Y - at.Y) * t;
                if (DMath.Abs(w.Pos[s].Y - lineY) <= halfW) n++;
            }
            c.DefendersBetween = n;
            c.ControlLikely = C01(controlLikely);
            c.DogsoValid = true;
        }

        /// <summary>
        /// FR-21 핸드볼 입력: 공 높이가 팔 구간 [armZoneLo, armZoneHi] 안일 때만 붙인다. 팔 실루엣 [대체] = clamp01(silBase + silSpeedK·몸 속도).
        /// 반응 시간 = 공과의 거리 / 공 속도(0이면 1 s), 자기 몸·근거리 굴절은 호출자가 넘긴다. 붙였으면 true.
        /// </summary>
        public static bool AttachHand(ActionParams p, ref ContactDetail c, float ballZ, float ballDist, float ballSpeed, float bodySpeed, bool selfDeflect)
        {
            if (!(ballZ >= p.HandZoneLo && ballZ <= p.HandZoneHi)) return false;
            c.HasHand = true;
            c.HandSilhouette = C01(p.HandSilBase + p.HandSilSpeedK * (FloatOk(bodySpeed) ? bodySpeed : 0f));
            c.HandBallDist = FloatOk(ballDist) && ballDist > 0f ? ballDist : 0f;
            c.HandReactTime = ballSpeed > 1e-3f ? c.HandBallDist / ballSpeed : 1f;
            c.HandSelfDeflect = selfDeflect;
            return true;
        }

        static bool FloatOk(float v) => v - v == 0f;
    }
}
