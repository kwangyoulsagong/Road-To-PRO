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
            new Color32(255, 220, 190, 255), new Color32(240, 198, 160, 255), new Color32(222, 172, 130, 255), new Color32(198, 145, 105, 255),
            new Color32(170, 118, 82, 255), new Color32(140, 92, 62, 255), new Color32(110, 70, 46, 255), new Color32(80, 52, 36, 255),
        };

        public static readonly Color[] HairColors =
        {
            new Color32(25, 20, 18, 255), new Color32(70, 45, 28, 255), new Color32(120, 80, 45, 255),
            new Color32(200, 160, 90, 255), new Color32(150, 60, 30, 255), new Color32(170, 170, 165, 255),
        };

        /// <summary>헤어 스타일 0~9 → 메시 조각 이름 접두(생성기 hair.STYLES, 056). 9 = 삭발(형상 없음 → null). LOD는 "_LOD0..2".</summary>
        public static readonly string[] HairMeshes =
        {
            "Hair_Buzz", "Hair_Short", "Hair_Long", "Hair_Curly", "Hair_Bun", "Hair_Fade", "Hair_Afro", "Hair_Mohawk", "Hair_Dreads", null,
        };

        /// <summary>홍채 색 (가상 팔레트, FR-23 [미정] — 043처럼 균등). 갈색 쪽이 많다.</summary>
        public static readonly Color[] EyeColors =
        {
            new Color32(60, 36, 20, 255), new Color32(84, 52, 28, 255), new Color32(46, 30, 18, 255), new Color32(110, 78, 40, 255),
            new Color32(70, 90, 60, 255), new Color32(70, 100, 130, 255),
        };

        /// <summary>수염 종류(face.json beards 순서: none·stubble·short·full) → 셰이더 _BeardLevel.</summary>
        public static readonly float[] BeardLevels = { 0f, 0.45f, 0.75f, 1f };

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
