using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 T28·T29 진단 (판정 없음): 실경기 판정 분해 — 오프사이드 플래그·유형·d 분포, 카드 사유, 파울 판정 단계.</summary>
    public class RefereeDiagnosticReport
    {
        [Test, Explicit("진단 (012 T28)"), Category("Slow")]
        public void Breakdown()
        {
            var sb = new StringBuilder();
            var offFlags = new SortedDictionary<int, int>();
            var offType = new SortedDictionary<int, int>();
            var dBins = new int[8];
            var cardReason = new SortedDictionary<int, int>();
            var foulA = new SortedDictionary<int, int>();
            int contacts = 0, whistles = 0, trueFouls = 0, major = 0, correct = 0, inv = 0, dives = 0, fooled = 0, hb = 0;
            foreach (var sim in LiveReferee.Played())
            {
                var t = LiveReferee.Stage(sim).Telemetry;
                contacts += t.Contacts; whistles += t.Whistles; trueFouls += t.TrueFouls; major += t.MajorDecisions; correct += t.MajorCorrect; inv += t.OffsideInvolvements;
                dives += t.Dives; fooled += t.DivesFooled; hb += t.Handballs;
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    ref readonly var e = ref log[i];
                    if (e.Kind == SimEventKind.Offside)
                    {
                        offFlags[e.B] = offFlags.TryGetValue(e.B, out var a) ? a + 1 : 1;
                        offType[e.A] = offType.TryGetValue(e.A, out var b) ? b + 1 : 1;
                        var bin = (int)((e.F0 + 2f) / 0.5f); if (bin < 0) bin = 0; if (bin > 7) bin = 7;
                        dBins[bin]++;
                    }
                    else if (e.Kind == SimEventKind.Card) cardReason[e.B * 10 + e.A] = cardReason.TryGetValue(e.B * 10 + e.A, out var c) ? c + 1 : 1;
                    else if (e.Kind == SimEventKind.Foul) foulA[e.A] = foulA.TryGetValue(e.A, out var f) ? f + 1 : 1;
                }
            }
            var n = (double)LiveReferee.Matches;
            sb.AppendLine($"경기당 접촉 {contacts / n:F1} · 진짜 파울 {trueFouls / n:F1} · 휘슬 {whistles / n:F1} · 오프사이드 관여 {inv / n:F1} · 다이빙 {dives / n:F2}(속음 {fooled / n:F2}) · 핸드볼 {hb / n:F2} · 주요 판정 정확도 {(major > 0 ? correct / (double)major : 0):P1}");
            sb.Append("오프사이드 B 플래그: "); foreach (var kv in offFlags) sb.Append($"{kv.Key}={kv.Value / n:F2} "); sb.AppendLine();
            sb.Append("오프사이드 유형: "); foreach (var kv in offType) sb.Append($"{kv.Key}={kv.Value / n:F2} "); sb.AppendLine();
            sb.Append("오프사이드 d 구간(−2~+2, 0.5 m): "); foreach (var v in dBins) sb.Append($"{v / n:F2} "); sb.AppendLine();
            sb.Append("카드 사유×10+색: "); foreach (var kv in cardReason) sb.Append($"{kv.Key}={kv.Value / n:F2} "); sb.AppendLine();
            sb.Append("파울 A(내린×10+진짜): "); foreach (var kv in foulA) sb.Append($"{kv.Key}={kv.Value / n:F2} "); sb.AppendLine();
            TestContext.WriteLine(sb.ToString());
        }
    }
}
