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
            }
        }

        void ShowProto(string name, int baseHead, int tone, float orbitDeg, bool raw = false)
        {
            int ent = ExprPlayers[0];
            Prepare062($"proto{name}{(orbitDeg == 0f ? "Front" : "Three")}{(raw ? "Raw" : "")}");
            var a = _looks[ent];
            int o1 = (baseHead + 1) % AppearanceGenerator.BaseHeads, o2 = (baseHead + 2) % AppearanceGenerator.BaseHeads;
            // 갈색 눈: PlayerRig 눈 색 = EyeColors[(톤 × 7 + Face) % 6] → 0(진갈색)이 되는 Face
            int faceIdx = ((-tone * 7) % PlayerLook.EyeColors.Length + PlayerLook.EyeColors.Length) % PlayerLook.EyeColors.Length;
            var pure = new PlayerAppearance(a.HeightCm, a.WeightKg, a.Thin, a.Muscular, a.Heavy, tone, faceIdx, 9, a.HairColor,
                a.Broad, a.Limb, 0, 0, baseHead, o1, o2, 1000, 0, 0, 1);
            _rigs[ent].ApplyAppearance(pure, new FaceShape(new float[_faces[ent].Axis.Length]), Face062);
            _over062.Add(ent);
            PoseStill(e => (e == ent, 0f, 0f, 0f), false, 0);
            var rig = _rigs[ent];
            rig.SetCloseup(true);
            EnterStudio(rig.transform);
            if (raw) KeyPost(true);
            float s = rig.transform.localScale.y;
            var eye = rig.Bone("Head").position + rig.transform.rotation * (EyeFromHead * s);
            var aim = eye - Vector3.up * ProtoAimDrop;
            // 시안 3/4: 카메라가 선수 오른쪽으로 35° 돈다
            float r = orbitDeg * Mathf.Deg2Rad;
            var dir = rig.transform.forward * Mathf.Cos(r) + rig.transform.right * Mathf.Sin(r);
            cam.fieldOfView = ProtoFov;
            cam.transform.position = aim + dir * ProtoDist - Vector3.up * ProtoCamDrop;
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
            _studio = false;
        }
    }
}
