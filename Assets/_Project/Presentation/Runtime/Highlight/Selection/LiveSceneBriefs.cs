using RoadToPro.MatchEngine.Match.Events;
using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;

namespace RoadToPro.Presentation.Highlight.Selection
{
    /// <summary>
    /// 선정 결과 → 042 연출 입력 (specs/058 FR-20·21·24·48). 선수 id를 그 시각의 엔티티(슬롯)로 바꾸는 함수는 밖에서 준다
    /// (교체로 같은 선수가 다른 슬롯에 있을 수 있다). 장면마다 한 번 만들고 캐시한다.
    /// 내 선수가 그 장면에 없으면(미출전·명단 제외·교체 전후) MyPlayer = −1 → 042가 내 선수 추적 모드를 방송 모드로 대체한다 (FR-48 ③).
    /// </summary>
    public sealed class LiveSceneBriefs
    {
        readonly LiveSceneList _list;
        readonly Func<long, int, int> _slotOf;
        readonly int _myPlayerId;
        readonly PitchSpec _pitch;
        readonly Dictionary<int, SceneBrief> _cache = new Dictionary<int, SceneBrief>();

        /// <param name="slotOf">(틱, 선수 id) → 그 틱의 슬롯, 없으면 −1.</param>
        public LiveSceneBriefs(LiveSceneList list, Func<long, int, int> slotOf, int myPlayerId, PitchSpec pitch)
        {
            _list = list ?? throw new ArgumentNullException(nameof(list));
            _slotOf = slotOf ?? throw new ArgumentNullException(nameof(slotOf));
            _myPlayerId = myPlayerId;
            _pitch = pitch;
        }

        public LiveSceneInfo InfoOf(int sceneId) => _list.Info.TryGetValue(sceneId, out var i) ? i : null;

        public SceneBrief Brief(int sceneId)
        {
            if (_cache.TryGetValue(sceneId, out var b)) return b;
            if (!_list.Info.TryGetValue(sceneId, out var info)) return null;
            HighlightSceneRange(sceneId, out var start, out var end);
            var tick = info.EventTick;
            b = new SceneBrief
            {
                SceneId = sceneId,
                Role = info.Role,
                EventTime = info.EventTime,
                EventPos = new Vec3(info.EventX * _pitch.LengthM - _pitch.LengthM * 0.5f, 0f, info.EventY * _pitch.WidthM - _pitch.WidthM * 0.5f),
                Scorer = info.Kind == LiveSceneKind.Goal ? Slot(tick, info.PlayerId) : -1,
                Shooter = Slot(tick, info.ShooterId >= 0 ? info.ShooterId : info.PlayerId),
                AngleIndex = info.AngleIndex,
                AngleCount = info.AngleCount,
                MyPlayer = Slot(tick, _myPlayerId),
                MyPlayerInvolved = info.MyPlayerInvolved,
                EndWide = info.EndWide,
                DeadBalls = DeadBallsIn(start, end),
            };
            if (info.EventKind == SimEventKind.Dribble || info.EventKind == SimEventKind.Tackle)
            {
                // 081 FR-31: 결투 장면 — 두 선수 슬롯 (둘 다 있어야 줌)
                var da = Slot(tick, info.PlayerId);
                var db = Slot(tick, info.OtherId);
                if (da >= 0 && db >= 0 && da != db) { b.DuelA = da; b.DuelB = db; }
            }
            {
                // 081 FR-31 개정: 장면 구간 안 결투 순간 전부
                var list = new System.Collections.Generic.List<DuelMoment>();
                foreach (var d in _list.Duels)
                {
                    if (d.Time < start || d.Time > end) continue;
                    int sa = Slot(d.Tick, d.Player), sb = Slot(d.Tick, d.Other);
                    if (sa >= 0 && sb >= 0 && sa != sb) list.Add(new DuelMoment(d.Time, sa, sb));
                }
                b.Duels = list.ToArray();
            }
            if (b.MyPlayer < 0) b.MyPlayerInvolved = false;
            // 실점(상대 골): 내 선수 추적 모드에서도 세리머니에 내 선수를 비추지 않는다 (FR-24)
            if (info.Kind == LiveSceneKind.Goal && b.MyPlayer >= 0 && Slot(tick, info.PlayerId) != b.MyPlayer && TeamOfSlot(b.MyPlayer) != info.Team)
                b.MyPlayerInvolved = false;
            _cache[sceneId] = b;
            return b;
        }

        static int TeamOfSlot(int slot) => slot < 11 ? 0 : 1;

        int Slot(long tick, int playerId) => playerId < 0 ? -1 : _slotOf(tick, playerId);

        void HighlightSceneRange(int id, out double start, out double end)
        {
            start = end = 0;
            foreach (var s in _list.Scenes)
                if (s.Id == id) { start = s.Start; end = s.End; return; }
        }

        DeadBallWindow[] DeadBallsIn(double start, double end)
        {
            var r = new List<DeadBallWindow>();
            foreach (var w in _list.DeadBalls)
                if (w.End > start && w.Start < end) r.Add(w);
            return r.ToArray();
        }
    }
}
