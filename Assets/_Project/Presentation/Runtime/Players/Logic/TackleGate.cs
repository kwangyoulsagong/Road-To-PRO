using System.Collections.Generic;
using RoadToPro.Core.Logging;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 이벤트에 맞는 동작 선택 (specs/058 FR-50~52). 태클 동작(id가 "tackle_"로 시작)은 이벤트 순간 상대가 [제안] 2.0 m 안에 있을 때만 남긴다.
    /// Tackle은 상대 볼 캐리어(이벤트 상대), 인터셉트·블록·볼 회수는 가장 가까운 상대로 판정한다. 남는 후보가 없으면 일반 터치 동작 + 진단.
    /// </summary>
    public static class TackleGate
    {
        public const float DefaultMaxDistance = 2.0f;
        const string TacklePrefix = "tackle_", FallbackClip = "trap_foot_r";

        public static bool Applies(SimEventKind k) =>
            k == SimEventKind.Tackle || k == SimEventKind.Interception || k == SimEventKind.Block || k == SimEventKind.BallRecovered;

        /// <summary>이벤트 순간 판정 상대와의 거리 (m). 상대가 없으면 +∞.</summary>
        public static float OpponentDistance(HighlightData d, in AnimEvent ev, int entity)
        {
            if (entity < 0 || entity >= HighlightData.Players) return float.PositiveInfinity;
            var me = d.Pos(entity, ev.Time);
            if (ev.Kind == SimEventKind.Tackle && ev.Other >= 0 && ev.Other < HighlightData.Players)
                return (d.Pos(ev.Other, ev.Time) - me).Length();
            int from = entity < 11 ? 11 : 0;
            float best = float.PositiveInfinity;
            for (int o = from; o < from + 11; o++)
            {
                if (!d.Present[d.FrameIndex(ev.Time) * HighlightData.Entities + o]) continue;
                var dist = (d.Pos(o, ev.Time) - me).Length();
                if (dist < best) best = dist;
            }
            return best;
        }

        /// <summary>후보를 걸러 into에 담는다. 대체 동작을 썼으면 true.</summary>
        public static bool Filter(ClipLibrary lib, IReadOnlyList<int> cands, HighlightData d, in AnimEvent ev, int entity, float maxDistance, List<int> into, ILogger log)
        {
            into.Clear();
            bool near = OpponentDistance(d, ev, entity) <= maxDistance;
            for (int i = 0; i < cands.Count; i++)
            {
                var id = lib.Get(cands[i]).Id;
                bool tackle = id != null && id.StartsWith(TacklePrefix, System.StringComparison.Ordinal);
                if (tackle && !near) continue;
                into.Add(cands[i]);
            }
            if (into.Count > 0) return false;
            var fb = lib.IndexOf(FallbackClip);
            if (fb >= 0) into.Add(fb);
            log?.Info("[anim] {0}: 상대가 {1:0.0} m 밖이라 태클 동작 없음 — 일반 터치로 대체 (058 FR-52)", ev.Kind, maxDistance);
            return true;
        }
    }
}
