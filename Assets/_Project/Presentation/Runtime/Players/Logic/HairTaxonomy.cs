namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>헤어 계열 (specs/062 FR-51 분류표 "계열" 열, Art/Blender/scripts/player/hairstyles.py GROUP과 같다 (088: 형상은 그루밍 v2)).
    /// 057 분포 계열(<see cref="HairFamily"/> 짧은·중간·긴)과 별개 — 이쪽은 원거리 실루엣 계열(FR-44).</summary>
    public enum HairGroup : byte { Bald, Short, Fade, Medium, Curly, Braid, Long }

    /// <summary>분류표 유형이 요구하는 색 조합 (FR-51 "색 변형" 행).</summary>
    public enum HairColorReq : byte
    {
        /// <summary>색 조건 없음 (스타일만으로 유형).</summary>
        Any,
        /// <summary>탈색 색(탈색 금발·백금)일 때 이 유형.</summary>
        Bleached,
        /// <summary>컬러 포인트가 있을 때 이 유형.</summary>
        ColorPoint,
        /// <summary>탈색 색 또는 컬러 포인트 (탈색·염색 브레이드).</summary>
        BleachedOrColorPoint,
    }

    /// <summary>분류표 한 행: 일반 명칭(실존 인물 이름 아님, FR-50), ★ 사용자 지정 필수 여부, 해당 스타일 인덱스, 색 조건.</summary>
    public readonly struct HairType
    {
        public readonly string Name;
        public readonly bool Star;
        public readonly int[] Styles;
        public readonly HairColorReq Color;

        public HairType(string name, bool star, int[] styles, HairColorReq color = HairColorReq.Any)
        {
            Name = name;
            Star = star;
            Styles = styles;
            Color = color;
        }
    }

    /// <summary>
    /// 헤어 분류표 데이터 (specs/062 FR-51). 공개 자료는 유형 이름·대략 빈도 참고로만(정량 자료 없음, T02) — 빈도는
    /// <see cref="AppearanceGenerator"/> 가중치 표. "멀릿"은 이번에 만들지 않아(Q3) 표에서 뺐다.
    /// </summary>
    public static class HairTaxonomy
    {
        public static readonly HairType[] Types =
        {
            new HairType("민머리·삭발", false, new[] { 9 }),
            new HairType("버즈컷", false, new[] { 0, 17 }),
            new HairType("짧은 머리 (기본 컷)", false, new[] { 1 }),   // FR-51 표에서 빠진 057 Hair_Short — 매핑용 일반 행 (T24)
            new HairType("짧은 스포츠 컷", true, new[] { 10 }),
            new HairType("크롭 / 짧은 텍스처드 크롭", true, new[] { 16, 24 }),
            new HairType("페이드·언더컷", false, new[] { 5, 12 }),
            new HairType("가르마 투블록", true, new[] { 30 }),
            new HairType("사이드 파트 / 아이비리그", false, new[] { 11, 25 }),
            new HairType("슬릭백 / 쿠이프", false, new[] { 13, 14 }),
            new HairType("쉼표머리 펌", true, new[] { 31 }),
            new HairType("커튼 / 미디엄 웨이브", false, new[] { 15, 23 }),
            new HairType("스파이크", false, new[] { 26 }),
            new HairType("짧은 곱슬 탑 + 페이드", true, new[] { 27 }),
            new HairType("짧은 탈색 곱슬", true, new[] { 27 }, HairColorReq.Bleached),
            new HairType("곱슬 / 트위스트", false, new[] { 3, 20 }),
            new HairType("아프로 / 하이톱", false, new[] { 6, 18 }),
            new HairType("볼륨 큰 곱슬 장발", true, new[] { 32 }),
            new HairType("브레이드·콘로우 변형", true, new[] { 19, 28, 33, 34 }),
            new HairType("탈색·염색 브레이드 장발", true, new[] { 28 }, HairColorReq.BleachedOrColorPoint),
            new HairType("드레드", false, new[] { 8 }),
            new HairType("탈색 투블록·모히칸", true, new[] { 7, 12 }, HairColorReq.Bleached),
            new HairType("맨번·탑노트 / 포니테일 / 하프 업", false, new[] { 4, 21, 22, 29 }),
            new HairType("장발", false, new[] { 2 }),
        };

        // 스타일 0~34 → 계열 (hair.py STYLE_SPEC group 순서 그대로)
        static readonly HairGroup[] Groups =
        {
            HairGroup.Short, HairGroup.Short, HairGroup.Long, HairGroup.Curly, HairGroup.Braid, HairGroup.Fade, HairGroup.Curly,
            HairGroup.Fade, HairGroup.Braid, HairGroup.Bald, HairGroup.Short, HairGroup.Short, HairGroup.Fade, HairGroup.Medium,
            HairGroup.Medium, HairGroup.Medium, HairGroup.Short, HairGroup.Short, HairGroup.Curly, HairGroup.Braid, HairGroup.Curly,
            HairGroup.Braid, HairGroup.Braid, HairGroup.Medium, HairGroup.Short, HairGroup.Short, HairGroup.Short, HairGroup.Curly,
            HairGroup.Braid, HairGroup.Long, HairGroup.Fade, HairGroup.Medium, HairGroup.Long, HairGroup.Braid, HairGroup.Braid,
        };

        public static HairGroup GroupOf(int style) => Groups[style];
    }
}
