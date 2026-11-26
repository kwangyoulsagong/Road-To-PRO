using System.Collections.Generic;
using System.Diagnostics;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Stadium.Net
{
    /// <summary>
    /// 장면을 만들 때(로딩, <see cref="SceneClipBuilder"/>) 두 골대 그물을 굽는다 (specs/059 FR-27). 그물 시뮬 하나를 장면마다 재사용한다(Reset).
    /// 마지막 굽기 시간(ms)을 남긴다 — 성능 표 "그물 굽기 CPU".
    /// </summary>
    public sealed class NetSceneBaker
    {
        readonly NetCloth _cloth;
        readonly float _halfLength;
        readonly GoalBox _box;

        public double LastBakeMs { get; private set; }
        public NetCloth Cloth => _cloth;

        public NetSceneBaker(NetRippleTuning tuning, in GoalBox box)
        {
            _cloth = tuning != null && tuning.Valid ? new NetCloth(tuning) : null;
            _halfLength = box.HalfLength;
            _box = box;
        }

        /// <returns>[골대 번호 0 = 왼쪽, 1 = 오른쪽] 구운 데이터. 접촉이 없거나 튜닝이 잘못되면 null 칸.</returns>
        public NetBake[] Bake(HighlightData data, List<NetContact> contacts)
        {
            var result = new NetBake[2];
            if (_cloth == null || contacts == null || contacts.Count == 0) return result;
            var w = Stopwatch.StartNew();
            for (int s = 0; s < 2; s++)
                result[s] = NetBake.Build(_cloth, data, contacts, GoalNetDriver.SideOf(s), _halfLength, _box);
            LastBakeMs = w.Elapsed.TotalMilliseconds;
            return result;
        }
    }
}
