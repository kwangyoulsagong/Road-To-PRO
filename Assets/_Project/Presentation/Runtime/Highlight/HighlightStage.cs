using System.Collections;
using RoadToPro.Presentation.Players;
using UnityEngine;

namespace RoadToPro.Presentation.Highlight
{
    /// <summary>
    /// 3D 씬에 놓이는 무대. 선수 리그·공·클립을 준비한다 (specs/041 FR-30). 선수 외형은 002·020이 정하므로 구현은 씬마다 다르다.
    /// 카메라는 호출자 소유로 넘겨받는다.
    /// </summary>
    public abstract class HighlightStage : MonoBehaviour
    {
        public abstract PlayerRig[] Rigs { get; }
        public abstract AnimationClip[] Clips { get; }
        public abstract Transform Ball { get; }

        /// <summary>리그를 만든다. 프레임을 나눌 수 있게 코루틴이다.</summary>
        public abstract IEnumerator Prepare(Camera broadcastCamera);
    }
}
