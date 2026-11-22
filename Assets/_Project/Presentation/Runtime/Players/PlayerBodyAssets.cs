using System;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>선수 몸 텍스처·얼굴 데이터 묶음 (specs/056 → 088: 피부 = MakeHuman CC0 diffuse 3세트, 눈썹·속눈썹 마스크).
    /// 생성기 산출물 Generated/Textures/skin_*.png·skin_normal.png·brow_mask.png·parts.png, player.face.json.</summary>
    [Serializable]
    public sealed class PlayerBodyAssets
    {
        /// <summary>088 FR-01: 피부 세트 (SkinSets 순서 light·asian·dark, 2K — LOD0·클로즈업 공유). 톤 표는 PlayerLook.SkinToneSet·SkinToneTint.</summary>
        public Texture2D[] skinSets;
        public Texture2D skinNormal, parts;
        /// <summary>088 FR-02: 눈썹 02·04·09(RGB)·속눈썹 선(A) 피부 UV 마스크. null이면 눈썹 없이 그린다.</summary>
        public Texture2D browMask;
        /// <summary>062 FR-30~33·90·91 구운 맵 (공유, 선수별 아님). null이면 셰이더 기본값(평탄·흰색)으로 그린다.</summary>
        public Texture2D bodyDetailN, bodyDetailAO, skinThickness, kitDetailN, kitDetailAO, kitWeave, kitRegion;
        /// <summary>062 FR-66 수염 영역 4채널 (beard_mask0.png). null이면 057 피부 텍스처 B 채널(full).</summary>
        public Texture2D beardMask;
        /// <summary>062 FR-27 정장 마스크 (kit_mask_suit.png, 키트 UV). null이면 ApplyOutfit(Suit) 불가.</summary>
        public Texture2D suitMask;
        public TextAsset faceJson;
        /// <summary>062 FR-21: player.layout.json ("kitBones" 보조 뼈 축·상자). 없으면 2차 움직임 없음.</summary>
        public TextAsset layoutJson;

        public static readonly string[] SkinSets = { "Textures/skin_light.png", "Textures/skin_asian.png", "Textures/skin_dark.png" };
        public const string SkinNormal = "Textures/skin_normal.png", Parts = "Textures/parts.png", BrowMask = "Textures/brow_mask.png";
        public const string FaceJson = "player.face.json", LayoutJson = "player.layout.json";
        public const string BodyDetailN = "Textures/body_detail_n.png", BodyDetailAO = "Textures/body_detail_ao.png", SkinThickness = "Textures/skin_thickness.png";
        public const string KitDetailN = "Textures/kit_detail_n.png", KitDetailAO = "Textures/kit_detail_ao.png", KitWeave = "Textures/kit_weave.png",
            KitRegion = "Textures/kit_region.png";
        public const string BeardMask = "Textures/beard_mask0.png", SuitMask = "Textures/kit_mask_suit.png";
    }
}
