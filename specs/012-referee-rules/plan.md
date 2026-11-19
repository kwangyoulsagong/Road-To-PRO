# 012 — 심판 · 규칙 판정 · 재시작 · 플랜

상태: 완료 (2026-11-19) · 승인됨 (2026-11-19 사용자 "진행" — P1~P12 [제안]·위험 1~6 [제안] 전부 채택, 보류 AC(17·18·22)는 완료 조건에서 뺌)
스펙: ./spec.md

## 접근
대체 단계 `SimpleRefereeStage`(골·아웃·재시작·예정 사건 스크립트만)를 실제 심판 단계 `RefereeStage`(단계 8)로 바꾼다. 입력은 이미 엔진에 있다: 070·071의 `Contact` 이벤트 + `ContactDetail`(FR-20 필드 전부, 핸드볼 `Arm` 포함), 008 FR-15 패스 순간 오프사이드 스냅샷(`ActionDetailLog.AddOffside`), 수신 `TouchKind`(의도적·굴절), 011 세이브·캐치 이벤트, 072 `DiveAttempt`, 004 경계 통과(`PhysicsCall`). 심판 단계는 이 틱의 이벤트를 **발생 순서(Seq)대로** 한 번 훑어 ① 진짜 판정(규칙 정답, RNG 없음) → ② 심판 인지(시야 품질·성향·노이즈, `RefereeRng`) → ③ 어드밴티지 큐 → ④ 휘슬·재시작·카드 → ⑤ VAR 순서로 처리한다. 판정 로직은 상태 없는 정적 판정기(`OffsideJudge`, `FoulJudge`, `DogsoJudge`, `HandballJudge`, `RestartRules`)로 나눠 표 테스트를 쉽게 하고, 상태(보류 반칙·경고·공격 단계 기록·심판 위치)는 `RefereeState` 하나에 모아 직렬화·해시한다.

VAR은 **시뮬 틱을 쓰지 않는다**: 사안(득점·PK·직접 퇴장·신원 오인)이 생긴 같은 틱 끝에 판정을 확정하고, 체크 시간(30~120 s)은 `RefereeRng`으로 뽑아 정지 시간(006 FR-20)에만 더한다. 그래서 "체크 중 시뮬 상태 불변"(AC-14)과 "연출 길이 무관"(AC-20)이 구조로 성립한다. 대안 — VAR 체크 동안 데드볼 틱을 실제로 흘리기 — 은 선수 이동·RNG 소비가 생겨 AC-20을 깨기 쉬워 버린다.

