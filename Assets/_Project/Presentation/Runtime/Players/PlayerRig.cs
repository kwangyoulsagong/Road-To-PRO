using System.Collections.Generic;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
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
                else if (smr.name.StartsWith("Hair_"))
                {
                    rig._parts[smr.name] = smr;
                    smr.enabled = false; // 헤어 조각은 굽기 재료 — 직접 그리지 않는다
                }
            }
            rig._body = new Material(bodyShader) { name = "PlayerBody (instance)" };
            rig._kit = new Material(kitShader) { name = "PlayerKit (instance)" };
            SetBodyTextures(rig._body, bodyTex.skin, bodyTex.skinNormal, bodyTex.parts);
            if (rig._closeup != null)
            {
                rig._closeupMat = new Material(bodyShader) { name = "PlayerBody closeup (instance)" };
                SetBodyTextures(rig._closeupMat, bodyTex.closeupSkin, bodyTex.closeupNormal, bodyTex.parts);
                rig._closeup.sharedMaterials = new[] { rig._closeupMat, rig._closeupMat };
                rig._closeup.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rig._closeup.updateWhenOffscreen = false;
                rig._closeup.skinnedMotionVectors = false;
            }
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

        static void SetBodyTextures(Material m, Texture skin, Texture normal, Texture parts)
        {
            m.SetTexture("_SkinTex", skin);
            m.SetTexture("_SkinNormal", normal);
            m.SetTexture("_PartsTex", parts);
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
        }

        /// <summary>메시를 서브메시 하나로 합친 복사본 (그림자 전용).</summary>
        static Mesh SingleSubmesh(Mesh src)
        {
            var m = Object.Instantiate(src);
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

        static void Release(Object o)
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
            _baseScale = a.Scale;
            transform.localScale = new Vector3(_baseScale, _baseScale, _baseScale);
            var w = new Dictionary<string, float>
            {
                ["Thin"] = BodyShapeMap.Thin(a),
                ["Muscular"] = BodyShapeMap.Muscular(a),
                ["Heavy"] = BodyShapeMap.Heavy(a),
            };
            int beard = 0;
            if (face != null && data != null)
            {
                for (int i = 0; i < face.Axis.Length; i++)
                {
                    face.Key(data, i, out var key, out var v);
                    if (v > 0f) w[key] = v;
                }
                beard = face.Beard;
                if (beard > 0) w["Beard_" + data.Beards[beard]] = 1f;
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
            if (_shadow != null)
            {
                var old = _shadowMesh;
                _shadowMesh = SingleSubmesh(_lods[2].sharedMesh);
                _shadow.sharedMesh = _shadowMesh;
                if (old != null) Release(old);
            }
            var c = PlayerLook.SkinTones[a.SkinTone];
            foreach (var m in new[] { _body, _closeupMat })
            {
                if (m == null) continue;
                m.SetColor("_SkinColor", c);
                m.SetColor("_HairColor", PlayerLook.HairColors[a.HairColor]);
                m.SetColor("_EyeColor", PlayerLook.EyeColors[(a.SkinTone * 7 + a.Face) % PlayerLook.EyeColors.Length]);
                m.SetFloat("_BeardLevel", PlayerLook.BeardLevels[Mathf.Clamp(beard, 0, PlayerLook.BeardLevels.Length - 1)]);
            }
            if (_closeupOn)
            {
                _closeupOn = false;
                SetCloseup(true);
            }
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
            foreach (var m in _baked)
                if (m != null) Destroy(m);
            if (_shadowMesh != null) Destroy(_shadowMesh);
            if (_closeupMat != null) Destroy(_closeupMat);
            if (_body != null) Destroy(_body);
            if (_kit != null) Destroy(_kit);
        }
    }
}
