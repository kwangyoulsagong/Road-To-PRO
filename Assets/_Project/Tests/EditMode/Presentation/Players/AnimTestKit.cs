using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>테스트 공용: 생성된 클립 태그·매핑 표 로드.</summary>
    internal static class AnimTestKit
    {
        public const string ClipsPath = "Assets/_Project/Presentation/Players/Generated/player.clips.json";
        public const string MapPath = "Assets/_Project/Presentation/Players/Data/043-anim-map.json";

        static ClipLibrary _lib;
        static AnimationMap _map;

        public static ClipLibrary Lib
        {
            get
            {
                if (_lib != null) return _lib;
                var t = AssetDatabase.LoadAssetAtPath<TextAsset>(ClipsPath);
                Assert.IsNotNull(t, ClipsPath);
                return _lib = ClipLibrary.Parse(t.text);
            }
        }

        public static string MapJson
        {
            get
            {
                var t = AssetDatabase.LoadAssetAtPath<TextAsset>(MapPath);
                Assert.IsNotNull(t, MapPath);
                return t.text;
            }
        }

        public static AnimationMap Map => _map ??= AnimationMap.Parse(MapJson, Lib, NullLogger.Instance);
    }
}
