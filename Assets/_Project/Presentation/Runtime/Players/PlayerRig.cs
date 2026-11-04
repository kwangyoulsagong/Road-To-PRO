using System.Collections.Generic;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 선수 한 명 (specs/043 FR-01·10·11·13·20). player.glb 인스턴스 루트에 붙는다.
    /// LOD 3단(LODGroup 크로스페이드, 화면 높이 비율), 루트 균등 스케일 = 키/180 cm, 체형·헤어 블렌드셰이프, 머티리얼 2개(Body·Kit) 인스턴스.
    /// 루트 위치·정면 각은 매 프레임 SetRoot로(애니메이션 시스템이) 정한다. 정면 = 로컬 +Z, yaw는 +Z → +X 쪽이 양수(라디안).
    /// </summary>
    public sealed class PlayerRig : MonoBehaviour
    {
        /// <summary>LOD 전환 화면 높이 비율 (plan [제안] 0.25 / 0.08, LOD2는 끝까지 — 컬링 없음).</summary>
        public static readonly float[] LodHeights = { 0.25f, 0.08f, 0.0001f };
        /// <summary>히스테리시스 (FR-13): 내려갈 때는 경계의 1 − H, 올라갈 때는 1 + H를 지나야 바꾼다 → 경계에서 왕복 깜빡임 없음.</summary>
        public const float LodHysteresis = 0.15f;
        int _lod = -1;
        SkinnedMeshRenderer _shadow;

        [SerializeField] SkinnedMeshRenderer[] _lods = new SkinnedMeshRenderer[3];
        [SerializeField] Animator _animator;
        Material _body, _kit;
        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        float _baseScale = 1f;

        public SkinnedMeshRenderer Lod(int i) => _lods[i];
        public Animator Animator => _animator;
        public Material BodyMaterial => _body;
        public Material KitMaterial => _kit;
        public PlayerAppearance Appearance { get; private set; }

        /// <summary>glb 프리팹을 인스턴스로 만들고 리그를 붙인다. bodyShader·kitShader로 이 선수 전용 머티리얼 2개를 만든다.</summary>
        public static PlayerRig Create(GameObject prefab, Transform parent, Shader bodyShader, Shader kitShader, Texture bodyTex,
            Texture fabricTex)
        {
            var go = Instantiate(prefab, parent);
            go.name = prefab.name;
            var rig = go.AddComponent<PlayerRig>();
            rig._animator = go.GetComponent<Animator>();
            if (rig._animator == null) rig._animator = go.AddComponent<Animator>();
            rig._animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            rig._animator.applyRootMotion = false;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int l = smr.name.EndsWith("LOD0") ? 0 : smr.name.EndsWith("LOD1") ? 1 : smr.name.EndsWith("LOD2") ? 2 : -1;
                if (l >= 0) rig._lods[l] = smr;
            }
            rig._body = new Material(bodyShader) { name = "PlayerBody (instance)" };
            rig._kit = new Material(kitShader) { name = "PlayerKit (instance)" };
            rig._body.SetTexture("_BodyTex", bodyTex);
            rig._kit.SetTexture("_FabricTex", fabricTex);
            rig._kit.SetVector("_NameArea", PlayerLook.NameArea);
            rig._kit.SetVector("_NumberArea", PlayerLook.NumberArea);
            rig._kit.SetVector("_FrontArea", PlayerLook.FrontArea);
            rig._kit.SetVector("_NameSub", PlayerLook.NameSub);
            rig._kit.SetVector("_NumberSub", PlayerLook.NumberSub);
            var mats = new[] { rig._body, rig._kit };
            foreach (var smr in rig._lods)
            {
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = false;
                smr.skinnedMotionVectors = false;
            }
            // 그림자는 LOD와 무관하게 저폴리 프록시 하나가 맡는다(서브메시 1개, 그림자만) — 그림자 드로우 25회·삼각형 ≈ 2.4만 (AC-16)
            foreach (var l in rig._lods) l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rig._shadow = ShadowProxy(rig._lods[2]);
            var group = go.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            var lods = new LOD[3];
            for (int i = 0; i < 3; i++) lods[i] = new LOD(LodHeights[i], new Renderer[] { rig._lods[i] });
            group.SetLODs(lods);
            group.RecalculateBounds();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) rig._bones[t.name] = t;
            return rig;
        }

        public Transform Bone(string name) => _bones.TryGetValue(name, out var t) ? t : null;

        static readonly Dictionary<Mesh, Mesh> SingleSubmesh = new Dictionary<Mesh, Mesh>();

        /// <summary>LOD2 메시를 서브메시 하나로 합친 복사본(공유)으로 그림자 전용 스킨 렌더러를 만든다.</summary>
        static SkinnedMeshRenderer ShadowProxy(SkinnedMeshRenderer src)
        {
            if (!SingleSubmesh.TryGetValue(src.sharedMesh, out var m) || m == null)
            {
                m = Object.Instantiate(src.sharedMesh);
                m.name = src.sharedMesh.name + " (shadow)";
                var tris = new List<int>();
                for (int i = 0; i < m.subMeshCount; i++) tris.AddRange(m.GetTriangles(i));
                m.subMeshCount = 1;
                m.SetTriangles(tris, 0);
                SingleSubmesh[src.sharedMesh] = m;
            }
            var go = new GameObject("ShadowProxy");
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition;
            go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale = src.transform.localScale;
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = m;
            r.bones = src.bones;
            r.rootBone = src.rootBone;
            r.sharedMaterials = new[] { src.sharedMaterials[0] };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            r.receiveShadows = false;
            r.updateWhenOffscreen = false;
            r.skinnedMotionVectors = false;
            r.localBounds = src.localBounds;
            return r;
        }

        /// <summary>
        /// LOD 선택 (FR-13): 화면 높이 비율 = 키 / (2·거리·tan(세로 시야/2)). 히스테리시스로 경계 왕복 깜빡임 없이 한 단계만 그린다.
        /// 크로스페이드 대신 이 방식을 쓰는 이유: 전환 중 두 단계를 같이 그리면 드로우콜·삼각형이 겹쳐 예산(AC-16)을 넘는다.
        /// </summary>
        public int UpdateLod(Camera cam)
        {
            if (cam == null) return _lod;
            float dist = Vector3.Distance(cam.transform.position, transform.position + Vector3.up * (0.9f * transform.localScale.y));
            float h = 1.8f * transform.localScale.y / Mathf.Max(1e-3f, 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            int want = h >= LodHeights[0] ? 0 : h >= LodHeights[1] ? 1 : 2;
            if (_lod >= 0 && want != _lod)
            {
                // 경계에서 충분히 벗어났을 때만 바꾼다
                int edge = Mathf.Min(want, _lod);
                float t = LodHeights[edge];
                bool goingUp = want < _lod;
                if (goingUp ? h < t * (1f + LodHysteresis) : h > t * (1f - LodHysteresis)) want = _lod;
            }
            if (want != _lod)
            {
                _lod = want;
                var g = GetComponent<LODGroup>();
                if (Live(g)) g.ForceLOD(want);
            }
            return _lod;
        }

        /// <summary>LOD 고정(−1 = 자동). 검증 캡처처럼 크로스페이드 진행 없이 한 단계로 볼 때.</summary>
        /// <summary>그림자 프록시 켜기/끄기 (성능 진단·품질 프리셋 048).</summary>
        public void SetShadow(bool on)
        {
            if (_shadow != null) _shadow.enabled = on;
        }

        public void ForceLod(int lod)
        {
            _lod = lod;
            var g = GetComponent<LODGroup>();
            // 꺼졌거나 비활성인 그룹에 강제하면 Unity 경고 — 켜질 때(OnEnable·Freeze 해제) 다시 건다
            if (Live(g)) g.ForceLOD(lod);
        }

        /// <summary>체형·피부·얼굴·헤어 (FR-01·02).</summary>
        public void ApplyAppearance(in PlayerAppearance a)
        {
            Appearance = a;
            _baseScale = a.Scale;
            transform.localScale = new Vector3(_baseScale, _baseScale, _baseScale);
            _body.SetColor("_SkinColor", PlayerLook.SkinTones[a.SkinTone]);
            _body.SetColor("_HairColor", PlayerLook.HairColors[a.HairColor]);
            _body.SetFloat("_FaceCell", a.Face);
            foreach (var smr in Renderers())
            {
                var mesh = smr.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++) smr.SetBlendShapeWeight(i, 0f);
                Set(smr, "Thin", a.Thin);
                Set(smr, "Muscular", a.Muscular);
                Set(smr, "Heavy", a.Heavy);
                var hk = PlayerLook.HairKeys[a.HairStyle];
                if (hk != null) Set(smr, hk, 1f);
            }
        }

        IEnumerable<SkinnedMeshRenderer> Renderers()
        {
            foreach (var l in _lods) yield return l;
            if (_shadow != null) yield return _shadow;
        }

        static void Set(SkinnedMeshRenderer smr, string key, float w)
        {
            // glTFast는 프레임 가중치를 1로 만든다(FBX 관례 100이 아님) → 가중치 = 비율 × 프레임 가중치
            var mesh = smr.sharedMesh;
            int i = mesh.GetBlendShapeIndex(key);
            if (i >= 0) smr.SetBlendShapeWeight(i, w * mesh.GetBlendShapeFrameWeight(i, mesh.GetBlendShapeFrameCount(i) - 1));
        }

        /// <summary>키트 색·패턴·번호/이름 칸 (FR-20·21). textAtlas가 null이면 번호·이름 없음.</summary>
        public void ApplyKit(in KitColors k, Texture mask, Texture textAtlas, int cell)
        {
            _kit.SetTexture("_MaskTex", mask);
            _kit.SetColor("_Primary", PlayerLook.ToColor(k.Primary));
            _kit.SetColor("_Secondary", PlayerLook.ToColor(k.Secondary));
            _kit.SetColor("_Accent", PlayerLook.ToColor(k.Accent));
            _kit.SetColor("_TextColor", PlayerLook.ToColor(PlayerLook.TextColor(k)));
            _kit.SetTexture("_TextTex", textAtlas);
            _kit.SetVector("_TextCell", textAtlas != null && cell >= 0 ? PlayerLook.CellUv(cell) : Vector4.zero);
        }

        MeshRenderer _frozen;
        Mesh _frozenMesh;

        /// <summary>Freeze 중이면 그려지는 정적 메시와 그 렌더러(검증 영역 계산용), 아니면 null.</summary>
        public Mesh FrozenMesh => _frozen != null && _frozen.enabled ? _frozenMesh : null;
        public Transform FrozenTransform => _frozen != null ? _frozen.transform : null;

        /// <summary>
        /// 현재 포즈를 LOD0 정적 메시로 구워 그린다(true) / 스키닝으로 돌아간다(false).
        /// 정지 장면 캡처용 — GPU 스키닝 결과가 프레임마다 미세하게 달라 바이트 단위 안정 판정을 깨는 것을 피한다.
        /// </summary>
        public void Freeze(bool on)
        {
            if (on)
            {
                if (_frozen == null)
                {
                    var go = new GameObject("Frozen");
                    go.transform.SetParent(_lods[0].transform, false);
                    go.AddComponent<MeshFilter>();
                    _frozen = go.AddComponent<MeshRenderer>();
                    _frozenMesh = new Mesh { name = "FrozenPose" };
                }
                _lods[0].BakeMesh(_frozenMesh, false);
                _frozen.GetComponent<MeshFilter>().sharedMesh = _frozenMesh;
                _frozen.sharedMaterials = _lods[0].sharedMaterials;
                _frozen.transform.localScale = Vector3.one;
                _frozen.enabled = true;
                foreach (var l in _lods) l.enabled = false;
                _frozen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (_shadow != null) _shadow.enabled = false;
                // 강제 LOD를 풀고 끈다 — 강제가 걸린 채 꺼진 그룹은 매 프레임 "force a LOD on a disabled LODGroup" 경고
                var lg = GetComponent<LODGroup>();
                lg.ForceLOD(-1);
                lg.enabled = false;
            }
            else
            {
                if (_frozen != null) _frozen.enabled = false;
                foreach (var l in _lods) l.enabled = true;
                if (_shadow != null) _shadow.enabled = true;
                var g = GetComponent<LODGroup>();
                g.enabled = true;
                if (Live(g)) g.ForceLOD(_lod);
            }
        }

        /// <summary>검증용: 번호·이름 글자색을 키 색으로 바꾼다(null이면 원래 색). 키트 다른 색과 섞이지 않는 글자 픽셀 판정용.</summary>
        public void SetTextKey(Color? key, in KitColors k)
        {
            _kit.SetColor("_TextColor", key ?? PlayerLook.ToColor(PlayerLook.TextColor(k)));
        }

        /// <summary>화면 루트 위치(바닥)·정면 각(라디안).</summary>
        public void SetRoot(Vector3 position, float yaw)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));
        }

        static bool Live(LODGroup g) => g.enabled && g.gameObject.activeInHierarchy;

        void OnEnable()
        {
            var g = GetComponent<LODGroup>();
            if (g != null && Live(g)) g.ForceLOD(_lod);
        }

        void OnDestroy()
        {
            if (_frozenMesh != null) Destroy(_frozenMesh);
            if (_body != null) Destroy(_body);
            if (_kit != null) Destroy(_kit);
        }
    }
}
