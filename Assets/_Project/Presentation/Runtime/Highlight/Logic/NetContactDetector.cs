using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>골망 안쪽 면 (specs/059 FR-24). 값 순서 = 같은 시각 접촉 정렬 순서.</summary>
    public enum NetFace : byte
    {
        Back = 0,
        Top = 1,
        /// <summary>폭 − 쪽 옆그물 (하이라이트 Z &lt; 0).</summary>
        SideNegative = 2,
        /// <summary>폭 + 쪽 옆그물 (하이라이트 Z &gt; 0).</summary>
        SidePositive = 3,
    }

    /// <summary>그물 접촉 1회 (specs/059 FR-34): 시뮬 시각(재생 초), 골 쪽(−1 X &lt; 0 · +1), 면, 면 위 접촉점(하이라이트 좌표), 면 수직 속도(m/s, 양수).</summary>
    public readonly struct NetContact
    {
        public readonly double Time;
        public readonly sbyte Side;
        public readonly NetFace Face;
        public readonly Vec3 Point;
        public readonly float NormalSpeed;

        public NetContact(double time, sbyte side, NetFace face, Vec3 point, float normalSpeed)
        {
            Time = time;
            Side = side;
            Face = face;
            Point = point;
            NormalSpeed = normalSpeed;
        }
    }

    /// <summary>접촉 판정 값 (`Data/tuning/goalnet.json` `net.contactMargin`·`net.contactMinSpeed`).</summary>
    public readonly struct NetContactSettings
    {
        public readonly float Margin, MinSpeed;

        public NetContactSettings(float margin, float minSpeed)
        {
            Margin = margin;
            MinSpeed = minSpeed;
        }

        /// <summary>goalnet.json 기본값과 같다 (튜닝 로더가 없을 때·테스트).</summary>
        public static NetContactSettings Default => new NetContactSettings(0.02f, 0.5f);
    }

    /// <summary>
    /// 클립 공 궤적(30 Hz)에서 골 안쪽 그물 면 접촉을 모두 찾는다 (specs/059 FR-24·34·51, 058 <c>NetContact.Find</c> 대체).
    /// 시뮬(059 FR-41)은 공을 그물 면 반지름 안쪽에서 막으므로, 공 중심이 면에서 반지름 + margin 안으로 들어오는
    /// "접근 → 닿음" 전이 프레임이 접촉이다. 면을 향한 수직 속도가 minSpeed 이하면(기대 정지·굴러 닿음) 접촉이 아니다.
    /// 골라인을 넘지 않은 공(중심이 골라인 앞)·반만 넘은 공은 접촉 0 (FR-51). 바깥 면 접촉은 012 때 (Q2 (a)).
    /// 결과는 시각, 같으면 면 순. 클립만의 함수라 재생 속도·fps와 무관하다 (FR-28).
    /// </summary>
    public static class NetContactDetector
    {
        /// <summary>058 첫 그물 접촉 큐 종류 (059: 접촉마다 1개).</summary>
        public const int CueKind = 10_211;

        const int Faces = 4;

        public static void Find(HighlightData d, in GoalBox box, in NetContactSettings s, List<NetContact> into)
        {
            into.Clear();
            if (d == null || d.Frames < 2) return;
            float dt = 1f / HighlightData.FrameRate;
            float reach = box.BallRadius + s.Margin;
            Span<float> dist0 = stackalloc float[Faces], dist1 = stackalloc float[Faces], distPrev = stackalloc float[Faces];
            for (int f = 1; f < d.Frames; f++)
            {
                var p1 = d.Ball[f];
                if (Math.Abs(p1.X) <= box.HalfLength) continue; // 중심이 골라인 앞 (FR-51)
                sbyte side = p1.X < 0f ? (sbyte)-1 : (sbyte)1;
                var p0 = d.Ball[f - 1];
                if (!Inside(p1, side, box)) continue;
                Distances(p1, side, box, dist1);
                Distances(p0, side, box, dist0);
                bool hasPrev = f >= 2;
                if (hasPrev) Distances(d.Ball[f - 2], side, box, distPrev);
                for (int k = 0; k < Faces; k++)
                {
                    if (dist1[k] > reach) continue;           // 아직 면에 안 닿음
                    if (dist0[k] <= reach) continue;          // 앞 프레임에 이미 닿아 있었음 (기대 정지·연속 접촉)
                    float vin = (dist0[k] - dist1[k]) / dt;   // 이 구간 평균 접근 속도 (면에서 멈춰 실제보다 작을 수 있음)
                    float vpre = hasPrev ? (distPrev[k] - dist0[k]) / dt : 0f; // 닿기 전 구간 접근 속도
                    float vn = Math.Max(vin, vpre);
                    if (vn <= s.MinSpeed) continue;
                    // 접촉 시각: 면까지(중심 기준 반지름) 남은 거리 / 접근 속도 — 프레임 사이 보간
                    float gap = dist0[k] - box.BallRadius;
                    float frac = gap <= 0f ? 0f : Math.Min(1f, gap / (vn * dt));
                    double t = d.StartTime + (f - 1 + frac) / (double)HighlightData.FrameRate;
                    into.Add(new NetContact(t, side, (NetFace)k, OnFace(p1, side, (NetFace)k, box), vn));
                }
            }
            into.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : ((byte)a.Face).CompareTo((byte)b.Face));
        }

        /// <summary>t 이후(포함) 첫 접촉 시각. 없으면 NaN.</summary>
        public static double FirstAfter(List<NetContact> contacts, double t)
        {
            if (contacts == null || double.IsNaN(t)) return double.NaN;
            for (int i = 0; i < contacts.Count; i++)
                if (contacts[i].Time >= t) return contacts[i].Time;
            return double.NaN;
        }

        /// <summary>
        /// 그물 상자 + 주머니(059 FR-41 ⑥) 안인가. 바깥 공(빗나간 공)은 바깥 면에 막혀 늘 면 바깥쪽(거리 음수)이라
        /// "접근 → 닿음" 전이가 생기지 않는다 — 안쪽 면 접촉 대상이 아니다.
        /// </summary>
        static bool Inside(Vec3 p, sbyte side, in GoalBox box)
        {
            float depth = side * p.X - box.HalfLength;
            return depth > 0f && depth < box.NetDepth + box.Pocket && Math.Abs(p.Z) < box.HalfMouth + box.Pocket && p.Y < box.Height + box.Pocket;
        }

        /// <summary>면별 공 중심–면 거리 (안쪽 기준, 양수 = 면 안쪽). 순서 = <see cref="NetFace"/>.</summary>
        static void Distances(Vec3 p, sbyte side, in GoalBox box, Span<float> into)
        {
            float depth = side * p.X - box.HalfLength;
            into[(int)NetFace.Back] = box.NetDepth - depth;
            into[(int)NetFace.Top] = box.Height - p.Y;
            into[(int)NetFace.SideNegative] = box.HalfMouth + p.Z;
            into[(int)NetFace.SidePositive] = box.HalfMouth - p.Z;
        }

        static Vec3 OnFace(Vec3 p, sbyte side, NetFace face, in GoalBox box)
        {
            switch (face)
            {
                case NetFace.Back: return new Vec3(side * (box.HalfLength + box.NetDepth), p.Y, p.Z);
                case NetFace.Top: return new Vec3(p.X, box.Height, p.Z);
                case NetFace.SideNegative: return new Vec3(p.X, p.Y, -box.HalfMouth);
                default: return new Vec3(p.X, p.Y, box.HalfMouth);
            }
        }
    }
}
