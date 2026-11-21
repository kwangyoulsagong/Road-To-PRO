using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 외형 팔레트·키트 아틀라스 배치 (specs/043 FR-02·21). 가상 색 목록 — 실존 인물·클럽 재현 아님.
    /// 팀 아틀라스: 1024², 칸 256×160 (4열 × 6행 = 24칸). 칸 안: 이름 240×48(위), 번호 112×112(아래 가운데).
    /// </summary>
    public static class PlayerLook
    {
        public static readonly Color[] SkinTones =
        {
            // 062: 0·1번이 061 조명에서 흰색으로 떠 낮춤 (T35 육안)
            new Color32(236, 200, 170, 255), new Color32(226, 186, 150, 255), new Color32(222, 172, 130, 255), new Color32(198, 145, 105, 255),
            new Color32(170, 118, 82, 255), new Color32(140, 92, 62, 255), new Color32(110, 70, 46, 255), new Color32(80, 52, 36, 255),
        };

        /// <summary>헤어 색 (AppearanceGenerator.HairColors 순서): 검정 · 진갈색 · 갈색 · 금발 · 적갈색 · 회색 + 062 FR-53 탈색 금발 · 백금.</summary>
        public static readonly Color[] HairColors =
        {
            new Color32(25, 20, 18, 255), new Color32(70, 45, 28, 255), new Color32(120, 80, 45, 255),
            new Color32(200, 160, 90, 255), new Color32(150, 60, 30, 255), new Color32(170, 170, 165, 255),
            new Color32(226, 196, 128, 255), new Color32(236, 232, 218, 255),
        };

        /// <summary>062 FR-54 컬러 포인트 가상 팔레트 (PlayerAppearance.ColorPoint 1~3): 백금 · 애시 · 붉은 계열.</summary>
        public static readonly Color[] ColorPointColors =
        {
            new Color32(232, 228, 212, 255), new Color32(168, 172, 176, 255), new Color32(168, 48, 40, 255),
        };

        /// <summary>헤어 스타일 0~34 → 메시 조각 이름 접두(생성기 hair.STYLE_SPEC, specs/057 FR-30 + 062 FR-52). 0~9는 056 순서, 9 = 삭발(형상 없음 → null), 30~34 = 062 신규.
        /// LOD는 "_LOD0..2". 순서는 AppearanceGenerator 가중치·계열 표와 같다.</summary>
        public static readonly string[] HairMeshes =
        {
            "Hair_Buzz", "Hair_Short", "Hair_Long", "Hair_Curly", "Hair_Bun", "Hair_Fade", "Hair_Afro", "Hair_Mohawk", "Hair_Dreads", null,
            "Hair_Crew", "Hair_SidePart", "Hair_Undercut", "Hair_SlickBack", "Hair_Quiff", "Hair_Curtains", "Hair_TexturedCrop",
            "Hair_BuzzLine", "Hair_HighTop", "Hair_Cornrows", "Hair_Twists", "Hair_TopKnot", "Hair_Ponytail", "Hair_MediumWave",
            "Hair_FrenchCrop", "Hair_IvyLeague", "Hair_Spiky", "Hair_CurlyTop", "Hair_BoxBraids", "Hair_HalfUp",
            "Hair_PartedTwoBlock", "Hair_CommaPerm", "Hair_VolumeCurlyLong", "Hair_StraightCornrows", "Hair_ZigzagCornrows",
        };

        /// <summary>홍채 색 (가상 팔레트, FR-23 [미정] — 043처럼 균등). 갈색 쪽이 많다.</summary>
        public static readonly Color[] EyeColors =
        {
            new Color32(60, 36, 20, 255), new Color32(84, 52, 28, 255), new Color32(46, 30, 18, 255), new Color32(110, 78, 40, 255),
            new Color32(70, 90, 60, 255), new Color32(70, 100, 130, 255),
        };

        /// <summary>수염 종류(face.json beards 순서: none·stubble·short·full + 062 mustache·goatee·heavy_stubble) → 셰이더 _BeardLevel (굵기).</summary>
        public static readonly float[] BeardLevels = { 0f, 0.45f, 0.75f, 1f, 0.7f, 0.7f, 0.6f };   // 062: 콧수염·염소수염 0.85 → 0.7 (평면 조각처럼 보임, T35 육안)

        /// <summary>062 FR-66: 수염 종류 → beard_mask0 영역 채널 선택 (R full · G mustache · B goatee · A heavy, face.py BEARD_REGION).</summary>
        public static readonly Vector4[] BeardChannels =
        {
            new Vector4(1, 0, 0, 0), new Vector4(1, 0, 0, 0), new Vector4(1, 0, 0, 0), new Vector4(1, 0, 0, 0),
            new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1),
        };

        /// <summary>062 FR-27 정장 천 중립 3색 [제안] (가상): 차콜 · 네이비 · 미드 그레이. 셔츠 흰색·넥타이 짙은 회색.</summary>
        public static readonly Color[] SuitColors =
        {
            new Color32(52, 54, 58, 255), new Color32(30, 38, 60, 255), new Color32(96, 98, 102, 255),
        };
        public static readonly Color SuitShirt = new Color32(236, 236, 232, 255);
        public static readonly Color SuitTie = new Color32(44, 44, 52, 255);

        public const int AtlasSize = 1024;
        public const int CellW = 256, CellH = 160, Cols = 4, Rows = 6;
        public const int Cells = Cols * Rows;
        public static readonly Vector4 NameSub = new Vector4(0.03125f, 0.7f, 0.9375f, 0.3f);
        public static readonly Vector4 NumberSub = new Vector4(0.28125f, 0f, 0.4375f, 0.7f);
        public static readonly Vector4 NameArea = new Vector4(0.60f, 0.865f, 0.90f, 0.925f);
        public static readonly Vector4 NumberArea = new Vector4(0.63f, 0.60f, 0.87f, 0.84f);
        public static readonly Vector4 FrontArea = new Vector4(0.29f, 0.80f, 0.37f, 0.88f);

        /// <summary>칸 i의 아틀라스 UV (u, v, w, h). 행 0 = 위.</summary>
        public static Vector4 CellUv(int i)
        {
            int cx = i % Cols, cy = i / Cols;
            float w = (float)CellW / AtlasSize, h = (float)CellH / AtlasSize;
            return new Vector4(cx * w, 1f - (cy + 1) * h, w, h);
        }

        /// <summary>칸 i의 픽셀 사각형(왼쪽 아래 원점, Texture2D 좌표).</summary>
        public static RectInt CellPixels(int i)
        {
            int cx = i % Cols, cy = i / Cols;
            return new RectInt(cx * CellW, AtlasSize - (cy + 1) * CellH, CellW, CellH);
        }

        public static Color ToColor(Rgb c) => new Color(c.R, c.G, c.B, 1f);

        /// <summary>글자 색: 주색 위에서 더 잘 보이는 흰색/검은색, 포인트색이 주색과 충분히 다르면 포인트색 (FR-21).</summary>
        public static Rgb TextColor(in KitColors k)
        {
            if (CieDe2000.DeltaE(k.Accent, k.Primary) >= 40) return k.Accent;
            var white = new Rgb(1, 1, 1);
            var black = new Rgb(0.07f, 0.07f, 0.07f);
            return CieDe2000.DeltaE(white, k.Primary) >= CieDe2000.DeltaE(black, k.Primary) ? white : black;
        }
    }
}
