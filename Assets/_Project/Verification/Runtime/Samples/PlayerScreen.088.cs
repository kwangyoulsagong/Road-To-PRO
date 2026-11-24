using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// specs/088 T30c (AC-13 e2e/088-prototype-compare): 시안(Cycles, Art/Blender/scripts/player/groom/compare.py setup + MPFB 사람)과
    /// 같은 조건의 얼굴 캡처 — 시안 인종 짝 바탕 머리 단독(얼굴 축 0), 갈색 눈, 눈썹 04, 삭발·수염 없음, 클로즈업 머리.
    /// 카메라·조명·배경을 시안 렌더에 맞추고, 다른 화면으로 가면 원래 경기장 조명으로 되돌린다.
    /// </summary>
    public sealed partial class PlayerScreen
    {
        /// <summary>(화면 이름, 바탕 머리, 피부톤) — 시안 caucasian ↔ Base_5(유럽), asian ↔ Base_0(동아시아), african ↔ Base_4(서·중앙아프리카).
        /// 톤은 세트 원본(틴트 1)에 가장 가까운 것: light 1 (1.00), asian 3 (1.03), dark 7 (0.92).</summary>
        static readonly (string name, int baseHead, int tone)[] ProtoFaces = { ("Europe", 5, 1), ("Asia", 0, 3), ("Africa", 4, 7) };

        // 시안 compare.setup: 85 mm 렌즈(세로 센서 36 mm → 세로 화각 23.9°), 눈 중심 − 3.5 cm를 0.62 m에서, 카메라는 겨냥점 − 2 cm 높이
        const float ProtoFov = 23.91f, ProtoDist = 0.62f, ProtoAimDrop = 0.035f, ProtoCamDrop = 0.02f;
        // 눈 중심 = Head 뼈 + (0, 3.94 cm, 8.22 cm) (기준 메시, face.eye_frame − skeleton.HEAD["Head"])
        static readonly Vector3 EyeFromHead = new Vector3(0f, 0.0394f, 0.0822f);
        // 시안 키 태양(회전 (55°, 0, 30°), 세기 4) — 빛이 오는 방향 = 선수 왼쪽 0.41 · 앞 0.709 · 위 0.574.
        // Cycles 복사휘도 환산: 태양 E/π = 1.27, 하늘 (0.55, 0.65, 0.85) × 0.7 균일 → 셰이더(램버트 π 없음) 값 그대로.
        // 뒤쪽 림 태양(세기 3)은 셰이더가 주 광원 하나만 받아 뺀다.
        const float ProtoSun = 1.27f;
        static readonly Color ProtoAmbient = new Color(0.385f, 0.455f, 0.595f);
        static readonly Color ProtoBackground = new Color32(165, 170, 185, 255);   // 시안 렌더 배경 픽셀(sRGB)

        bool _studio;
        static bool StudioCinematicShadow = false;   // 진단: 연출 모드 그림자 거리 배율 적용 여부
        float _shadowDistBefore = -1f;
        Light _studioSun;
        Quaternion _sunRot;
        float _sunIntensity, _fovBefore;
        Color _sunColor, _ambSky, _ambEq, _ambGround, _bgBefore;
        UnityEngine.Rendering.AmbientMode _ambMode;
        CameraClearFlags _clearBefore;

        void Register088()
        {
            foreach (var p in ProtoFaces)
            {
                var face = p;
                FarmContext.Screens[$"proto{face.name}Front"] = () => ShowProto(face.name, face.baseHead, face.tone, 0f);
                FarmContext.Screens[$"proto{face.name}Three"] = () => ShowProto(face.name, face.baseHead, face.tone, 35f);
                // 후처리(게임 룩: 톤매핑·노출·대비) 끈 판 — 시안 Blender "Standard" 렌더와 셰이딩만 비교
                FarmContext.Screens[$"proto{face.name}FrontRaw"] = () => ShowProto(face.name, face.baseHead, face.tone, 0f, true);
                // 면도 자국(수염 stubble, 062 BeardLevels 1) — 남성 얼굴 단서 비교 (T30d)
                FarmContext.Screens[$"proto{face.name}FrontStubble"] = () => ShowProto(face.name, face.baseHead, face.tone, 0f, false, 1);
                // 진단: 근육 노멀(_MuscleDef 0) 끈 판 — 목 감량 면 음영 원인 확인 (T30h)
                FarmContext.Screens[$"proto{face.name}FrontNoMuscle"] = () => { ShowProto(face.name, face.baseHead, face.tone, 0f); SetBodyFloat(ExprPlayers[0], "_MuscleDef", 0f); };
                FarmContext.Screens[$"proto{face.name}FrontNoAO"] = () => { ShowProto(face.name, face.baseHead, face.tone, 0f); SetBodyTex(ExprPlayers[0], "_MuscleAO", Texture2D.whiteTexture); };
                FarmContext.Screens[$"proto{face.name}FrontFlat"] = () => { ShowProto(face.name, face.baseHead, face.tone, 0f); SetBodyFloat(ExprPlayers[0], "_MuscleDef", 0f); SetBodyFloat(ExprPlayers[0], "_NormalStrength", 0f); SetBodyTex(ExprPlayers[0], "_MuscleAO", Texture2D.whiteTexture); };
            }
            // 088 승인 헤어 4종(포마드 14·가르마 15·시스루 16·가일컷 30) — 같은 얼굴(유럽 바탕 머리)·시안 조명, 정면·3/4
            foreach (var hs in new[] { (14, "Pomade"), (15, "Garma"), (16, "SeeThrough"), (30, "Gail") })
            {
                var st = hs;
                FarmContext.Screens[$"hair{st.Item2}Front"] = () => ShowApprovedHair(st.Item2, st.Item1, 0f);
                FarmContext.Screens[$"hair{st.Item2}Three"] = () => ShowApprovedHair(st.Item2, st.Item1, 35f);
                // 진단 (T30h): 클로즈업 그림자 끔 / 대리 그림자 머리 안 접음
                FarmContext.Screens[$"hair{st.Item2}FrontNoCloseupShadow"] = () => { ShowApprovedHair(st.Item2, st.Item1, 0f); _rigs[ExprPlayers[0]].CloseupRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; };
                FarmContext.Screens[$"hair{st.Item2}FrontProxyHead"] = () => { ShowApprovedHair(st.Item2, st.Item1, 0f); var px = _rigs[ExprPlayers[0]].ShadowProxyRenderer; if (px != null) px.SetBlendShapeWeight(0, 0f); };
            }
            // 088 T30f (AC-16 e2e/088-face-variety): 지역 7 × 인상 유형 3, 화면 하나에 한 명
            for (int r = 0; r < AppearanceGenerator.Regions; r++)
                for (int t = 0; t < 3; t++)
                {
                    int region = r;
                    var type = (FaceImpression)t;
                    FarmContext.Screens[$"variety{r + 1}{type}"] = () => ShowVariety(region, type);
                }
        }

        void ShowApprovedHair(string name, int style, float orbitDeg)
        {
            int ent = ExprPlayers[0];
            Prepare062($"hair{name}{(orbitDeg == 0f ? "Front" : "Three")}");
            var a = _looks[ent];
            var look = new PlayerAppearance(a.HeightCm, a.WeightKg, a.Thin, a.Muscular, a.Heavy, 1, 0, style, 1,
                a.Broad, a.Limb, 0, 0, 5, 6, 7, 1000, 0, 0, 1);
            _rigs[ent].ApplyAppearance(look, new FaceShape(new float[_faces[ent].Axis.Length]), Face062);
            _over062.Add(ent);
            PoseStill(e => (e == ent, 0f, 0f, 0f), false, 0);
            var rig = _rigs[ent];
            rig.SetCloseup(true);
            EnterStudio(rig.transform);
            FrameProto(rig, orbitDeg, 0.75f);
        }

        /// <summary>지역 r에서 주 바탕이 그 지역이고 인상 유형이 type인 첫 선수 id(20000부터)를 생성값 그대로(혼합·피부톤·헤어) — 수염만 뺀다.
        /// 시안 동일 조건 조명·카메라(헤어가 들어오게 0.75 m).</summary>
        void ShowVariety(int region, FaceImpression type)
        {
            int ent = ExprPlayers[0];
            Prepare062($"variety{region + 1}{type}");
            int id = 20_000;
            PlayerAppearance a = default;
            for (; id < 60_000; id++)
            {
                if (FaceGenerator.ImpressionOf(id) != type) continue;
                a = AppearanceGenerator.Generate(new BodyInput(id));
                if (AppearanceGenerator.RegionOf(a.BaseA) == region) break;
            }
            var noBeard = new PlayerAppearance(a.HeightCm, a.WeightKg, a.Thin, a.Muscular, a.Heavy, a.SkinTone, a.Face, a.HairStyle, a.HairColor,
                a.Broad, a.Limb, a.ColorPoint, 0, a.BaseA, a.BaseB, a.BaseC, a.WeightA, a.WeightB, a.WeightC, a.Eyebrow);
            _rigs[ent].ApplyAppearance(noBeard, FaceGenerator.Generate(Face062, id), Face062);
            Debug.Log($"[088] variety{region + 1}{type} id {id} hair {PlayerLook.HairMeshes[a.HairStyle]} {noBeard}");
            _over062.Add(ent);
            PoseStill(e => (e == ent, 0f, 0f, 0f), false, 0);
            var rig = _rigs[ent];
            rig.SetCloseup(true);
            EnterStudio(rig.transform);
            FrameProto(rig, 0f, 0.75f);
        }

        void ShowProto(string name, int baseHead, int tone, float orbitDeg, bool raw = false, int beard = 0)
        {
            int ent = ExprPlayers[0];
            Prepare062($"proto{name}{(orbitDeg == 0f ? "Front" : "Three")}{(raw ? "Raw" : "")}{(beard > 0 ? "Stubble" : "")}");
            var a = _looks[ent];
            int o1 = (baseHead + 1) % AppearanceGenerator.BaseHeads, o2 = (baseHead + 2) % AppearanceGenerator.BaseHeads;
            // 갈색 눈: PlayerRig 눈 색 = EyeColors[(톤 × 7 + Face) % 6] → 0(진갈색)이 되는 Face
            int faceIdx = ((-tone * 7) % PlayerLook.EyeColors.Length + PlayerLook.EyeColors.Length) % PlayerLook.EyeColors.Length;
            var pure = new PlayerAppearance(a.HeightCm, a.WeightKg, a.Thin, a.Muscular, a.Heavy, tone, faceIdx, 9, a.HairColor,
                a.Broad, a.Limb, 0, beard, baseHead, o1, o2, 1000, 0, 0, 1);
            _rigs[ent].ApplyAppearance(pure, new FaceShape(new float[_faces[ent].Axis.Length]), Face062);
            _over062.Add(ent);
            PoseStill(e => (e == ent, 0f, 0f, 0f), false, 0);
            var rig = _rigs[ent];
            rig.SetCloseup(true);
            EnterStudio(rig.transform);
            if (raw) KeyPost(true);
            FrameProto(rig, orbitDeg, ProtoDist);
        }

        /// <summary>진단용: 선수의 모든 PlayerBody 머티리얼(몸·클로즈업)에 값을 넣는다. 다음 ApplyAppearance에서 원래 값으로 돌아간다.</summary>
        void SetBodyFloat(int ent, string prop, float v)
        {
            foreach (var r in _rigs[ent].GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name == "RoadToPro/PlayerBody") m.SetFloat(prop, v);
        }

        void SetBodyTex(int ent, string prop, Texture t)
        {
            foreach (var r in _rigs[ent].GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name == "RoadToPro/PlayerBody") m.SetTexture(prop, t);
        }

        void FrameProto(PlayerRig rig, float orbitDeg, float dist)
        {
            float s = rig.transform.localScale.y;
            var eye = rig.Bone("Head").position + rig.transform.rotation * (EyeFromHead * s);
            var aim = eye - Vector3.up * ProtoAimDrop;
            // 시안 3/4: 카메라가 선수 오른쪽으로 35° 돈다
            float r = orbitDeg * Mathf.Deg2Rad;
            var dir = rig.transform.forward * Mathf.Cos(r) + rig.transform.right * Mathf.Sin(r);
            cam.fieldOfView = ProtoFov;
            cam.transform.position = aim + dir * dist - Vector3.up * ProtoCamDrop;
            cam.transform.rotation = Quaternion.LookRotation(aim - cam.transform.position, Vector3.up);
        }

        void EnterStudio(Transform player)
        {
            if (!_studio)
            {
                _studioSun = RenderSettings.sun;
                if (_studioSun == null)
                    foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional) { _studioSun = l; break; }
                if (_studioSun != null)
                {
                    _sunRot = _studioSun.transform.rotation;
                    _sunIntensity = _studioSun.intensity;
                    _sunColor = _studioSun.color;
                }
                _ambMode = RenderSettings.ambientMode;
                _ambSky = RenderSettings.ambientSkyColor;
                _ambEq = RenderSettings.ambientEquatorColor;
                _ambGround = RenderSettings.ambientGroundColor;
                _clearBefore = cam.clearFlags;
                _bgBefore = cam.backgroundColor;
                _fovBefore = cam.fieldOfView;
                _studio = true;
            }
            var from = -player.right * 0.41f + player.forward * 0.709f + Vector3.up * 0.574f;
            if (_studioSun != null)
            {
                _studioSun.transform.rotation = Quaternion.LookRotation(-from.normalized, Vector3.up);
                _studioSun.intensity = ProtoSun;
                _studioSun.color = Color.white;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ProtoAmbient;
            RenderSettings.ambientEquatorColor = ProtoAmbient;
            RenderSettings.ambientGroundColor = ProtoAmbient;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ProtoBackground;
            pitch.enabled = false;
            // 실제 클로즈업과 같게 연출 모드(061 CinematicModeController)의 그림자 거리 배율을 쓴다
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                if (_shadowDistBefore < 0f) _shadowDistBefore = urp.shadowDistance;
                urp.shadowDistance = _shadowDistBefore * (StudioCinematicShadow ? RoadToPro.Presentation.Rendering.CinematicModeController.ShadowDistanceScale : 1f);
            }
        }

        /// <summary>시안 조명에서 나올 때 경기장 조명·카메라를 되돌린다 (PrepareStill·ShowRow 처음에 부른다).</summary>
        void LeaveStudio()
        {
            if (!_studio) return;
            if (_studioSun != null)
            {
                _studioSun.transform.rotation = _sunRot;
                _studioSun.intensity = _sunIntensity;
                _studioSun.color = _sunColor;
            }
            RenderSettings.ambientMode = _ambMode;
            RenderSettings.ambientSkyColor = _ambSky;
            RenderSettings.ambientEquatorColor = _ambEq;
            RenderSettings.ambientGroundColor = _ambGround;
            cam.clearFlags = _clearBefore;
            cam.backgroundColor = _bgBefore;
            cam.fieldOfView = _fovBefore;
            pitch.enabled = true;
            if (_shadowDistBefore >= 0f && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.shadowDistance = _shadowDistBefore;
            _shadowDistBefore = -1f;
            _studio = false;
        }
    }
}
