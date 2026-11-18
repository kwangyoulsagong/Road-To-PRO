using System;
using RoadToPro.Core.Math;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 놓친 공중볼 경합 점프 (specs/083 FR-01·02). 071 Missed 경합의 승자·가장 가까운 상대 참여자가 기록 정점 시각에 맞춰 점프 헤딩 동작을 한다.
    /// 공에 맞추지 않는다(루트 보정 0, 054 FR-36·43). 점프 높이 = 기록 점프 높이를 082 점프 리프트 범위(<see cref="HeaderMotion.JumpLift"/>)로 자름.
    /// 같은 선수의 다른 동작이 정점 전에 시작해 점프 구간과 겹치면 점프하지 않는다(FR-02). 정점 뒤 다음 동작이 있으면 그 시작에서 끝낸다(054 FR-82).
    /// </summary>
    public static class MissedJumpMotion
    {
        /// <summary>점프 액션의 타임라인 번호: 이벤트 번호(≥ 0)와 겹치지 않는 음수 — 054 접촉 측정이 Header 이벤트 시각으로 재지 않게.</summary>
        public static long SeqOf(long headerSeq) => -(headerSeq + 1);

        public const string ReasonOverlap = "다른 동작과 겹침 (FR-02)";
        public const string ReasonKeeperBox = "GK 자기 박스 (011 손 처리)";
        public const string ReasonNoRecord = "경합 기록 없음";
        public const string ReasonNoClip = "점프 클립 없음";
        public const string ReasonBoundary = "정점이 장면 밖";
        public const string ReasonBallLow = "공이 머리 위로 안 지나감 (085 FR-02)";
        public const string ReasonBallHigh = "공이 구간 내내 머리 위 (085 경계)";

        /// <summary>
        /// 기록 정점에서 정점을 옮길 수 있는 최대 폭(s) — specs/085 Q4, 071 FR-06 자름 폭과 같음.
        /// 값은 Data/tuning/highlight.json `highlight.missedJumpWindow`와 같다(테스트가 확인).
        /// </summary>
        public const float Window = 0.5f;

        /// <summary>
        /// 정점 목표 높이(m, 시뮬 Y) — specs/085 FR-01: 점프 머리 높이(점프 클립 접촉점 Y × 배율 + 083 리프트) + 머리 여유(<see cref="HeadClearance"/>).
        /// 공이 이 높이를 내려오며 지나는 순간이 정점이다. 클립이 없으면 NaN.
        /// </summary>
        public static float TargetHeadY(ClipLibrary lib, float jumpHeight, float scale)
        {
            int clip = lib.IndexOf(HeaderMotion.JumpClip), stand = lib.IndexOf(HeaderMotion.StandClip);
            if (clip < 0 || stand < 0) return float.NaN;
            float standY = lib.Get(stand).ContactPoint.Y, jumpY = lib.Get(clip).ContactPoint.Y;
            float lift = HeaderMotion.JumpLift(standY * scale + jumpHeight, jumpY, standY, scale);
            return jumpY * scale + lift + HeadClearance;
        }

        /// <summary>
        /// specs/085 FR-01·02: [center − window, center + window] ∩ 장면 안에서 공(30 Hz 프레임 선형 보간)이 <paramref name="targetY"/>를
        /// 위에서 아래로 처음 지나는 시각. 없으면 false와 사유 — 한 번도 위에 없음 → <see cref="ReasonBallLow"/>,
        /// 내내 위(내려오지 않음) → <see cref="ReasonBallHigh"/>, 구간이 장면과 안 겹치거나 장면 경계에서 잘려 판단할 수 없음 → <see cref="ReasonBoundary"/>.
        /// 할당 없음, 프레임 순서대로 한 번 훑는다.
        /// </summary>
        public static bool FindPassTime(HighlightData d, float targetY, float center, float window, out float t, out string reason)
        {
            t = float.NaN;
            if (float.IsNaN(targetY) || float.IsNaN(center)) { reason = ReasonNoRecord; return false; }
            float lo = Math.Max(d.StartTime, center - window), hi = Math.Min(d.EndTime, center + window);
            bool clipped = center - window < d.StartTime || center + window > d.EndTime;
            if (!(lo < hi) || d.Frames < 2) { reason = ReasonBoundary; return false; }
            float dt = 1f / HighlightData.FrameRate;
            float pt = lo, py = BallY(d, lo);
            bool above = py >= targetY;
            int f = Math.Max(0, (int)Math.Ceiling((lo - d.StartTime) * HighlightData.FrameRate));
            while (true)
            {
                float ft = f < d.Frames ? d.StartTime + f * dt : float.PositiveInfinity;
                bool last = ft >= hi;
                float ct = last ? hi : ft, cy = last ? BallY(d, hi) : d.Ball[f].Y;
                if (ct > pt)
                {
                    if (py >= targetY && cy < targetY)
                    {
                        t = pt + (ct - pt) * (py - targetY) / (py - cy);
                        reason = null;
                        return true;
                    }
                    if (cy >= targetY) above = true;
                    pt = ct;
                    py = cy;
                }
                if (last) break;
                f++;
            }
            reason = clipped ? ReasonBoundary : above ? ReasonBallHigh : ReasonBallLow;
            return false;
        }

        static float BallY(HighlightData d, float t)
        {
            d.FrameAt(t, out var f, out var a);
            return d.Ball[f].Y + (d.Ball[Math.Min(f + 1, d.Frames - 1)].Y - d.Ball[f].Y) * a;
        }

        /// <summary>
        /// 예상 머리–공 거리 여유(m): 054 머리 접촉 허용치 0.05 m + 계획(시뮬 방향) ↔ 화면(평활 방향) 차 여유 0.10 m.
        /// 이보다 가까우면 리프트를 하한으로 낮춘다(083 플랜 접근 1).
        /// </summary>
        public const float HeadClearance = 0.15f;

        /// <summary>점프 액션을 계획한다. 넣을 수 없으면 false와 사유.</summary>
        /// <param name="simPos">정점 시각 시뮬 루트 위치.</param><param name="yaw">정점 시각 정면 각(라디안, ActionClipSelector와 같은 규약).</param>
        /// <param name="ball">정점 시각 시뮬 공.</param>
        public static bool Plan(ClipLibrary lib, ActionTimeline tl, in AerialJump j, float scale, float sceneStart, float sceneEnd,
            Vec3 simPos, float yaw, Vec3 ball, out ActionChoice choice, out string reason)
        {
            choice = default;
            int clip = lib.IndexOf(HeaderMotion.JumpClip), stand = lib.IndexOf(HeaderMotion.StandClip);
            if (clip < 0 || stand < 0) { reason = ReasonNoClip; return false; }
            if (j.ApexAt <= sceneStart || j.ApexAt > sceneEnd) { reason = ReasonBoundary; return false; }
            var c = lib.Get(clip);
            float apex = j.ApexAt;
            float start = Math.Max(sceneStart, apex - c.ContactTime);
            float rate = apex > start ? c.ContactTime / (apex - start) : 1f;
            float end = start + c.Length / rate;
            // FR-02: 같은 선수 다른 동작이 정점 전에 시작하고 점프 구간과 겹치면 점프하지 않는다
            for (int i = 0; i < tl.Count; i++)
            {
                var a = tl.Get(i);
                if (a.Start <= apex && a.End > start) { reason = ReasonOverlap; return false; }
            }
            // 054 FR-82: 정점 뒤 다음 동작이 있으면 그 시작(+ 빠지는 시간)에서 끝낸다
            for (int i = 0; i < tl.Count; i++)
            {
                var a = tl.Get(i);
                if (a.Start > apex) end = Math.Max(apex + 1e-3f, Math.Min(end, a.Start + AnimPlan.ActionFadeOut));
            }
            float standY = lib.Get(stand).ContactPoint.Y, jumpY = c.ContactPoint.Y;
            // 기록 점프 높이만큼 뛴 머리 높이 = 서서 이마 높이 + 점프 높이 → 082 점프 리프트 범위로
            float headY = standY * scale + j.JumpHeight;
            float lift = HeaderMotion.JumpLift(headY, jumpY, standY, scale);
            float low = HeaderMotion.JumpLift(float.NegativeInfinity, jumpY, standY, scale);
            if (HeadDistance(c.ContactPoint, scale, simPos, yaw, lift, ball) <= HeadClearance) lift = low;
            choice = new ActionChoice(clip, start, rate, apex, end, 1f, Vec3.Zero, false, lift, 0f);
            reason = null;
            return true;
        }

        /// <summary>예상 머리(클립 접촉점) – 공 거리: 시뮬 루트 + 정면 각으로 돌린 접촉점 × 배율 + 리프트.</summary>
        public static float HeadDistance(Vec3 clipContact, float scale, Vec3 simPos, float yaw, float lift, Vec3 ball)
        {
            float sy = (float)Math.Sin(yaw), cy = (float)Math.Cos(yaw);
            var cp = clipContact * scale;
            var head = new Vec3(simPos.X + cp.X * cy + cp.Z * sy, cp.Y + lift, simPos.Z - cp.X * sy + cp.Z * cy);
            return (head - ball).Length();
        }
    }
}
