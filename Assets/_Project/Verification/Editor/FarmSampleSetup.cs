using RoadToPro.Verification.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RoadToPro.Verification.Editor
{
    /// <summary>
    /// 샘플 씬·패널 설정 에셋 생성 (specs/000 T26). 씬·에셋 YAML은 손으로 쓰지 않고 이 메서드로 만든다.
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Verification.Editor.FarmSampleSetup.Generate
    /// </summary>
    public static class FarmSampleSetup
    {
        public const string Dir = "Assets/_Project/Verification/Samples";
        public const string ScenePath = Dir + "/FarmSampleCards.unity";
        const string PanelPath = Dir + "/FarmSamplePanel.asset";
        const string ThemePath = Dir + "/FarmTheme.tss";
        const string UxmlPath = Dir + "/SampleCards.uxml";

        [MenuItem("Road To PRO/디바이스팜/샘플 씬 다시 만들기")]
        public static void Generate()
        {
            AssetDatabase.Refresh();
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (theme == null || uxml == null)
                throw new System.InvalidOperationException($"샘플 에셋 없음: theme={theme != null} uxml={uxml != null}");

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = 1f;
            EditorUtility.SetDirty(panel);

            const string resDir = "Assets/_Project/Verification/Resources";
            const string tablePath = resDir + "/FarmRenderAssets.asset";
            if (!AssetDatabase.IsValidFolder(resDir)) AssetDatabase.CreateFolder("Assets/_Project/Verification", "Resources");
            var table = AssetDatabase.LoadAssetAtPath<FarmRenderAssets>(tablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<FarmRenderAssets>();
                AssetDatabase.CreateAsset(table, tablePath);
            }
            table.Entries = new[]
            {
                new FarmRenderAssets.Entry { name = "Mobile", asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset") },
                new FarmRenderAssets.Entry { name = "PC", asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset") },
            };
            foreach (var en in table.Entries)
                if (en.asset == null) throw new System.InvalidOperationException("렌더 에셋 없음: " + en.name);
            EditorUtility.SetDirty(table);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            var go = new GameObject("SampleCards");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;
            go.AddComponent<SampleCardsScreen>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[farm] 샘플 씬 생성: " + ScenePath);
        }
    }
}
