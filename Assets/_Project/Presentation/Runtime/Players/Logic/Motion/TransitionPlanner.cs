using System;
using System.Collections.Generic;

namespace RoadToPro.Presentation.Players.Logic.Motion
{
    /// <summary>로코모션 전환 동작 한 개 (출발·감속·컷·턴). 클립 시각 = (t − Start) × Rate.</summary>
    public readonly struct TransitionClip
    {
        public readonly int Clip;
        public readonly float Start, End, Rate;

        public TransitionClip(int clip, float start, float end, float rate)
        {
            Clip = clip;
            Start = start;
            End = end;
            Rate = rate;
        }
    }

    /// <summary>
    /// 궤적에서 전환 동작을 찾아 클립을 끼운다 (specs/054 FR-13·20·21·23·24, 플랜 "탐지 규칙"). 장면 로드 때 한 번.
    /// 공 접촉 액션이 항상 이긴다: 겹치면 전환을 버리거나 그 앞에서 끝낸다. 접촉 시각은 밀리지 않는다.
    /// </summary>
    public static class TransitionPlanner
    {
        const float Dt = 1f / HighlightData.FrameRate;
        const float Deg = (float)(Math.PI / 180.0);
        const float MinRate = 0.65f, MaxRate = 1.6f;
        public const float FadeIn = 0.10f, FadeOut = 0.15f;
        const float ActionGuard = 0.10f;
        const float StartMinSpeed = 4f, StopMinSpeed = 3f;
        const float MinTurnDeg = 45f;
        const int StopLookFrames = 60;   // 9.7 m/s ÷ 감속 4.5~7 m/s² ≈ 2 s

        static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        static float Time(HighlightData d, int f) => d.StartTime + f * Dt;

        static float AngleDelta(float a, float b) => (float)Math.IEEERemainder(b - a, 2 * Math.PI);

        public static List<TransitionClip> Plan(ClipLibrary lib, HighlightData d, MotionTrack tr, int e, ActionTimeline actions)
        {
            var found = new List<TransitionClip>();
            FindStarts(lib, d, tr, e, found);
            FindStops(lib, d, tr, e, found);
            FindCuts(lib, d, tr, e, found);
            FindTurns(lib, d, tr, e, found);
            found.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Clip.CompareTo(b.Clip));
            var o = new List<TransitionClip>(found.Count);
            foreach (var c0 in found)
            {
                var c = c0;
                // 앞 전환과 겹치면 앞 전환을 여기서 끝낸다 (나중 사건이 최신 궤적이다)
                if (o.Count > 0 && o[o.Count - 1].End > c.Start)
                {
                    var p = o[o.Count - 1];
                    if (c.Start - p.Start < FadeIn + FadeOut) continue;
                    o[o.Count - 1] = new TransitionClip(p.Clip, p.Start, c.Start + FadeOut, p.Rate);
                }
                if (!TrimForActions(actions, ref c)) continue;
                o.Add(c);
            }
            return o;
        }

        static bool TrimForActions(ActionTimeline tl, ref TransitionClip c)
        {
            for (int k = 0; k < tl.Count; k++)
            {
                var a = tl.Get(k);
                float a0 = a.Start - ActionGuard, a1 = a.End;
                if (c.End <= a0 || c.Start >= a1) continue;
                if (c.Start >= a0) return false;
                // 액션 앞에서 끝낸다. 너무 짧아지면 버린다
                if (a0 - c.Start < FadeIn + FadeOut) return false;
                c = new TransitionClip(c.Clip, c.Start, a0, c.Rate);
            }
            return true;
        }

