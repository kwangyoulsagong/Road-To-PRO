using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Flow;

namespace RoadToPro.MatchEngine.Tactics
{
    public struct SetPieceCounts
    {
        public int Wall, Zones, Markers;
    }

    /// <summary>
    /// 정지 상황 기본 배치 (FR-80·81, A9). 입력 targets(팀 관점 정규화, 일반 형태 결과)를 재시작 규칙에 맞게 고친다.
    /// 공격 팀 시나리오 배역의 덮어쓰기는 013 몫이다.
    /// </summary>
    public sealed class SetPieceLayout
    {
        const int Angles = 32;
        const float BoxDepthM = 16.5f, BoxHalfWidthM = 20.16f, GoalHalfWidthM = 3.66f;

        readonly ShapeParams _p;
        readonly bool[] _used = new bool[FormationDefinition.SlotCount];
        readonly int[] _oppOrder = new int[FormationDefinition.SlotCount];

        public SetPieceLayout(ShapeParams p) => _p = p;

        /// <summary>팀 관점 배치. attacking = 재시작을 하는 팀. taker는 키커(팀 내 슬롯) 또는 −1.</summary>
        public SetPieceCounts Apply(RestartKind kind, bool attacking, Vec2 spot, MarkingScheme scheme, int taker,
            bool[] active, Vec2[] own, Vec2[] opp, bool[] oppActive, Vec2[] targets)
        {
            var counts = default(SetPieceCounts);
            System.Array.Clear(_used, 0, _used.Length);
            var spotM = M(spot);
            var rule = _p.RuleDistance + _p.DistanceMargin;

            switch (kind)
            {
                case RestartKind.FreeKick:
                    if (!attacking) { counts.Wall = PlaceWall(spotM, active, own, targets); KeepAway(spotM, rule, active, targets); }
                    break;
                case RestartKind.Corner:
                    if (!attacking) { CornerDefence(spotM, scheme, active, own, opp, oppActive, targets, ref counts); KeepAway(spotM, rule, active, targets); }
                    break;
                case RestartKind.ThrowIn:
                    if (!attacking) KeepAway(spotM, _p.ThrowDistance + _p.DistanceMargin, active, targets);
                    break;
                case RestartKind.GoalKick:
                    if (!attacking) ClearBox(false, active, targets);
                    break;
                case RestartKind.Penalty:
                    ClearBox(!attacking, active, targets);
                    KeepAway(spotM, rule, active, targets);
                    break;
                case RestartKind.Kickoff:
                    OwnHalf(active, targets);
                    if (!attacking) KeepAway(spotM, rule, active, targets);
                    break;
            }

            if (attacking && taker > 0 && taker < targets.Length && active[taker] && kind != RestartKind.Kickoff) targets[taker] = spot;
            return counts;
        }

        int PlaceWall(Vec2 spotM, bool[] active, Vec2[] own, Vec2[] targets)
        {
            var goal = new Vec2(0f, _p.PitchW * 0.5f);
            var dist = Vec2.Distance(spotM, goal);
            var central = System.Math.Abs(spotM.Y - goal.Y) < _p.CentralHalfWidth;
            var want = central && dist < _p.WallDistanceCentral ? _p.WallCentral : _p.WallSide;
            var dir = (goal - spotM).Normalized();
            var side = new Vec2(-dir.Y, dir.X);
            var center = spotM + dir * (_p.RuleDistance + _p.DistanceMargin);
            int placed = 0;
            for (int n = 0; n < want; n++)
            {
                var off = (n - (want - 1) * 0.5f) * _p.WallSpacing;
                var slotPos = Inside(center + side * off);
                int who = NearestFree(N(slotPos), active, own);
                if (who < 0) break;
                _used[who] = true;
                targets[who] = N(slotPos);
                placed++;
            }
            return placed;
        }

