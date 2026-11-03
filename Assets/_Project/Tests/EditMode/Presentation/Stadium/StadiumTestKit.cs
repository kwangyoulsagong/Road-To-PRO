using System.IO;
using RoadToPro.Presentation.Stadium;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    static class StadiumTestKit
    {
        public const string DefaultGlb = "Assets/_Project/Presentation/Stadium/Generated/default/stadium.glb";
        public const string DefaultLayoutPath = "Assets/_Project/Presentation/Stadium/Generated/default/stadium.layout.json";

        static StadiumLayout _layout;
        public static StadiumLayout DefaultLayout => _layout ??= StadiumLayout.Parse(File.ReadAllText(DefaultLayoutPath));
    }
}