        /// <summary>정지(≤ 0.3 m/s) → 가속 ≥ 2.78 m/s² → 1.2 s 안에 4 m/s 이상: start_f.</summary>
        static void FindStarts(ClipLibrary lib, HighlightData d, MotionTrack tr, int e, List<TransitionClip> o)
        {
            int clip = lib.IndexOf("start_f");
            if (clip < 0) return;
            var c = lib.Get(clip);
            for (int f = 1; f < tr.Frames; f++)
            {
                if (!(tr.Speed(f - 1, e) <= MotionDefaults.StopSpeed && tr.Speed(f, e) > MotionDefaults.StopSpeed)) continue;
                int look = Math.Min(tr.Frames - 1, f + 36);
                if (tr.Speed(look, e) < StartMinSpeed) continue;
                bool hard = false;
                for (int g = f; g <= Math.Min(tr.Frames - 1, f + 12) && !hard; g++) hard = tr.AccelAlong(g, e) >= MotionDefaults.AccelPosture;
                if (!hard) continue;
                // 클립 평균 속도 vs 시뮬 평균 속도로 재생 속도 맞춤 (보폭 끝맺음은 발 고정이 맡는다)
                float clipAvg = 0f;
                foreach (var v in c.SpeedCurve) clipAvg += v;
                clipAvg /= c.SpeedCurve.Length;
                int span = Math.Min(look, f + (int)(c.Length * HighlightData.FrameRate));
                float simAvg = 0f;
                for (int g = f; g <= span; g++) simAvg += tr.Speed(g, e);
                simAvg /= Math.Max(1, span - f + 1);
                float rate = Clamp(simAvg / clipAvg, MinRate, MaxRate);
                float lead = c.StanceR.Length > 0 ? 6f / ClipInfo.Fps : 0f;    // 앞 6프레임 = 준비 자세 → 첫 걸음 (055 start_f)
                float start = Time(d, f) - lead / rate;
                o.Add(new TransitionClip(clip, Math.Max(d.StartTime, start), start + c.Length / rate, rate));
            }
        }

        /// <summary>감속 ≥ 3 m/s², 3 m/s 이상에서 2 s 안에 정지: 짧으면 quick_stop, 아니면 stop_f.</summary>
        static void FindStops(ClipLibrary lib, HighlightData d, MotionTrack tr, int e, List<TransitionClip> o)
        {
            int stopF = lib.IndexOf("stop_f"), quick = lib.IndexOf("quick_stop");
            if (stopF < 0 || quick < 0) return;
            for (int f = 1; f < tr.Frames; f++)
            {
                if (tr.AccelAlong(f, e) > -MotionDefaults.DecelPosture || tr.AccelAlong(f - 1, e) <= -MotionDefaults.DecelPosture) continue;
                if (tr.Speed(f, e) < StopMinSpeed) continue;
                int g = f;
                while (g < tr.Frames - 1 && tr.Speed(g, e) > MotionDefaults.StopSpeed && g - f < StopLookFrames) g++;
                if (tr.Speed(g, e) > MotionDefaults.StopSpeed) continue;
                float dur = (g - f) * Dt;
                var c = lib.Get(dur < 0.5f ? quick : stopF);
                // 클립의 제동 끝(뒤 8프레임 전)을 시뮬 정지 시각에 맞춘다
                float brake = Math.Max(0.1f, c.Length - 8f / ClipInfo.Fps);
                float rate = Clamp(brake / dur, MinRate, MaxRate);
                // 제동 끝을 시뮬 정지 시각에 맞춘다: 재생 속도 범위로 다 못 늘리면 늦게 시작 (앞부분은 자세 오버레이가 맡는다)
                float start = Math.Max(Time(d, f), Time(d, g) - brake / rate);
                o.Add(new TransitionClip(c.Index, start, start + c.Length / rate, rate));
                f = g;
            }
        }

