using System.Collections.Generic;

namespace RoadToPro.Presentation.Rendering.Logic
{
    /// <summary>하위 시스템 렌더 몫 한 줄 (specs/048 FR-21).</summary>
    public readonly struct RenderShare
    {
        public readonly string System;
        public readonly double GpuMs;
        public readonly int DrawCalls;
        public readonly int Triangles;
        public readonly string Spec;

        public RenderShare(string system, double gpuMs, int drawCalls, int triangles, string spec)
        {
            System = system;
            GpuMs = gpuMs;
            DrawCalls = drawCalls;
            Triangles = triangles;
            Spec = spec;
        }
    }

    /// <summary>CPU 메인 스레드 몫 한 줄 (specs/048 FR-22).</summary>
    public readonly struct CpuShare
    {
        public readonly string System;
        public readonly double Ms;

        public CpuShare(string system, double ms)
        {
            System = system;
            Ms = ms;
        }
    }

    /// <summary>측정된 시스템 하나 (절제 측정 결과).</summary>
    public readonly struct MeasuredShare
    {
        public readonly string System;
        public readonly double GpuMs;
        public readonly int DrawCalls;
        public readonly int Triangles;

        public MeasuredShare(string system, double gpuMs, int drawCalls, int triangles)
        {
            System = system;
            GpuMs = gpuMs;
            DrawCalls = drawCalls;
            Triangles = triangles;
        }
    }

    /// <summary>몫 초과 한 건.</summary>
    public readonly struct ShareOverrun
    {
        public readonly string System;
        public readonly string Metric;
        public readonly double Measured;
        public readonly double Budget;

        public ShareOverrun(string system, string metric, double measured, double budget)
        {
            System = system;
            Metric = metric;
            Measured = measured;
            Budget = budget;
        }
    }

    /// <summary>
    /// 하이라이트 60fps 전체 프레임 예산과 하위 시스템 몫의 원본 (specs/048 FR-20·21·22, 2026-11-10 채택).
    /// 다른 스펙(043·044·045·039)은 이 값을 인용한다.
    /// </summary>
    public static class FrameBudget
    {
        public const double FrameMs = 16.6;
        public const double GpuMs = 10.0;
        public const double CpuMainMs = 8.0;
        public const int DrawCallsLow = 300;
        public const int Triangles = 500_000;
        public const long TextureBytes = 1L << 30;
        public const double MaxFrameMs = 33.0;
        public const double TransitionMaxFrameMs = 25.0;

        public const string Pitch = "pitch", Structure = "structure", Players = "players", Crowd = "crowd",
            Props = "props", Post = "post", Hud = "hud", Reserve = "reserve";

        public static readonly RenderShare[] Shares =
        {
            new RenderShare(Pitch, 1.0, 10, 5_000, "044"),
            new RenderShare(Structure, 1.5, 50, 95_000, "044"),
            new RenderShare(Players, 2.0, 75, 150_000, "043"),
            new RenderShare(Crowd, 1.5, 20, 100_000, "045"),
            new RenderShare(Props, 0.5, 30, 10_000, "041/044"),
            new RenderShare(Post, 1.0, 5, 0, "048"),
            new RenderShare(Hud, 0.5, 30, 0, "039"),
            new RenderShare(Reserve, 2.0, 80, 140_000, "-"),
        };

        public static readonly CpuShare[] CpuShares =
        {
            new CpuShare("playbackAnimation", 3.0),
            new CpuShare("camera", 1.0),
            new CpuShare("ui", 1.0),
            new CpuShare("audio", 0.3),
            new CpuShare("crowd", 0.3),
            new CpuShare("reserve", 2.4),
        };

        /// <summary>043 선수 애니메이션 2 ms (재생·애니메이션 3 ms 안), 그 안의 054 궤적 맞춤 0.8 ms.</summary>
        public const double PlayerAnimationMs = 2.0, TrajectoryFitMs = 0.8;

        public static bool TryGetShare(string system, out RenderShare share)
        {
            foreach (var s in Shares)
                if (s.System == system)
                {
                    share = s;
                    return true;
                }
            share = default;
            return false;
        }

        /// <summary>측정값과 몫을 비교해 넘긴 항목을 돌려준다 (AC-16). 모르는 시스템·여유 칸은 판정하지 않는다.</summary>
        public static List<ShareOverrun> Overruns(IEnumerable<MeasuredShare> measured)
        {
            var list = new List<ShareOverrun>();
            foreach (var m in measured)
            {
                if (m.System == Reserve || !TryGetShare(m.System, out var s)) continue;
                if (m.GpuMs > s.GpuMs) list.Add(new ShareOverrun(m.System, "gpuMs", m.GpuMs, s.GpuMs));
                if (m.DrawCalls > s.DrawCalls) list.Add(new ShareOverrun(m.System, "drawCalls", m.DrawCalls, s.DrawCalls));
                if (s.Triangles > 0 && m.Triangles > s.Triangles) list.Add(new ShareOverrun(m.System, "triangles", m.Triangles, s.Triangles));
            }
            return list;
        }
    }
}
