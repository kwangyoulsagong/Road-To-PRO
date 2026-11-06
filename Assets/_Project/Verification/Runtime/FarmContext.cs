using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 현재 디바이스팜 실행 상태와 게임 쪽이 등록하는 연결점 (specs/000 plan FarmContext).
    /// 게임 화면·시뮬은 Verification을 참조하지 않으므로, 연결은 Verification 쪽 어댑터나 샘플이 등록한다.
    /// 일반 실행(-farmScenario 없음)에서는 Args가 null이다.
    /// </summary>
    public static class FarmContext
    {
        public static FarmArgs Args { get; internal set; }
        public static bool IsFarmRun => Args != null && Args.IsFarmRun;
        public static string Theme => Args?.Theme ?? "light";
        public static string Lang => Args?.Lang ?? "ko";
        public static bool BatterySaver => Args != null && Args.BatterySaver;

        /// <summary>시나리오가 조작·캡처할 UI 문서들. 화면이 OnEnable에서 등록한다.</summary>
        public static readonly List<UIDocument> Documents = new List<UIDocument>();

        /// <summary>3D 장면 캡처용 카메라 (specs/044). 등록하면 캡처 RT에 그린다. UI 문서 없이 카메라만 있어도 된다.</summary>
        public static readonly List<UnityEngine.Camera> Cameras = new List<UnityEngine.Camera>();

        /// <summary>
        /// 캡처 순간의 이름 붙은 영역 (PNG 픽셀, 왼쪽 위 원점) — Python 이미지 검사 입력 (specs/044).
        /// 값: 사각형 [x, y, w, h], 선분 {a: [x, y], b: [x, y]}, 점 [x, y], 점 목록 [[x, y], ...].
        /// </summary>
        public static Func<int, int, Dictionary<string, object>> CaptureRegions { get; set; }

        public static void RegisterCamera(UnityEngine.Camera cam)
        {
            if (cam != null && !Cameras.Contains(cam)) Cameras.Add(cam);
        }

        /// <summary>
        /// 성능 구간에서 프레임마다 읽는 사용자 정의 카운터 (specs/043: 애니메이션 CPU ms·GC 바이트). 값이 null이면 그 프레임은 기록 없음.
        /// perf.json 구간의 "custom": {이름: [값…]} → perf.py가 custom.이름.p95 등으로 집계.
        /// </summary>
        public static readonly Dictionary<string, Func<double?>> PerfCounters = new Dictionary<string, Func<double?>>();

        /// <summary>goto(screen) 단계가 부를 화면 전환 함수.</summary>
        public static readonly Dictionary<string, Action> Screens = new Dictionary<string, Action>();

        /// <summary>motionMeasure 단계 측정기 (specs/054). 선수 씬이 등록한다.</summary>
        public static IFarmMotionMeasure MotionMeasure { get; set; }

        /// <summary>checkpoint 단계가 읽는 결정론 프로브 (없으면 단계 실패).</summary>
        public static IDeterminismProbe DeterminismProbe { get; set; }

        /// <summary>waitFor(simTime/highlightScene)·freeze 단계용 재생 제어 (하이라이트 재생기 041이 등록).</summary>
        public static IFarmPlayback Playback { get; set; }

        /// <summary>테마·언어가 바뀌었을 때 화면이 다시 그리도록 (지금은 실행 시작 때 한 번).</summary>
        public static event Action Changed;

        internal static void RaiseChanged() => Changed?.Invoke();

        public static void RegisterDocument(UIDocument doc)
        {
            if (doc != null && !Documents.Contains(doc)) Documents.Add(doc);
        }

        public static void UnregisterDocument(UIDocument doc) => Documents.Remove(doc);

        internal static void ResetForTests()
        {
            Args = null;
            Documents.Clear();
            Cameras.Clear();
            CaptureRegions = null;
            Screens.Clear();
            DeterminismProbe = null;
            Playback = null;
            PerfCounters.Clear();
            Changed = null;
        }
    }

    /// <summary>하이라이트 재생 제어 (FR-25). 041 하이라이트 플레이어가 구현한다.</summary>
    public interface IFarmPlayback
    {
        double SimTimeSeconds { get; }
        int CurrentScene { get; }
        void SetSpeed(float speed);
        /// <summary>지정 시뮬 시각(또는 장면 시작)으로 이동해 멈춘다. 준비되면 true.</summary>
        bool SeekAndFreeze(double? simTimeSeconds, int? scene);
    }
}
