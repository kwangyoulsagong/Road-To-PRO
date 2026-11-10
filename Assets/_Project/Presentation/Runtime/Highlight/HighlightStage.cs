using System.Collections;
using System.Collections.Generic;
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

        /// <summary>블롭 그림자 머티리얼 (specs/048 FR-01 Low). null이면 블롭을 그리지 않는다.</summary>
        public virtual Material BlobMaterial => null;
        /// <summary>낮 블롭 방향을 정하는 태양 방향광.</summary>
        public virtual Light Sun => null;
        /// <summary>나이트 경기면 광원 위치(4개 이상), 낮이면 null.</summary>
        public virtual IReadOnlyList<Vector3> NightEmitters => null;
    }
}
