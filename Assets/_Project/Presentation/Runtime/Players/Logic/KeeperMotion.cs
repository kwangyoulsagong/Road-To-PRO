using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>재생 이벤트에서 읽은 GK 동작 정보 (specs/080 FR-03, 011 FR-91).</summary>
    public readonly struct KeeperEventInfo
    {
        public readonly bool Valid;
        public readonly KeeperMotionKind Kind;
        /// <summary>높이 구간 (011 FR-17). <see cref="HasBand"/>가 거짓이면 의미 없음.</summary>
        public readonly DiveHeight Band;
        public readonly bool HasBand;
        /// <summary>DiveStart 방향 (−1 왼쪽 · 0 정면 · +1 오른쪽, 011 SaveJudgement.Direction). 다른 이벤트는 0.</summary>
        public readonly int Direction;
        /// <summary>실점 다이빙이 손끝에 닿고 들어갔다 (011 손끝 플래그 → 변환기가 DiveStart B에 옮김, 080 FR-05).</summary>
        public readonly bool Fingertip;

        public KeeperEventInfo(KeeperMotionKind kind, DiveHeight band, bool hasBand, int direction, bool fingertip = false)
        {
            Fingertip = fingertip;
            Valid = true;
            Kind = kind;
            Band = band;
            HasBand = hasBand;
            Direction = direction;
        }
    }

    /// <summary>
    /// GK 동작 재생 로직 (specs/080). 순수 로직 — 같은 이벤트 → 같은 결과.
    /// 이벤트 해석: <see cref="AnimEvent.Flags"/> 하위 15비트 = SimEvent.A, 상위 16비트 = SimEvent.B (<see cref="PackFlags"/>).
    /// 접촉(Catch·Parry·Punch): A = <see cref="KeeperMotionKind"/>, B = 높이 구간 | 비트(ParryDanger 등).
    /// DiveStart(실점 다이빙): A = 방향 × 10 + 높이 — 방향 −1이면 음수라 15비트 부호 확장으로 되돌린다.
    /// Smother·ClaimFail: A = 동작 종류, 높이 없음.
    /// </summary>
    public static class KeeperMotion
    {
        const int BandMask = 0x3;

        /// <summary>시뮬 이벤트 → 재생 이벤트 Flags (041 ClipConverter와 같은 포장, 080 T02에서 한 곳으로).</summary>
        public static int PackFlags(in SimEvent ev) => (ev.A & 0x7FFF) | AnimEvent.FromSim | (ev.B << 16);

        /// <summary>Flags 하위 15비트를 부호 있는 A로 (−16384 ~ 16383).</summary>
        public static int SignedA(int flags)
        {
            int a = flags & 0x7FFF;
            return a >= 0x4000 ? a - 0x8000 : a;
        }

        public static int B(int flags) => flags >> 16;

        public static KeeperEventInfo Decode(in AnimEvent ev)
        {
            if ((ev.Flags & AnimEvent.FromSim) == 0) return default;
            int a = SignedA(ev.Flags);
            int b = B(ev.Flags);
            switch (ev.Kind)
            {
                case SimEventKind.Catch:
                case SimEventKind.Parry:
                case SimEventKind.Punch:
                    if (!IsKind(a)) return default;
                    return new KeeperEventInfo((KeeperMotionKind)a, (DiveHeight)(b & BandMask), (b & BandMask) <= (int)DiveHeight.High, 0);
                case SimEventKind.DiveStart:
                {
                    int h = ((a % 10) + 10) % 10;
                    int dir = (a - h) / 10;
                    if (h > (int)DiveHeight.High || dir < -1 || dir > 1) return default;
                    return new KeeperEventInfo(KeeperMotionKind.Dive, (DiveHeight)h, true, dir, (b & EventCodes.Fingertip) != 0);
                }
                case SimEventKind.Smother:
                case SimEventKind.ClaimFail:
                    if (!IsKind(a)) return default;
                    return new KeeperEventInfo((KeeperMotionKind)a, DiveHeight.Low, false, 0);
                default:
                    return default;
            }
        }

        /// <summary>
        /// GK 시뮬 이벤트의 후보 (080 FR-03·06): 동작 종류·높이 구간 후보가 매핑 표에 있으면 그것, 없으면 fallback(이벤트 종류 후보).
        /// 손 배급(Pass, Part = Hand)은 같은 시각·같은 선수의 Distribution(308) 이벤트 A(굴리기·던지기)로 정한다.
        /// </summary>
        public static IReadOnlyList<int> Candidates(AnimationMap map, IReadOnlyList<AnimEvent> events, in AnimEvent ev, IReadOnlyList<int> fallback)
        {
            if ((ev.Flags & AnimEvent.FromSim) == 0) return fallback;
            if (ev.Kind == SimEventKind.Pass && ev.Part == BodyPart.Hand)
            {
                var k = HandPassKind(events, ev);
                if (k == KeeperMotionKind.None) return fallback;
                var hc = map.KeeperCandidates(k, DiveHeight.Low);
                return hc.Count > 0 ? hc : fallback;
            }
            if (ev.Part == BodyPart.Hand && IsHandReceive(ev.Kind))
            {
                // GK가 손으로 받기(느슨한 공·가로채기·크로스 클레임): 공 높이로 땅볼 · 가슴 · 머리 위/클레임 (080 T13 — 일반 받기 후보엔 손 클립이 없다)
                var kind = HasSmotherAt(events, ev) && ev.BallPos.Y < BandLowMax ? KeeperMotionKind.Smother // 같은 순간 땅볼 스마더(073)면 그 동작으로 (이벤트 하나로 합침)
                    : ev.BallPos.Y < BandLowMax ? KeeperMotionKind.CatchGround : ev.BallPos.Y < BandMidMax ? KeeperMotionKind.CatchChest : KeeperMotionKind.ClaimJump;
                var rc = map.KeeperCandidates(kind, DiveHeight.Mid);
                return rc.Count > 0 ? rc : fallback;
            }
            var info = Decode(ev);
            if (!info.Valid) return fallback;
            var c = map.KeeperCandidates(info.Kind, info.HasBand ? info.Band : DiveHeight.Mid);
            return c.Count > 0 ? c : fallback;
        }

        /// <summary>손 받기 높이 경계 (m): 생성기 DIVE_BALL_RANGE와 같은 값 (낮음 &lt; 0.6 ≤ 중간 &lt; 1.5 ≤ 높음).</summary>
        public const float BandLowMax = 0.6f, BandMidMax = 1.5f;

        /// <summary>
        /// 073 스마더는 같은 순간(±1/60 s) 같은 GK의 손 받기 이벤트와 짝으로 나온다 — 받기 이벤트가 스마더 동작을 맡고 스마더 이벤트는 건너뛴다
        /// (두 동작이 같은 접촉 시각이면 두 번째가 시간 0으로 압축돼 재생 배율이 1만이 된다, 080 T13a).
        /// </summary>
        public static bool SupersededSmother(IReadOnlyList<AnimEvent> events, in AnimEvent ev)
        {
            if (ev.Kind != SimEventKind.Smother) return false;
            for (int i = 0; i < events.Count; i++)
            {
                var o = events[i];
                if (o.Entity == ev.Entity && o.Part == BodyPart.Hand && IsHandReceive(o.Kind) && Math.Abs(o.Time - ev.Time) <= 1f / 60f) return true;
            }
            return false;
        }

        static bool HasSmotherAt(IReadOnlyList<AnimEvent> events, in AnimEvent ev)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var o = events[i];
                if (o.Kind == SimEventKind.Smother && o.Entity == ev.Entity && Math.Abs(o.Time - ev.Time) <= 1f / 60f) return true;
            }
            return false;
        }

        static bool IsHandReceive(SimEventKind k) =>
            k == SimEventKind.BallRecovered || k == SimEventKind.Interception || k == SimEventKind.PassReceived;

        /// <summary>같은 시각(±1/60 s)·같은 선수의 Distribution 이벤트 → 굴리기·던지기. 없거나 발 방식이면 None.</summary>
        public static KeeperMotionKind HandPassKind(IReadOnlyList<AnimEvent> events, in AnimEvent pass)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var d = events[i];
                if (d.Kind != SimEventKind.Distribution || d.Entity != pass.Entity || Math.Abs(d.Time - pass.Time) > 1f / 60f) continue;
                if ((d.Flags & AnimEvent.FromSim) == 0) continue;
                switch ((DistributionKind)SignedA(d.Flags))
                {
                    case DistributionKind.Roll: return KeeperMotionKind.DistributeRoll;
                    case DistributionKind.Throw: return KeeperMotionKind.DistributeOverarm;
                    default: return KeeperMotionKind.None;
                }
            }
            return KeeperMotionKind.None;
        }

        // ---------------------------------------------------------------- 손 IK 이동량 (080 FR-04·05)

        /// <summary>손 IK 보정 상한 (080 FR-04 [제안] 30 cm, 2026-11-20 승인).</summary>
        public const float ReachMax = 0.30f;
        /// <summary>섞기 시간: 접촉 전 0 → 1, 접촉 후 1 → 0 (080 FR-04 [제안] 0.2 s).</summary>
        public const float Blend = 0.2f;
        /// <summary>실점 다이빙 손–공 하한 (080 FR-05 [제안] 15 cm).</summary>
        public const float MissClear = 0.15f;
        /// <summary>손끝 실점 손–공 상한 (054 FR-31 GK 손 10 cm).</summary>
        public const float FingertipMax = 0.10f;
        /// <summary>어깨 → 접촉점(공 중심) 여유: 손목 → 손바닥 → 공 중심 (생성기 checks.HAND_BALL_SLACK와 같은 값).</summary>
        public const float HandBallSlack = 0.30f;

        /// <summary>
        /// 손 이동량: need = 클립 접촉점 → 공 중심(월드). 길이 ≤ ReachMax, 옮긴 접촉점이 가까운 어깨에서 (팔 길이 + 여유) × 키 배율 안.
        /// 클립에 IK 태그가 없으면(ArmLength = 0) 0.
        /// </summary>
        public static Vec3 HandReach(Vec3 need, Vec3 contact, Vec3 shoulderL, Vec3 shoulderR, float armLength, float scale)
        {
            if (armLength <= 0f) return Vec3.Zero;
            var r = Cap(need, ReachMax);
            var sh = (contact - shoulderL).LengthSq() <= (contact - shoulderR).LengthSq() ? shoulderL : shoulderR;
            float radius = (armLength + HandBallSlack) * scale;
            var p = contact + r;
            var d = p - sh;
            float len = d.Length();
            if (len > radius && len > 1e-6f) r = sh + d * (radius / len) - contact;
            return Cap(r, ReachMax);
        }

        /// <summary>
        /// 실점 다이빙 (닿지 않음): 손끝 플래그면 손–공 ≤ FingertipMax까지 공 쪽으로, 아니면 ≥ MissClear가 되게 공 반대쪽으로만 옮긴다 (080 FR-05).
        /// </summary>
        public static Vec3 MissReach(Vec3 contact, Vec3 ball, bool fingertip)
        {
            var d = ball - contact;
            float len = d.Length();
            if (fingertip)
                return len <= FingertipMax ? Vec3.Zero : Cap(d * ((len - FingertipMax * 0.5f) / len), ReachMax);
            if (len >= MissClear) return Vec3.Zero;
            var away = len > 1e-4f ? d * (-1f / len) : new Vec3(0, 1f, 0);
            return away * Math.Min(ReachMax, MissClear + 0.02f - len);
        }

        static Vec3 Cap(Vec3 v, float max)
        {
            float l = v.Length();
            return l > max ? v * (max / l) : v;
        }

        /// <summary>손 IK를 쓰는 클립: GK 손 접촉 클립(IK 태그 있음). 오버암 던지기는 제외 — 시뮬 공이 발밑이라 기록만 (080 FR-06, D31).</summary>
        public static bool UsesReach(ClipInfo c) =>
            c.HasContact && c.ContactPart == BodyPart.Hand && c.ArmLength > 0f && c.Id.StartsWith("gk_", StringComparison.Ordinal) && c.Id != ThrowClip;

        public const string ThrowClip = "gk_throw_r";

        /// <summary>클립 접촉 규칙이 쓰는 손: 1 = 왼손, 2 = 오른손 (HANDS = 3).</summary>
        public static int HandsOf(in ContactRule rule)
        {
            int m = 0;
            if (rule.Bone == "LeftHand" || rule.Toward == "LeftHand") m |= 1;
            if (rule.Bone == "RightHand" || rule.Toward == "RightHand") m |= 2;
            return m;
        }

        // ---------------------------------------------------------------- 선방 방향 (080 T12a)

        /// <summary>선방 화면 방향 구간: 접촉 전·후 (s). 시뮬 GK 방향이 공 쪽이 아닐 때가 많아(T12 진단) 재생에서 공이 오는 쪽을 보게 한다.</summary>
        public const float FaceBefore = 0.5f, FaceAfter = 0.6f;
        /// <summary>공이 오는 방향을 잴 시점: 접촉 0.4 s 전 공 위치.</summary>
        public const float FaceLookBack = 0.4f;

        /// <summary>앞으로 잡는 동작이 접촉 지점 쪽을 보는 최소 거리 (m) — 더 가까우면 공이 오는 쪽.</summary>
        public const float FaceNear = 0.3f;

        static bool Sideways(KeeperMotionKind k) =>
            k == KeeperMotionKind.Dive || k == KeeperMotionKind.ParrySide || k == KeeperMotionKind.TipOverBar;

        /// <summary>GK 선방 동작 이벤트 (잡기·쳐내기·펀칭·실점 다이빙·스마더·클레임 실패, 시뮬 이벤트).</summary>
        public static bool IsSave(in AnimEvent ev)
        {
            if ((ev.Flags & AnimEvent.FromSim) == 0) return false;
            if (ev.Part == BodyPart.Hand && IsHandReceive(ev.Kind)) return true; // T13: GK 손 받기(클레임·느슨한 공)
            switch (ev.Kind)
            {
                case SimEventKind.Catch:
                case SimEventKind.Parry:
                case SimEventKind.Punch:
                case SimEventKind.DiveStart:
                case SimEventKind.Smother:
                case SimEventKind.ClaimFail:
                    return Decode(ev).Valid;
                default:
                    return false;
            }
        }

        /// <summary>선방마다 (GK, 구간, 공이 오는 방향). 공이 GK에서 0.5 m 안이면(이미 몸 앞) 건너뛴다. 시각 순.</summary>
        public static List<(int e, float t0, float t1, float yaw)> FacingOverrides(HighlightData d)
        {
            var o = new List<(int, float, float, float)>();
            foreach (var ev in d.Events)
            {
                if (!IsSave(ev) || ev.Entity < 0 || ev.Entity >= HighlightData.Players) continue;
                var gk = d.Pos(ev.Entity, ev.Time);
                // 옆으로 뻗는 동작(다이빙·측면 쳐내기·넘기기)은 공이 오는 쪽, 앞으로 잡는 동작은 접촉 지점 쪽 (T12: 달려 나가 잡으면 공이 오는 방향 기준 뒤에 있다)
                var at = ev.BallPos - gk;
                var from = Sideways(Decode(ev).Kind) || at.X * at.X + at.Z * at.Z < FaceNear * FaceNear
                    ? d.BallAt(Math.Max(d.StartTime, ev.Time - FaceLookBack)) - gk : at;
                if (from.X * from.X + from.Z * from.Z < 0.25f) continue;
                o.Add((ev.Entity, ev.Time - FaceBefore, ev.Time + FaceAfter, (float)Math.Atan2(from.X, from.Z)));
            }
            o.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return o;
        }

        static bool IsKind(int a) => a > (int)KeeperMotionKind.None && a <= (int)KeeperMotionKind.DistributeDropKick;
    }

    /// <summary>
    /// 선수 한 명의 손 IK 키 (080 FR-04): 접촉 시각마다 이동량·손. 가중치는 접촉 ±Blend 평활(smoothstep) — 이웃 키와 겹치면 두 접촉 사이 절반까지로 줄인다.
    /// 포즈 = f(t) (이전 프레임 상태 없음).
    /// </summary>
    public sealed class HandReachTrack
    {
        readonly List<(float at, Vec3 reach, int hands)> _keys = new List<(float, Vec3, int)>();
        float[] _pre = Array.Empty<float>(), _post = Array.Empty<float>();

        public int Count => _keys.Count;
        public (float at, Vec3 reach, int hands) Get(int i) => _keys[i];

        public void Add(float at, Vec3 reach, int hands)
        {
            if (hands == 0 || reach.LengthSq() < 1e-8f) return;
            _keys.Add((at, reach, hands));
        }

        /// <summary>키를 시각 순으로 정렬하고 창을 정한다 (Build 끝에 한 번).</summary>
        public void Finish()
        {
            _keys.Sort((a, b) => a.at.CompareTo(b.at));
            _pre = new float[_keys.Count];
            _post = new float[_keys.Count];
            for (int i = 0; i < _keys.Count; i++)
            {
                float before = i > 0 ? (_keys[i].at - _keys[i - 1].at) * 0.5f : KeeperMotion.Blend;
                float after = i + 1 < _keys.Count ? (_keys[i + 1].at - _keys[i].at) * 0.5f : KeeperMotion.Blend;
                _pre[i] = Math.Min(KeeperMotion.Blend, before);
                _post[i] = Math.Min(KeeperMotion.Blend, after);
            }
        }

        /// <summary>시각 t의 손 이동량(가중치 곱한 값)·손. 창 밖이면 0.</summary>
        public void Sample(float t, out Vec3 reach, out int hands, out float weight)
        {
            reach = Vec3.Zero;
            hands = 0;
            weight = 0f;
            for (int i = 0; i < _keys.Count; i++)
            {
                var k = _keys[i];
                float w;
                if (t <= k.at) w = _pre[i] > 1e-6f ? Smooth(1f - (k.at - t) / _pre[i]) : (t == k.at ? 1f : 0f);
                else w = _post[i] > 1e-6f ? Smooth(1f - (t - k.at) / _post[i]) : 0f;
                if (w <= 0f) continue;
                reach = k.reach * w;
                hands = k.hands;
                weight = w;
                return; // 창이 겹치지 않으므로 하나뿐
            }
        }

        static float Smooth(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }

        public void HashInto(ref ulong h)
        {
            foreach (var k in _keys)
            {
                h = (h ^ (ulong)BitConverter.SingleToInt32Bits(k.at)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.SingleToInt32Bits(k.reach.X)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.SingleToInt32Bits(k.reach.Y)) * 1099511628211UL;
                h = (h ^ (ulong)BitConverter.SingleToInt32Bits(k.reach.Z)) * 1099511628211UL;
                h = (h ^ (ulong)k.hands) * 1099511628211UL;
            }
        }
    }
}
