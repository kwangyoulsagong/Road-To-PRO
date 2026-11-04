using System.Collections.Generic;
using System.Linq;
using RoadToPro.Presentation.Editor.Stadium;
using RoadToPro.Presentation.Stadium;
using RoadToPro.Verification.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadToPro.Verification.Editor
{
    /// <summary>
    /// specs/044 E2E 씬 생성 (T19). 씬 YAML은 손으로 쓰지 않고 이 메서드로 만든다.
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Verification.Editor.StadiumSceneSetup.Generate
    /// </summary>
    public static class StadiumSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Verification/Samples/Stadium044.unity";
        const string StandInMatPath = "Assets/_Project/Verification/Samples/StandIn.mat";
        const string GlbPath = StadiumImportSettings.GeneratedDir + "/default/stadium.glb";
        const string LayoutPath = StadiumImportSettings.GeneratedDir + "/default/stadium.layout.json";
        static readonly Vector3 PlayerSpot = new Vector3(-20f, 0f, 12f);

        [MenuItem("Road To PRO/디바이스팜/경기장 씬(044) 다시 만들기")]
        public static void Generate()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var st = BuildStadium(scene);
            var layout = st.Layout;
            var layoutAsset = st.LayoutAsset;
            var binder = st.Binder;
            var pitch = st.Pitch;
            var structure = st.Structure;
            var emissive = st.Emissive;
            var sun = st.Sun;
            var cam = st.Camera;

            var standInMat = AssetDatabase.LoadAssetAtPath<Material>(StandInMatPath);
            if (standInMat == null)
            {
                standInMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(standInMat, StandInMatPath);
            }
            standInMat.SetColor("_BaseColor", new Color(0.85f, 0.2f, 0.2f));
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "PlayerStandIn";
            Object.DestroyImmediate(player.GetComponent<Collider>());
            player.transform.position = PlayerSpot + Vector3.up * 0.9f;
            player.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            player.GetComponent<Renderer>().sharedMaterial = standInMat;
            player.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var blobGo = new GameObject("BlobShadows");
            var blob = blobGo.AddComponent<BlobShadow4>();
            blob.Configure(player.transform, layout.LightEmitters, Resources.GetBuiltinResource<Mesh>("Quad.fbx"),
                AssetDatabase.LoadAssetAtPath<Material>(StadiumAssets.BlobMatPath));

            var lightingGo = new GameObject("Lighting");
            var lighting = lightingGo.AddComponent<StadiumLighting>();
            lighting.Configure(sun, emissive, new[] { cam }, new[] { blob });

            var mount = layout.MainMountCenter;
            var views = new[]
            {
                V("main", mount, Vector3.zero, 30f),
                V("behindGoal", new Vector3(-80f, 28f, -6f), new Vector3(-36f, 0f, 0f), 30f),
                V("top", new Vector3(0f, 150f, 0f), Vector3.zero, 30f),
                V("pitchLevel", new Vector3(-20f, 1.7f, -36f), new Vector3(0f, 1f, 0f), 30f),
                V("mowA", new Vector3(-30f, 9f, 0f), Vector3.zero, 24f),
                V("mowB", new Vector3(30f, 9f, 0f), Vector3.zero, 24f),
                V("lineFov8", new Vector3(-6f, 18f, -14f), new Vector3(-36.06f, 0f, -14f), 8f),
                V("lineFov30", new Vector3(-6f, 18f, -14f), new Vector3(-36.06f, 0f, -14f), 30f),
                V("night", PlayerSpot + new Vector3(0f, 12f, 0.001f), PlayerSpot, 30f, night: true),
                V("perfEmpty", mount, Vector3.zero, 30f, show: 1),
                V("perfPitch", mount, Vector3.zero, 30f, show: 2),
                V("perfStructure", mount, Vector3.zero, 30f, show: 3),
                V("perfFull", mount, Vector3.zero, 30f, show: 0),
            };
            var screen = new GameObject("StadiumViews").AddComponent<StadiumViewsScreen>();
            screen.Configure(cam, lighting,
                AssetDatabase.LoadAssetAtPath<StadiumLightingPreset>(StadiumAssets.DayPath),
                AssetDatabase.LoadAssetAtPath<StadiumLightingPreset>(StadiumAssets.NightPath),
                binder, new[] { pitch }, structure.ToArray(), player.transform, blob, views);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[farm] 경기장 씬 생성: {ScenePath} (구조 렌더러 {structure.Count}, 발광 머티리얼 {(emissive != null ? emissive.name : "없음")})");
        }

        /// <summary>경기장·피치 바인더·태양·카메라 (044 E2E 씬과 043 선수 씬이 공유).</summary>
        public sealed class StadiumParts
        {
            public StadiumLayout Layout;
            public TextAsset LayoutAsset;
            public PitchMaterialBinder Binder;
            public Renderer Pitch;
            public List<Renderer> Structure;
            public Material Emissive;
            public Light Sun;
            public Camera Camera;
        }

        public static StadiumParts BuildStadium(UnityEngine.SceneManagement.Scene scene)
        {
            StadiumAssets.Generate();
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            var layoutAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            if (glb == null || layoutAsset == null) throw new System.InvalidOperationException($"경기장 산출물 없음: glb={glb != null} layout={layoutAsset != null}");
            var layout = StadiumLayout.Parse(layoutAsset.text);

            var stadium = (GameObject)PrefabUtility.InstantiatePrefab(glb, scene);
            stadium.name = "Stadium";

            Renderer pitch = null;
            var structure = new List<Renderer>();
            Material emissive = null;
            foreach (var r in stadium.GetComponentsInChildren<Renderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                if (r.name == "Pitch") { pitch = r; continue; }
                structure.Add(r);
                // FR-22: glTFast 머티리얼 → URP Lit
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null)
                    {
                        mats[i] = StadiumAssets.LitFor(mats[i]);
                        if (mats[i].name.StartsWith("Stadium_LightEmit")) emissive = mats[i];
                    }
                r.sharedMaterials = mats;
                // 작은 소품·반투명은 그림자를 드리우지 않는다 (그림자 패스 비용)
                if (r.name.StartsWith("Net_") || r.name.StartsWith("Boards") || r.name.StartsWith("Bench_") || r.name == "Tunnel" || r.name == "CornerFlags" || r.name == "Ground")
                    r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (pitch == null) throw new System.InvalidOperationException("Pitch 렌더러 없음");
            pitch.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(StadiumAssets.PitchMatPath);
            pitch.shadowCastingMode = ShadowCastingMode.Off;
            var binder = stadium.AddComponent<PitchMaterialBinder>();
            binder.Configure(layoutAsset, pitch);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;

            var camGo = new GameObject("FarmCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            cam.allowMSAA = false;

            return new StadiumParts
            {
                Layout = layout, LayoutAsset = layoutAsset, Binder = binder, Pitch = pitch, Structure = structure,
                Emissive = emissive, Sun = sun, Camera = cam,
            };
        }

        static StadiumViewsScreen.View V(string name, Vector3 pos, Vector3 target, float fov, bool night = false, int show = 0) =>
            new StadiumViewsScreen.View { name = name, position = pos, target = target, fov = fov, night = night, show = show };
    }
}
