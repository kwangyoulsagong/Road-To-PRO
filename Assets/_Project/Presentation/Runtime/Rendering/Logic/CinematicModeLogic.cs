using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;

namespace RoadToPro.Presentation.Rendering.Logic
{
    /// <summary>고품질 모드 구간 하나 (specs/061 FR-80). 시각은 시뮬 시각(초).</summary>
    public readonly struct CinematicSpan
    {
        public readonly double Start, End;
        /// <summary>컷(또는 장면 시작)과 함께 들어온다 → 블렌드 없이 바로 켬.</summary>
        public readonly bool CutIn;
        /// <summary>컷(또는 장면 끝)과 함께 나간다 → 블렌드 없이 바로 끔.</summary>
        public readonly bool CutOut;

        public CinematicSpan(double start, double end, bool cutIn, bool cutOut)
        {
            Start = start;
            End = end;
            CutIn = cutIn;
            CutOut = cutOut;
        }
    }

    /// <summary>장면 하나의 고품질 모드 가중치·피사계 심도 초점. 장면 빌드 때 만들고 재생 중엔 조회만 (할당 없음).</summary>
    public sealed class CinematicTimeline
    {
        readonly CinematicSpan[] _spans;
        readonly double _start;
        readonly Vec3[] _focus;

        internal CinematicTimeline(CinematicSpan[] spans, double start, Vec3[] focus)
        {
            _spans = spans;
            _start = start;
            _focus = focus;
        }

        public IReadOnlyList<CinematicSpan> Spans => _spans;

        /// <summary>0(일반) ~ 1(고품질). 컷 없는 진입·퇴장은 0.3 s smoothstep (FR-83).</summary>
        public float Weight(double t)
        {
            for (int i = 0; i < _spans.Length; i++)
            {
                var s = _spans[i];
                if (t < s.Start || t >= s.End) continue;
                float w = 1f;
                if (!s.CutIn) w = MathF.Min(w, CameraMath.Smoothstep(0f, (float)CinematicModeLogic.BlendSeconds, (float)(t - s.Start)));
                if (!s.CutOut) w = MathF.Min(w, CameraMath.Smoothstep(0f, (float)CinematicModeLogic.BlendSeconds, (float)(s.End - t)));
                return w;
            }
            return 0f;
        }

        /// <summary>피사계 심도 초점 위치 (대상 선수, 화면 밖이면 0.3 s에 걸쳐 공으로).</summary>
        public Vec3 Focus(double t)
        {
            if (_focus.Length == 0) return default;
            double u = (t - _start) * HighlightData.FrameRate;
            if (u <= 0d) return _focus[0];
            if (u >= _focus.Length - 1) return _focus[_focus.Length - 1];
            int f = (int)Math.Floor(u);
            float a = (float)(u - f);
            return _focus[f] + (_focus[f + 1] - _focus[f]) * a;
        }

        /// <summary>결정론 비교용 (AC-10).</summary>
        public ulong Hash()
        {
            var h = StableHasher.Create();
            h.Add(_spans.Length);
            foreach (var s in _spans)
            {
                h.Add(s.Start);
                h.Add(s.End);
                h.Add(s.CutIn);
                h.Add(s.CutOut);
            }
            foreach (var f in _focus)
            {
                h.Add(f.X);
                h.Add(f.Y);
                h.Add(f.Z);
            }
            return h.Value;
        }
    }

    /// <summary>
    /// specs/061 FR-80·83: 카메라 트랙(샷 종류·카메라 위치)만으로 고품질 모드 구간을 정한다. 같은 트랙 → 같은 결과.
    /// 047 3D 컷신·076 우승 세레모니 샷은 해당 <see cref="ShotKind"/>이 생기면 <see cref="IsCinematicShot"/>에 추가한다 (연결 때 재확인).
    /// </summary>
    public static class CinematicModeLogic
    {
        /// <summary>이보다 짧은 고품질 구간은 일반 모드로 둔다 [제안] 0.5 s.</summary>
        public const double MinShotSeconds = 0.5d;
        /// <summary>컷 없는 진입·퇴장·초점 이동 블렌드 [제안] 0.3 s.</summary>
        public const double BlendSeconds = 0.3d;
        /// <summary>리플레이 저각 판정: 카메라–공 거리 [제안] 20 m.</summary>
        public const float ReplayNearMeters = 20f;

