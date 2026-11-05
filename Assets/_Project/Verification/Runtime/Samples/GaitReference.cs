using System;
using System.Collections.Generic;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// 연구 기준 스틱 피겨 (specs/055 FR-71). 생성기 player/clips/reference.py가 낸 player.gait-reference.json — 기준 파일 범위 중앙값과
    /// 표준 파형으로 만든 보법별 8위상 관절각. 필름스트립 E2E가 모델 옆에 선으로 그린다.
    /// </summary>
    public sealed class GaitReference
    {
        public readonly struct SidePose
        {
            public readonly float HipL, KneeL, HipR, KneeR, Trunk, ShoulderL, ShoulderR, ElbowL, ElbowR, PelvisY;

            public SidePose(float hipL, float kneeL, float hipR, float kneeR, float trunk, float shL, float shR, float elL, float elR, float py)
            {
                HipL = hipL; KneeL = kneeL; HipR = hipR; KneeR = kneeR; Trunk = trunk;
                ShoulderL = shL; ShoulderR = shR; ElbowL = elL; ElbowR = elR; PelvisY = py;
            }
        }

        public readonly struct FrontPose
        {
            public readonly float Obliquity, Abduction, FootWidth;

            public FrontPose(float o, float a, float w)
            {
                Obliquity = o;
                Abduction = a;
                FootWidth = w;
            }
        }

        public sealed class Gait
        {
            public SidePose[] Side;
            public FrontPose[] Front;
        }

        public sealed class Seg
        {
            public float Thigh, Shin, Trunk, Neck, Head, UpperArm, ForeArm, Foot, PelvisHeight;
        }

        readonly Dictionary<string, Gait> _gaits = new Dictionary<string, Gait>(StringComparer.Ordinal);
        public Seg Segments { get; private set; }

        /// <summary>보법 이름(walk·jog·run·sprint) → 기준. 없으면 null (출발·감속·드리블 런은 기준 없음).</summary>
        public Gait Get(string gait) => gait != null && _gaits.TryGetValue(gait, out var g) ? g : null;

        static float F(JsonObject o, string k) => o.TryGet(k, out var v) ? (float)(double)v : 0f;

        public static GaitReference Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            var r = new GaitReference();
            root.TryGet("segments", out var so);
            var s = (JsonObject)so;
            r.Segments = new Seg
            {
                Thigh = F(s, "thigh"), Shin = F(s, "shin"), Trunk = F(s, "trunk"), Neck = F(s, "neck"), Head = F(s, "head"),
                UpperArm = F(s, "upperArm"), ForeArm = F(s, "foreArm"), Foot = F(s, "foot"), PelvisHeight = F(s, "pelvisHeight"),
            };
            root.TryGet("gaits", out var go);
            foreach (var kv in ((JsonObject)go).Members)
            {
                var g = (JsonObject)kv.Value;
                g.TryGet("side", out var sa);
                g.TryGet("front", out var fa);
                var side = (List<object>)sa;
                var front = (List<object>)fa;
                var gait = new Gait { Side = new SidePose[side.Count], Front = new FrontPose[front.Count] };
                for (int i = 0; i < side.Count; i++)
                {
                    var p = (JsonObject)side[i];
                    gait.Side[i] = new SidePose(F(p, "hipL"), F(p, "kneeL"), F(p, "hipR"), F(p, "kneeR"), F(p, "trunk"), F(p, "shoulderL"),
                        F(p, "shoulderR"), F(p, "elbowL"), F(p, "elbowR"), F(p, "pelvisY"));
                }
                for (int i = 0; i < front.Count; i++)
                {
                    var p = (JsonObject)front[i];
                    gait.Front[i] = new FrontPose(F(p, "obliquity"), F(p, "abduction"), F(p, "footWidth"));
                }
                r._gaits[kv.Key] = gait;
            }
            return r;
        }
    }
}
