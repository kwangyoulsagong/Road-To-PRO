using System;
using System.Collections.Generic;

namespace RoadToPro.Core.Ordering
{
    /// <summary>
    /// 결과에 영향을 주는 순회·정렬 규칙 (specs/001 FR-90·91).
    /// - 해시 기반 컬렉션(Dictionary·HashSet)은 키를 정렬해서 순회한다.
    /// - 정렬은 안정 정렬. <c>List.Sort</c>/<c>Array.Sort</c>는 불안정이라 결과 경로에서 쓰지 않는다.
    /// - 동점은 id 오름차순 (<see cref="ThenById"/>).
    /// - 문자열 비교는 서수(ordinal)만.
    /// </summary>
    public static class DeterministicOrder
    {
        /// <summary>주 비교 결과가 0이면 id 오름차순.</summary>
        public static int ThenById(int primary, int idA, int idB) => primary != 0 ? primary : idA.CompareTo(idB);

        public static int ThenById(int primary, long idA, long idB) => primary != 0 ? primary : idA.CompareTo(idB);

        /// <summary>안정 병합 정렬. scratch는 items 이상 길이 (할당 없음). 16개 이하는 삽입 정렬.</summary>
        public static void StableSort<T, TComparer>(Span<T> items, Span<T> scratch, TComparer comparer)
            where TComparer : IComparer<T>
        {
            if (items.Length <= 1) return;
            if (scratch.Length < items.Length) throw new ArgumentException("scratch가 items보다 짧다");
            if (items.Length <= 16)
            {
                InsertionSort(items, comparer);
                return;
            }
            var mid = items.Length / 2;
            StableSort(items.Slice(0, mid), scratch, comparer);
            StableSort(items.Slice(mid), scratch, comparer);
            if (comparer.Compare(items[mid - 1], items[mid]) <= 0) return;
            items.CopyTo(scratch);
            int i = 0, j = mid, k = 0;
            while (i < mid && j < items.Length)
                items[k++] = comparer.Compare(scratch[j], scratch[i]) < 0 ? scratch[j++] : scratch[i++];
            while (i < mid) items[k++] = scratch[i++];
            while (j < items.Length) items[k++] = scratch[j++];
        }

        /// <summary>List용 안정 정렬 (배열을 하나 할당한다 — 틱 경로에서는 Span 오버로드를 쓴다).</summary>
        public static void StableSort<T>(List<T> list, IComparer<T> comparer)
        {
            var arr = list.ToArray();
            StableSort<T, IComparer<T>>(arr, new T[arr.Length], comparer);
            for (int i = 0; i < arr.Length; i++) list[i] = arr[i];
        }

        public static void StableSort<T>(List<T> list, Comparison<T> comparison) =>
            StableSort(list, Comparer<T>.Create(comparison));

        static void InsertionSort<T, TComparer>(Span<T> items, TComparer comparer) where TComparer : IComparer<T>
        {
            for (int i = 1; i < items.Length; i++)
            {
                var x = items[i];
                var j = i - 1;
                while (j >= 0 && comparer.Compare(items[j], x) > 0)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = x;
            }
        }

        /// <summary>사전 키를 정렬해 buffer에 채운다 (buffer는 비우고 시작). comparer는 **전순서**여야 한다 — 서로 다른 키에 0을 돌려주면 해시 순회 순서가 결과에 남는다.</summary>
        public static List<TKey> SortedKeys<TKey, TValue>(Dictionary<TKey, TValue> dict, List<TKey> buffer, IComparer<TKey> comparer)
        {
            buffer.Clear();
            foreach (var k in dict.Keys) buffer.Add(k);
            StableSort(buffer, comparer);
            return buffer;
        }

        /// <summary>집합 원소를 정렬해 buffer에 채운다.</summary>
        public static List<T> SortedItems<T>(HashSet<T> set, List<T> buffer, IComparer<T> comparer)
        {
            buffer.Clear();
            foreach (var k in set) buffer.Add(k);
            StableSort(buffer, comparer);
            return buffer;
        }

        public static List<string> SortedKeys<TValue>(Dictionary<string, TValue> dict, List<string> buffer) =>
            SortedKeys(dict, buffer, StringComparer.Ordinal);

        public static List<int> SortedKeys<TValue>(Dictionary<int, TValue> dict, List<int> buffer) =>
            SortedKeys(dict, buffer, Comparer<int>.Default);

        public static List<long> SortedKeys<TValue>(Dictionary<long, TValue> dict, List<long> buffer) =>
            SortedKeys(dict, buffer, Comparer<long>.Default);
    }
}
