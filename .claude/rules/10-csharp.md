---
paths:
  - "Assets/**/*.cs"
  - "Assets/**/*.asmdef"
---
# C# · Unity 공통

- 네임스페이스: `RoadToPro.<모듈>` (예: `RoadToPro.Core`, `RoadToPro.MatchEngine.Scenarios`). 폴더 구조와 맞춘다.
- 모듈마다 asmdef 하나: `RoadToPro.Core`, `RoadToPro.MatchEngine`, `RoadToPro.Career`, `RoadToPro.WorldGen`, `RoadToPro.Data`, `RoadToPro.Presentation`(+`RoadToPro.Presentation.Editor`), `RoadToPro.UI`, `RoadToPro.Verification`(+`RoadToPro.Verification.Editor`, 디바이스팜 드라이버), 테스트는 `RoadToPro.Tests.EditMode`, `RoadToPro.Tests.Performance`.
- 의존 방향 (화살표 = 참조, 역방향 금지):
  - `MatchEngine → Core`
  - `Career → MatchEngine, Core`
  - `WorldGen → Career, Core`
  - `Data → Core, MatchEngine, Career, WorldGen` (세이브·로더는 도메인을 알지만 도메인은 Data를 모른다. 로딩은 인터페이스로 주입)
  - `Presentation, UI → 위 전부`. 위 모듈들은 Presentation·UI를 참조하지 않는다.
  - `Presentation.Editor → 위 전부 + Presentation` (에디터 전용 임포트·에셋 생성 도구, specs/044). 런타임 모듈은 참조하지 않는다.
  - `Verification → 위 전부` (Presentation·UI와 같은 최상위 층). 다른 모듈은 Verification을 참조하지 않는다.
  - `Core`·`MatchEngine`·`Career`·`WorldGen`은 asmdef에 `noEngineReferences: true`.
- Unity 6 / C# 9 기준. `record`, 패턴 매칭 사용 가능. `init` 접근자는 Unity 버전에 따라 `IsExternalInit` 폴리필이 필요할 수 있다.
- `public` 필드 대신 `[SerializeField] private`. 도메인 모델은 MonoBehaviour가 아니다.
- 매 프레임 경로(`Update`, 시뮬 틱)에서 LINQ·람다 캡처·boxing·`string` 연결로 GC 할당을 만들지 않는다.
- 새 `.cs`를 만들면 Unity가 `.meta`를 만든다. 직접 `.meta`를 쓰지 않는다.
