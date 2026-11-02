using System;
using RoadToPro.Core.Hashing;

namespace RoadToPro.Core.Tuning
{
    /// <summary>
    /// 튜닝 값 묶음 = 레지스트리 정의 전부의 최종 값 (파일에 없으면 기본값). 불변.
    /// <see cref="Hash"/>는 경기 결과에 기록해 어떤 튜닝으로 나온 결과인지 추적한다 (FR-81).
    /// </summary>
    public sealed class TuningBundle
    {
        public const int HashFormatVersion = 1;

        readonly float[] _values;

        public TuningRegistry Registry { get; }
        public ulong Hash { get; }

        internal TuningBundle(TuningRegistry registry, float[] values)
        {
            Registry = registry;
            _values = values;
            var h = StableHasher.Create();
            h.Add(HashFormatVersion);
            h.Add(registry.Count);
            for (int i = 0; i < values.Length; i++)
            {
                h.Add(registry.Definitions[i].Id);
                h.Add(values[i]);
            }
            Hash = h.Value;
        }

        public static TuningBundle Defaults(TuningRegistry registry)
        {
            var v = new float[registry.Count];
            for (int i = 0; i < v.Length; i++) v[i] = registry.Definitions[i].Default;
            return new TuningBundle(registry, v);
        }

        public float Get(string id) =>
            Registry.TryGetIndex(id, out var i) ? _values[i] : throw new ArgumentException($"정의되지 않은 튜닝 id '{id}'");

        /// <summary>인덱스 접근 (틱 경로: 미리 TryGetIndex로 인덱스를 받아 둔다).</summary>
        public float this[int index] => _values[index];
    }
}
