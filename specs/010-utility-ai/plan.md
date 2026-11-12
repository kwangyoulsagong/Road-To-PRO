# 010 — 판단 AI: 22명 기본 실행 · 오프볼 · 결정 주기 · 시나리오 의도 · 플랜

상태: 구현 완료 · 보류 AC-24(속도 점프 0)·AC-33·AC-37 (2026-11-12)
스펙: ./spec.md

## 접근
지금 단계 5는 `SimpleDecisionStage`(대체)다. 공 없는 선수는 "팀마다 가장 가까운 1명 추격 / 상대 소유자 최근접 1명 압박 / 나머지 HoldShape"뿐이고, HoldShape·MoveTo·Chase가 모두 `MaxSpeed = 0`(= 005에서 최고 속도)으로 나가 전원이 전력질주한다(스펙 배경). 시나리오 배역 선수는 `ScenarioRuntime.TryDecide`가 행동을 직접 정한다.

이 플랜은 순수 C# 모듈 `MatchEngine/Runtime/UtilityAI/`을 새로 만들고 단계 5를 다음처럼 나눈다.
1. **스케줄러(틱 시작)** — `LodDecisionScheduler`가 FR-40·41 결정 LOD(공 20 m·배역·소유자 매 틱, 나머지 2~4틱, id 위상 분산, 동시 상한 12, 즉시 재결정 트리거)를 006 `IDecisionScheduler` 자리에 끼운다. 기본 `FixedPeriodScheduler(1)`을 대체한다.
2. **단계 5 = `UtilityDecisionStage`** — 결정 대상마다 ① 데드볼·골 후·재시작·외부 입력 → 기존 대체 경로 그대로(012·058 소유) ② GK(인플레이, 공 미소유) → 011 `GoalkeeperSystem.Decide`(같은 틱 위임, FR-90) ③ 공 소유자 → **072 자리 대체 경로**(지금의 보유·슈팅·패스·드리블, 배역이면 `StepExecutor`) ④ 그 밖의 필드 선수 = **010 오프볼 판단**. 기존 `SimpleDecisionStage`의 ①②③ 코드를 `StandInOnBall`로 옮겨 재사용하고 `SimpleDecisionStage`는 테스트·대체 파이프라인용으로 남긴다(006·013 기존 테스트 보존).
3. **오프볼 판단 = 공통 점수 틀** — 후보 생성(국면별, ≤ 8) → 기본 점수(이득·손실) → 결합 배수(이득 항에만, FR-11) → 공 몰림 억제(FR-15) → 평가 노이즈(FR-21·26) → 최댓값 정규화(FR-20) → 의도 보너스(FR-92) → 기회주의 필터(FR-94) → 결정 유지 판정(FR-42·44·45, 재추첨하지 않으면 RNG 미소비) → softmax(τ) → 목표점 이동 제한(FR-46) → 이동 강도(FR-70~72) → `Decision{Kind, Point, TargetSlot, MaxSpeed}`. 선택 규칙(정규화·의도·θ_opp·softmax)은 `UtilityChooser` 하나로 두고 011 `KeeperChooser`를 이리로 옮긴다(011 플랜 약속, 072도 같은 것을 쓴다).

의존 스펙 중 003(결합 배수)·007(피치 컨트롤·xT)·002(정신 능력치·성격)·013 의도 패킷이 아직 없다. 011의 `KeeperAttributes`처럼 **대체 입력**을 둔다: `DecisionAttributes`(002 전), `StandInStyleSource`(003 전 결합 배수), `StandInSpace`(007 전 공간 평가), `ScenarioIntentAdapter`(013 개정 전 의도 패킷). 각 대체는 인터페이스 뒤에 있어 연결 때 갈아 끼우고 AC-37로 재확인한다.
대안: `SimpleDecisionStage`의 HoldShape 분기에만 010 판단을 덧대기 → 배역 선수(FR-50·53)·LOD·트리거·RNG 순서를 표현할 수 없어 버렸다.