        /// <summary>속도 ≥ 2 m/s(들어오거나 나갈 때)에서 0.35 s 안에 진행 방향 ≥ 45°: 바깥발 디딤 컷. 디딤 프레임을 가장 많이 도는 프레임에 맞춘다.</summary>
        static void FindCuts(ClipLibrary lib, HighlightData d, MotionTrack tr, int e, List<TransitionClip> o)
        {
            int win = (int)Math.Round(MotionDefaults.CutWindow * HighlightData.FrameRate);
            for (int f = 0; f + win < tr.Frames; f++)
            {
                // 005는 2 m/s 아래에서 피벗한다: 컷은 들어올 때 또는 나갈 때 속도가 2 m/s 이상이면 된다
                if (tr.Speed(f, e) < MotionDefaults.PivotSpeed && tr.Speed(f + win, e) < MotionDefaults.PivotSpeed) continue;
                float delta = AngleDelta(tr.Heading(f, e), tr.Heading(f + win, e)) / Deg;
                float abs = Math.Abs(delta);
                if (abs < MotionDefaults.CutHeadingDeg || abs >= MotionDefaults.TurnHeadingDeg) continue;
                int peak = f;
                float best = -1f;
                for (int g = f; g < f + win; g++)
                {
                    float dh = Math.Abs(AngleDelta(tr.Heading(g, e), tr.Heading(g + 1, e)));
                    if (dh > best) { best = dh; peak = g; }
                }
                // 차분 창에 걸친 값으로 먼저 걸린다: 클립 각도는 창 뒤까지 본 전체 방향 변화로 정한다
                int after = Math.Min(tr.Frames - 1, f + 2 * win);
                float total = AngleDelta(tr.Heading(f, e), tr.Heading(after, e)) / Deg;
                if (Math.Abs(total) >= MotionDefaults.TurnHeadingDeg) { f += win; continue; }
                string name = (total > 0 ? "cut_r" : "cut_l") + (Math.Abs(total) >= 67.5f ? "90" : "45");
                int clip = lib.IndexOf(name);
                if (clip >= 0)
                {
                    var c = lib.Get(clip);
                    // 발이 방향 전환 지점에 닿도록: 디딤 위치 = 그 순간 루트 + 몸 앞쪽 발끝 위치. 그만큼 일찍 딛는다 (FR-23)
                    var spans = c.PlantLeft ? c.StanceL : c.StanceR;
                    float plantAt = spans.Length > 0 ? spans[0].F0 : c.PlantFrame;
                    float ahead = spans.Length > 0 ? spans[0].Z0 / Math.Max(1f, tr.Speed(peak, e)) : 0f;
                    float start = Time(d, peak) - plantAt / ClipInfo.Fps - ahead;
                    o.Add(new TransitionClip(clip, Math.Max(d.StartTime, start), start + c.Length, 1f));
                }
                f += win;
            }
        }

