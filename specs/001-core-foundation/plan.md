# 001 — 코어 기반 · 플랜

상태: 완료
스펙: ./spec.md

## 접근
도메인 모듈 7개(asmdef)를 만들고, 그중 `RoadToPro.Core` 하나에 결정론 도구 전부(수학·좌표·RNG·시계·날짜·해시·로거·튜닝·정렬 순서)를 둔다. 다른 도메인 모듈은 이번에는 빈 어셈블리(마커 클래스 하나)로만 만들어 참조 그래프를 고정한다. 수학은 `float` 일관 + 초월함수 래퍼 `DMath` 한 곳(내부 `System.MathF`)으로, 나중에 고정소수점으로 바꿀 때 이 파일만 바뀌게 한다. RNG는 xoshiro256\*\*(상태 32바이트, 빠르고 상태 저장이 쉬움) + SplitMix64로 스트림을 파생한다 — PCG32도 고려했지만 64비트 출력·스트림 파생이 xoshiro+SplitMix 쪽이 단순하다. 시뮬 시간은 `long` 결정 틱이 유일한 기준이고, 경기 시각 표기는 기간(전·후반·연장)별 추가시간을 아는 타임라인이 틱↔시각을 일대일로 바꾼다. 해시는 FNV-1a 64(바이트열, 리틀엔디언 고정)로, 90분 로그 3만 이벤트(≈1.2 MB)도 예산(10 ms) 안이다. 코어는 Unity 없이 테스트되므로 JSON 읽기도 코어 안의 최소 파서로 한다 (Unity `JsonUtility`는 엔진 참조).

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.Core | Assets/_Project/Core/Runtime/RoadToPro.Core.asmdef | `noEngineReferences: true`, 참조 없음 |
| RoadToPro.Core | Core/Runtime/Math/DMath.cs | 초월함수 래퍼(Sqrt·Sin·Cos·Atan2·Exp·Log·Pow) + Abs·Min·Max·Floor. 유일한 `MathF`/`Math` 호출 지점 (FR-11, FR-14) |
| RoadToPro.Core | Core/Runtime/Math/Vec2.cs, Vec3.cs | 값 타입 벡터. 0 벡터 정규화 → 0 (FR-10) |
| RoadToPro.Core | Core/Runtime/Math/Angle.cs | 정규화 (−π, π], 차이, 최단 보간 (FR-12) |
| RoadToPro.Core | Core/Runtime/Math/Interp.cs | Lerp·InverseLerp·Clamp·Smoothstep·Sigmoid (FR-12) |
| RoadToPro.Core | Core/Runtime/Math/Geometry.cs | `SweptSegmentCircle(a, b, center, r, out t)` (FR-13) |
| RoadToPro.Core | Core/Runtime/Math/FloatGuard.cs | `GuardMode {Strict, Sanitize}`, `NumericException`. 기본 모드 = `#if DEBUG` Strict, 아니면 Sanitize (FR-15) |
| RoadToPro.Core | Core/Runtime/Pitch/PitchSpec.cs, PitchCoords.cs | 피치 크기(기본 105×68), 정규화↔미터, 팀 관점 변환 `ToPerspective(p, side, halfIndex)` 한 함수 (FR-20~22) |
| RoadToPro.Core | Core/Runtime/Random/SplitMix64.cs | 시드 혼합·스트림 파생 |
| RoadToPro.Core | Core/Runtime/Random/SeedRng.cs | xoshiro256\*\* + 분포(균등 실수·편향 없는 정수·정규·로그정규·베르누이·가중·softmax·셔플) (FR-32) |
| RoadToPro.Core | Core/Runtime/Random/RngState.cs | 상태 값 타입(4×ulong, 형식 버전 1), 바이트 직렬화 (FR-33) |
| RoadToPro.Core | Core/Runtime/Random/RngStreamId.cs, RngStreams.cs | 고정 번호 스트림 id(물리 노이즈·선수 결정·심판·부상·시나리오·하이라이트·컨디션·커리어 계열), `RngStreams.Create(seed, stream, subId)` (FR-30, 31, 35) |
| RoadToPro.Core | Core/Runtime/Time/SimClock.cs | 상수: 결정 10Hz, 서브스텝 10회, dt 0.1/0.01 s. 틱→초는 정수 연산 (FR-40) |
| RoadToPro.Core | Core/Runtime/Time/MatchPeriod.cs, MatchTimeline.cs, MatchTime.cs | 기간별 시작 틱·추가시간, 틱↔(기간, 기간 내 틱) 일대일, 표기·파싱 `45+2:30` (FR-41, 42) |
| RoadToPro.Core | Core/Runtime/Calendar/GameDate.cs | 일 번호(0001-01-01 기준 int) 값 타입. 연월일·요일·ISO 주차·더하기·차이·윤년·`AddYears`(2/29→3/1)·나이 (FR-50, 51) |
| RoadToPro.Core | Core/Runtime/Hashing/StableHasher.cs | FNV-1a 64 struct. `Add(byte/int/long/ulong/float/double/bool/string(UTF-8)/ReadOnlySpan<byte>)`, 실수 정규화 (FR-60, 62) |
| RoadToPro.Core | Core/Runtime/Hashing/Checkpoint.cs, CheckpointLog.cs | 10초(100 결정 틱) 간격 `(index, tick, eventHash, stateHash)`, `FindFirstDivergence(a, b)` (FR-61) |
| RoadToPro.Core | Core/Runtime/Logging/ILogger.cs, LogLevel.cs, LoggerExtensions.cs, NullLogger.cs, MemoryLogger.cs | 주입형 로거. 제네릭 오버로드 `Debug<T1,…>(fmt, a…)`는 `IsEnabled` 확인 후에만 문자열 생성 (FR-70, 71) |
| RoadToPro.Core | Core/Runtime/Tuning/TuningDefinition.cs, SourceTag.cs, TuningRegistry.cs, TuningBundle.cs, TuningLoader.cs, TuningLoadException.cs | 정의(id·기본값·범위·단위·출처 태그·원문 위치) + 값 묶음 로드·검증·묶음 해시 (FR-80~82) |
| RoadToPro.Core | Core/Runtime/Serialization/JsonReader.cs | 튜닝용 최소 JSON 파서 (객체·배열·문자열·숫자(InvariantCulture)·bool·null, 줄·열 오류 위치) |
| RoadToPro.Core | Core/Runtime/Ordering/DeterministicOrder.cs | 안정 정렬(병합, 외부 버퍼로 할당 0), 키 정렬 순회 `SortedKeys(dict, buffer)`, 동점 id 오름차순 비교자 (FR-90, 91) |
| RoadToPro.MatchEngine / Career / WorldGen | <모듈>/Runtime/RoadToPro.<모듈>.asmdef + AssemblyMarker.cs | `noEngineReferences: true`, 참조는 아래 그래프대로. 마커 클래스 하나 (어셈블리가 생기게) |
| RoadToPro.Data / UI | <모듈>/Runtime/RoadToPro.<모듈>.asmdef + AssemblyMarker.cs | 엔진 참조 허용, 그래프대로 |
| RoadToPro.Presentation | Presentation/Runtime/RoadToPro.Presentation.asmdef, Logging/UnityLogger.cs | `ILogger` → `UnityEngine.Debug.Log*`. 000 `FarmLog`가 이 출력을 수집 (FR-72) |
| RoadToPro.Tests.EditMode | Tests/EditMode/Core/*Tests.cs, Tests/EditMode/Architecture/ModuleGraphTests.cs | 로직·결정론 테스트. asmdef 참조에 새 모듈 추가 |
| RoadToPro.Tests.Performance | Tests/Performance/Core/CorePerfTests.cs | 성능 예산 측정 |
| 하네스(000 도구) | Tools/devicefarm/farm/logic.py, commands.py, perf.py + tests | **보강** — 아래 "검증 경로" 참고 |
| 데이터 | Data/perf/001-core-foundation.json | 성능 예산(기계가 읽는 사본) |

의존 방향 (`.claude/rules/10-csharp.md`): MatchEngine→Core · Career→MatchEngine, Core · WorldGen→Career, Core · Data→Core, MatchEngine, Career, WorldGen · Presentation·UI→위 전부 · Verification→위 전부(기존 Verification asmdef는 이번에 참조를 추가하지 않는다) · Tests→전부. 위반 없음.
> FR-01 목록은 2026-11-02 사용자 승인으로 rules와 같게 고쳤다.

## 데이터 · 스키마
- **RngState v1**: 33바이트 = 버전(1바이트) + s0..s3 (ulong 리틀엔디언). 세이브 배치는 022.
- **스트림 id**: `RngStreamId` 열거값은 명시 번호로 고정하고 재번호 금지(주석). 파생: `streamSeed = SplitMix64(matchSeed ⊕ SplitMix64(streamId))`, 하위: `SplitMix64(streamSeed ⊕ SplitMix64(subId))` — 언어 런타임 `GetHashCode` 미사용.
- **튜닝 정의 JSON**: `{"version":1,"definitions":[{"id","default","min","max","unit","tag","source"}]}`. tag는 기획서 표기 그대로(`[설계값]` `[V]` `[U]` `[M]` `[E]` `[제안]` …).
- **튜닝 값 묶음 JSON**: `{"version":1,"values":[{"id","value","unit"?}]}`. 실패: 정의에 없는 id · 중복 id · 범위 밖 · 단위 불일치 · 파일 없음/파싱 오류 — 예외 메시지에 id(또는 줄·열). 빠진 id는 기본값. 묶음 해시 = 정의 전체를 id 서수 정렬해 (id, 최종 값 비트) 해시.
- **실수 해시 규칙**: −0 → +0, 모든 NaN → 0x7FC00000(float)/0x7FF8000000000000(double) 정규화 후 비트 해시. 그 외는 비트 그대로 (1 ulp 차이 → 다른 해시).
- **경기 시각 표기**: 정규 시간 `MM:SS`, 추가시간 `B+M` / `B+M:SS` (B ∈ 45, 90, 105, 120). 기간 경계 틱은 그 기간 끝으로 취급하지 않고 다음 기간의 시작으로 (후반 시작 = `45:00`). 연장 없는 경기에서 연장 표기 요청 → 예외.
- 실제 튜닝 데이터 파일은 이번 스펙에 없다(로더만). 첫 사용 스펙(004~)이 `Data/tuning/`에 만든다.

## 결정론 · 성능
- RNG는 `SeedRng` 인스턴스(sealed class, 복사 버그 방지)로만, 인자 주입. static 상태 없음. `FloatGuard`의 기본 모드는 컴파일 상수라 상태가 아니다.
- 순회: 결과에 영향 주는 순회는 `DeterministicOrder`로 키 정렬. `List<T>.Sort`는 불안정이라 결과 경로에서 쓰지 않는다 (안정 병합 정렬 제공).
- 할당 0 경로: 벡터·각도·보간·swept·RNG 분포(가중·softmax는 `ReadOnlySpan<float>` 입력)·시계·해시 Add·비활성 로그. Performance 테스트에서 프로파일러 `Recorder("GC.Alloc")` 횟수로 확인 (Unity Mono는 `GC.GetAllocatedBytesForCurrentThread`가 0을 돌려줘 쓸 수 없음).
- 측정: ns/op는 1억 회(RNG 균등) / 1천만 회(나머지) 루프를 `Stopwatch`로 재고 `Measure.Custom`으로 기록 (테스트 코드에서만 Stopwatch). 3만 이벤트 해시·1,000개 튜닝 로드는 `Measure.Method`.

## 검증 경로 (하네스 보강 — 2026-11-02 사용자 승인)
현재 `farm.py logic --spec NNN`은 `[로직]` AC만 연결하고 EditMode 성능 테스트 결과를 리포트에 넣지 않는다. 001은 `[결정론]`·`[성능]` AC가 화면 없이 EditMode에서만 검증되므로 다음을 추가한다 (000 도구 수정 → `Tools/devicefarm/tests` 회귀 통과 필수):
1. `logic --spec`: `[결정론]` AC도 EditMode 결과로 연결 (카테고리 `Determinism` 테스트 통과 여부).
2. `logic --spec`: Unity `-perfTestResults`로 성능 결과 JSON을 받아 `evidence/perf.json`에 쓰고, `Data/perf/<spec>.json` 예산과 비교해 `[성능]` AC에 연결. 기준 측정 대비 +10% 회귀 검사는 기존 `perf-baselines.json` 로직 재사용, 첫 측정은 `approve-perf`(사용자 승인 후).
3. 테스트 이름 필터: `--spec 001`이면 네임스페이스 `RoadToPro.Tests.EditMode.Core`, `…Architecture`, `RoadToPro.Tests.Performance.Core`만.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `ModuleGraphTests.AsmdefGraph_MatchesAllowedDirections`, `…_IsAcyclic`, `…_Checker_RejectsCorePresentationAndMatchEngineCareer` (그래프에 금지 간선을 넣으면 검사기가 실패 + 순환이 생겨 Unity 컴파일 불가임을 확인) | 로직 | — |
| AC-02 | `ModuleGraphTests.CoreModules_HaveNoUnityEngineReferences` (`Assembly.GetReferencedAssemblies` + asmdef `noEngineReferences`), `CoreSources_HaveNoForbiddenApis` (FR-04 소스 스캔) | 로직 | — |
| AC-03 | `Vec2Tests.Normalize_Zero_ReturnsZeroNotNaN`, `AngleTests.Normalize_MinusPi_IsPi`, `AngleTests.LerpShortest_179_Minus179_Mid_Is180` | 로직 | — |
| AC-04 | `GeometryTests.Swept_ThroughCenter_ExactT`, `…_Miss06`, `…_StartInside_T0` (+ 경계: 길이 0, 접선, r=0) | 로직 | — |
| AC-05 | `PitchCoordsTests.Center_Is52_5_34`, `…_Perspective_RoundTrip_Within1Ulp` | 로직 | — |
| AC-06 | `SeedRngTests.SameSeed_Million_Identical` | 결정론 | — |
| AC-07 | `RngStreamsTests.ExtraDrawsOnA_DoNotChangeB` | 결정론 | — |
| AC-08 | `SeedRngTests.SaveRestore_Next10k_Identical` (바이트 직렬화 왕복 포함) | 결정론 | — |
| AC-09 | `DistributionTests.IntDice_ChiSquare_p001`, `…Normal_MeanStd`, `…Softmax_LowTemp_PicksMax99` | 로직 | — |
| AC-10 | `DistributionTests.Weighted_AllZero_Throws`, `…Negative_Throws`, `…NaN_Throws`, `…Int_AGreaterB_Throws` | 로직 | — |
| AC-11 | `MatchTimeTests.Tick27000_Is4500`, `…Stoppage_45plus2_30`, `…SecondHalfStart_Is4500`, `…AllTicks_RoundTrip` | 로직 | — |
| AC-12 | `MatchTimeTests.Tick90000_And_10x_NoOverflow` | 로직 | — |
| AC-13 | `GameDateTests.Feb29_PlusYear_IsMar1`, `…Age_Feb29Born`, `…Dec31_PlusDay`, ISO 주차 해 경계 | 로직 | — |
| AC-14 | `StableHasherTests.FixedLog_MatchesGoldenHash` (상수 기대값 = 프로세스 무관), `…OneUlp_Differs`, `…NegZero_EqualsPosZero`, `…NaNPatterns_Equal` | 결정론 | — |
| AC-15 | `CheckpointTests.FirstDivergence_At7th_Returns60to70s` | 결정론 | — |
| AC-16 | `TuningLoaderTests.OutOfRange_/Duplicate_/Unknown_/UnitMismatch_Fails_WithId`, `…Valid_HashChangesWithValue` | 로직 | — |
| AC-17 | `DeterministicOrderTests.HashSet_DifferentInsertOrder_SameResultHash`, `…StableSort_TiesById` | 로직 | — |
| AC-18 | `CorePerfTests.Rng_Uniform_ns`, `…Rng_Normal_ns`, `…Vec_Normalize_ns`, `…Swept_ns`, `…Hash_30kEvents_ms`, `…Tuning_Load1000_ms`, `…ZeroAlloc_MathRngClockLog` → `evidence/perf.json` | 성능 | mac-m1-1600p |
| AC-19 | `FloatGuardTests.Strict_NaN_Throws`, `…Sanitize_NaN_SafeValue_OneWarning`, `DMathTests.Sqrt_Negative_StrictThrows` | 로직 | — |
| FR-72 | `UnityLoggerTests.Warning_Error_ReachUnityLog` (`LogAssert.Expect`) | 로직 | — |

## 버그 방지
- 아주 작은 길이(1e−7) 벡터 정규화: 제곱길이 임계 미만이면 0 벡터 — 임계값 상수를 테스트로 고정.
- `atan2(0,0)` = 0, 각도 정확히 ±π, 180° 반대 방향 보간은 양의 방향으로 규칙 고정.
- swept: 이차방정식 판별식 음수(미스)·a=0(정지한 공)·시작점 내부·접선(판별식 0) 분기 각각 테스트.
- RNG: `Int(a,a)`, 전체 int 범위, Lemire 거부 루프 경계, softmax τ≤1e−6이면 argmax(동점 낮은 인덱스).
- 정규분포 꼬리값은 클램프하지 않는다 (테스트로 확인 안 함 — 호출자 몫, 주석).
- 시계: 틱 0, 전반 마지막 틱, 추가시간 0분, 연장 없는 경기의 연장 표기 → 예외, 표기 문자열 파싱 왕복.
- 날짜: 2/29, 12/31→1/1, ISO 주차의 1월 1일·12월 31일, 40년 이상 미래(9999년 상한 예외).
- 해시: 빈 로그 = FNV 오프셋 값, 문자열은 길이 접두 + UTF-8 (연결 모호성 방지: "ab"+"c" ≠ "a"+"bc").
- 튜닝: 파일 없음·빈 파일·JSON 문법 오류는 줄·열 포함 메시지. 숫자는 `InvariantCulture`만.
- 동시성 없음: 코어 static 가변 필드 0개를 `CoreSources_HaveNoForbiddenApis`에 포함해 검사 (`static` 비-`readonly`/비-`const` 필드 금지).

## 위험 · 미정
- [미정] 크로스플랫폼 결정론(고정소수점) — `DMath` 래퍼로 교체 지점만 확보.
- `MathF`가 Unity Mono/IL2CPP에서 플랫폼별로 다른 비트를 낼 수 있다 — 이번 기준(같은 빌드·같은 플랫폼)에는 영향 없음. AC-14 골든 해시는 해시 함수만 검증하고 초월함수 결과는 포함하지 않는다.
- 하네스 보강(위 "검증 경로")은 000 도구를 바꾼다. 000의 셀프테스트·회귀가 깨지지 않아야 한다.
- 빈 모듈 asmdef에 마커 클래스를 두는 것은 참조 검사용이다. 각 모듈의 실제 코드는 해당 스펙에서.
- (T23 리뷰) `MatchTimeline`은 모든 기간의 추가시간·연장 여부를 생성 시점에 받는다. 경기 중에는 이벤트를 전역 틱으로만 저장하고, 표기는 경기 후 확정된 타임라인으로 한다 — 006 통합 때 이 규칙을 따르거나 기간 단위 확정 API를 추가한다.
- (T23 리뷰) 에디터 Mono와 IL2CPP 플레이어는 float 중간값 정밀도·FMA 축약이 다를 수 있다. 결정론 비교는 항상 같은 런타임 안에서 두 번 실행해 하고, 에디터에서 만든 float 골든 값을 플레이어 결과와 비교하지 않는다 (AC-14 골든은 정수·비트 해시만 포함).
