using System;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Presentation.Stadium.Net
{
    /// <summary>
    /// 그물 정지 모양 인자 (specs/059 FR-02·03·05·07). 골대 로컬 좌표: X = 골라인 뒤 깊이(+), Y = 높이, Z = 폭(중심 0).
    /// 044 생성기(`Art/Blender/scripts/stadium/goals.py` `net_point`)와 같은 식 — 그물 폭 = 골문 반폭 + 포스트 반지름(포스트 중심선),
    /// 그물 높이 = 크로스바 중심 + 5 cm, 깊이 = `ball.net.depth`, 칸 수 = ceil(면 길이 / `net.gridCell`).
    /// </summary>
    public sealed class NetShapeParams
    {
        public float HalfMouth = 3.66f, Height = 2.44f, PostRadius = 0.06f, Depth = 2f, TopExtra = 0.05f;
        public float SagTop = 0.08f, BulgeBack = 0.1f, PinBand = 0.3f, GridCell = 0.2f;

        public float HalfWidth => HalfMouth + PostRadius;
        public float Top => Height + PostRadius + TopExtra;

        /// <summary>면의 (가로, 세로) 길이 — 뒤: 폭×높이, 위: 폭×깊이, 옆: 깊이×높이.</summary>
        public void FaceSize(NetFace f, out float lu, out float lv)
        {
            switch (f)
            {
                case NetFace.Back: lu = 2f * HalfWidth; lv = Top; break;
                case NetFace.Top: lu = 2f * HalfWidth; lv = Depth; break;
                default: lu = Depth; lv = Top; break;
            }
        }

        public int Cells(float length) => Math.Max(2, (int)Math.Ceiling(length / GridCell - 1e-4f));
    }

    /// <summary>
    /// 그물 정지 격자 (specs/059 플랜 "데이터"): 면 4개(<see cref="NetFace"/> 순서), 면마다 (U+1)·(V+1) 정점.
    /// 정점마다 위치·바깥 법선·면·고정 가중치(테두리 0 → 안쪽 1, FR-26). 정점 번호 = 면 시작 + j·(U+1) + i.
    /// 런타임은 이 격자로 동적 메시를 만든다(glb 그물은 정지 모양 대체용, 059 T15).
    /// </summary>
    public sealed class NetRestShape
    {
        public const int FaceCount = 4;

        public readonly Vec3[] Positions;
        public readonly Vec3[] Normals;
        public readonly float[] Pin;
        public readonly byte[] Face;
        /// <summary>면 위 m 좌표 (셰이더 육각 망, 생성기 UV와 같은 크기).</summary>
        public readonly Vec2[] Uvs;
        public readonly int[] Triangles;
        public readonly int[] FaceU = new int[FaceCount], FaceV = new int[FaceCount], FaceStart = new int[FaceCount];
        /// <summary>면별 격자 칸 크기 최대값(m) — 공 감싸기 범위에 쓴다.</summary>
        public readonly float[] Spacing = new float[FaceCount];
        public readonly NetShapeParams Params;

        public int Count => Positions.Length;

        NetRestShape(NetShapeParams p, int count, int quads)
        {
            Params = p;
            Positions = new Vec3[count];
            Normals = new Vec3[count];
            Pin = new float[count];
            Face = new byte[count];
            Uvs = new Vec2[count];
            Triangles = new int[quads * 6];
        }

        public static Vec3 FaceNormal(NetFace f)
        {
            switch (f)
            {
                case NetFace.Back: return new Vec3(1f, 0f, 0f);
                case NetFace.Top: return new Vec3(0f, 1f, 0f);
                case NetFace.SideNegative: return new Vec3(0f, 0f, -1f);
                default: return new Vec3(0f, 0f, 1f);
            }
        }

        /// <summary>면 매개변수 (u, v ∈ [0, 1]) → 정지 위치와 테두리까지 거리(m). 생성기 `net_point`와 같은 식.</summary>
        public static Vec3 Point(NetShapeParams p, NetFace face, float u, float v, out float edge)
        {
            float hw = p.HalfWidth, top = p.Top, D = p.Depth;
            switch (face)
            {
                case NetFace.Back:
                {
                    float w = -hw + 2f * hw * u, h = top * v;
                    // 아래쪽 바깥 부풂 (FR-03): v(1−v)² 최대 4/27 @ v = 1/3, 옆 테두리 0
                    float bulge = p.BulgeBack * (float)Math.Sin(Math.PI * u) * (27f / 4f) * v * (1f - v) * (1f - v);
                    edge = Math.Min(Math.Min(w + hw, hw - w), Math.Min(h, top - h));
                    return new Vec3(D + bulge, h, w);
                }
                case NetFace.Top:
                {
                    float w = -hw + 2f * hw * u, d = D * v;
                    float sag = p.SagTop * (float)(Math.Sin(Math.PI * u) * Math.Sin(Math.PI * v));
                    edge = Math.Min(Math.Min(w + hw, hw - w), Math.Min(d, D - d));
                    return new Vec3(d, top - sag, w);
                }
                default:
                {
                    float d = D * u, h = top * v;
                    edge = Math.Min(Math.Min(d, D - d), Math.Min(h, top - h));
                    return new Vec3(d, h, face == NetFace.SideNegative ? -hw : hw);
                }
            }
        }

        public static NetRestShape Build(NetShapeParams p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            int count = 0, quads = 0;
            var us = new int[FaceCount];
            var vs = new int[FaceCount];
            for (int f = 0; f < FaceCount; f++)
            {
                p.FaceSize((NetFace)f, out var lu, out var lv);
                us[f] = p.Cells(lu);
                vs[f] = p.Cells(lv);
                count += (us[f] + 1) * (vs[f] + 1);
                quads += us[f] * vs[f];
            }
            var s = new NetRestShape(p, count, quads);
            int k = 0;
            for (int f = 0; f < FaceCount; f++)
            {
                var face = (NetFace)f;
                int U = us[f], V = vs[f];
                s.FaceU[f] = U;
                s.FaceV[f] = V;
                s.FaceStart[f] = k;
                var n = FaceNormal(face);
                for (int j = 0; j <= V; j++)
                for (int i = 0; i <= U; i++)
                {
                    s.Positions[k] = Point(p, face, i / (float)U, j / (float)V, out var edge);
                    s.Normals[k] = n;
                    s.Face[k] = (byte)f;
                    s.Pin[k] = Smooth(edge / p.PinBand);
                    p.FaceSize(face, out var fu, out var fv);
                    s.Uvs[k] = new Vec2(fu * i / U, fv * j / V);
                    k++;
                }
                p.FaceSize(face, out var lu, out var lv);
                s.Spacing[f] = Math.Max(lu / U, lv / V);
            }
            int t = 0;
            for (int f = 0; f < FaceCount; f++)
            for (int j = 0; j < s.FaceV[f]; j++)
            for (int i = 0; i < s.FaceU[f]; i++)
            {
                int a = s.FaceStart[f] + j * (s.FaceU[f] + 1) + i, b = a + 1, c = a + s.FaceU[f] + 1, d = c + 1;
                s.Triangles[t++] = a; s.Triangles[t++] = b; s.Triangles[t++] = d;
                s.Triangles[t++] = a; s.Triangles[t++] = d; s.Triangles[t++] = c;
            }
            return s;
        }

        static float Smooth(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return x * x * (3f - 2f * x);
        }
    }
}