        public static bool IsCinematicShot(ShotKind shot, float cameraToBallMeters) => shot switch
        {
            ShotKind.Closeup => true,
            ShotKind.Celebration => true,
            ShotKind.ReplayShooter or ShotKind.ReplayReverse or ShotKind.ReplayHigh => cameraToBallMeters <= ReplayNearMeters,
            _ => false,
        };

        /// <param name="ballAt">시뮬 시각 → 공 위치.</param>
        /// <param name="subjectAt">시뮬 시각 → 초점 대상(득점자 등) 위치, 없으면 공.</param>
        public static CinematicTimeline Build(CameraTrack track, Func<double, Vec3> ballAt, Func<double, Vec3> subjectAt)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (ballAt == null) throw new ArgumentNullException(nameof(ballAt));
            int n = track.Frames;
            double dt = 1d / HighlightData.FrameRate;
            var on = new bool[n];
            var focus = new Vec3[n];
            double offSince = double.NaN;
            for (int f = 0; f < n; f++)
            {
                double t = track.StartTime + f * dt;
                var pose = track.Poses[f];
                var ball = ballAt(t);
                on[f] = IsCinematicShot(track.Shots[f], (ball - pose.Position).Length());

                var subject = subjectAt != null ? subjectAt(t) : ball;
                bool visible = subjectAt == null || InView(pose, subject);
                if (visible) offSince = double.NaN;
                else if (double.IsNaN(offSince)) offSince = t;
                float toBall = double.IsNaN(offSince) ? 0f : CameraMath.Smoothstep(0f, (float)BlendSeconds, (float)(t - offSince));
                focus[f] = subject + (ball - subject) * toBall;
            }

            var spans = new List<CinematicSpan>();
            int i = 0;
            while (i < n)
            {
                if (!on[i])
                {
                    i++;
                    continue;
                }
                int j = i;
                while (j + 1 < n && on[j + 1]) j++;
                double start = track.StartTime + i * dt;
                double end = track.StartTime + (j + 1) * dt;
                if (end - start >= MinShotSeconds - 1e-9)
                {
                    bool cutIn = i == 0 || track.CutAt[i];
                    bool cutOut = j == n - 1 || track.CutAt[j + 1];
                    spans.Add(new CinematicSpan(start, end, cutIn, cutOut));
                }
                i = j + 1;
            }
            return new CinematicTimeline(spans.ToArray(), track.StartTime, focus);
        }

        static bool InView(in CameraPose pose, Vec3 p)
        {
            CameraMath.Basis(pose.Forward, out var right, out var up);
            var v = p - pose.Position;
            float z = Vec3.Dot(v, pose.Forward);
            if (z <= 0.1f) return false;
            return MathF.Abs(Vec3.Dot(v, right)) <= pose.TanH * z && MathF.Abs(Vec3.Dot(v, up)) <= pose.TanV * z;
        }

        /// <summary>카메라에 가까운 순으로 최대 <paramref name="max"/>명 (062 최고 품질 등급 신호, FR-81). 할당 없음.</summary>
        public static int TopTier(Vec3 camera, IReadOnlyList<Vec3> players, int max, int[] result)
        {
            int count = 0;
            int limit = Math.Min(max, result.Length);
            for (int k = 0; k < limit; k++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int p = 0; p < players.Count; p++)
                {
                    bool used = false;
                    for (int q = 0; q < count; q++)
                        if (result[q] == p) used = true;
                    if (used) continue;
                    float d = (players[p] - camera).LengthSq();
                    if (d < bestD)
                    {
                        bestD = d;
                        best = p;
                    }
                }
                if (best < 0) break;
                result[count++] = best;
            }
            return count;
        }
    }
}
