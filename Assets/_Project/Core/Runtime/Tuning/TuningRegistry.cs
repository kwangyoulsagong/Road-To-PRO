using System;
using System.Collections.Generic;
using RoadToPro.Core.Ordering;

namespace RoadToPro.Core.Tuning
{
    /// <summary>튜닝 정의 묶음. id 서수 정렬 순서로 보관 (해시·순회 순서 고정).</summary>
    public sealed class TuningRegistry
    {
        readonly List<TuningDefinition> _defs;
        readonly Dictionary<string, int> _index;

        public TuningRegistry(IEnumerable<TuningDefinition> definitions)
        {
            _defs = new List<TuningDefinition>(definitions);
            _index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in _defs)
                if (_index.ContainsKey(d.Id)) throw new TuningLoadException(d.Id, "중복 정의");
                else _index[d.Id] = 0;
            DeterministicOrder.StableSort(_defs, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < _defs.Count; i++) _index[_defs[i].Id] = i;
        }

        public int Count => _defs.Count;
        public IReadOnlyList<TuningDefinition> Definitions => _defs;

        public bool TryGetIndex(string id, out int index) => _index.TryGetValue(id, out index);

        public TuningDefinition Get(string id) =>
            _index.TryGetValue(id, out var i) ? _defs[i] : throw new TuningLoadException(id, "정의되지 않은 id");
    }
}
