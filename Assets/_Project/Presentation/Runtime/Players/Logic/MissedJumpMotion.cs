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
