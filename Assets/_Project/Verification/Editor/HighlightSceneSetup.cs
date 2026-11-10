using RoadToPro.Presentation.Highlight;
using RoadToPro.Presentation.Stadium;
using RoadToPro.Verification.Samples.Highlight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadToPro.Verification.Editor
{
    /// <summary>
    /// specs/041 E2E 씬 생성: 호스트(메뉴·카메라·HUD) + 3D 무대. 무대는 호스트에 추가로 올렸다 내린다.
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Verification.Editor.HighlightSceneSetup.Generate
    /// </summary>
    public static class HighlightSceneSetup
    {
        public const string HostScenePath = FarmSampleSetup.Dir + "/Highlight041.unity";
        public const string StageScenePath = FarmSampleSetup.Dir + "/Highlight3D.unity";
        const string PanelPath = FarmSampleSetup.Dir + "/FarmSamplePanel.asset";

        [MenuItem("Road To PRO/디바이스팜/하이라이트 씬(041) 다시 만들기")]
        public static void Generate()
        {
            GenerateStage();
            GenerateHost();
            AssetDatabase.SaveAssets();
            Debug.Log($"[farm] 하이라이트 씬 생성: {HostScenePath}, {StageScenePath}");
        }

        static void GenerateStage()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var st = StadiumSceneSetup.BuildStadium(scene);
            Object.DestroyImmediate(st.Camera.gameObject);
            var lighting = new GameObject("Lighting").AddComponent<StadiumLighting>();
            var day = AssetDatabase.LoadAssetAtPath<StadiumLightingPreset>(RoadToPro.Presentation.Editor.Stadium.StadiumAssets.DayPath);
            if (day == null) throw new System.InvalidOperationException("조명 프리셋 없음");
            var stage = new GameObject("HighlightStage").AddComponent<HighlightCastStage>();
            var night = AssetDatabase.LoadAssetAtPath<StadiumLightingPreset>(RoadToPro.Presentation.Editor.Stadium.StadiumAssets.NightPath);
            var blob = AssetDatabase.LoadAssetAtPath<Material>(RoadToPro.Presentation.Editor.Stadium.StadiumAssets.BlobMatPath);
            if (night == null || blob == null) throw new System.InvalidOperationException("나이트 프리셋·블롭 머티리얼 없음");
            stage.Configure(PlayerSceneSetup.BuildAssets(), st.Sun, st.Emissive, lighting, day, st.Binder, night, blob);
            EditorSceneManager.SaveScene(scene, StageScenePath);
        }

        static void GenerateHost()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("BroadcastCamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.95f, 0.95f, 0.96f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            cam.allowMSAA = false;

            var go = new GameObject("HighlightHost");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (doc.panelSettings == null) throw new System.InvalidOperationException("패널 설정 없음: " + PanelPath);
            var player = go.AddComponent<HighlightPlayer>();
            go.AddComponent<HighlightScreen>().Configure(cam, doc, player);
            EditorSceneManager.SaveScene(scene, HostScenePath);
        }
    }
}
