using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 062 FR-21·22 키트 2차 움직임 (AC-03·10·14). 오프셋은 재생 시각의 순수 함수(커널 합성, 적분 상태 없음)라
    /// fps·배속·평가 순서와 무관하게 같은 시각이면 같은 값이어야 한다.
    /// </summary>
    public class KitSecondaryMotionTests
    {
        const float Dt = 1f / 30f;

        static ClipLibrary Lib() => ClipLibrary.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(PlayerRigTests.Gen + "/player.clips.json").text);
        static KitSecondaryMotion Motion() => KitSecondaryMotion.FromLayout(AssetDatabase.LoadAssetAtPath<TextAsset>(PlayerRigTests.Gen + "/player.layout.json").text);

        static float Peak(Span<Vec3> o)
        {
            float m = 0;
            for (int i = 0; i < o.Length; i++) m = Math.Max(m, o[i].Length());
            return m;
        }

        /// <summary>앞 구간 가속도 펄스 후 0 — 몸이 멈춘 상황.</summary>
        static KitAccelTrack PulseThenStill(float start, float pulse, Vec3 a, float total)
        {
            int n = (int)Math.Round(total / Dt) + 1, np = (int)Math.Round(pulse / Dt);
            var s = new Vec3[n];
            for (int i = 0; i < np; i++) s[i] = a * (float)Math.Sin(Math.PI * i / np);
            return new KitAccelTrack(start, s);
        }

        [Test]
        public void Amplitude_0_5to3cm_Settles0_5s()
        {
            var lib = Lib();
            var m = Motion();
            Assert.AreEqual(KitSecondaryMotion.Bones, 6);
            Span<Vec3> o = stackalloc Vec3[KitSecondaryMotion.Bones];
            // 질주·방향 전환·출발·점프·세리머니·급정지 (FR-21). 제자리 회전(turn_*)은 궤적 가속도 몫이라 클립 커브만으로는 작다
            foreach (var id in new[] { "sprint_f", "cut_l90", "start_f", "header_jump", "celebrate_knee", "quick_stop" })
            {
                int ci = lib.IndexOf(id);
                Assert.GreaterOrEqual(ci, 0, id);
                var clip = lib.Get(ci);
                Assert.IsNotNull(clip.PelvisAccel, id + " pelvisAccel");
                Assert.AreEqual(clip.Frames + 1, clip.PelvisAccel.Length, id + " 프레임 + 1개");
                var track = KitAccelTrack.FromClip(clip, 0f, clip.Loop ? 3 : 1);
                float peak = 0, end = clip.Length * (clip.Loop ? 3 : 1);
                for (float t = 0; t <= end; t += 1f / 120f)
                {
                    m.Evaluate(track, t, 0f, 0f, o);
                    float p = Peak(o);
                    Assert.LessOrEqual(p, KitSecondaryMotion.MaxOffset + 1e-5f, $"{id} t={t:F3} 상한 3 cm");
                    for (int b = 0; b < o.Length; b++) Assert.IsFalse(float.IsNaN(o[b].X + o[b].Y + o[b].Z), id);
                    peak = Math.Max(peak, p);
                }
                Assert.GreaterOrEqual(peak, KitSecondaryMotion.MinPeak, $"{id} 진폭 ≥ 0.5 cm (실제 {peak * 100:F2} cm)");
            }

            // 멈춘 뒤 0.5 s 안에 가라앉는다 (커널 창 = 0.5 s)
            Assert.LessOrEqual(KitSecondaryMotion.Window, 0.5f + 1e-6f);
            var still = PulseThenStill(0f, 0.3f, new Vec3(0f, -6f, 8f), 2f);
            m.Evaluate(still, 0.3f, 0f, 0f, o);
            Assert.Greater(Peak(o), 1e-3f, "펄스 직후 움직임이 있다");
            for (float t = 0.3f + 0.5f; t <= 2f; t += 1f / 60f)
            {
                m.Evaluate(still, t, 0f, 0f, o);
                Assert.Less(Peak(o), 5e-4f, $"정지 0.5 s 뒤 t={t:F3} 가라앉음");
            }
        }

        [Test]
        public void SameTime_SameOffsets_FrameRateIndependent()
        {
            var lib = Lib();
            var m = Motion();
            var clip = lib.Get(lib.IndexOf("cut_l90"));
            var track = KitAccelTrack.FromClip(clip, 2f, 1);
            Span<Vec3> direct = stackalloc Vec3[KitSecondaryMotion.Bones];
            Span<Vec3> o = stackalloc Vec3[KitSecondaryMotion.Bones];
            float[] probes = { 2.1f, 2.37f, 2.5f, 2f + clip.Length * 0.5f, 2f + clip.Length - 0.01f };
            foreach (float tp in probes)
            {
                m.Evaluate(track, tp, 10f, 5f, direct);
                // 30 fps·60 fps·배터리 절약(30 fps)·역순으로 이전 시각을 평가한 뒤에도 같은 시각이면 비트 단위로 같다
                foreach (float step in new[] { 1f / 30f, 1f / 60f, 1f / 144f, -1f / 45f })
                {
                    float t = step > 0 ? 2f : tp + 0.6f;
                    for (int k = 0; k < 200 && (step > 0 ? t < tp : t > tp); k++, t += step) m.Evaluate(track, t, 10f, 5f, o);
                    m.Evaluate(track, tp, 10f, 5f, o);
                    for (int b = 0; b < o.Length; b++) Assert.AreEqual(direct[b], o[b], $"t={tp} step={step} bone={b}");
                }
            }
            // 같은 입력으로 만든 다른 인스턴스도 같다
            var m2 = Motion();
            m2.Evaluate(track, probes[1], 10f, 5f, o);
            m.Evaluate(track, probes[1], 10f, 5f, direct);
            for (int b = 0; b < o.Length; b++) Assert.AreEqual(direct[b], o[b]);
        }

        [Test]
        public void TrackEdges_NoSpike()
        {
            var m = Motion();
            Span<Vec3> o = stackalloc Vec3[KitSecondaryMotion.Bones];
            Span<Vec3> prev = stackalloc Vec3[KitSecondaryMotion.Bones];
            // 첫 샘플부터 큰 가속도(트랙 시작이 급가속 한가운데) + 1.0 s에 컷 + 끝 뒤까지 평가
            int n = 61;
            var s = new Vec3[n];
            for (int i = 0; i < n; i++) s[i] = new Vec3(0f, 0f, i < 30 ? 9f : -9f);
            var track = new KitAccelTrack(5f, s, new[] { 6.0f });
            const float step = 1f / 60f;
            // 컷·시작 직후 0.5 s 동안은 창을 잘라 이전(또는 범위 밖) 가속도를 쓰지 않는다 → 시작·컷 순간 오프셋 ≈ 0
            m.Evaluate(track, 5f, 0f, 0f, o);
            Assert.Less(Peak(o), 1e-3f, "트랙 시작 순간 튐 없음");
            m.Evaluate(track, 6f, 0f, 0f, o);
            Assert.Less(Peak(o), 1e-3f, "컷 순간 튐 없음");
            // 시작 전·끝 뒤: 가장자리 유지, 유한·상한 안
            foreach (float t in new[] { 3f, 4.99f, 7.01f, 9f })
            {
                m.Evaluate(track, t, 0f, 0f, o);
                Assert.LessOrEqual(Peak(o), KitSecondaryMotion.MaxOffset + 1e-5f, $"t={t}");
            }
            // 컷 구간 안쪽에서는 프레임 사이 변화가 작다 (컷 자체는 카메라가 바뀌는 순간이라 제외)
            float maxStep = 0;
            for (float t = 4.5f; t <= 8.5f; t += step)
            {
                m.Evaluate(track, t, 0f, 0f, o);
                bool crossesCut = t - step < 6f && t >= 6f;
                if (t > 4.5f && !crossesCut)
                    for (int b = 0; b < o.Length; b++) maxStep = Math.Max(maxStep, (o[b] - prev[b]).Length());
                o.CopyTo(prev);
            }
            Assert.Less(maxStep, 0.004f, $"1/60 s 사이 최대 변화 {maxStep * 1000:F2} mm < 4 mm");
        }

        [Test]
        public void Speeds_SamePose_1x_1_5x_3x()
        {
            var lib = Lib();
            var m = Motion();
            var clip = lib.Get(lib.IndexOf("sprint_f"));
            var track = KitAccelTrack.FromClip(clip, 0f, 4);
            Span<Vec3> a = stackalloc Vec3[KitSecondaryMotion.Bones];
            Span<Vec3> b = stackalloc Vec3[KitSecondaryMotion.Bones];
            // 화면 60 fps에서 배속별로 재생 시각을 진행한다. 같은 재생 시각에 도달하면 포즈가 같다.
            float[] speeds = { 0.5f, 1f, 1.5f, 3f };
            float[] targets = { 0.4f, 0.85f, 1.3f };
            foreach (float target in targets)
            {
                m.Evaluate(track, target, 0f, 0f, a);
                foreach (float sp in speeds)
                {
                    float t = 0;
                    while (t + sp / 60f < target) { t += sp / 60f; m.Evaluate(track, t, 0f, 0f, b); }
                    m.Evaluate(track, target, 0f, 0f, b);
                    for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i], $"target={target} speed={sp} bone={i}");
                }
            }
        }

        [Test]
        public void Rig_BindsSixKitBones_OffsetFollowsParent()
        {
            // T17 리그 바인딩: glb에서 보조 뼈 6개를 찾고, 오프셋이 레스트 위치에 더해지며 0이면 레스트로 돌아온다
            var rig = PlayerRigTests.NewRig();
            try
            {
                var m = Motion();
                rig.BindKitBones(m);
                Assert.AreEqual(KitSecondaryMotion.Bones, rig.KitBoneCount, "Kit_* 뼈");
                var hem = rig.Bone("Kit_HemF") ?? rig.Bone("Kit_HemF".Replace('.', '_'));
                Vector3 p0 = rig.transform.InverseTransformPoint(hem.position);
                Span<Vec3> o = stackalloc Vec3[KitSecondaryMotion.Bones];
                o[0] = new Vec3(0f, 0.02f, 0.01f);
                rig.ApplyKitBones(o);
                Vector3 d = rig.transform.InverseTransformPoint(hem.position) - p0;
                Assert.Less((d - new Vector3(0f, 0.02f, 0.01f)).magnitude, 1e-4f, $"레스트 틀 오프셋 그대로 {d}");
                o.Clear();
                rig.ApplyKitBones(o);
                Assert.Less((rig.transform.InverseTransformPoint(hem.position) - p0).magnitude, 1e-5f);
                rig.HipFlex(out float fl, out float fr);
                Assert.Less(Math.Abs(fl), 15f, "레스트 고관절 굴곡 ≈ 0");
                Assert.Less(Math.Abs(fr), 15f);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig.gameObject); }
        }
    }
}
