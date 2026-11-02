---
paths:
  - "Assets/_Project/MatchEngine/**"
  - "Assets/_Project/Core/**"
  - "Assets/_Project/Career/**"
  - "Assets/_Project/WorldGen/**"
---
# 순수 C# 코어 · 결정론

이 모듈들은 Unity 없이(헤드리스 대량 시뮬, 서버, 테스트) 돌아야 하고, **같은 시드 + 같은 입력 → 비트 단위로 같은 결과**여야 한다. 리플레이·하이라이트·세이브 재현이 여기에 기대고 있다.

## 금지 (PostToolUse 훅이 검사한다)
- `using UnityEngine` / `UnityEditor`, `Mathf`, `Vector3`, `Time.*`, `Debug.Log` → 대신 `RoadToPro.Core.Math`, 로그는 주입된 `ILogger`.
- `System.Random`, `UnityEngine.Random`, `Guid.NewGuid()` → `RoadToPro.Core.Random`의 시드 RNG만. RNG는 인자로 넘기고 전역/static으로 두지 않는다.
- `DateTime.Now`, `DateTime.UtcNow`, `Stopwatch`, `Environment.TickCount` → 게임 시간은 시뮬 틱/게임 날짜로.
- `Dictionary`/`HashSet` **순회 결과에 의존하는 로직** → 순회가 결과에 영향을 주면 정렬하거나 `List`/`SortedDictionary` 사용.
- `Parallel`, `Task.Run`, 스레드 경쟁이 생기는 코드 → 경기 하나는 단일 스레드. 병렬은 "경기 여러 개"를 나눠 돌릴 때만.
- `float`/`double`의 순서 의존 누적(병렬 합산, `Sum()` 순서가 바뀌는 컬렉션) 금지.

## 원칙
- 경기 엔진: 공 주변 3~5명만 시나리오가 조종, 나머지는 팀 형태 레이어. 시나리오 실패 시 Utility 규칙 폴백(ML 아님). 상세는 `docs/scenarios.html`.
- 시뮬 상태는 직렬화 가능한 값 타입 위주. 이벤트 로그(누가·언제·무엇)를 남겨 하이라이트 재생과 디버깅에 쓴다.
- 밸런스 상수는 코드에 박지 않고 `Data/`의 JSON/YAML 또는 한 곳의 `Tuning` 클래스에 둔다.
- 새 로직에는 결정론 테스트(같은 시드 2회 실행 → 이벤트 로그 해시 동일)를 붙인다.
- 부동소수점 크로스플랫폼 결정론(고정소수점 여부)은 **아직 결정 안 됨**. 결정 전까지는 "같은 빌드·같은 플랫폼 재현"을 기준으로 한다.
