using System;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>선수 몸 텍스처·얼굴 데이터 묶음 (specs/056). 생성기 산출물 Generated/Textures/skin*.png·parts.png, player.face.json.</summary>
    [Serializable]
    public sealed class PlayerBodyAssets
    {
        public Texture2D skin, skinNormal, parts, closeupSkin, closeupNormal;
        /// <summary>062 FR-30~33·90·91 구운 맵 (공유, 선수별 아님). null이면 셰이더 기본값(평탄·흰색)으로 그린다.</summary>
        public Texture2D bodyDetailN, bodyDetailAO, skinThickness, kitDetailN, kitDetailAO, kitWeave, kitRegion;
        /// <summary>062 FR-66 수염 영역 4채널 (beard_mask0.png). null이면 057 피부 텍스처 B 채널(full).</summary>
        public Texture2D beardMask;
        /// <summary>062 FR-27 정장 마스크 (kit_mask_suit.png, 키트 UV). null이면 ApplyOutfit(Suit) 불가.</summary>
        public Texture2D suitMask;
        public TextAsset faceJson;
        /// <summary>062 FR-21: player.layout.json ("kitBones" 보조 뼈 축·상자). 없으면 2차 움직임 없음.</summary>
        public TextAsset layoutJson;

        public const string Skin = "Textures/skin.png", SkinNormal = "Textures/skin_normal.png", Parts = "Textures/parts.png";
        public const string CloseupSkin = "Textures/skin_closeup.png", CloseupNormal = "Textures/skin_closeup_normal.png";
        public const string FaceJson = "player.face.json", LayoutJson = "player.layout.json";
        public const string BodyDetailN = "Textures/body_detail_n.png", BodyDetailAO = "Textures/body_detail_ao.png", SkinThickness = "Textures/skin_thickness.png";
        public const string KitDetailN = "Textures/kit_detail_n.png", KitDetailAO = "Textures/kit_detail_ao.png", KitWeave = "Textures/kit_weave.png",
            KitRegion = "Textures/kit_region.png";
        public const string BeardMask = "Textures/beard_mask0.png", SuitMask = "Textures/kit_mask_suit.png";
    }
}
