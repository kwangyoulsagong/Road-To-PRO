using System;
using System.Globalization;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 디바이스팜 플레이어 실행 인자 (specs/000 FR-22). Python 실행기(farm/runner.py)가 같은 이름으로 넘긴다.
    /// -farmScenario가 없으면 일반 실행이며 드라이버는 아무것도 하지 않는다.
    /// </summary>
    public sealed class FarmArgs
    {
        public string ScenarioPath { get; private set; }
        public string OutDir { get; private set; }
        public int Seed { get; private set; }
        public string ProfileId { get; private set; } = "";
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float UiScale { get; private set; } = 1f;
        public string Quality { get; private set; } = "Medium";
        public string UnityQualityLevel { get; private set; }
        public float RenderScale { get; private set; } = 1f;
        public int TargetFps { get; private set; } = 60;
        public string Theme { get; private set; } = "light";
        public string Lang { get; private set; } = "ko";
        public bool BatterySaver { get; private set; }

        // 셀프테스트 전용 (샘플 화면·SampleSim에만 영향)
        public string SampleButtonColor { get; private set; }
        public bool SampleBlack { get; private set; }
        public int SampleSimExtraRngAtTick { get; private set; } = -1;

        public bool IsFarmRun => ScenarioPath != null;

        public static FarmArgs Parse(string[] argv)
        {
            var a = new FarmArgs();
            for (int i = 0; i < argv.Length; i++)
            {
                string k = argv[i];
                if (!k.StartsWith("-farm", StringComparison.Ordinal)) continue;
                switch (k)
                {
                    case "-farmBatterySaver": a.BatterySaver = true; continue;
                    case "-farmSampleBlack": a.SampleBlack = true; continue;
                }
                if (i + 1 >= argv.Length) throw new FormatException($"{k}에 값이 없다");
                string v = argv[++i];
                switch (k)
                {
                    case "-farmScenario": a.ScenarioPath = v; break;
                    case "-farmOut": a.OutDir = v; break;
                    case "-farmSeed": a.Seed = Int(k, v); break;
                    case "-farmProfile": a.ProfileId = v; break;
                    case "-farmWidth": a.Width = Int(k, v); break;
                    case "-farmHeight": a.Height = Int(k, v); break;
                    case "-farmUiScale": a.UiScale = Float(k, v); break;
                    case "-farmQuality": a.Quality = v; break;
                    case "-farmUnityQuality": a.UnityQualityLevel = v; break;
                    case "-farmRenderScale": a.RenderScale = Float(k, v); break;
                    case "-farmTargetFps": a.TargetFps = Int(k, v); break;
                    case "-farmTheme": a.Theme = OneOf(k, v, "light", "dark"); break;
                    case "-farmLang": a.Lang = OneOf(k, v, "ko", "en"); break;
                    case "-farmSampleButtonColor": a.SampleButtonColor = v; break;
                    case "-farmSampleSimExtraRngAtTick": a.SampleSimExtraRngAtTick = Int(k, v); break;
                    default: throw new FormatException($"알 수 없는 인자 {k}");
                }
            }
            if (a.IsFarmRun)
            {
                if (string.IsNullOrEmpty(a.OutDir)) throw new FormatException("-farmOut 필요");
                if (a.Width <= 0 || a.Height <= 0) throw new FormatException("-farmWidth/-farmHeight 필요 (양수)");
                if (a.UiScale <= 0 || a.RenderScale <= 0 || a.TargetFps <= 0) throw new FormatException("배율·렌더 스케일·목표 fps는 양수");
            }
            return a;
        }

        static int Int(string k, string v)
        {
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
                throw new FormatException($"{k}: 정수가 아니다 '{v}'");
            return r;
        }

        static float Float(string k, string v)
        {
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                throw new FormatException($"{k}: 실수가 아니다 '{v}'");
            return r;
        }

        static string OneOf(string k, string v, params string[] allowed)
        {
            if (Array.IndexOf(allowed, v) < 0) throw new FormatException($"{k}: '{v}'는 {string.Join("/", allowed)} 중 하나여야 한다");
            return v;
        }
    }
}
