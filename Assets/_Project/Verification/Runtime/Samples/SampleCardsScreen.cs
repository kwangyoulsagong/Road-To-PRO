using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// 디바이스팜 샘플 화면: 카드 3장 + 버튼 1개 (specs/000 AC-03~08·16). 게임 화면이 아니다.
    /// ko/en·light/dark를 FarmContext에서 읽고, 셀프테스트 인자(버튼 색·검은 화면)를 적용한다.
    /// 시작할 때 SampleSim을 돌려 결정론 프로브로 등록한다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SampleCardsScreen : MonoBehaviour
    {
        static readonly Dictionary<string, string[]> Text = new Dictionary<string, string[]>
        {
            // 요소 이름 → { ko, en }. en 'card3Caption'은 일부러 길다 (AC-06).
            { "title", new[] { "디바이스팜 샘플", "Device Farm Sample" } },
            { "card1Label", new[] { "잔액", "Balance" } },
            { "card1Value", new[] { "1,250,000", "1,250,000" } },
            { "card1Caption", new[] { "지난주보다 +4%", "+4% from last week" } },
            { "card2Label", new[] { "이번 주 경기", "Matches this week" } },
            { "card2Value", new[] { "3", "3" } },
            { "card2Caption", new[] { "다음 경기 토요일", "Next match Saturday" } },
            { "card3Label", new[] { "컨디션", "Condition" } },
            { "card3Value", new[] { "87%", "87%" } },
            { "card3Caption", new[] { "새 시즌 일정 공개", "The new season fixture list has just been published for every club in the league" } },
            { "confirm", new[] { "확인", "Confirm" } },
            { "statusDone", new[] { "확인 완료", "Confirmed" } },
        };

        static FontAsset _font;
        UIDocument _doc;
        SampleSim _sim;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            FarmContext.RegisterDocument(_doc);
            FarmContext.Screens["home"] = Build;
            Build();

            var args = FarmContext.Args;
            _sim = new SampleSim(args?.Seed ?? 0, args?.SampleSimExtraRngAtTick ?? -1);
            _sim.Run();
            FarmContext.DeterminismProbe = _sim;
        }

        void OnDisable()
        {
            FarmContext.UnregisterDocument(_doc);
            FarmContext.Screens.Remove("home");
            if (ReferenceEquals(FarmContext.DeterminismProbe, _sim)) FarmContext.DeterminismProbe = null;
        }

        static string T(string key)
        {
            var i = FarmContext.Lang == "en" ? 1 : 0;
            return Text[key][i];
        }

        void Build()
        {
            var top = _doc.rootVisualElement;
            var root = top.Q("root");
            if (root == null) return;
            if (root.parent != null) root.parent.style.flexGrow = 1;

            var font = HangulFont();
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);

            root.EnableInClassList("theme-dark", FarmContext.Theme == "dark");
            foreach (var kv in Text)
                if (root.Q(kv.Key) is TextElement te) te.text = T(kv.Key);

            var status = root.Q<Label>("status");
            status.text = "";
            var confirm = root.Q<Button>("confirm");
            confirm.clicked -= OnConfirm;
            confirm.clicked += OnConfirm;

            var args = FarmContext.Args;
            if (args != null && !string.IsNullOrEmpty(args.SampleButtonColor) && ColorUtility.TryParseHtmlString(args.SampleButtonColor, out var c))
                confirm.style.backgroundColor = c; // 셀프테스트 AC-05: 버튼 색 변경
            if (args != null && args.SampleBlack)
                root.style.display = DisplayStyle.None; // 셀프테스트 AC-07: 패널 지움색(검정)만 남는다
        }

        void OnConfirm()
        {
            var status = _doc.rootVisualElement.Q<Label>("status");
            if (status != null) status.text = T("statusDone");
        }

        /// <summary>
        /// 한글 글리프: 기본 런타임 폰트에는 한글이 없다. 샘플은 맥 시스템 폰트로 동적 폰트 에셋을 만든다
        /// (게임 폰트는 037 UI 기반에서 정한다).
        /// </summary>
        static FontAsset HangulFont()
        {
            if (_font != null) return _font;
            _font = FontAsset.CreateFontAsset("Apple SD Gothic Neo", "Regular");
            if (_font == null) Debug.Log("[farm] 샘플: 'Apple SD Gothic Neo' 폰트를 찾지 못해 기본 폰트 사용");
            return _font;
        }
    }
}
