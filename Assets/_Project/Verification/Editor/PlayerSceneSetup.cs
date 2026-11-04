using System.Linq;
using RoadToPro.Presentation.Editor.Players;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Verification.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadToPro.Verification.Editor
{
    /// <summary>
    /// specs/043 E2E 씬 생성 (T23). 044 경기장 + 선수 화면(PlayerScreen). 선수 25명은 실행 때 화면이 만든다.
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Verification.Editor.PlayerSceneSetup.Generate
    /// </summary>
    public static class PlayerSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Verification/Samples/Player043.unity";
        const string Gen = PlayerImportSettings.GeneratedDir;
        const string Fonts = PlayerImportSettings.FontsDir;
        static readonly string[] Patterns = { "plain", "stripes", "hoops", "halves", "yoke", "sash" };

        [MenuItem("Road To PRO/디바이스팜/선수 씬(043) 다시 만들기")]
        public static void Generate()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var st = StadiumSceneSetup.BuildStadium(scene);
            var lighting = new GameObject("Lighting").AddComponent<RoadToPro.Presentation.Stadium.StadiumLighting>();
            lighting.Configure(st.Sun, st.Emissive, new[] { st.Camera }, new RoadToPro.Presentation.Stadium.BlobShadow4[0]);
            var day = AssetDatabase.LoadAssetAtPath<RoadToPro.Presentation.Stadium.StadiumLightingPreset>(RoadToPro.Presentation.Editor.Stadium.StadiumAssets.DayPath);
            st.Camera.fieldOfView = 30f;

            var clipsJson = Load<TextAsset>(Gen + "/player.clips.json");
            var lib = ClipLibrary.Parse(clipsJson.text);
            var a = new PlayerScreen.PlayerAssets
            {
                prefab = Load<GameObject>(Gen + "/player.glb"),
                clipsJson = clipsJson,
                mapJson = Load<TextAsset>("Assets/_Project/Presentation/Players/Data/043-anim-map.json"),
                glyphJson = Load<TextAsset>(Fonts + "/kit-glyphs.json"),
                body = new PlayerBodyAssets
                {
                    skin = Load<Texture2D>(Gen + "/" + PlayerBodyAssets.Skin),
                    skinNormal = Load<Texture2D>(Gen + "/" + PlayerBodyAssets.SkinNormal),
                    parts = Load<Texture2D>(Gen + "/" + PlayerBodyAssets.Parts),
                    closeupSkin = Load<Texture2D>(Gen + "/" + PlayerBodyAssets.CloseupSkin),
                    closeupNormal = Load<Texture2D>(Gen + "/" + PlayerBodyAssets.CloseupNormal),
                    faceJson = Load<TextAsset>(Gen + "/" + PlayerBodyAssets.FaceJson),
                },
                fabric = Load<Texture2D>(Gen + "/Textures/kit_normal.png"),
                names = Load<Texture2D>(Fonts + "/kit-glyphs-names.png"),
                numbers = Load<Texture2D>(Fonts + "/kit-glyphs-numbers.png"),
                masks = Patterns.Select(p => Load<Texture2D>(Gen + $"/Textures/kit_mask_{p}.png")).ToArray(),
                bodyShader = Shader.Find("RoadToPro/PlayerBody"),
                kitShader = Shader.Find("RoadToPro/PlayerKit"),
                clips = PlayerAnimationSystem.Order(lib, AssetDatabase.LoadAllAssetsAtPath(Gen + "/player.glb").OfType<AnimationClip>()),
            };
            var structure = st.Structure.ToArray();
            var screen = new GameObject("PlayerScreen").AddComponent<PlayerScreen>();
            screen.Configure(st.Camera, a, structure, st.Pitch, lighting, day, st.Binder);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[farm] 선수 씬 생성: {ScenePath} (클립 {a.clips.Length}, 경기장 렌더러 {structure.Length})");
        }

        static T Load<T>(string path) where T : Object
        {
            var o = AssetDatabase.LoadAssetAtPath<T>(path);
            if (o == null) throw new System.InvalidOperationException("에셋 없음: " + path);
            return o;
        }
    }
}