`RefereeScript`(예정 사건 주입 훅)는 테스트·검증 장면이 많이 쓰므로 **남긴다** — 새 단계도 스크립트를 먼저 실행한다(006 대체 계약 유지). 판정 결과가 실경기 양상(파울로 끊김·프리킥·PK·퇴장)을 바꾸므로, 마지막에 081~087과 같은 100경기 측정으로 보호 지표를 확인하고 E2E·고정 시드 테스트를 다시 고른다(위험 1).

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `Runtime/Referee/RefereeParams.cs` | FR-07·21·23·30~32·40·50·71~73·80 계수(아래 "데이터"), `Create(TuningSet)` + 코드 기본값 |
| RoadToPro.MatchEngine | `Runtime/Referee/RefereeTuningIds.cs` | `Data/tuning/referee.json`의 `ref.*` id |
| RoadToPro.MatchEngine | `Runtime/Referee/RuleSet.cs` | FR-25·63 시즌 규칙 세트(`Season2025_26`, `Season2026_27`): GK 8초 → 코너, 어드밴티지 후 골 DOGSO 경고 없음, 백패스. 경기 설정 입력 |
| RoadToPro.MatchEngine | `Runtime/Referee/RefereeProfile.cs` | FR-30 성향 7변수(0~1) + 이름 없는 id. `MatchSetup.Referee`로 받음 (없으면 리그 평균 프로필) |
| RoadToPro.MatchEngine | `Runtime/Referee/OffsideJudge.cs` | FR-01~06·10: 몸 기준점 x, 오프사이드 위치(d > 0), 면제, 관여 3유형, 리셋 판정. 정적·상태 없음 |
| RoadToPro.MatchEngine | `Runtime/Referee/OffsideTracker.cs` | 패스 스냅샷 → "오프사이드 위치 후보" 목록 유지, 터치·굴절·세이브·슛으로 갱신(FR-04~06), 부심 지각 d_perceived(FR-07), 늦은 깃발 보류(FR-08) |
| RoadToPro.MatchEngine | `Runtime/Referee/FoulJudge.cs` | FR-21·22·26: 심각도·파울 점수·경중 3단계(진짜 판정), 반복 반칙 임계 보정, 슬라이딩 장인 ×1.4 |
| RoadToPro.MatchEngine | `Runtime/Referee/DogsoJudge.cs` | FR-23·24·25: 4요소, 박스 안 완화, SPA, 어드밴티지 후 골 |
| RoadToPro.MatchEngine | `Runtime/Referee/HandballJudge.cs` | FR-50·51: 실루엣·거리·반응 시간 → 확률, 면제, 공격 측 득점 직전 우연 핸드볼 |
| RoadToPro.MatchEngine | `Runtime/Referee/Perception.cs` | FR-31·32: 시야 품질(거리·가림·각도·부심), 인지 노이즈, 홈 편향, 압박 저항. 다이빙 속임(FR-27) |
| RoadToPro.MatchEngine | `Runtime/Referee/AdvantageQueue.cs` | FR-40~43: 보류 반칙(최대 4개 고정 배열), 3 s 창, 이득 점수, 복귀·해제, 다음 정지 때 징계 |
| RoadToPro.MatchEngine | `Runtime/Referee/Discipline.cs` | FR-22·26·28·29: 선수별 경고 수·반칙 수, 감독 항의 누적, 두 번째 경고 → 퇴장, 퇴장 → `RemoveFromPitch` |
| RoadToPro.MatchEngine | `Runtime/Referee/RestartRules.cs` | FR-61~66: 재시작 종류·팀·위치 표(골 에어리어 안 간접 FK 보정, PK, 드롭볼, 8초 → 코너, 백패스 간접 FK, PK 재차기) |
| RoadToPro.MatchEngine | `Runtime/Referee/AttackPhaseLog.cs` | VAR 대상 구간(공격 단계 = 마지막 점유 변경 이후) 사건 기록(고정 링 버퍼 16): 놓친 파울·놓친 핸드볼·오프사이드 관여·다이빙 속임 |
| RoadToPro.MatchEngine | `Runtime/Referee/VarReview.cs` | FR-70~74: 4대 사안 판정, 명백한 오류 임계, 사실/주관 구분, OFR 확률, 체크 시간, 결과 유형 |
| RoadToPro.MatchEngine | `Runtime/Referee/OfficialsMotion.cs` | FR-80·81: 주심 대각선 목표·부심 x 목표·체력 → 속도 한계. 기존 `Officials`(위치·이동 저장)를 그대로 쓰고 목표·속도만 정함 |
| RoadToPro.MatchEngine | `Runtime/Referee/RefereeState.cs` | 위 상태 모음: Write/Read/AddTo (체크포인트·해시) |
| RoadToPro.MatchEngine | `Runtime/Referee/IRefereeDiagnostics.cs` | FR-97 진단 콜백(오프사이드 라인·몸 기준점·보류 남은 시간·심판 위치·시야 품질). 널이면 호출·할당 0 |
| RoadToPro.MatchEngine | `Runtime/Referee/RefereeTelemetry.cs` | FR-95 판정 레코드(진짜·내린 판정·오심·시야·VAR) — `SimEvent` 필드로 싣는 규칙 + 경기 누계(측정용, 해시 밖) |
| RoadToPro.MatchEngine | `Runtime/Match/Stages/RefereeStage.cs` | 단계 8 본체: 스크립트 → 경계(아웃·골) → 이벤트 순회(접촉·패스·수신·세이브·다이빙·캐치) → 보류 큐 갱신 → VAR → 7명 미만 → 심판 이동 |
| RoadToPro.MatchEngine | `Runtime/Match/Stages/StandIn/SimpleRefereeStage.cs` (수정) | 경계 판정·스크립트 실행을 `RestartRules`·공용 헬퍼로 옮기고 이 클래스는 얇은 래퍼로 남김 — `DefaultStages()`·`MatchTestKit` 기존 테스트용 |
| RoadToPro.MatchEngine | `Runtime/Match/MatchSim.cs` (수정) | 실경기 단계 구성에서 단계 8 = `RefereeStage` |
| RoadToPro.MatchEngine | `Runtime/Match/MatchSetup.cs` (수정) | `Referee`(프로필), `RuleSet`, `VarEnabled` 추가. 기존 `RefereeLeniency`는 프로필 엄격함으로 매핑(호환), 복제·검증 포함 |
| RoadToPro.MatchEngine | `Runtime/Match/Events/SimEventKind.cs` (수정) | 400번대 데이터 규약 문서화(Foul·Card·Offside·FreeKick·Penalty·VarCheck·VarOverturn) + 예약 번호 사용: `AdvantagePlayed = 417`, `AdvantageEnd = 418`, `HandballOffence = 419`, `PenaltyRetake = 420`, `KeeperHoldViolation = 421`, `RestartCancelled = 422`, `ContactInputClamped = 423`. `EventCodes`에 카드 사유·VAR 결과·판정 플래그 비트 |
| RoadToPro.MatchEngine | `Runtime/Match/Flow/MatchFlow.cs` (수정, 작게) | 재시작 취소(VAR 번복 시 이미 낸 PK → 다른 재시작) API, 정지 시간 VAR·카드 사유 기록(기존 `AddStoppage` 재사용) |
| RoadToPro.MatchEngine | `Runtime/Match/Flow/DeadBallFlow.cs` (수정, 작게) | FR-69 재시작 대기 상한을 `ref.restartWaitCap`에서 읽음(058 대체 40 s 대신) |
| — | `Data/tuning/referee.json` (신규) | `ref.*` 키 (코드 기본값 일치 테스트·`source` 표기) |
| — | `Data/referees/profiles.json` (신규) | FR-34 가상 심판 프로필 묶음(id·성향 7개, 이름 없음 — 이름은 051) + 리그 평균 프로필 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/MatchEngine/Referee/*.cs` | AC-01~16·23·25 표·통계 테스트 (테스트 이름에 IFAB 조항: `Law11_…`, `Law12_…`) |
| RoadToPro.Tests.EditMode | `Tests/EditMode/MatchEngine/Referee/RefereeDeterminismTests.cs` | AC-19·20·24 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/MatchEngine/MatchFeelMetrics.cs`·`MatchFeelReportTests.cs` (수정) | 판정 지표(파울·경고·퇴장·PK·오프사이드·VAR·오심·정확도) 측정 항목 추가 — AC-17·18 중간 측정 |
| RoadToPro.Tests.Performance | `Tests/Performance/MatchEngine/RefereePerfTests.cs` | AC-21 Mono 회귀 기준선 |
| RoadToPro.Verification | `Runtime/Bench/RefereeBenchWorkloads.cs`, `Data/perf/012-*.json` | AC-21 IL2CPP 판정(D17) |

의존 방향 확인: 전부 `RoadToPro.MatchEngine` 안. `Referee` 네임스페이스는 `Core`·`Match`(컨텍스트·이벤트·흐름)·`Actions`(`ContactDetail`·`ActionDetailLog` 읽기 전용)만 참조하고, `Actions`·`UtilityAI`는 `Referee`를 모른다(카드 상태 조회는 FR 범위 밖 — 010/008이 나중에 `Discipline` 읽기 인터페이스로). `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마

### 스펙 `[미정]`을 정하는 [제안] (플랜 승인 = 아래 채택)
| # | 스펙 항목 | [제안] | 근거 |
|---|---|---|---|
| P1 | FR-21 파울 심각도 계수 | 리서치 07 § 1.4 설계 예시를 바탕으로 늦음 항 추가: `sev = 0.40·I + 0.20·Behind + 0.25·Studs + 0.05·H + 0.10·Late` (H = 접촉 높이 1.2 m 이상 → 1, 선형), `foul = sev × (1 − 0.7·BallFirst) × (슬라이딩 장인 ? 1.4 : 1)`, 임계 파울 0.30 · 경고 0.60 · 퇴장 0.85. 계수는 데이터, AC-23(태클당 10~14 %)으로 1회 이상 보정 | 원 예시 합 1.0 유지(I 0.45 → 0.40, H 0.10 → 0.05로 늦음 0.10 확보). 리서치 02 로지스틱은 능력치 항이 있어 "012는 접촉 특징값만" 원칙(FR-21)과 안 맞아 버림 |
| P2 | FR-32 주심 인지 모델 | 리서치 07 § 1.4 **시야 품질 기반 노이즈**로 통일: `perceived = foul + N(0, σ)`, `σ = σ0 · (1.5 − consistency) · (1.5 − sight)`, σ0 = 0.12. 홈 편향은 홈 팀이 피해일 때 `+homeBias`. 시야 = 거리(15~25 m 최적, 밖은 선형 감소)·가림(주심–사건 선분 1 m 안 선수 수)·각도·가장 가까운 부심 보정의 곱 | AC-13이 일관성·시야를 따로 검정 → 연속 노이즈가 맞음. P_miss 로지스틱은 "놓침"만 표현 |
| P3 | FR-25 규칙 연도 | 규칙 세트를 데이터로 고를 수 있게 하고 **기본 = 2026/27**(2025/26 GK 8초 포함 + 어드밴티지 후 골 DOGSO 경고 없음) | 오늘(2026-11) 진행 시즌. 첫 시즌 실제 일정과 맞음 |
| P4 | FR-08 늦은 깃발 보류 임계 | `|d| ≤ 0.5 m`이고 공격이 진행 중이면 보류, 공격 단계 종료(점유 변경·데드볼·슛)에 깃발 | scenarios 03 § 4.2 σ_AR 0.3 m의 약 1.5σ |
| P5 | FR-40 어드밴티지 이득 점수 | `adv = 반칙당한 팀 점유 유지(0/1) × clamp(0.3 + 위협도, 0, 1)` — 위협도는 072가 쓰는 거리 기반 대체 위협(공 → 상대 골 거리, xT 표 전까지). 심각 반칙(퇴장급)은 어드밴티지 후에도 징계 집행 | xT 표는 007에서 분리돼 아직 없음 |
| P6 | FR-71 명백한 오류 임계 | 주관 판정: `|perceived − foul| > 0.25` **그리고** 판정 단계(파울 없음/파울/경고/퇴장)가 다름. 사실 판정(오프사이드·골라인·신원)은 항상 정정 | 리서치 07 § 1.4 "명백한 오류만" |
| P7 | FR-72 OFR 확률 | 명백한 오류 판정이면 OFR 뒤 번복 0.85 | 리서치 07 § 1.1 개입 대부분이 번복 |
| P8 | FR-27 다이빙 속임 확률 | `p_fool = 0.35 × (1 − sight) × (1 − 0.5·consistency) + (약한 접촉 ? 0.15 : 0)`, 들키면 경고 확률 0.6 | VAR 개입 0.3/경기(FR-96)와 같이 보정 |
| P9 | FR-73 VAR 체크 시간 | 오프사이드 근소 60~120 s, 그 밖 사실 30~60 s, 주관(OFR) 60~120 s — 구간 균등, `RefereeRng` | 리서치 07 § 1.4 30~120 s |
| P10 | FR-69 재시작 대기 상한 | 058 대체 40 s 그대로 채택 (`ref.restartWaitCap`) | 058에서 실경기로 검증된 값 |
| P11 | 오프사이드 몸 기준점(FR-01) | 시뮬은 2D 원점 + 방향만 있어 **몸 앞쪽 끝 x = 위치 x + 공격 방향 × 0.25 m**(머리·몸통·발 중 가장 앞, 팔 제외)를 공격수·수비수 모두에 적용. AC-01 "팔만 앞"은 팔 오프셋이 기준점에 안 들어가는 표 테스트로 | 3D 신체 관절은 시뮬에 없음 |
| P12 | 스펙 `[제안]` (FR-60·64·65·81, 경계) | 골라인 오차 0, 백패스 간접 FK 포함, 드롭볼 포함, 심판 맞고 소유 변경 시 드롭볼, 같은 틱 동시 반칙 = 무거운 쪽 → 같으면 Seq 순, 하프 종료 때 보류 반칙은 종료 시 징계만 집행, 골라인 밖 수비수 = 경계선 위로 간주 — 전부 채택 | 스펙 [제안] 그대로 |

### 튜닝 · 데이터 파일
- `Data/tuning/referee.json` `ref.*`: P1 계수·임계, P2 σ0·시야 최적 거리 15/25 m·가림 폭 1 m, FR-07 σ_AR 0.25 + 0.04·Δ·flash-lag 0.3~0.5 m(균등), P4~P10, 반복 반칙(같은 선수 3번째 반칙부터 경고 임계 −0.15 [제안]), 어드밴티지 창 3.0 s, 근소 표시 0.10 m, FR-80 대각선 lerp 0.6·거리 15~20 m·심판 최대 속도 6.0~7.5 m/s(체력 선형 [제안], 기존 대체 7 m/s 포함), 8초 기준 8.0 s, 7명 하한.
- `Data/referees/profiles.json`: 리그 평균 1개 + 보정용 엄격 상·하위 예시(AC-17 엄격함 분포)를 포함한 가상 프로필 20개 [제안]. 실명 없음.
- `MatchSetup`: `Referee`(프로필, null → 리그 평균), `RuleSet`(기본 2026/27), `VarEnabled`(기본 true — 리그 데이터 020이 생기면 거기서). 세이브(022)는 경기 전·후만이라 경기 설정 필드 추가만, 마이그레이션 없음.
- 체크포인트: 단계 8 상태 형식이 바뀐다 (`_belowReported` 1개 → `RefereeState`). 체크포인트는 같은 빌드 재현용이라 버전 번호만 올린다 (006 FR-33 규칙).

### SimEvent 데이터 규약 (FR-95, 기존 필드 재사용 · 할당 없음)
- `Foul`: PlayerId = 반칙한 선수, OtherId = 당한 선수, A = 내린 판정(0 없음 → 이벤트 안 냄 · 1 파울 · 2 경고 · 3 퇴장) × 10 + 진짜 판정, B = 플래그(오심 · 어드밴티지 · 박스 안 · DOGSO · SPA · 슬라이딩 · VAR 대상), F0 = 진짜 파울 점수, F1 = 시야 품질.
- `Card`: A = 색(기존), B = 사유 코드(`EventCodes.Card*Reason`), OtherId = 관련 반칙 Seq 하위 비트 대신 반칙한 순간 틱은 `Foul` 이벤트로 연결(같은 틱·같은 선수).
- `Offside`: PlayerId = 관여 선수, A = 관여 유형(1~3), B = 플래그(근소 · 늦은 깃발 · 부심 오판 · VAR 정정), F0 = 실제 d, F1 = 지각 d.
- `VarCheck`: A = 사안(득점 · PK · 퇴장 · 신원), B = 정지 틱, F0 = 오류 크기. `VarOverturn`: A = 결과 유형(FR-74), 기존 골 취소 데이터와 호환.
- `Stoppage`: 기존 `StopVar`·`StopCard`·`StopDissent` 코드 그대로.
- 누락 오심(휘슬 안 분 진짜 파울)은 이벤트를 늘리지 않고 `RefereeTelemetry` 누계 + `AttackPhaseLog`에만 둔다 [제안] — 이벤트 수·로그 크기 증가를 막음. 018·030이 필요하면 그때 이벤트 추가.

## 결정론 · 성능
- RNG: `ctx.RefereeRng` 하나만 쓴다(이미 있음, 해시·체크포인트 포함). 소비 순서 = 이벤트 Seq 순 → 사건마다 고정 개수(인지 1 · 어드밴티지 0 · VAR 체크 시 시간 1 + OFR 1). 진짜 판정은 RNG를 쓰지 않는다. 진단 콜백은 RNG·상태를 건드리지 않는다(AC-24).
- 순회: 이벤트는 `SimEventLog`의 이번 틱 구간을 Seq 순으로, 선수는 슬롯 오름차순. 동시 반칙 우선순위 = (경중 내림차순, Seq 오름차순).
- VAR = 0틱 처리 → 체크 시간은 정지 시간에만 더해짐(AC-20). 연출 길이 값은 시뮬에 입력되지 않는다.
- 할당 0: 오프사이드 후보·보류 큐·공격 단계 로그·경고 표는 고정 배열(22·4·16). 판정기는 `in`/`ref` 값 타입.
- 성능 예산(스펙): 접촉 판정 ≤ 5 µs, 오프사이드 판정 ≤ 3 µs, 심판 위치 + 시야 ≤ 10 µs(p95), GC 0 B. 측정: Mono 성능 테스트(회귀 기준선) + IL2CPP 벤치(판정 — 시뮬 틱 경로를 바꾸므로 `farm.py bench --spec 012` 1회, 기존 결정 틱·물리 예산 회귀 +10 % 이내 함께 확인).
- 결정론 리뷰: 코어 변경 → `determinism-reviewer`.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `OffsideJudgeTests.Law11_Position_Table` (+0.01·0·−0.01·하프라인·자기 진영·팔만 앞) | 로직 | — |
| AC-02 | `OffsideJudgeTests.Law11_Exempt_ThrowGoalKickCorner`, `Law11_GkThrow_JudgedAtRelease` | 로직 | — |
| AC-03 | `OffsideTrackerTests.Law11_Involvement_ThreeTypes`, `Law11_NotInvolved_NoOffence`, `Law11_GkSightBlock_Off09` | 로직 | — |
| AC-04 | `OffsideTrackerTests.Law11_DeliberatePlay_Resets`, `Law11_DeflectionSave_Keeps`, `Law11_Rebound_JudgedAtShot_Off07` | 로직 | — |
| AC-05 | `OffsideTrackerTests.AssistantPerception_10000_AlignedBetter_FlashLagBias` (이항 검정) | 로직 | — |
| AC-06 | `VarReviewTests.Offside_Var_TrueDistance_TightFlag`, `Offside_NoVar_AssistantStands` | 로직 | — |
| AC-07 | `FoulJudgeTests.Law12_TrueDecision_Table20` (P1 계수 고정 표 20행) | 로직 | — |
| AC-08 | `DogsoJudgeTests.Law12_Dogso_Table` (박스 밖 퇴장 · 박스 안 도전 PK+경고 · 잡기 PK+퇴장 · 커버 1명 아님 · 어드밴티지 후 골 경고 없음) | 로직 | — |
| AC-09 | `DisciplineTests.Law12_SecondYellow_SendOff_NoSub_CountDrops` (실경기 스텁 + `RemoveFromPitch`) | 로직 | — |
| AC-10 | `AdvantageQueueTests.Law12_Advantage_LossAt2p9_ReturnsToSpot`, `Hold3s_Clears_BookingNextStop`, `GoalInWindow_Stands_Booking` | 로직 | — |
| AC-11 | `RestartRulesTests.Law15to17_Restart_Table` (스로인·골킥·코너·8.01/8.0 s·박스 안 직접 FK → PK) | 로직 | — |
| AC-12 | `RestartRulesTests.Law10_GoalLine_99_100`, `HandballJudgeTests.Law12_AttackAccidentalBeforeGoal_Disallow` | 로직 | — |
| AC-13 | `PerceptionTests.Consistency_Sight_ErrorRate_10000`, `HomeBias_WhistleRate_10000` (각 이항 검정 p < 0.01) | 로직 | — |
| AC-14 | `VarReviewTests.ClearError_RedUpgrade`, `MinorDiff_NoIntervention`, `NotReviewable_NoIntervention`, `Check_StateHashUnchanged` | 로직 | — |
| AC-15 | `RefereeStoppageTests.VarAndTwoCards_SumMatchesRng_FlowsInto006` (006 AC-08 집계 재사용), `SameSeed_SameVarDuration` | 로직 | — |
| AC-16 | `DisciplineTests.Law3_SixPlayers_AbandonEventOnce` | 로직 | — |
| AC-17 | `MatchFeelReportTests.Report_100Matches`에 판정 지표 추가 → **중간 측정만**. 1,000경기 판정은 017 하네스 의존 → 017 전까지 **보류**(100경기 값·엄격 상·하위 각 100경기 경고 평균 기록) | 로직 | — |
| AC-18 | 같은 측정에서 주요 판정 정확도(VAR 전) 기록. 목표 "미검증" 표시 유지, 017 전까지 **보류**(100경기 값 기록) | 로직 | — |
| AC-19 | `RefereeDeterminismTests.SameSeed_90min_RefereeEventHash`, `SimultaneousFouls_Fixture_Hash` | 결정론 | — |
| AC-20 | `RefereeDeterminismTests.VarPresentationLength_DoesNotChangeHash` (연출 길이 값을 바꿔 2회) | 결정론 | — |
| AC-21 | `RefereePerfTests`(Mono 기준선) + `farm.py bench --spec 012`(IL2CPP 판정, `Data/perf/012-*.json`) | 성능 | mac-m1 |
| AC-22 | `e2e/012-referee-overlay` — 040 디버그 뷰가 없어 **보류** (진단 출력 FR-97은 이 스펙에서 구현·AC-24로 검증). 기준 이미지 비교는 폐지(2026-11-11) → 040 뒤 육안 승인으로 읽음 | E2E | mac-m1-1600p |
| AC-23 | `FoulJudgeTests.StandingTackle_10000_Rate10to14`, `Sliding_AtLeast2x`, `FromBehind_Higher` (평균 능력치 접촉 생성기 픽스처) | 로직 | — |
| AC-24 | `RefereeDeterminismTests.Diagnostics_OnOff_SameHashes_ZeroAlloc` | 결정론 | — |
| AC-25 | `RestartMoveTests`에 `GoalLineOut_BallContinues_SideNetOutside_NoGoal` 추가 + 기존 058 AC-04 테스트를 `RefereeStage` 구성으로 재실행 | 로직 | — |

검증 범위(05-sdd): MatchEngine·튜닝 변경 → `farm.py logic --spec 012 --scope auto`(→ `match`) 1회, 시뮬 틱 경로 변경 → `bench` 1회, 결정론 + `determinism-reviewer`. 화면 변경 없음 → 장면 스크린샷은 실경기 하이라이트 E2E 시드가 바뀐 경우에만 재선택·재촬영(위험 1).

## 버그 방지
- **오프사이드 경계**: d = 0 온사이드, 하프라인 위 온사이드, 수비수 1명뿐(GK 전진) → 골라인을 두 번째 기준으로, 비활성(퇴장) 슬롯 제외 — 표 테스트(AC-01) + `SnapshotOffside`가 NaN으로 넣는 비활성 슬롯 처리 테스트.
- **같은 틱 겹침**: 패스와 수비 터치, 두 반칙, 득점 + 7명 미만 + 보류 반칙 → 우선순위 표(경계 판정 → 득점 → 반칙 → 보류 큐 → 7명) 고정 + 픽스처 테스트(AC-19 동시 반칙).
- **보류 창 경계**: 정확히 3.0 s(틱 단위 ≥ 30틱이면 해제), 보류 중 아웃·하프 종료·두 번째 반칙 — `AdvantageQueueTests` 경계 케이스.
- **두 번째 경고 경로**: 완화 경고(박스 안 DOGSO) + 기존 경고 → 퇴장, 보류된 경고 집행 시 이미 퇴장한 선수 → 무시 — `DisciplineTests`.
- **VAR 번복 후 재시작**: 이미 낸 PK 재시작을 취소 이벤트로 바꾸고 데드볼 흐름(058)이 중복 재배치 안 함 — 058 "재배치 1회" 테스트 재사용.
- **입력 범위 밖**: `ContactDetail`은 이미 [0,1]로 잘림. 방어로 한 번 더 자르고 `ContactInputClamped` 경고 이벤트(경계 조건).
- **GK 8초**: 8.0 s 정확히 위반 아님(틱 = 80 → 아님, 81 → 위반). 캐치 뒤 GK가 놓치거나 굴려 다시 잡은 경우 리셋.
- **세이브 왕복**: `RefereeState` Write → Read → 해시 같음 테스트(체크포인트 재개 후 판정 같음).
- **기존 테스트 호환**: `RefereeScript` 경로를 그대로 두고, `DefaultStages()`·`MatchTestKit`은 `SimpleRefereeStage`(얇은 래퍼)를 계속 써서 006·058 계약 테스트가 안 바뀌게 한다.

## 위험 · 미정
1. **실경기 양상 변화 (가장 큼)**: 지금 실경기는 파울로 멈추지 않는다(`TakeOnOutcome.FoulWon`도 접촉 이벤트만). 파울·프리킥·PK·퇴장이 생기면 081~087 보정 지표(선방률·전환율 상한 18 %·탈취율 등)와 E2E 고정 시드(083 슬라이딩 승리 시드 등)가 움직인다. **[제안]** ① 012 판정 지표는 017 FR-20 목표 대비 **측정·기록만**(보정은 1회까지, AC-23 파울률만) ② 081~087 보호 지표는 `Report_100Matches`(7400·8400)로 확인해 범위 밖이면 D 항목으로 기록하고 017 정식 보정으로 넘김(경계 규칙: 이 스펙에서 엔진 다른 계수 안 건드림) ③ 고정 시드가 깨진 테스트·E2E 장면은 시드 재선택 태스크로 처리(087 T13a와 같은 방식).
2. 프리킥·PK의 **차는 쪽 실행**은 008·071·074 범위다. 지금 `RestartKind.FreeKick`은 데드볼 흐름(058)이 대체 키커로 처리하고, PK는 `StandInPenaltyJudge`(확률)다 — 012는 판정·위치·팀만 내고 실행은 기존 대체를 그대로 쓴다 [제안]. 직접 FK 슈팅·PK 키커–GK 대결은 074 몫으로 남김.
3. 다이빙 입력(072 `DiveAttempt`)은 지금 넘어지기만 하고 판정이 없다 → 012 연결 뒤 다이빙 빈도가 경기 양상에 영향을 줄 수 있어 측정 항목에 넣는다.
4. FR-28 감독 항의·FR-26 항의·지연·셔츠 세리머니 경고는 그 행동을 만드는 쪽(050 AI 감독, 060 세리머니, 010 지연)이 아직 이벤트를 내지 않는다 → 012는 판정 규칙·입력 훅(`RefereeScript` 확장 없이 공개 메서드 `ReportConduct`)과 단위 테스트까지만, 실경기 발생은 0 [제안].
5. FR-81 공이 심판에 맞는 판정: 004 물리에 심판 충돌체가 없다 → 이 스펙은 판정 규칙(드롭볼)과 단위 테스트만, 물리 충돌은 [미정](004 후속) [제안].
6. AC-17·18(1,000경기, 017 하네스)·AC-22(040 뷰)는 의존 스펙 전까지 **보류**로 시작한다 — 084·087과 같은 "보류 AC는 완료 조건에서 뺌" 규칙을 이 스펙에도 쓸지 승인 필요.
