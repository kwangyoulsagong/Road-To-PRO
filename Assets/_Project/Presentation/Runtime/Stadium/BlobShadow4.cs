using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>블롭 그림자 한 장의 자세.</summary>
    public struct BlobShadowPose
    {
        public Vector3 Center;     // 지면 위 중심
        public Vector3 Direction;  // 길게 늘어나는 방향 (수평 단위벡터, 광원 반대)
        public float Length;
        public float Width;
    }

    /// <summary>
    /// 나이트 경기 선수 발밑 블롭 그림자 4장 (specs/044 FR-41, 확정: 실제 그림자 대신). 광원(조명탑·지붕 라인 모서리) 4개마다
    /// 광원 반대 방향으로 늘린 타원 1장 → 모서리 광원이면 X자. 공유 머티리얼 + GPU 인스턴싱으로 그린다.
    /// 낮 프리셋에서는 꺼진다 (<see cref="StadiumLighting"/>).
    /// </summary>
    public sealed class BlobShadow4 : MonoBehaviour
    {
        public const float DefaultLength = 1.6f;
        public const float DefaultWidth = 0.55f;
        const float GroundOffset = 0.015f;

        [SerializeField] Transform target;
        [SerializeField] Mesh quad;
        [SerializeField] Material material;
        [SerializeField] float length = DefaultLength;
        [SerializeField] float width = DefaultWidth;

        [SerializeField] Vector3[] emitters = new Vector3[0];
        readonly BlobShadowPose[] _poses = new BlobShadowPose[4];
        readonly Matrix4x4[] _matrices = new Matrix4x4[4];

        public void Configure(Transform follow, IReadOnlyList<Vector3> emitters, Mesh mesh, Material mat)
        {
            target = follow;
            quad = mesh;
            material = mat;
            this.emitters = new Vector3[emitters.Count];
            for (int i = 0; i < emitters.Count; i++) this.emitters[i] = emitters[i];
        }

        public static void Compute(Vector3 foot, IReadOnlyList<Vector3> emitters, BlobShadowPose[] into,
            float length = DefaultLength, float width = DefaultWidth)
        {
            if (emitters == null || emitters.Count < 4) throw new ArgumentException("광원 4개 필요");
            if (into == null || into.Length < 4) throw new ArgumentException("결과 배열 4칸 필요");
            for (int i = 0; i < 4; i++)
            {
                var d = new Vector3(foot.x - emitters[i].x, 0f, foot.z - emitters[i].z);
                d = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
                into[i] = new BlobShadowPose
                {
                    Direction = d,
                    Center = new Vector3(foot.x, 0f, foot.z) + d * (length * 0.5f),
                    Length = length,
                    Width = width,
                };
            }
        }

        void LateUpdate()
        {
            if (target == null || quad == null || material == null || emitters.Length < 4) return;
            Compute(target.position, emitters, _poses, length, width);
            for (int i = 0; i < 4; i++)
            {
                var p = _poses[i];
                var rot = Quaternion.LookRotation(p.Direction, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                _matrices[i] = Matrix4x4.TRS(p.Center + Vector3.up * GroundOffset, rot, new Vector3(p.Width, p.Length, 1f));
            }
            Graphics.DrawMeshInstanced(quad, 0, material, _matrices, 4, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
}
