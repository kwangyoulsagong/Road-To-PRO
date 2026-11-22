using System;
using System.Collections.Generic;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>062 FR-27 의상: 유니폼(기본) · 정장(076 시상 장면).</summary>
    public enum PlayerOutfit { Uniform, Suit }

    /// <summary>
    /// 선수 한 명 (specs/043 FR-01·10·11·13·20, 056 FR-22·40·61). player.glb 인스턴스 루트에 붙는다.
    /// LOD 3단(히스테리시스), 루트 균등 스케일 = 키/180 cm, 머티리얼 2개(Body·Kit) 인스턴스.
    /// 056: 체형·얼굴·수염·헤어는 로딩 때 PlayerMeshBaker로 선수별 메시를 구워 넣는다(재생 중 블렌드셰이프는 HideHead 하나).
    /// 클로즈업 머리(Head_Closeup + 헤어 LOD0)는 SetCloseup(true)일 때만 그린다(FR-61).
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
        Material _body, _kit, _closeupMat;
        readonly Mesh[] _srcLods = new Mesh[3];
        readonly Dictionary<string, SkinnedMeshRenderer> _parts = new Dictionary<string, SkinnedMeshRenderer>();
        SkinnedMeshRenderer _closeup;
        Mesh _closeupSrc;
        readonly List<Mesh> _baked = new List<Mesh>();
        Mesh _shadowMesh;
        bool _closeupOn;
        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        float _baseScale = 1f;

        public SkinnedMeshRenderer Lod(int i) => _lods[i];
        /// <summary>지금 그리는 LOD (0~2, 아직 정하지 않았으면 0).</summary>
        public int CurrentLod => _lod < 0 ? 0 : _lod;
        public Animator Animator => _animator;
        public Material BodyMaterial => _body;
        public Material KitMaterial => _kit;
        public PlayerAppearance Appearance { get; private set; }

        /// <summary>glb 프리팹을 인스턴스로 만들고 리그를 붙인다. bodyShader·kitShader로 이 선수 전용 머티리얼 2개를 만든다.</summary>
        public static PlayerRig Create(GameObject prefab, Transform parent, Shader bodyShader, Shader kitShader, PlayerBodyAssets bodyTex,
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
                if (smr.name.StartsWith("Player_LOD"))
                {
                    int l = smr.name[smr.name.Length - 1] - '0';
                    rig._lods[l] = smr;
                    rig._srcLods[l] = smr.sharedMesh;
                }
                else if (smr.name == "Head_Closeup")
                {
                    rig._closeup = smr;
                    rig._closeupSrc = smr.sharedMesh;
                    smr.enabled = false;
                }
                else if (smr.name.StartsWith("Suit_LOD"))
                {
                    rig._suitSrc[smr.name[smr.name.Length - 1] - '0'] = smr.sharedMesh;
                    smr.enabled = false; // 정장 몸 LOD는 ApplyOutfit 굽기 재료
                }
                else if (smr.name.StartsWith("Hair_"))
                {
                    rig._parts[smr.name] = smr;
                    smr.enabled = false; // 헤어 조각은 굽기 재료 — 직접 그리지 않는다
                }
            }
            rig._suitMask = bodyTex.suitMask;
            rig._body = new Material(bodyShader) { name = "PlayerBody (instance)" };
            rig._kit = new Material(kitShader) { name = "PlayerKit (instance)" };
            rig._skinSets = bodyTex.skinSets;
            SetBodyTextures(rig._body, SkinSet(bodyTex.skinSets, 0), bodyTex.skinNormal, bodyTex.parts);
            SetBodyDetail(rig._body, bodyTex);
            if (rig._closeup != null)
            {
                rig._closeupMat = new Material(bodyShader) { name = "PlayerBody closeup (instance)" };
                SetBodyTextures(rig._closeupMat, SkinSet(bodyTex.skinSets, 0), bodyTex.skinNormal, bodyTex.parts);   // 088: 같은 2K 세트
                SetBodyDetail(rig._closeupMat, bodyTex);
                rig._closeup.sharedMaterials = new[] { rig._closeupMat, rig._closeupMat };
                rig._closeup.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rig._closeup.updateWhenOffscreen = false;
                rig._closeup.skinnedMotionVectors = false;
            }
            // 062: 043 원단 노멀(fabricTex, kit_normal.png)은 구운 주름 노멀 + 짜임 타일로 대체 — 인자는 호출부 호환으로만 남는다
            // 062 FR-20·25·26·30·31: 구운 주름·봉제선 노멀·AO, 짜임 타일, 영역 맵(거칠기·인쇄 거칠기·결 선택·젖음 대상)
            SetIf(rig._kit, "_DetailN", bodyTex.kitDetailN);
            SetIf(rig._kit, "_DetailAO", bodyTex.kitDetailAO);
            SetIf(rig._kit, "_WeaveTex", bodyTex.kitWeave);
            SetIf(rig._kit, "_RegionTex", bodyTex.kitRegion);
            rig._kit.SetVector("_NameArea", PlayerLook.NameArea);
            rig._kit.SetVector("_NumberArea", PlayerLook.NumberArea);
            rig._kit.SetVector("_FrontArea", PlayerLook.FrontArea);
            rig._kit.SetVector("_NameSub", PlayerLook.NameSub);
            rig._kit.SetVector("_NumberSub", PlayerLook.NumberSub);
            var mats = new[] { rig._body, rig._kit };
            rig._matsNormal = mats;
            // 062 FR-86: LOD0 고정 등급의 고해상도 머티리얼 = 클로즈업 머리 머티리얼(같은 셰이더·같은 값, 2K 피부 텍스처만 다름 → FR-88)
            rig._matsHigh = rig._closeupMat != null ? new[] { rig._closeupMat, rig._kit } : mats;
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
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                rig._bones[t.name] = t;
                rig._restLocal[t] = t.localRotation; // 062: 만든 직후 = 바인드 포즈 (손가락 델타 변환에 쓴다)
            }
            return rig;
        }

        // ---------------------------------------------------------------- 062 FR-10·11 손가락
        readonly Dictionary<Transform, Quaternion> _restLocal = new Dictionary<Transform, Quaternion>();
        Transform[] _fingers;
        Quaternion[] _fingerRestLocal, _fingerRestModel;
        HandPoseTable _fingerTable;

        /// <summary>손가락 뼈 수 (glb에 손가락 뼈가 없으면 0 — 062 이전 생성물).</summary>
        public int FingerBoneCount { get; private set; }

        /// <summary>표의 뼈 이름으로 손가락 뼈를 찾고 레스트 회전(부모 local · 루트 기준 model)을 기억한다. 한 번만.</summary>
        public void BindFingers(HandPoseTable t)
        {
            if (t == null || ReferenceEquals(t, _fingerTable)) return;
            _fingerTable = t;
            _fingers = new Transform[HandPoseTable.Bones];
            _fingerRestLocal = new Quaternion[HandPoseTable.Bones];
            _fingerRestModel = new Quaternion[HandPoseTable.Bones];
            FingerBoneCount = 0;
            for (int i = 0; i < HandPoseTable.Bones; i++)
            {
                var b = Bone(t.BoneNames[i]);
                _fingers[i] = b;
                if (b == null) continue;
                FingerBoneCount++;
                _fingerRestLocal[i] = _restLocal.TryGetValue(b, out var rl) ? rl : b.localRotation;
                var q = Quaternion.identity;
                for (var x = b; x != null && x != transform; x = x.parent)
                    q = (_restLocal.TryGetValue(x, out var r) ? r : x.localRotation) * q;
                _fingerRestModel[i] = q;
            }
        }

        /// <summary>손가락 회전 델타(레스트 축, 선수 = Unity 좌표)를 뼈 local로 바꿔 적용한다. 클립·IK 평가 뒤에 부른다. 할당 없음.</summary>
        public void ApplyFingers(ReadOnlySpan<Quat4> delta)
        {
            if (_fingers == null) return;
            for (int i = 0; i < _fingers.Length && i < delta.Length; i++)
            {
                var b = _fingers[i];
                if (b == null) continue;
                var d = new Quaternion(delta[i].X, delta[i].Y, delta[i].Z, delta[i].W);
                var m = _fingerRestModel[i];
                b.localRotation = _fingerRestLocal[i] * (Quaternion.Inverse(m) * d * m);
            }
        }

        // ---------------------------------------------------------------- 062 FR-21 키트 보조 뼈
        Transform[] _kit6;
        Vector3[] _kitRestPos;
        Quaternion[] _kitParentRestModelInv;
        Transform _hips, _thighL, _thighR, _kneeL, _kneeR;
        Quaternion _hipsRestModelInv;
        KitSecondaryMotion _kitMotion;

        /// <summary>보조 뼈 수 (glb에 Kit_* 뼈가 없으면 0 — 062 T15 이전 생성물).</summary>
        public int KitBoneCount { get; private set; }

        /// <summary>레스트 회전을 루트(선수) 틀로 누적한다.</summary>
        Quaternion RestModel(Transform b)
        {
            var q = Quaternion.identity;
            for (var x = b; x != null && x != transform; x = x.parent)
                q = (_restLocal.TryGetValue(x, out var r) ? r : x.localRotation) * q;
            return q;
        }

        /// <summary>보조 뼈 6개와 고관절 굴곡 계산용 뼈를 찾고 레스트 위치·부모 레스트 회전을 기억한다. 한 번만.
        /// glb 임포트가 이름의 '.'을 바꿀 수 있어 '_'·' ' 변형도 찾는다.</summary>
        public void BindKitBones(KitSecondaryMotion m)
        {
            if (m == null || ReferenceEquals(m, _kitMotion)) return;
            _kitMotion = m;
            _kit6 = new Transform[KitSecondaryMotion.Bones];
            _kitRestPos = new Vector3[KitSecondaryMotion.Bones];
            _kitParentRestModelInv = new Quaternion[KitSecondaryMotion.Bones];
            KitBoneCount = 0;
            for (int i = 0; i < KitSecondaryMotion.Bones; i++)
            {
                string n = m.BoneName(i);
                var b = Bone(n) ?? Bone(n.Replace('.', '_')) ?? Bone(n.Replace('.', ' '));
                _kit6[i] = b;
                if (b == null) continue;
                KitBoneCount++;
                _kitRestPos[i] = b.localPosition;
                _kitParentRestModelInv[i] = Quaternion.Inverse(RestModel(b.parent));
            }
            _hips = Bone("Hips");
            _thighL = Bone("LeftUpperLeg"); _thighR = Bone("RightUpperLeg");
            _kneeL = Bone("LeftLowerLeg"); _kneeR = Bone("RightLowerLeg");
            if (_hips != null) _hipsRestModelInv = Quaternion.Inverse(RestModel(_hips));
        }

        /// <summary>현재 포즈의 고관절 굴곡 각(°): 골반 틀에서 허벅지 방향의 앞뒤 기울기 (아래 = 0, 앞 = +). skeleton.hip_flex와 같은 정의.</summary>
        public void HipFlex(out float left, out float right)
        {
            left = right = 0f;
            if (_hips == null || _thighL == null || _kneeL == null || _thighR == null || _kneeR == null) return;
            var rootInv = Quaternion.Inverse(transform.rotation);
            var d = rootInv * _hips.rotation * _hipsRestModelInv; // 골반 레스트 → 현재 (선수 틀)
            Vector3 down = d * Vector3.down, fwd = d * Vector3.forward;
            left = Flex(rootInv * (_kneeL.position - _thighL.position), down, fwd);
            right = Flex(rootInv * (_kneeR.position - _thighR.position), down, fwd);
        }

        static float Flex(Vector3 thigh, Vector3 down, Vector3 fwd) =>
            Mathf.Atan2(Vector3.Dot(thigh, fwd), Vector3.Dot(thigh, down)) * Mathf.Rad2Deg;

        /// <summary>오프셋(선수 레스트 틀 벡터, m)을 부모 레스트 틀로 바꿔 레스트 위치에 더한다 — 포즈에서는 부모 회전을 따라 돈다. 할당 없음.</summary>
        public void ApplyKitBones(ReadOnlySpan<RoadToPro.Core.Math.Vec3> offsets)
        {
            if (_kit6 == null) return;
            for (int i = 0; i < _kit6.Length && i < offsets.Length; i++)
            {
                var b = _kit6[i];
                if (b == null) continue;
                var o = new Vector3(offsets[i].X, offsets[i].Y, offsets[i].Z);
                b.localPosition = _kitRestPos[i] + _kitParentRestModelInv[i] * o;
            }
        }

        public Transform Bone(string name) => _bones.TryGetValue(name, out var t) ? t : null;

        Texture2D[] _skinSets;

        static Texture SkinSet(Texture2D[] sets, int i) => sets != null && sets.Length > 0 ? sets[Mathf.Clamp(i, 0, sets.Length - 1)] : null;

        static void SetBodyTextures(Material m, Texture skin, Texture normal, Texture parts)
        {
            m.SetTexture("_SkinTex", skin);
            m.SetTexture("_SkinNormal", normal);
            m.SetTexture("_PartsTex", parts);
        }

        /// <summary>062 FR-30~33·90·91 몸 구운 맵: 근육·손 디테일 노멀·AO, 피부 두께(R)·땀 마스크(G). 몸·클로즈업 머리 머티리얼 공통.</summary>
        static void SetBodyDetail(Material m, PlayerBodyAssets a)
        {
            SetIf(m, "_MuscleN", a.bodyDetailN);
            SetIf(m, "_MuscleAO", a.bodyDetailAO);
            SetIf(m, "_ThicknessTex", a.skinThickness);
            // 062 FR-66: 수염 영역 4채널 마스크(full·mustache·goatee·heavy). 088: 피부 텍스처 B 채널 대체 없음 — 마스크가 없으면 수염 없음
            SetIf(m, "_BeardMask", a.beardMask);
            SetIf(m, "_BrowMask", a.browMask);   // 088 FR-02
        }

        static void SetIf(Material m, string name, Texture t)
        {
            if (t != null) m.SetTexture(name, t);
        }

        // ---------------------------------------------------------------- 062 FR-85~89 품질 등급
        Material[] _matsNormal, _matsHigh;
        bool _topTier;
        float _sweat;

        /// <summary>LOD0 고정 + 고해상도 등급(QualityTierPolicy의 3번째 선수 등).</summary>
        public bool TopTierPinned => _topTier;

        /// <summary>최고 품질 등급(클로즈업 머리 또는 LOD0 고정) — 땀은 이때만 보인다 (FR-85). 표정은 LOD0·클로즈업 전부(FR-71, 전환 튐 방지).</summary>
        public bool HighQuality => _topTier || _closeupOn;

        /// <summary>
        /// LOD0 고정·고해상도 머티리얼 켜기/끄기 (FR-86). 끄면 다음 UpdateLod부터 히스테리시스로 내려간다(LOD 0에서 출발 — 튐 없음, FR-89).
        /// 할당 없음(머티리얼 배열은 Create에서).
        /// </summary>
        public void SetTopTier(bool on)
        {
            if (on == _topTier) return;
            _topTier = on;
            if (_lods[0] != null) _lods[0].sharedMaterials = on ? _matsHigh : _matsNormal;
            if (on) ForceLod(0);
            ApplyTierState();
        }

        /// <summary>땀 0~1 (FR-33). 선수 상태 — 셰이더 값은 최고 품질 등급일 때만 두 머티리얼에 같게 넣는다.</summary>
        public void SetSweat(float v)
        {
            _sweat = Mathf.Clamp01(v);
            ApplyTierState();
        }

        void ApplyTierState()
        {
            float sw = HighQuality ? _sweat : 0f;
            if (_body != null) _body.SetFloat("_Sweat", sw);
            if (_closeupMat != null) _closeupMat.SetFloat("_Sweat", sw);
        }

        // ---------------------------------------------------------------- 062 FR-70·71 표정
        /// <summary>표정 교차 섞기 상태 (선수 단위 — 클로즈업 머리와 LOD0에 같은 값). 반응을 받는 쪽이 Set(표정, 재생 시각, 섞기)을 부른다.</summary>
        public readonly ExpressionBlend ExpressionState = new ExpressionBlend();
        readonly float[] _expr = new float[ExpressionLogic.Keys.Length];

        /// <summary>
        /// 표정 키 가중치 0~1 (ExpressionLogic.Keys 순서)을 LOD0·클로즈업 머리 블렌드셰이프 1~4번에 넣는다(프레임 가중치 1로 구움).
        /// LOD1 이하는 표정 키가 없어 무표정(FR-71). 값이 같으면 건드리지 않는다. 할당 없음.
        /// </summary>
        public void SetExpression(ReadOnlySpan<float> w)
        {
            int n = Math.Min(w.Length, _expr.Length);
            bool changed = false;
            for (int k = 0; k < n; k++)
                if (_expr[k] != w[k])
                {
                    _expr[k] = w[k];
                    changed = true;
                }
            if (changed) PushExpression();
        }

        void PushExpression()
        {
            PushExpression(_lods[0]);
            PushExpression(_closeup);
        }

        void PushExpression(SkinnedMeshRenderer r)
        {
            if (r == null || r.sharedMesh == null || r.sharedMesh.blendShapeCount < 1 + _expr.Length) return;
            // FR-71: LOD0·클로즈업 머리 모두 같은 값 — 등급 전환(클로즈업 ↔ LOD0 고정 ↔ 일반 LOD0)에서 표정이 튀지 않는다(FR-89)
            for (int k = 0; k < _expr.Length; k++) r.SetBlendShapeWeight(1 + k, _expr[k]);
        }

        public bool CloseupActive => _closeupOn;
        public SkinnedMeshRenderer CloseupRenderer => _closeup;

        /// <summary>
        /// 클로즈업 머리 켜기/끄기 (FR-61). 켜면 몸 LOD의 머리를 HideHead로 접고 고해상도 머리(Head_Closeup + 헤어 LOD0)를 그린다.
        /// 할당 없음(메시는 ApplyAppearance 때 구워 둔다).
        /// </summary>
        public void SetCloseup(bool on)
        {
            if (_closeup == null || on == _closeupOn) return;
            _closeupOn = on;
            _closeup.enabled = on;
            float w = on ? 1f : 0f;
            foreach (var l in _lods)
                if (l != null && l.sharedMesh != null && l.sharedMesh.blendShapeCount > 0) l.SetBlendShapeWeight(0, w);
            ApplyTierState();   // 062: 등급이 바뀌면 표정·땀 표시도 같이
        }

        /// <summary>피부 정점(UV u &lt; 1 — 머리카락·눈·카드 제외) 최고 높이 = 정수리 (메시 좌표).</summary>
        static float SkinTop(Mesh m)
        {
            var v = m.vertices;
            var uv = new List<Vector2>(v.Length);
            m.GetUVs(0, uv);
            float top = 0f;
            for (int i = 0; i < v.Length && i < uv.Count; i++)
                if (uv[i].x < 1f && v[i].y > top) top = v[i].y;
            return top;
        }

        /// <summary>메시를 서브메시 하나로 합친 복사본 (그림자 전용).</summary>
        static Mesh SingleSubmesh(Mesh src)
        {
            var m = UnityEngine.Object.Instantiate(src);
            m.name = src.name + " (shadow)";
            var tris = new List<int>();
            for (int i = 0; i < m.subMeshCount; i++) tris.AddRange(m.GetTriangles(i));
            m.subMeshCount = 1;
            m.SetTriangles(tris, 0);
            return m;
        }

        /// <summary>LOD2 메시를 서브메시 하나로 합친 복사본으로 그림자 전용 스킨 렌더러를 만든다.</summary>
        static SkinnedMeshRenderer ShadowProxy(SkinnedMeshRenderer src)
        {
            var m = SingleSubmesh(src.sharedMesh);
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
            r.GetComponentInParent<PlayerRig>(true)?.TrackShadow(m);
            return r;
        }

        /// <summary>
        /// LOD 선택 (FR-13): 화면 높이 비율 = 키 / (2·거리·tan(세로 시야/2)). 히스테리시스로 경계 왕복 깜빡임 없이 한 단계만 그린다.
        /// 크로스페이드 대신 이 방식을 쓰는 이유: 전환 중 두 단계를 같이 그리면 드로우콜·삼각형이 겹쳐 예산(AC-16)을 넘는다.
        /// </summary>
        public int UpdateLod(Camera cam)
        {
            if (_topTier) return _lod;   // 062 FR-86: 등급 대상은 LOD0 고정
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

        void TrackShadow(Mesh m) => _shadowMesh = m;

        static void Release(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o); // EditMode 테스트·에디터 도구
        }

        /// <summary>체형·피부·헤어 (043 FR-01·02) — 얼굴은 중립(축 0, 수염 없음).</summary>
        public void ApplyAppearance(in PlayerAppearance a) => ApplyAppearance(a, null, null);

        /// <summary>
        /// 체형·피부·얼굴·수염·헤어 (043 FR-01·02, 056 FR-10·22·23·40). 로딩 단계에서 부른다 — 선수별 메시를 굽는다(LOD 3 + 클로즈업 머리).
        /// face·data가 null이면 중립 얼굴.
        /// </summary>
        public void ApplyAppearance(in PlayerAppearance a, FaceShape face, FaceData data)
        {
            Appearance = a;
            _face = face;
            _faceData = data;
            _baseScale = a.Scale;
            transform.localScale = new Vector3(_baseScale, _baseScale, _baseScale);
            var w = new Dictionary<string, float>
            {
                ["Thin"] = BodyShapeMap.Thin(a),
                ["Muscular"] = BodyShapeMap.Muscular(a),
                ["Heavy"] = BodyShapeMap.Heavy(a),
                ["Broad"] = BodyShapeMap.Broad(a),   // 062 FR-01
                ["Limb"] = BodyShapeMap.Limb(a),
            };
            // 062 FR-66: 수염은 외형(PlayerAppearance.Beard)에서
            int beard = a.Beard;
            if (face != null && data != null)
            {
                for (int i = 0; i < face.Axis.Length; i++)
                {
                    face.Key(data, i, out var key, out var v);
                    if (v > 0f) w[key] = v;
                }
                if (beard > 0 && beard < data.Beards.Count) w["Beard_" + data.Beards[beard]] = 1f;
                // 062 FR-60·61: 바탕 머리 2~3개 혼합 (천분율 → 0~1). 굽기는 키 이름 정렬 순서로 더해 결정론
                for (int k = 0; k < 3; k++)
                {
                    a.Base(k, out int head, out int pm);
                    if (pm > 0 && head >= 0 && head < data.BaseHeads.Count) w[data.BaseHeads[head]] = pm * 0.001f;
                }
            }
            foreach (var m in _baked)
                if (m != null) Release(m);
            _baked.Clear();
            string hair = PlayerLook.HairMeshes[a.HairStyle];
            for (int l = 0; l < 3; l++)
            {
                if (_lods[l] == null || _srcLods[l] == null) continue;
                Mesh hm = null;
                if (hair != null && _parts.TryGetValue($"{hair}_LOD{l}", out var hs)) hm = hs.sharedMesh;
                var baked = PlayerMeshBaker.Bake(_srcLods[l], hm, w, $"Player_LOD{l} (baked)");
                _baked.Add(baked);
                _lods[l].sharedMesh = baked;
                // localBounds는 루트 뼈 기준 — 임포트 값 유지(메시 좌표 bounds를 넣으면 머리가 컬링된다)
            }
            if (_closeup != null && _closeupSrc != null)
            {
                Mesh hm = null;
                if (hair != null && _parts.TryGetValue($"{hair}_LOD0", out var hs)) hm = hs.sharedMesh;
                var baked = PlayerMeshBaker.Bake(_closeupSrc, hm, w, "Head_Closeup (baked)");
                _baked.Add(baked);
                _closeup.sharedMesh = baked;
                // localBounds는 루트 뼈 기준 — 임포트 값 유지(메시 좌표 bounds를 넣으면 머리가 컬링된다)
            }
            // 062 FR-61: 바탕 머리 혼합이 정수리를 옮긴다(긴 머리형 최대 ≈ 1.3 cm) → 구운 LOD0 정수리 / 원본 정수리로 루트 스케일을 보정해
            // 키(043 FR-01 ±1 cm)를 입력값 그대로 둔다
            if (_lods[0] != null && _srcLods[0] != null)
            {
                float src = SkinTop(_srcLods[0]), baked = SkinTop(_lods[0].sharedMesh);
                if (src > 0f && baked > 0f)
                {
                    _baseScale = a.Scale * src / baked;
                    transform.localScale = new Vector3(_baseScale, _baseScale, _baseScale);
                }
            }
            if (_shadow != null)
            {
                var old = _shadowMesh;
                _shadowMesh = SingleSubmesh(_lods[2].sharedMesh);
                _shadow.sharedMesh = _shadowMesh;
                if (old != null) Release(old);
            }
            var tint = PlayerLook.SkinToneTint[a.SkinTone];   // 088 Q4: 톤 = MakeHuman 세트 + 틴트
            var skinTex = SkinSet(_skinSets, PlayerLook.SkinToneSet[a.SkinTone]);
            foreach (var m in new[] { _body, _closeupMat })
            {
                if (m == null) continue;
                if (skinTex != null) m.SetTexture("_SkinTex", skinTex);
                m.SetColor("_SkinColor", tint);
                m.SetVector("_BrowChannel", PlayerLook.BrowChannels[a.Eyebrow]);
                m.SetColor("_HairColor", PlayerLook.HairColors[a.HairColor]);
                // 062 FR-54: 위·끝 색 — 컬러 포인트가 없으면 같은 색(셰이더는 분기 없이 정점 마스크로 섞는다)
                m.SetColor("_HairColor2", a.ColorPoint > 0 ? PlayerLook.ColorPointColors[a.ColorPoint - 1] : PlayerLook.HairColors[a.HairColor]);
                m.SetColor("_EyeColor", PlayerLook.EyeColors[(a.SkinTone * 7 + a.Face) % PlayerLook.EyeColors.Length]);
                int bi = Mathf.Clamp(beard, 0, PlayerLook.BeardLevels.Length - 1);
                m.SetFloat("_BeardLevel", PlayerLook.BeardLevels[bi]);
                m.SetVector("_BeardChannel", PlayerLook.BeardChannels[bi]);
            }
            PushExpression();   // 새로 구운 메시에 현재 표정 유지
            if (_closeupOn)
            {
                _closeupOn = false;
                SetCloseup(true);
            }
        }

        // ---------------------------------------------------------------- 062 FR-27 정장
        readonly Mesh[] _suitSrc = new Mesh[3];
        Mesh[] _uniformSrc;
        Texture _suitMask;
        FaceShape _face;
        FaceData _faceData;
        bool _kitSet;
        KitColors _kitColors;
        Texture _kitMask, _kitText;
        int _kitCell;
        public PlayerOutfit Outfit { get; private set; }
        Texture _regionUniform;
        float? _weaveUniform, _detailUniform;
        Texture _aoUniform;

        /// <summary>
        /// 의상 바꾸기 (로딩 단계 — 몸 LOD를 다시 굽는다). 정장: 몸 LOD 원본 = Suit_LOD*, 키트 머티리얼 = 정장 마스크·PlayerLook.SuitColors[color]
        /// (셔츠 흰색·넥타이·구두), 번호·이름 없음. 유니폼: 원래 원본·마지막 ApplyKit 값으로 되돌린다(왕복 동일, plan 위험).
        /// 얼굴·헤어·체형은 그대로(같은 외형으로 다시 굽는다).
        /// </summary>
        public void ApplyOutfit(PlayerOutfit outfit, int color)
        {
            if (outfit == PlayerOutfit.Suit && (_suitSrc[0] == null || _suitMask == null))
                throw new InvalidOperationException("정장 메시(Suit_LOD*)·마스크(kit_mask_suit.png)가 없다 — 062 생성물 필요");
            _uniformSrc ??= new[] { _srcLods[0], _srcLods[1], _srcLods[2] };
            for (int l = 0; l < 3; l++) _srcLods[l] = outfit == PlayerOutfit.Suit ? _suitSrc[l] : _uniformSrc[l];
            Outfit = outfit;
            ApplyAppearance(Appearance, _face, _faceData);
            if (outfit == PlayerOutfit.Suit)
            {
                _kit.SetTexture("_MaskTex", _suitMask);
                _kit.SetColor("_Primary", PlayerLook.SuitColors[Mathf.Clamp(color, 0, PlayerLook.SuitColors.Length - 1)]);
                _kit.SetColor("_Secondary", PlayerLook.SuitTie);
                _kit.SetColor("_Accent", PlayerLook.SuitShirt);
                _kit.SetVector("_TextCell", Vector4.zero);
                // 062 T35 육안: 유니폼 영역 맵(밑단 광택)·짜임 타일(깃 골지)이 정장에 남는다 → 정장은 고른 무광 천
                // 구운 주름 노멀·AO도 유니폼 형태(셔츠 밑단·골지 깃)라 끈다
                _regionUniform ??= _kit.GetTexture("_RegionTex");
                _weaveUniform ??= _kit.GetFloat("_WeaveStrength");
                _detailUniform ??= _kit.GetFloat("_DetailStrength");
                _aoUniform ??= _kit.GetTexture("_DetailAO");
                _kit.SetTexture("_RegionTex", Texture2D.whiteTexture);
                _kit.SetFloat("_WeaveStrength", 0f);
                _kit.SetFloat("_DetailStrength", 0f);
                _kit.SetTexture("_DetailAO", Texture2D.whiteTexture);
            }
            else
            {
                if (_weaveUniform.HasValue)
                {
                    _kit.SetTexture("_RegionTex", _regionUniform);
                    _kit.SetFloat("_WeaveStrength", _weaveUniform.Value);
                    _kit.SetFloat("_DetailStrength", _detailUniform.Value);
                    _kit.SetTexture("_DetailAO", _aoUniform);
                }
                if (_kitSet) ApplyKit(_kitColors, _kitMask, _kitText, _kitCell);
            }
        }

        /// <summary>키트 색·패턴·번호/이름 칸 (FR-20·21). textAtlas가 null이면 번호·이름 없음.</summary>
        public void ApplyKit(in KitColors k, Texture mask, Texture textAtlas, int cell)
        {
            _kitSet = true;
            _kitColors = k;
            _kitMask = mask;
            _kitText = textAtlas;
            _kitCell = cell;
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
            foreach (var m in _baked)
                if (m != null) Destroy(m);
            if (_shadowMesh != null) Destroy(_shadowMesh);
            if (_closeupMat != null) Destroy(_closeupMat);
            if (_body != null) Destroy(_body);
            if (_kit != null) Destroy(_kit);
        }
    }
}