### 072·070·008 대체 경계 (이번 스펙에서 바뀌지 않는 것)
- 공 소유자의 결정(슈팅·패스·드리블), 태클 성공 굴림, 패스 속도는 지금 대체 경로 그대로다. 010은 "압박 행동으로 상대에게 붙는 것"까지 정하고, 태클 거리 안이면 기존 대체 태클 굴림(070 대체)이 그대로 돈다(FR-83 "태클 시도 효용"은 대체 태클 시도 확률 배수로만 연결).
- 배역 공 소유자는 지금처럼 `StepExecutor`가 공 동작을 정한다(072 대체). 배역 **비소유** 선수는 010이 판단하고 시나리오는 의도만 준다(FR-50·53).

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `D/` = `ME/UtilityAI/` (네임스페이스 `RoadToPro.MatchEngine.UtilityAI` — `Decision`은 기존 결정 구조체 이름과 충돌), `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/UtilityAI/`, `P/` = `Assets/_Project/Tests/Performance/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `D/DecisionAttributes.cs` | 정신·성격 대체 입력(002 전): Decisions·Vision·Composure·OffBall·Teamwork·WorkRate·Temperament·Professionalism·Ambition·Leadership·Familiarity (내부 0~100, 범위 검사). Anticipation·Concentration은 기존 `KinematicAttributes` 값을 읽는다(이중 정의 금지) |
| 〃 | `D/Consideration.cs` | 고려 항목 id: 오프볼 11종(RunInBehind·DropToReceive·Overlap·Underlap·Invert·StayWide·Press·CoverShadow·Mark·BoxCrash·RecoveryRun) + 오프볼 보조 후보(HoldShape·Support·LineHold·Cover·PassLaneBlock·LooseBall). 온볼 id는 072가 추가 |
| 〃 | `D/PlayStyle.cs` | 003 전 대체 입력: 역할 자유도(이탈 상한 m)·역할 오프볼 가중치 벡터·성향 4축(침투·오버래핑·방향·압박 적극성, 0~100)·버릇 레벨 15개(0/1/2) |
| 〃 | `D/IStyleMultiplierSource.cs`, `D/StandInStyleSource.cs`, `D/HabitTable.cs` | 결합 배수 캐시(역할 × 성향 × 버릇 × 실효 지시 × 날씨 1.0) 질의 인터페이스와 대체 구현. 성향 → 배수는 대체 선형식 `[대체]`, 버릇 배수는 기획서 4.2.1 표 15행(데이터). 금지 지시·준수율 1 → 0 규칙(FR-11, 003 FR-31) 포함. 003이 구현되면 003 캐시로 교체 |
| 〃 | `D/ISpaceEvaluator.cs`, `D/StandInSpace.cs` | 007 전 대체 공간 평가: 후보 지점의 "우리 도달 시간 − 상대 최단 도달 시간"(005 TTI) 로지스틱 + 전진 거리 기반 xT 대체값. 질의 횟수 카운터(FR-04) |
| 〃 | `D/OffBallCandidates.cs` | 국면(009)별 후보 생성(FR-03): 공격 = HoldShape·RunInBehind·DropToReceive·Overlap/Underlap·StayWide/Invert·BoxCrash·Support·RecoveryRun(전환), 수비 = HoldShape·Press(009 지정·게겐프레싱 창)·CoverShadow·Mark·Cover·LineHold·PassLaneBlock·RecoveryRun, 공통 = LooseBall(소유자 없음). 상한 8, 사전 점수로 자름. 목표점 규칙: 지원 각도 8~15 m(FR-19), 침투 출발·오프사이드 라인(FR-19a), 마킹 상대–골 선 1~2 m·009 재배정 주기(FR-19b), 수비 목표는 골 쪽(FR-16), 형태 앵커·이탈 상한(FR-85·96) |
| 〃 | `D/OffBallScorer.cs` | 기본 점수(이득·손실, FR-18·19·16) + 결합 배수(이득 항에만) + 공 몰림 억제(FR-15) + 전술 → 운동량 보정(FR-100·101) + 성격 보정(FR-83·84) + 점수 분해 기록 |
| 〃 | `D/EvalNoise.cs` | σ = f(Decisions, Vision) `[대체]` × (1 + P·(1 − s_Composure)) (FR-21), 친숙도 → 온도 가산(FR-23), 집중 저하 hazard·지속·효과(FR-26), 리더십 반경 완화(FR-86) |
| 〃 | `D/UtilityChooser.cs` | 공통 선택기(010·011·072 공용): 최댓값 정규화(ε 1e-6) → 의도 보너스 `+ w_intent·m` → θ_opp 필터 → 유지 임계(15%) → softmax(τ = τ_max − (τ_max − τ_min)·s). 동점 = 생성 순서. τ → 0 은 오버플로 없는 argmax(최댓값 빼고 exp). 할당 0 |
| 〃 | `D/IntensityPicker.cs` | 걷기·조깅·달리기·질주 → 최고 속도 비(데이터). 행동별 기본 강도 + 거리 기준(형태 유지 근거리는 조깅 이하) + 질주 비용(005 `(1+2C)`·WorkRate·성격, FR-71·84) + 틱당 한 단계(트리거 시 두 단계, FR-72) |
| 〃 | `D/IntentPacket.cs`, `D/IIntentSource.cs`, `D/ScenarioIntentAdapter.cs` | 의도 패킷 {배역 슬롯, 행동 종류, 목표 지점, 타이밍 창, 다음 패스 대상, 단계 번호}(FR-91). 013 개정 전 대체: `ScenarioRuntime`의 현재 단계(행동 → 고려 항목 대응표, `run.Target`, 단계 시작·제한 시간)를 읽어 패킷을 만든다. 해제 통보 `NotifyRelease(slot, reason)` |
| 〃 | `D/PlayerDecisionState.cs` | 선수별 상태(직렬화·해시): 현재 행동·목표점·시작 틱·강도·의도 단계 키·준수 여부·출처(FR-99)·집중 저하 종료 틱·즉시 트리거 플래그 |
| 〃 | `D/OffBallDecider.cs` | 선수 1명 오프볼 결정 파이프라인(위 3번) — 단계와 테스트 픽스처가 같은 경로를 쓴다 |
| 〃 | `D/LodDecisionScheduler.cs` | FR-40·41: `IDecisionScheduler` 구현. 트리거는 `IMatchView`(소유 변화·패스 대상·데드볼·키 모먼트)와 단계가 남긴 다음 틱 요청(의도 도착·해제·게겐프레싱 창 시작)으로 판정 |
| 〃 | `D/DecisionParams.cs`, `D/DecisionTuningIds.cs` | 튜닝 로드(`Data/tuning/decision.json`) |
| 〃 | `D/DecisionTelemetry.cs`, `D/IDecisionDiagnostics.cs` | FR-60(상위 3 후보 점수 분해, 사전 할당 링 버퍼, 꺼지면 0 B), FR-61·99·104(선수별 고려 항목 시도 횟수·출처 시간·준수율·기회주의 횟수·질주 거리·고강도 시간·압박 시도·게겐프레싱 참여), FR-62(040 진단 공급) |
| 〃 | `D/KeyMomentOverride.cs` | FR-52: 019가 넣을 "다음 결정 덮어쓰기" 입구(선수, 행동 종류, 지점). 019 전에는 테스트·외부 입력만 쓴다 |
| 〃 | `ME/Match/Stages/UtilityDecisionStage.cs` | 단계 5. 분기(데드볼·GK·소유자·오프볼), 선수 id 순 처리, 결정 보드 기록, 시나리오 선택(`runtime.Select`)을 먼저 호출 |
| 〃 | `ME/Match/Stages/StandIn/StandInOnBall.cs` | `SimpleDecisionStage`에서 데드볼·골 후·재시작·소유자·태클·외부 입력 코드를 옮긴 공용 대체 클래스(072·012 자리). `SimpleDecisionStage`도 이것을 쓴다(동작 동일, 기존 해시 유지) |
| 〃 | `ME/Goalkeeper/KeeperChooser.cs` (수정) | 선택 수식을 `UtilityChooser` 호출로 바꾼다(결과 동일 테스트로 확인) |
| 〃 | `ME/Scenarios/ScenarioRuntime.cs` (수정) | 비소유 배역은 `TryDecide`로 행동을 정하지 않고 의도만 노출(`TryReadIntent` 확장 → 패킷). 기회주의 해제 `ReleaseOpportunistic(slot)`(관련 배역만, 사유 opportunistic 이벤트). 붕괴·종료 해제 시 010에 같은 틱 통보 |
| 〃 | `ME/Match/MatchSim.cs`, `ME/Match/MatchSetup.cs` (수정) | 전술이 있는 기본 파이프라인에서 단계 5 = `UtilityDecisionStage`, 스케줄러 = `LodDecisionScheduler`. `MatchPlayer`에 `DecisionAttributes`·`PlayStyle` 선택 인자(없으면 기본값 50·중립) |
| — | `Data/tuning/decision.json` | 아래 "데이터" |
| — | `Data/e2e/scenarios/010-offball-motion.json` | AC-24 장면(058 완료 뒤 재확인 — 058은 구현 완료 상태라 이번에 찍는다) |
| RoadToPro.Tests.EditMode | `T/*.cs` | 테스트 전략 표 |
| RoadToPro.Tests.Performance | `P/DecisionPerfTests.cs` | AC-17 |
| RoadToPro.Verification | 기존 IL2CPP 벤치 드라이버에 결정 틱 측정 항목 추가 | AC-17 판정(D17) |

의존 방향 확인: `UtilityAI`는 `MatchEngine` 안(→ Core만). `.claude/rules/10-csharp.md` 위반 없음. UnityEngine 비참조.

## 데이터 · 스키마
- `Data/tuning/decision.json` (기존 튜닝 형식: id·default·min·max·unit·tag·source). 태그 규칙: 스펙 `[제안]` 값은 `[제안]`, 스펙 `[미정]` 자리의 시작값은 `[대체]`(017 튜닝 전 임시, 근거 문장 포함).
  - `[제안]`(스펙 Q5 채택): 후보 상한 8, 지점 질의 평균 3, LOD 반경 20 m, LOD 주기 2~4, 동시 상한 12, 유지 15 %, 최소 유지 0.5 s, 목표 이동 2 m/틱, 지원 거리 8~15 m, 몰림 10 m·3명, ε 1e-6, 트리거 시 강도 두 단계, 텔레메트리 상위 3.
  - 확정 시작값: τ_max 0.35, τ_min 0.05 (FR-20).
  - `[대체]`: w_intent 0.3, r_intent 8 m, 창 밖 감쇠 0.5(011 `KeeperChooser`와 같은 값), θ_opp 0.25, 배역 앵커 배율 0.5, 준수 p = clamp(0.35 + 0.6·s_Teamwork − 0.15·버릇레벨 − 0.1·성향충돌, 0.05, 0.98), σ = 0.02 + 0.10·(1 − (s_Dec + s_Vis)/2), 집중 저하 h0 = 0.002/결정 틱·지속 1.5 s·노이즈 ×2·지각 +0.1 s, 리더십 반경 15 m·완화 20 %, 친숙도 온도 가산 최대 +0.1, 형태 유지 조깅 거리 6 m, 강도 속도 비(걷기 0.3·조깅 0.5·달리기 0.75·질주 1.0), 압박 강도 → Press·질주 효용 ×(0.7 + 0.6·PressIntensity), 게겐프레싱 t_gp 5 s·r_gp 15 m, 성향 배수 0.5 + v/100, 다혈질·프로의식·야망·WorkRate 계수 0.2~0.4, 오프볼 후보 지점 수 6, 침투 출발 보정, 로밍 상한(앵커 4 m ~ 트레콸르티스타 18 m). 대체 값은 리포트에 표로 남기고 017 튜닝으로 넘긴다.
  - 실제 값(017 튜닝) 전까지 AC 판정은 "방향·유의성"(쌍둥이 비교 p < 0.01)과 스펙이 정한 수치(AC-05·09·20 등)로만 한다.
- `MatchPlayer` 새 선택 인자 `DecisionAttributes`·`PlayStyle`: 세이브 스키마 변경 없음(경기 입력만). 스냅샷(006 상태 직렬화)에는 `PlayerDecisionState` 22개 + 스케줄러 상태가 새 섹션으로 들어간다.
- 스케줄러 스냅샷 태그는 기존 `SnapshotTagScheduler` 재사용(구현 타입이 바뀜). 단계 5 상태는 `SnapshotTagStageBase + 5`.

## 결정론 · 성능
- RNG: 기존 `ctx.DecisionRng`(PlayerDecision 스트림) 하나. 선수 id 오름차순으로, 선수마다 고정 순서: ① 집중 저하 굴림 1회 ② (의도 단계가 바뀐 경우만) 준수 굴림 1회 ③ 후보 생성 순서대로 노이즈 표본(후보 수만큼) ④ (재추첨일 때만) softmax 1회. ③은 후보 수가 상태로 정해지므로 결정론. 대체 온볼 경로의 RNG 소비는 같은 스트림에서 그 선수 차례에 일어난다(지금과 같음).
- 텔레메트리·진단은 RNG를 쓰지 않고 상태를 바꾸지 않는다(AC-16·19). 꺼지면 버퍼를 만들지 않는다.
- 순회: 슬롯 배열 순서만. Dictionary 없음. 부동소수 누적은 고정 순서.
- 할당 0: 후보·점수·softmax 버퍼는 생성 시 고정 크기 배열(후보 8). 이벤트는 기존 `SimEvent` 구조체.
- 성능: 오프볼 1명 ≤ 0.4 µs·결정 틱 ≤ 12 µs(평균, 006 FR-80 몫). 공간 질의 비용을 줄이려고 상대 최단 도달 시간은 선수당 결정 틱 1회 미리 계산한 위치 배열로 근사(대체). Mono EditMode Performance 테스트로 회귀 기준선, 판정은 IL2CPP 벤치(D17).
- 058에서 정한 틱 예산 27.86 µs(006 기준선 ×1.05 + 뒤 스펙 몫)에 010 몫 12 µs가 이미 FR-80 몫으로 포함되어 있는지 벤치 때 확인하고, 아니면 회귀 기준 재설정을 사용자에게 묻는다.

## 테스트 전략
픽스처: `DecisionFixture`(MatchContext 없이 `OffBallDecider`를 직접 부르는 고정 장면 — 선수 위치·능력치·국면·지시 주입) + 헤드리스 `MatchSim` 실행.

| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-02 | `Scoring_CombinedMultiplier_AppliesToGainOnly`, `Scoring_ForbiddenInstruction_ExcludesCandidate` | 로직 | — |
| AC-03 | `HabitTable_EachOf15_AppliesMultiplier` (15 케이스, 오프볼 2종은 후보 점수까지, 온볼 13종은 결합 배수 질의까지 — 후보 생성 쪽은 072 AC로 재확인) | 로직 | — |
| AC-05 | `Chooser_Softmax_Decisions20_88pct`, `Chooser_Softmax_Decisions1_57pct` (10,000회) | 로직 | — |
| AC-06 | `Noise_PressureComposure_SigmaOrdering` | 로직 | — |
| AC-08 | `Lod_FarPlayers_DecideTwoToFourPer10Ticks`, `Lod_PossessionChange_AllDecideNextTick`, `Lod_ConcurrentCap` | 로직 | — |
| AC-09 | `Hold_10pctNoSwitch_20pctReroll`, `Hold_NoRerollConsumesNoRng`, `Hold_ExecutedPassNotCancelled` | 로직 | — |
| AC-10 | `Intent_ReleaseSameTick_BonusZeroNoGap`, `Intent_ReleaseScope_OthersKept` | 로직 | — |
| AC-12 | `Crowding_FourthPlayerLessBallward`, `Crowding_YieldOscillationEndsIn2s` | 로직 | — |
| AC-13 | `KeyMoment_OverridesNextDecisionOnly` | 로직 | — |
| AC-15 | `Determinism_90min_DecisionHashSame`, `Determinism_ShuffledRosterOrder_SameHash` | 결정론 | — |
| AC-16 | `Determinism_TelemetryOnOff_SameHash` | 결정론 | — |
| AC-17 | `Perf_DecisionTick_P95`, `Perf_OffBallPlayer_P95`, `Perf_SpaceQueries_Count`, `Perf_DecisionTick_ZeroGc` + IL2CPP 벤치 결정 틱 항목 | 성능 | mac-m1-1600p |
| AC-19 | `Determinism_DiagnosticsOnOff_EventAndCheckpointHashSame`, `Diagnostics_Off_ZeroAlloc` | 결정론 | — |
| AC-20 | `Hold_CloseCandidates_AtMostOneSwitchIn2s_TargetStep2m` | 로직 | — |
| AC-21 | `Intensity_90min_SprintShareAndFatigue`, `Intensity_HoldShapeNear_JogOrLess`, `Intensity_OneStepPerTick` (질주 비율 범위는 `[미정]` → 측정값 기록 + 피로 대비 방향만 판정) | 로직 | — |
| AC-22 | `Run_OffBall20_EarlierAndLessOffside` (1,000회) | 로직 | — |
| AC-23 | `Cast_EveryTickHasDecision_SourceFlag`, `Cast_EndTick_GeneralWithShapeTarget`, `Mark_NoSwitchBetweenReassign` | 로직 | — |
| AC-24 | `e2e/010-offball-motion` (오프볼 `[anim] 속도 점프` 0 + 형태 유지 조깅 육안) | E2E | mac-m1-1600p |
| AC-26 | `Twin_PaceAccel_RunInBehindMore` (1,000회 이항) | 로직 | — |
| AC-27 | `Twin_Tendency_RunDirectionPress_DistributionDiffers`, `Twin_LineBreaker_RunInBehindMore` | 로직 | — |
| AC-28 | `Twin_Temperament_PressTackleMore`, `Twin_WorkRatePro_RecoverySprintMore`, `Role_AnchorVsTrequartista_RoamDistance` | 로직 | — |
| AC-29 | `Headless_90min_All22HaveDecision_NoIntentOnOpenPlayDefence_GkFrom011` | 로직 | — |
| AC-30 | `Intent_OverlapBonus_RaisesMatchRate`, `Intent_BonusNotMultiplied` | 로직 | — |
| AC-31 | `Comply_TeamworkTwin`, `Comply_HabitLevel2Twin`, `Comply_OneRngPerStep` | 로직 | — |
| AC-32 | `Opportunistic_BelowTheta_Never`, `Opportunistic_AboveTheta_OneReleaseEach` | 로직 | — |
| AC-33 | `Baseline_NoScenario_1000Matches` — **보류**(072·008·070·071·012 연결 전, 스펙 명시). 이번에는 100경기 측정값만 리포트에 기록 | 로직 | — |
| AC-34 | `Tactics_PressIntensityHighLow_100Matches` | 로직 | — |
| AC-35 | `Gegenpress_OnOff_WindowPressers`, `Gegenpress_RegainEndsWindow` (창 뒤 허용 차이 `[미정]` → 측정값 기록) | 로직 | — |
| AC-36 | `Fatigue_LastVsFirst15_SprintDrops`, `Fatigue_Off_NoSignificantDrop` | 로직 | — |
| AC-37 | **보류** — 003·007·013 개정 연결 때 재실행 | 로직 | — |

추가(회귀 보호): `KeeperChooser_SameResultAfterMove`(011 결과 동일), `SimpleDecisionStage_HashUnchanged`(StandInOnBall 추출 전후 006 기존 해시 동일), 013 기존 테스트 전체(배역 비소유가 010으로 바뀌어 기대값이 달라지는 테스트는 목록을 만들어 사용자에게 보고 후 갱신).

## 버그 방지
- 점수 전부 0·음수: 정규화 분모 `max(|U_max|, ε)`, softmax는 최댓값 빼고 exp → NaN 0 (`Chooser_AllNonPositive_NoNaN`, `Chooser_TauNearZero_Argmax`).
- 동점: 생성 순서 첫 후보 (`Chooser_Tie_FirstGenerated`).
- 소유자 없음(공중·느슨): 온볼 후보 없이 LooseBall·위치 후보만 (`Candidates_LooseBall_NoOnBall`).
- 같은 지점 두 명: 몰림 억제 + 진동 2 s 종료(AC-12).
- 퇴장·부상·교체로 인원 감소, 배역 선수 교체: 결정 상태 제거 + 013 통보 (`Cast_SubstitutedPlayer_StateRemovedAndNotified`).
- 키 모먼트와 의도 해제 같은 틱: 키 모먼트 우선 (`KeyMoment_WinsOverRelease`).
- 의도 지점이 피치 밖: 일치도는 자른 지점 기준 (`Intent_OffPitchTarget_Clamped`).
- 의도 받은 소유자가 같은 틱 소유 상실: 같은 틱 보너스 0 (`Intent_LossSameTick_BonusZero`).
- 게겐프레싱 창 중 재소유: 창 즉시 종료(AC-35).
- 극단 능력치 1·20, 피로 1.0: 정규화 0~1 클램프 (`Attributes_Extremes_InRange`).
- 강도 단계 점프: 비트리거 틱 한 단계 (AC-21).
- 058에서 고친 "걷는 선수"(`IsSlotControllable`)·GK 다이빙 중(`IsBusySlot`) 분기를 새 단계에서도 그대로 유지 (`Stage_WalkingAndDivingSkipped`).
- 스냅샷 왕복: 결정 상태·스케줄러 저장/복원 후 해시 동일 (`Snapshot_RoundTrip_DecisionState`).

## 위험 · 미정
- **평균 득점·경기 양상 변화**: 오프볼이 바뀌면 058 AC-15(대체 판단 평균 16골)·011 T17 수치가 달라진다. 득점은 주로 대체 온볼(072 전)·대체 태클이 정하므로 이번 스펙 판정에 넣지 않고(AC-33 보류) 측정값만 리포트에 남긴다. E2E 고정 경기(`standin.shotProb 0.05`)는 그대로 둔다.
- **013 배역 실행 변화**: 비소유 배역이 의도 보너스로만 움직이면 기존 시나리오 성공률·E2E 컷백 장면이 달라질 수 있다. E2E 고정 장면은 배역 선수 Teamwork 20·w_intent 높게 주입한 픽스처로 유지하는 안을 기본으로 하고, 013 테스트 기대값 변경은 목록으로 보고한다.
- 대체 값(`[대체]`) 다수: 스펙 `[미정]` 자리. 017 튜닝 전까지 AC는 방향·유의성으로만 판정. 값 표는 리포트에 남긴다.
- 정신 능력치·성격은 002 전이라 `MatchPlayer`에 대체 입력으로 넣는다. 기본값(전원 50·중립 스타일)이면 쌍둥이 차이가 없는 "평균 선수" 경기가 된다 — 실경기(058 하이라이트) 장면에는 포지션별 대체 스타일 프리셋(예: 윙어 StayWide, ST RunInBehind)을 E2E 셋업에서만 넣는다.
- 009 FR-91 "다음 결정 틱부터 → 같은 틱"(FR-53) 수정 제안: 지금 `ShapeStage`는 배역 슬롯에 `MarkNoShapeTarget`을 찍는다. 배역 선수도 형태 앵커가 있어야 하므로(FR-96) 단계 2에서 배역 슬롯에도 형태 목표를 계산해 두도록 009 코드를 고친다(009 스펙 문구 수정은 사용자 확인 후).
- 005 재확인(FR-102): 기존 FR-33·36 피로로 충분한지 AC-36 결과로 판단한다.
