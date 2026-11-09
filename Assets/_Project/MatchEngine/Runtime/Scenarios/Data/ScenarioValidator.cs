using System;
using System.Collections.Generic;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>
    /// 파일을 넘나드는 검증 (FR-04): 중복 id, 분기 대상, 무조건 분기 순환, 배역 상한.
    /// 오류가 난 시나리오만 빼고 나머지는 그대로 둔다. 로드되지 않은 카탈로그 시나리오를 가리키는 분기는 경고 후 폴백이다.
    /// </summary>
    public static class ScenarioValidator
    {
        public const int MaxAttackCast = 5, MaxDefenseCast = 4;

        public static List<ScenarioDefinition> Validate(List<ScenarioDefinition> parsed, HashSet<string> catalogIds, ScenarioLoadReport report)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in parsed)
            {
                counts.TryGetValue(d.Id, out var n);
                counts[d.Id] = n + 1;
            }

            var bad = new HashSet<ScenarioDefinition>();
            foreach (var d in parsed)
                if (counts[d.Id] > 1)
                {
                    report.Errors.Add(new ScenarioIssue(d.SourceFile, d.Line, "id", $"중복 id '{d.Id}'"));
                    bad.Add(d);
                }

            foreach (var d in parsed)
            {
                if (bad.Contains(d)) continue;
                int own = 0, opp = 0;
                foreach (var r in d.Cast) { if (r.Opponent) opp++; else own++; }
                var ownCap = d.Side == ScenarioSide.Attack ? MaxAttackCast : MaxDefenseCast;
                var oppCap = d.Side == ScenarioSide.Attack ? MaxDefenseCast : MaxAttackCast;
                if (own > ownCap || opp > oppCap)
                {
                    report.Errors.Add(new ScenarioIssue(d.SourceFile, d.Line, "cast", $"배역 상한 초과 (자기 팀 {own}/{ownCap}, 상대 {opp}/{oppCap})"));
                    bad.Add(d);
                }
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in parsed) known.Add(d.Id);

            foreach (var d in parsed)
            {
                if (bad.Contains(d)) continue;
                var ok = true;
                foreach (var b in d.Branches)
                {
                    ok &= CheckTarget(d, b.Success, known, catalogIds, report);
                    ok &= CheckTarget(d, b.Fail, known, catalogIds, report);
                    ok &= CheckTarget(d, b.SituationTarget, known, catalogIds, report);
                    foreach (var a in b.Alts) ok &= CheckTarget(d, a.Target, known, catalogIds, report);
                }
                if (!ok) bad.Add(d);
            }

            RejectUnconditionalCycles(parsed, bad, report);

            var result = new List<ScenarioDefinition>(parsed.Count);
            foreach (var d in parsed)
                if (!bad.Contains(d)) result.Add(d);
            return result;
        }

        static bool CheckTarget(ScenarioDefinition d, BranchTarget t, HashSet<string> known, HashSet<string> catalog, ScenarioLoadReport report)
        {
            if (t == null || t.Kind != BranchTargetKind.Chain) return true;
            var ok = true;
            foreach (var id in t.Ids)
            {
                if (known.Contains(id)) continue;
                if (catalog != null && catalog.Contains(id)) continue;
                report.Errors.Add(new ScenarioIssue(d.SourceFile, d.Line, "branches", $"존재하지 않는 분기 대상 '{id}'"));
                ok = false;
            }
            return ok;
        }

        /// <summary>
        /// 단계 실행 없이 `when: always`로만 이어지는 순환을 찾는다. 단계 결과를 거치는 순환은 런타임 체이닝 상한이 끊는다.
        /// 자기 자신으로 돌아오는 시나리오만 제외하므로 방문 순서와 무관하다.
        /// </summary>
        static void RejectUnconditionalCycles(List<ScenarioDefinition> parsed, HashSet<ScenarioDefinition> bad, ScenarioLoadReport report)
        {
            var byId = new Dictionary<string, ScenarioDefinition>(StringComparer.Ordinal);
            foreach (var d in parsed)
                if (!bad.Contains(d)) byId[d.Id] = d;

            var cyclic = new List<ScenarioDefinition>();
            foreach (var d in parsed)
                if (!bad.Contains(d) && ReachesSelf(d, byId)) cyclic.Add(d);

            foreach (var d in cyclic)
            {
                bad.Add(d);
                report.Errors.Add(new ScenarioIssue(d.SourceFile, d.Line, "branches", "순환 체이닝: 무조건 분기만으로 무한 루프가 된다"));
            }
        }

        static bool ReachesSelf(ScenarioDefinition start, Dictionary<string, ScenarioDefinition> byId)
        {
            var seen = new HashSet<ScenarioDefinition>();
            var stack = new Stack<ScenarioDefinition>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                foreach (var b in d.Branches)
                {
                    if (!b.HasSituation || b.Situation != BranchCondition.Always || b.SituationTarget == null || b.SituationTarget.Kind != BranchTargetKind.Chain) continue;
                    foreach (var id in b.SituationTarget.Ids)
                    {
                        if (!byId.TryGetValue(id, out var next)) continue;
                        if (ReferenceEquals(next, start)) return true;
                        if (seen.Add(next)) stack.Push(next);
                    }
                }
            }
            return false;
        }
    }
}
