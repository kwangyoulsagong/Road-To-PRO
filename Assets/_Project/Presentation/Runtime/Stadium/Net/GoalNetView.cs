using System.Collections.Generic;
using ILogger = RoadToPro.Core.Logging.ILogger;
using LogLevel = RoadToPro.Core.Logging.LogLevel;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadToPro.Presentation.Stadium.Net
{
    /// <summary>
    /// 골망 출렁임 표시 (specs/059 FR-20~29). glb의 정적 그물(Net_L·Net_R)을 끄고, 그물 시뮬(<see cref="NetCloth"/>) 격자를
    /// 동적 메시로 그린다(재질은 glb 그물 재질 = GoalNet 셰이더). 정점은 장면에 구운 데이터(<see cref="NetBake"/>)를 보간해 움직일 때만 쓴다(FR-27).
    /// 좌표: 하이라이트 좌표 = Unity 월드(경기장 루트가 원점·무회전, 044). 튜닝이 잘못되면 glb 정적 그물을 그대로 둔다(FR-56).
    /// 프레임 경로 할당 없음.
    /// </summary>
    public sealed class GoalNetView : MonoBehaviour
    {
        static readonly ProfilerMarker RippleMarker = new ProfilerMarker("GoalNet.Ripple");
        static readonly ProfilerMarker SleepMarker = new ProfilerMarker("GoalNet.Sleep");

        GoalNetDriver _driver;
        readonly Mesh[] _meshes = new Mesh[2];
        readonly Renderer[] _glb = new Renderer[2];
        readonly GameObject[] _objects = new GameObject[2];
        Vector3[] _buffer;
        float _halfLength;

        public bool IsActive => _driver != null;
        public GoalNetDriver Driver => _driver;

        /// <summary>그물 렌더러를 찾아 동적 메시를 만든다. 못 찾거나 튜닝이 잘못되면 false (정적 그물 유지).</summary>
        public bool Init(NetRippleTuning tuning, float halfLength, IEnumerable<GameObject> sceneRoots, ILogger log)
        {
            if (tuning == null || !tuning.Valid) return false;
            foreach (var root in sceneRoots)
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "Net_L" || r.name == "Net_R")
                    {
                        // 이름의 L/R은 044 생성기 기준이고 실제 쪽은 위치로 정한다
                        int side = r.bounds.center.x < 0f ? -1 : 1;
                        _glb[GoalNetDriver.IndexOf(side)] = r;
                    }
                }
            if (_glb[0] == null || _glb[1] == null)
            {
                log?.Log(LogLevel.Warning, "[059 골망] Net_L·Net_R 렌더러 없음 — 출렁임 끔");
                return false;
            }
            _halfLength = halfLength;
            _driver = new GoalNetDriver(new NetCloth(tuning));   // 정지 모양 늘어뜨리기 포함 (로딩 1회)
            var rest = _driver.Cloth.Shape;
            var restPos = _driver.RestRender;
            _buffer = new Vector3[rest.Count];
            var tris = rest.Triangles;
            var uv = new Vector2[rest.Count];
            var nrm = new Vector3[rest.Count];
            for (int i = 0; i < rest.Count; i++) uv[i] = new Vector2(rest.Uvs[i].X, rest.Uvs[i].Y);
            for (int s = 0; s < 2; s++)
            {
                int side = GoalNetDriver.SideOf(s);
                var mesh = new Mesh { name = "GoalNet_" + (side < 0 ? "L" : "R") };
                mesh.MarkDynamic();
                for (int i = 0; i < rest.Count; i++)
                {
                    var n = rest.Normals[i];
                    nrm[i] = new Vector3(side * n.X, n.Y, n.Z);
                }
                mesh.SetVertices(new Vector3[rest.Count]);
                mesh.SetNormals(nrm);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(tris, 0);
                Write(s, mesh);
                // 바운드는 최대 변위(+ 공 감싸기 2배)만큼 미리 넓혀 매 프레임 다시 계산하지 않는다
                var b = new Bounds();
                b.SetMinMax(ToUnity(restPos[0], side), ToUnity(restPos[0], side));
                for (int i = 1; i < rest.Count; i++) b.Encapsulate(ToUnity(restPos[i], side));
                b.Expand(2f * tuning.MaxDisp + 1f);
                mesh.bounds = b;
                _meshes[s] = mesh;

                var go = new GameObject(mesh.name);
                go.transform.SetParent(_glb[s].transform.parent, false);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _glb[s].sharedMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                _objects[s] = go;
                _glb[s].enabled = false;
            }
            return true;
        }

        /// <summary>장면·재생 시각으로 그물을 갱신한다 (HighlightPlayer가 매 프레임 부른다).</summary>
        public void Apply(BuiltScene scene, double time)
        {
            if (scene == null) return;
            Apply(scene.Scene.Id, time, scene.NetBakes);
        }

        /// <summary>장면 id·시각·구운 데이터로 갱신 (검증 화면의 합성 장면도 이 경로).</summary>
        public void Apply(int sceneId, double time, NetBake[] bakes)
        {
            if (_driver == null) return;
            // 성능 표: 깨어 있을 때(GoalNet.Ripple)·잠들 때(GoalNet.Sleep) CPU를 따로 잰다 — 앞 프레임 상태로 고른다
            var marker = _driver.AnyAwake ? RippleMarker : SleepMarker;
            using (marker.Auto())
            {
                int dirty = _driver.Update(sceneId, time, bakes);
                for (int s = 0; s < 2; s++)
                    if ((dirty & (1 << s)) != 0) Write(s, _meshes[s]);
            }
        }

        /// <summary>동적 그물 렌더러 (검증 화면: 켬/끔·균일 판 비교용).</summary>
        public Renderer RendererOf(int side) => _objects[GoalNetDriver.IndexOf(side)] != null ? _objects[GoalNetDriver.IndexOf(side)].GetComponent<Renderer>() : null;

        void Write(int s, Mesh mesh)
        {
            int side = GoalNetDriver.SideOf(s);
            var local = _driver.Local[s];
            for (int i = 0; i < local.Length; i++) _buffer[i] = ToUnity(local[i], side);
            mesh.SetVertices(_buffer);
            // T18: 부푼 자리 음영 — 정점을 쓴 프레임만 법선을 다시 계산 (정지 모양이면 원래 면 법선과 같다)
            mesh.RecalculateNormals();
        }

        Vector3 ToUnity(Core.Math.Vec3 local, int side)
        {
            var w = GoalLocal.ToWorld(local, side, _halfLength);
            return new Vector3(w.X, w.Y, w.Z);
        }

        void OnDestroy()
        {
            for (int s = 0; s < 2; s++)
            {
                if (_meshes[s] != null) Destroy(_meshes[s]);
                if (_objects[s] != null) Destroy(_objects[s]);
                if (_glb[s] != null) _glb[s].enabled = true;
            }
        }
    }
}
