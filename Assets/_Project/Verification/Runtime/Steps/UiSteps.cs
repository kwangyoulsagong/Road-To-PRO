using System.Collections;
using UnityEngine.UIElements;

namespace RoadToPro.Verification
{
    // 화면 이동·누르기·입력·대기 단계 (specs/000 FR-20)
    public sealed partial class ScenarioRunner
    {
        IEnumerator Goto(StepDef s, StepOutcome o)
        {
            var screen = s.GetString("screen");
            if (!FarmContext.Screens.TryGetValue(screen, out var show))
            {
                o.Fail($"화면 '{screen}'이 등록되지 않았다");
                yield break;
            }
            show();
            yield return null;
            yield return null;
        }

        IEnumerator Tap(StepDef s, StepOutcome o)
        {
            var name = s.GetString("element");
            var e = Find(name);
            while (!IsVisible(e))
            {
                yield return null;
                e = Find(name);
            }
            if (!e.enabledInHierarchy)
            {
                o.Fail($"'{name}'이 비활성 상태라 누를 수 없다");
                yield break;
            }
            // 버튼은 Submit(=클릭)으로, 그 밖의 요소는 ClickEvent로 보낸다.
            using (var ev = NavigationSubmitEvent.GetPooled())
            {
                ev.target = e;
                e.SendEvent(ev);
            }
            if (!(e is Button))
            {
                using (var click = ClickEvent.GetPooled())
                {
                    click.target = e;
                    e.SendEvent(click);
                }
            }
            yield return null;
            yield return null;
        }

        IEnumerator TypeText(StepDef s, StepOutcome o)
        {
            var name = s.GetString("element");
            var e = Find(name);
            while (!IsVisible(e))
            {
                yield return null;
                e = Find(name);
            }
            if (!(e is TextField tf))
            {
                o.Fail($"'{name}'은 입력 칸(TextField)이 아니다");
                yield break;
            }
            tf.value = s.GetString("text");
            yield return null;
        }

        IEnumerator WaitFor(StepDef s, StepOutcome o)
        {
            if (s.Has("element"))
            {
                var name = s.GetString("element");
                while (!IsVisible(Find(name))) yield return null;
                yield break;
            }
            if (FarmContext.Playback == null)
            {
                o.Fail("재생기(IFarmPlayback)가 등록되지 않아 시뮬 시각·장면 대기를 할 수 없다");
                yield break;
            }
            if (s.Has("simTime"))
            {
                double t = s.GetDouble("simTime");
                while (FarmContext.Playback.SimTimeSeconds < t) yield return null;
            }
            else
            {
                int scene = (int)s.GetDouble("highlightScene");
                while (FarmContext.Playback.CurrentScene < scene) yield return null;
            }
        }
    }
}
