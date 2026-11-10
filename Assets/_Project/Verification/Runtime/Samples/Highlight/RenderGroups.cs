using System.Collections.Generic;
using RoadToPro.Presentation.Highlight;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEngine;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// 하위 시스템 몫 절제 측정 (specs/048 AC-16, FR-21). 고정 프레임에서 그룹 하나를 끄고 재서 "전체 − 끈 것"을 그 시스템 몫으로 본다.
    /// 그룹 이름은 <see cref="FrameBudget"/>의 시스템 이름을 쓴다. 관중(045)은 아직 없다.
    /// </summary>
    public sealed class RenderGroups
    {
        readonly Dictionary<string, List<Renderer>> _groups = new Dictionary<string, List<Renderer>>();
        readonly List<Behaviour> _players = new List<Behaviour>();

        public IEnumerable<string> Names => _groups.Keys;

        public static RenderGroups Collect(HighlightStage stage, Presentation.Stadium.PlayerBlobShadows blobs)
        {
            var g = new RenderGroups();
            foreach (var n in new[] { FrameBudget.Pitch, FrameBudget.Structure, FrameBudget.Players, FrameBudget.Props })
                g._groups[n] = new List<Renderer>();
            var rigRoots = new HashSet<Transform>();
            foreach (var r in stage.Rigs)
                if (r != null) rigRoots.Add(r.transform);
            // 수집 시점에 켜져 있던 렌더러만 다룬다 — 원래 꺼진 것(클로즈업 머리·그림자 프록시·대체 메시)을 켜면 측정이 부풀려진다
            foreach (var r in stage.gameObject.scene.GetRootGameObjects())
            foreach (var ren in r.GetComponentsInChildren<Renderer>(false))
                if (ren.enabled) g._groups[Classify(ren.transform, rigRoots, stage.Ball)].Add(ren);
            if (blobs != null) g._players.Add(blobs);
            return g;
        }

        static string Classify(Transform t, HashSet<Transform> rigRoots, Transform ball)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (rigRoots.Contains(p)) return FrameBudget.Players;
                if (p == ball) return FrameBudget.Props;
                string n = p.name;
                if (n == "Pitch") return FrameBudget.Pitch;
                if (n.StartsWith("Goal") || n.Contains("Net")) return FrameBudget.Props;
            }
            return FrameBudget.Structure;
        }

        public int Count(string group) => _groups.TryGetValue(group, out var l) ? l.Count : 0;

        /// <summary>3D 렌더러를 전부 끈다 — 렌더 스케일과 무관한 UI 비교용 (AC-06).</summary>
        public void HideAll()
        {
            foreach (var kv in _groups)
                foreach (var r in kv.Value)
                    if (r != null) r.enabled = false;
            foreach (var b in _players)
                if (b != null) b.enabled = false;
        }

        /// <summary>그룹 하나만 끈다 (나머지는 켬). null이면 전부 켬.</summary>
        public void HideOnly(string group)
        {
            foreach (var kv in _groups)
                foreach (var r in kv.Value)
                    if (r != null) r.enabled = kv.Key != group;
            foreach (var b in _players)
                if (b != null) b.enabled = group != FrameBudget.Players;
        }
    }
}
