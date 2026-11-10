using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Presentation.Highlight.Broadcast;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Verification.Samples.Highlight;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/042 AC-14 벤치 (IL2CPP 판정). 카메라 갱신(트랙 샘플 + Camera 적용) p95·GC와 리플레이 시작당 가림 검사 시간을 잰다.
    /// </summary>
    public static class BroadcastCameraBench
    {
        /// <summary>실제 경기장의 금지 상자 수(44 생성기 최대 27개)보다 여유 있게 잡는다.</summary>
        const int NoGoBoxes = 32;
        const int PlacementRepeats = 2000;

        public static Dictionary<string, double> Run(int warmup, int frames)
        {
            var scenes = BroadcastScenes.Scenes();
            var built = new List<BuiltScene>(scenes.Length);
            for (int i = 0; i < scenes.Length; i++)
            {
                var clip = BroadcastScenes.Extract((long)Math.Floor(scenes[i].Start * 10d), (long)Math.Ceiling(scenes[i].End * 10d) + 1L);
                built.Add(new BuiltScene(scenes[i], ClipConverter.ToHighlight(clip, PitchSpec.Default), null, null));
            }

            var rig = Rig();
            var go = new GameObject("BroadcastBenchCamera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            var broadcast = new BroadcastCamera(camera, BroadcastScenes.Brief, () => rig, new CameraTuning(), new CameraSettings());

            var build = Stopwatch.StartNew();
            for (int i = 0; i < built.Count; i++) broadcast.OnSceneBuilt(built[i]);
            build.Stop();

            var ms = new double[frames];
            long gc = 0;
            int scene = 0;
            double time = scenes[0].Start;
            broadcast.OnSceneBegin(scenes[0]);
            for (int i = -warmup; i < frames; i++)
            {
                time += 1.5d / 60d;
                if (time >= scenes[scene].End)
                {
                    scene = (scene + 1) % scenes.Length;
                    time = scenes[scene].Start;
                    broadcast.OnSceneBegin(scenes[scene]);
                }

                long g0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                broadcast.OnPlayback(time);
                sw.Stop();
                long g = GC.GetAllocatedBytesForCurrentThread() - g0;
                if (i < 0) continue;
                ms[i] = sw.Elapsed.TotalMilliseconds;
                gc += g;
            }
            UnityEngine.Object.Destroy(go);

            double placeMax = PlacementMax(rig);
            Array.Sort(ms);
            double P(double q) => ms[Math.Min(frames - 1, Math.Max(0, (int)Math.Ceiling(q * frames) - 1))];
            return new Dictionary<string, double>
            {
                { "camera_p50_ms", P(0.5) },
                { "camera_p95_ms", P(0.95) },
                { "camera_max_ms", ms[frames - 1] },
                { "camera_gc_bytes", gc },
                { "occlusion_replay_start_ms", placeMax },
                { "camera_track_build_ms", build.Elapsed.TotalMilliseconds },
            };
        }

        static CameraRigData Rig()
        {
            var rig = CameraRigData.Default();
            for (int i = 0; i < NoGoBoxes; i++)
            {
                float z = 60f + i;
                rig.NoGo.Add(new CameraBox(new Vec3(-60f, 0f, z), new Vec3(60f, 20f, z + 0.5f)));
            }
            return rig;
        }

        /// <summary>리플레이 시작 1회 = 앵글 3개 배치. 후보를 모두 검사하는 사건 지점으로 잰다.</summary>
        static double PlacementMax(CameraRigData rig)
        {
            var kinds = new[] { ShotKind.ReplayHigh, ShotKind.ReplayShooter, ShotKind.ReplayReverse };
            double max = 0d;
            float sink = 0f;
            for (int r = 0; r < PlacementRepeats; r++)
            {
                var ev = new Vec3(-50f + r % 100, 0f, -30f + r % 60);
                var sw = Stopwatch.StartNew();
                for (int k = 0; k < kinds.Length; k++) sink += ReplayAnglePlacer.Place(kinds[k], ev, rig).Position.X;
                sw.Stop();
                if (sw.Elapsed.TotalMilliseconds > max) max = sw.Elapsed.TotalMilliseconds;
            }
            if (float.IsNaN(sink)) throw new InvalidOperationException("배치 결과가 NaN");
            return max;
        }
    }
}
