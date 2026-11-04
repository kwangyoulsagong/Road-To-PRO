using System;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>선수 몸 텍스처·얼굴 데이터 묶음 (specs/056). 생성기 산출물 Generated/Textures/skin*.png·parts.png, player.face.json.</summary>
    [Serializable]
    public sealed class PlayerBodyAssets
    {
        public Texture2D skin, skinNormal, parts, closeupSkin, closeupNormal;
        public TextAsset faceJson;

        public const string Skin = "Textures/skin.png", SkinNormal = "Textures/skin_normal.png", Parts = "Textures/parts.png";
        public const string CloseupSkin = "Textures/skin_closeup.png", CloseupNormal = "Textures/skin_closeup_normal.png";
        public const string FaceJson = "player.face.json";
    }
}
