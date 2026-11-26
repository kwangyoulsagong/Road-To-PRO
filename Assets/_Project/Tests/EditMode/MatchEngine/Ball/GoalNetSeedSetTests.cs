using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Tests.EditMode.Presentation.Highlight;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>
    /// specs/059 AC-12 · AC-03(이탈 부분), 2026-11-26 사용자 결정으로 개정:
    /// ① 고정 시드 80경기 이벤트 로그가 "공이 골라인 밖으로 처음 나가는 사건(골·골킥·코너)"까지 main(그물 면 변경 전)과 똑같다 —
    ///    그물 코드는 골라인 뒤 공만 다루므로 그 전까지 경기가 갈라질 수 없다. 기준은 main에서 뽑은 `evidence/events-main-prefix.txt`.
    ///    (그 뒤는 나간 공 움직임 → 재시작 시각 차이로 경기가 갈라진다. 전체 골·슈팅 수는 기록만.)
    /// ② 골 200개 이상에서 골 뒤 재시작 전까지 자유로운 공(Air·Rolling, 30 Hz 기록)이 그물 상자(뒤·옆·위 면 − 반지름 + 주머니 깊이, FR-41 ⑥) 밖으로 나간 프레임 0 (FR-43).
    ///    선수가 몰고 들어간 공(Controlled)은 스펙 제외 범위(선수×그물)라 세어서 기록만 한다.
    /// 실경기 픽스처는 플레이어 하이라이트와 같다(<see cref="LiveContactAudit"/>, 시드별 1회 공유).
    /// </summary>
    [Category("Slow")]
    public class GoalNetSeedSetTests
    {
        const int SeedCount = 80;
        const ulong FirstSeed = 59_000UL;
        const int MinGoals = 200;
        const float Eps = 0.01f;

        static string BaselinePath => Path.Combine(Directory.GetCurrentDirectory(), "specs", "059-goal-net", "evidence", "events-main-prefix.txt");

        static List<ulong> Seeds()
        {
            var l = new List<ulong>();
            for (int i = 0; i < SeedCount; i++) l.Add(FirstSeed + (ulong)i);
            return l;
        }

        static bool IsGoalLineExit(SimEventKind k) => k == SimEventKind.Goal || k == SimEventKind.GoalKick || k == SimEventKind.Corner;

        static string Line(ulong seed, in SimEvent ev) =>
            string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} t{4} p{5} ({6:F3},{7:F3},{8:F2})", seed, ev.Seq, ev.Tick, ev.Kind, ev.Team, ev.PlayerId, ev.X, ev.Y, ev.Z);

        /// <summary>시드마다 첫 골라인 밖 사건(포함)까지의 이벤트 줄, 시드 순.</summary>
        static List<string> PrefixLines(out int goals, out int shots)
        {
            var seeds = Seeds();
            LiveContactAudit.Prewarm(seeds);
            var lines = new List<string>();
            goals = shots = 0;
            foreach (var seed in seeds)
            {
                var sim = LiveContactAudit.Played90(seed);
                bool done = false;
                foreach (var ev in sim.Result.EventLog)
                {
                    if (ev.Kind == SimEventKind.Goal) goals++;
                    if (ev.Kind == SimEventKind.Shot) shots++;
                    if (done) continue;
                    lines.Add(Line(seed, ev));
                    if (IsGoalLineExit(ev.Kind)) done = true;
                }
            }
            return lines;
        }

        /// <summary>기준 파일 만들기 — main(그물 면 변경 전) 코드에서 한 번 돌린다. 평소에는 돌지 않는다.</summary>
        [Test, Explicit("059 기준 이벤트 생성 (main 코드에서만)")]
        public void DumpBaseline()
        {
            // 클래스 단위 실행에서도 Explicit가 돌아 기준을 덮어쓰는 일을 막는다 (T04에서 한 번 겪음)
            if (Environment.GetEnvironmentVariable("RTP_DUMP_059") != "1") Assert.Ignore("RTP_DUMP_059=1일 때만 기준을 만든다");
            var lines = PrefixLines(out var goals, out var shots);
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath));
            File.WriteAllText(BaselinePath, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            TestContext.WriteLine($"main: 이벤트 {lines.Count}줄, 전체 골 {goals}, 슈팅 {shots} → {BaselinePath}");
        }

        [Test]
        public void Matches80_SameEventsUntilFirstGoalLineExit()
        {
            Assert.IsTrue(File.Exists(BaselinePath), "기준 이벤트 없음 — main에서 RTP_DUMP_059=1로 DumpBaseline을 돌려 만든다");
            var expected = new List<string>(File.ReadAllText(BaselinePath).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            var actual = PrefixLines(out var goals, out var shots);
            TestContext.WriteLine($"이벤트 {actual.Count}줄 비교, 이번 코드 전체 골 {goals}, 슈팅 {shots} (기록만)");
            for (int i = 0; i < Math.Min(expected.Count, actual.Count); i++)
                Assert.AreEqual(expected[i], actual[i], $"{i}번째 줄이 main과 다름 — 첫 골라인 밖 사건 전에 경기가 갈라짐");
            Assert.AreEqual(expected.Count, actual.Count, "비교 줄 수가 main과 다름");
        }

        /// <summary>
        /// 결정론 리뷰(2차) 1: 골 직후 공이 그물 안(주머니)에 있는 구간의 스냅샷에서 복원해 끝까지 돌려도 원본과 같다 —
        /// 스냅샷이 공의 그물 표시(NetSide)를 담는다 (형식 8). 그물 안 구간 스냅샷 3개.
        /// </summary>
        [Test]
        public void RestoreInsideNet_SameAsOriginal()
        {
            var seeds = Seeds();
            LiveContactAudit.Prewarm(seeds);
            int done = 0;
            foreach (var seed in seeds)
            {
                if (done >= 3) break;
                var sim = LiveContactAudit.Played90(seed);
                foreach (var ev in sim.Result.EventLog)
                {
                    if (ev.Kind != SimEventKind.Goal || done >= 3) continue;
                    MatchSnapshot snap = null;
                    foreach (var sn in sim.Snapshots)
                        if (sn.Tick > ev.Tick && sn.Tick <= ev.Tick + 60) { snap = sn; break; }
                    if (snap == null) continue;
                    var r = sim.Fork();
                    r.Restore(snap, sim.Log, sim.Checkpoints);
                    var res = r.RunToEnd();
                    Assert.AreEqual(sim.Result.FinalStateHash, res.FinalStateHash, $"seed {seed} 골 {ev.Tick} 뒤 스냅샷 {snap.Tick}: 끝 상태");
                    Assert.AreEqual(sim.Result.EventLogHash, res.EventLogHash, $"seed {seed} 골 {ev.Tick} 뒤 스냅샷 {snap.Tick}: 이벤트");
                    Assert.IsNull(CheckpointLog.FindFirstDivergence(res.Checkpoints, sim.Result.Checkpoints), "체크포인트 해시");
                    done++;
                }
            }
            Assert.GreaterOrEqual(done, 1, "골 직후 6 s 안 스냅샷이 있는 골이 있어야 한다");
        }

        [Test]
        public void Goals200_FreeBall_NoEscape()
        {
            var seeds = Seeds();
            LiveContactAudit.Prewarm(seeds);
            var p = BallTestKit.Params();
            float half = BallParams.GoalWidth * 0.5f, L = p.Pitch.LengthM, cy = p.GoalCenterY, r = p.Radius;
            int goals = 0, escapes = 0, carried = 0;
            var sb = new StringBuilder();
            foreach (var seed in seeds)
            {
                var sim = LiveContactAudit.Played90(seed);
                foreach (var ev in sim.Result.EventLog)
                {
                    if (ev.Kind != SimEventKind.Goal) continue;
                    goals++;
                    var clip = sim.ExtractClip(ev.Tick, ev.Tick + 80); // 골 뒤 8 s (세리머니 7 s + 여유)
                    bool inGoal = false;
                    foreach (var f in clip.Frames)
                    {
                        float x = f.X(ReplayFrame.BallEntity), y = f.Y(ReplayFrame.BallEntity), z = f.Z(ReplayFrame.BallEntity);
                        bool behind = x < 0f || x > L;
                        if (!behind) { if (inGoal) break; continue; } // 재시작 순간이동 등으로 피치에 돌아오면 끝
                        inGoal = true;
                        float back = x < 0f ? -x : x - L;
                        bool outBack = back > p.NetDepth - r + p.NetPocket + Eps;   // FR-41 ⑥ 주머니까지
                        bool outSide = Math.Abs(y - cy) > half - r + p.NetPocket + Eps;
                        bool outTop = z > BallParams.GoalHeight - r + p.NetPocket + Eps;
                        if ((outBack || outSide || outTop) && f.BallPhase == BallPhase.Controlled) { carried++; break; } // 선수×그물 — 제외 범위, 기록만
                        if (outBack || outSide || outTop)
                        {
                            escapes++;
                            if (escapes <= 10) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "seed {0} goal tick {1} frame {2}: ({3:F3},{4:F3},{5:F3}) {6} owner {7}", seed, ev.Tick, f.Index, x, y, z, f.BallPhase, f.BallOwnerSlot));
                            break;
                        }
                    }
                }
            }
            TestContext.WriteLine($"골 {goals}개, 자유로운 공 그물 상자 이탈 {escapes}개, 선수가 몰고 나간 공(제외·기록) {carried}개\n{sb}");
            Assert.GreaterOrEqual(goals, MinGoals, "검증 골 수 부족");
            Assert.AreEqual(0, escapes, "골 뒤 공이 그물 상자 밖 (FR-43):\n" + sb);
        }
    }
}