        void CornerDefence(Vec2 spotM, MarkingScheme scheme, bool[] active, Vec2[] own, Vec2[] opp, bool[] oppActive, Vec2[] targets, ref SetPieceCounts counts)
        {
            var c = _p.PitchW * 0.5f;
            var s = spotM.Y < c ? -1f : 1f;
            for (int z = 0; z < 6; z++)
            {
                Vec2 m;
                switch (z)
                {
                    case 0: m = new Vec2(0.8f, c + s * GoalHalfWidthM); break;
                    case 1: m = new Vec2(6f, c + s * 5f); break;
                    case 2: m = new Vec2(6f, c); break;
                    case 3: m = new Vec2(6f, c - s * 5f); break;
                    case 4: m = new Vec2(0.8f, c - s * GoalHalfWidthM); break;
                    default: m = new Vec2(BoxDepthM + 1.5f, c); break;
                }
                int who = NearestFree(N(m), active, own);
                if (who < 0) return;
                _used[who] = true;
                targets[who] = N(m);
                counts.Zones++;
            }

            var want = scheme == MarkingScheme.Zonal ? 0 : (scheme == MarkingScheme.Hybrid ? _p.MarkersHybrid : _p.MarkersMan);
            var goal = new Vec2(0f, c);
            int k = 0;
            for (int i = 1; i < opp.Length; i++) if (oppActive[i]) _oppOrder[k++] = i;
            for (int i = 1; i < k; i++)
            {
                var v = _oppOrder[i];
                var dv = Vec2.DistanceSq(M(opp[v]), goal);
                int j = i - 1;
                while (j >= 0 && Vec2.DistanceSq(M(opp[_oppOrder[j]]), goal) > dv) { _oppOrder[j + 1] = _oppOrder[j]; j--; }
                _oppOrder[j + 1] = v;
            }
            for (int n = 0; n < want && n < k; n++)
            {
                var o = M(opp[_oppOrder[n]]);
                var toGoal = (goal - o).Normalized();
                var pos = N(Inside(o + toGoal * _p.GoalSideM));
                int who = NearestFree(pos, active, own);
                if (who < 0) return;
                _used[who] = true;
                targets[who] = pos;
                counts.Markers++;
            }
        }

        /// <summary>규칙 거리 안의 선수를 밀어낸다. 피치 밖으로 밀리면 원 위에서 피치 안 가장 가까운 지점을 고른다.</summary>
        void KeepAway(Vec2 spotM, float radius, bool[] active, Vec2[] targets)
        {
            for (int i = 1; i < targets.Length; i++)
            {
                if (!active[i]) continue;
                var p = M(targets[i]);
                var d = Vec2.Distance(p, spotM);
                if (d >= radius) continue;
                var dir = d < 1e-3f ? new Vec2(-1f, 0f) : (p - spotM) / d;
                var q = spotM + dir * radius;
                if (!IsInside(q)) q = BestOnCircle(spotM, radius, p);
                targets[i] = N(q);
            }
        }

        Vec2 BestOnCircle(Vec2 c, float r, Vec2 near)
        {
            var best = c;
            var bd = float.MaxValue;
            for (int a = 0; a < Angles; a++)
            {
                var q = c + new Vec2(r, 0f).Rotate(a * (2f * 3.14159265f / Angles));
                if (!IsInside(q)) continue;
                var d = Vec2.DistanceSq(q, near);
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        void ClearBox(bool ownEnd, bool[] active, Vec2[] targets)
        {
            var c = _p.PitchW * 0.5f;
            for (int i = 1; i < targets.Length; i++)
            {
                if (!active[i]) continue;
                var p = M(targets[i]);
                if (System.Math.Abs(p.Y - c) > BoxHalfWidthM + _p.DistanceMargin) continue;
                var edge = ownEnd ? BoxDepthM + _p.DistanceMargin : _p.PitchL - BoxDepthM - _p.DistanceMargin;
                if (ownEnd ? p.X < edge : p.X > edge) targets[i] = N(new Vec2(edge, p.Y));
            }
        }

        void OwnHalf(bool[] active, Vec2[] targets)
        {
            var limit = 0.5f - _p.DistanceMargin / _p.PitchL;
            for (int i = 1; i < targets.Length; i++)
                if (active[i] && targets[i].X > limit) targets[i] = new Vec2(limit, targets[i].Y);
        }

        int NearestFree(Vec2 point, bool[] active, Vec2[] own)
        {
            int best = -1;
            var bd = float.MaxValue;
            for (int i = 1; i < own.Length; i++)
            {
                if (!active[i] || _used[i]) continue;
                var d = Vec2.DistanceSq(M(own[i]), M(point));
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        bool IsInside(Vec2 m)
        {
            var mg = _p.PitchMarginM;
            return m.X >= mg && m.X <= _p.PitchL - mg && m.Y >= mg && m.Y <= _p.PitchW - mg;
        }

        Vec2 Inside(Vec2 m)
        {
            var mg = _p.PitchMarginM;
            return new Vec2(Interp.Clamp(m.X, mg, _p.PitchL - mg), Interp.Clamp(m.Y, mg, _p.PitchW - mg));
        }

        Vec2 M(Vec2 n) => new Vec2(n.X * _p.PitchL, n.Y * _p.PitchW);
        Vec2 N(Vec2 m) => new Vec2(m.X / _p.PitchL, m.Y / _p.PitchW);
    }
}