        /// <summary>저속(2 m/s 미만)에서 몸 방향·진행 방향이 45° 이상 바뀜: 스텝 턴, 135° 이상이면 turn_180.</summary>
        static void FindTurns(ClipLibrary lib, HighlightData d, MotionTrack tr, int e, List<TransitionClip> o)
        {
            const int E = HighlightData.Entities;
            int win = 15;
            for (int f = 0; f + win < tr.Frames; f++)
            {
                float delta = AngleDelta(d.Yaw[f * E + e], d.Yaw[(f + win) * E + e]) / Deg;
                float abs = Math.Abs(delta);
                if (abs < MinTurnDeg) continue;
                // 저속 피벗은 양쪽 다 2 m/s 미만. 135° 이상 돌면 속도와 상관없이 턴
                bool slow = tr.Speed(f, e) < MotionDefaults.PivotSpeed && tr.Speed(f + win, e) < MotionDefaults.PivotSpeed;
                if (!slow && abs < MotionDefaults.TurnHeadingDeg) continue;
                // 회전 구간: 변화가 시작하고 끝나는 프레임
                int a = f;
                while (a < f + win && Math.Abs(AngleDelta(d.Yaw[a * E + e], d.Yaw[(a + 1) * E + e])) < 0.5f * Deg) a++;
                int b = f + win;
                while (b > a + 1 && Math.Abs(AngleDelta(d.Yaw[(b - 1) * E + e], d.Yaw[b * E + e])) < 0.5f * Deg) b--;
                string name = abs >= MotionDefaults.TurnHeadingDeg ? "turn_180" : delta > 0 ? "turn_r90" : "turn_l90";
                int clip = lib.IndexOf(name);
                if (clip >= 0)
                {
                    var c = lib.Get(clip);
                    float dur = Math.Max(Dt, (b - a) * Dt);
                    float rate = Clamp(c.Length / dur, MinRate, MaxRate);
                    float start = Time(d, a);
                    o.Add(new TransitionClip(clip, start, start + c.Length / rate, rate));
                }
                f = b + win;
            }
        }
    }

    /// <summary>
    /// 프레임별 자세 오버레이 (specs/054 FR-20~22): 횡가속 기울기 + 가감속 숙임·젖힘. 온몸을 발 쪽 지면 기준으로 기울인다 — 발은 발 고정이 되돌린다.
    /// </summary>
    public sealed class PostureTrack
    {
        const int E = HighlightData.Entities;
        /// <summary>가감속 자세는 물리값 atan(a/g)의 이 비율 [제안]: 클립이 이미 기본 경사를 갖는다.</summary>
        const float PitchScale = 0.6f;
        const float MaxPitch = 0.2f, MaxRoll = 0.6f;
        /// <summary>이 속도 아래는 기울기 없음(정지·저속 피벗에서 넘어져 보이지 않게).</summary>
        const float MinSpeed = 1f;
        const int SmoothFrames = 2;

        readonly float[] _roll, _pitch;
        readonly HighlightData _d;

        public PostureTrack(HighlightData d, MotionTrack tr)
        {
            _d = d;
            int n = d.Frames * E;
            _roll = new float[n];
            _pitch = new float[n];
            var rr = new float[d.Frames];
            var pp = new float[d.Frames];
            for (int e = 0; e < E; e++)
            {
                for (int f = 0; f < d.Frames; f++)
                {
                    float v = tr.Speed(f, e);
                    float k = v <= MinSpeed ? 0f : v >= 2f * MinSpeed ? 1f : (v - MinSpeed) / MinSpeed;
                    float roll = MotionTrack.LeanFor(tr.AccelLat(f, e)) * k;
                    rr[f] = roll > MaxRoll ? MaxRoll : roll < -MaxRoll ? -MaxRoll : roll;
                    float al = tr.AccelAlong(f, e);
                    float pitch = 0f;
                    if (al >= MotionDefaults.AccelPosture) pitch = MotionTrack.LeanFor(al) * PitchScale;
                    else if (al <= -MotionDefaults.DecelPosture) pitch = MotionTrack.LeanFor(al) * PitchScale;
                    pp[f] = pitch > MaxPitch ? MaxPitch : pitch < -MaxPitch ? -MaxPitch : pitch;
                }
                for (int f = 0; f < d.Frames; f++)
                {
                    float sr = 0f, sp = 0f;
                    int c = 0;
                    for (int g = Math.Max(0, f - SmoothFrames); g <= Math.Min(d.Frames - 1, f + SmoothFrames); g++)
                    {
                        sr += rr[g];
                        sp += pp[g];
                        c++;
                    }
                    _roll[f * E + e] = sr / c;
                    _pitch[f * E + e] = sp / c;
                }
            }
        }

        /// <summary>시각 t의 기울기(라디안): roll + = 오른쪽, pitch + = 앞.</summary>
        public void At(int e, float t, out float roll, out float pitch)
        {
            _d.FrameAt(t, out var f, out var a);
            int i = f * E + e;
            roll = _roll[i] + (_roll[i + E] - _roll[i]) * a;
            pitch = _pitch[i] + (_pitch[i + E] - _pitch[i]) * a;
        }
    }
}
