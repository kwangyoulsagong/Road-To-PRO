# 006 — 경기 루프 · 틱 파이프라인 · SimEvent · 플랜

상태: 구현 완료 · AC-20 보류 (2026-11-03, 스펙 수정 1건·IL2CPP 백엔드 임시 전환 포함)
스펙: ./spec.md

## 접근
경기 하나는 `RoadToPro.MatchEngine`의 `Match` 네임스페이스에 있는 **`MatchSim`**(상태 소유 + 틱 진행)이 돌린다. 매 결정 틱마다 FR-01 순서의 **단계 배열(`IMatchStage[10]`)**을 고정 순서로 실행한다. 단계끼리 주고받는 값은 **틱 보드(`TickBoard`)**의 슬롯으로만 전달한다. 슬롯마다 "쓰는 단계"가 정해져 있고, 앞 단계가 아직 쓰지 않은 슬롯을 읽으면 즉시 `StageOrderViolation`이 난다(FR-02, AC-03). 아직 없는 009·003·013·010·012·008·011 단계는 **대체 단계**(고정 대형 유지, 가까운 선수 추적, 단순 패스·슈팅, 라인 아웃 재시작, 고정 확률 승부차기)로 끼운다(FR-04). 이 단계들은 나중에 같은 인터페이스로 바꿔 끼운다. 물리 단계는 004 `BallStepper`와 005 `KinematicsSystem`을 서브스텝 10회 돌리고, 공 이벤트를 서브스텝 시각과 함께 보드에 남긴다(FR-03). 경기 진행(킥오프·전후반·추가시간·연장·승부차기·종료)은 단계 밖의 **`MatchFlow`** 상태 기계가 틱 끝에서 판정한다. 모든 사건은 **고정 크기 값 타입 `SimEvent`**로 미리 잡아 둔 배열 로그에 쌓이고, 누적 해시와 10초마다의 체크포인트 해시로 결정론을 증명한다. 재현은 **"시드 + 입력 로그 + 10초 상태 스냅샷"**으로 한다. 스냅샷은 모든 하위 상태를 같은 바이너리 기록기(`StateWriter`)로 직렬화한다. 리플레이 30 Hz 프레임은 **절대 시각 k/30 s를 서브스텝 사이에서 선형 보간**해 만든다. 그래서 어느 체크포인트에서 다시 돌려도 같은 프레임이 비트 단위로 같게 나온다(AC-16).

검토한 대안: (1) 단계를 이벤트 버스로 느슨하게 연결하는 방식은 읽기 순서 의존 버그(FR-02)를 구조적으로 막지 못해서 버렸다. (2) 리플레이를 매 3~4 서브스텝 원본으로 기록하는 방식은 간격이 일정하지 않고(100/30) 3D 보간이 흔들려서 버렸다. (3) 스냅샷을 "C# 객체 복제"로 하는 방식은 할당과 누락 위험이 있어서 버렸다. 바이트 직렬화 하나로 크기 측정(16 KB)·해시·복원을 같이 한다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.Core | Core/Runtime/Random/RngStreamId.cs | `MatchFlow = 8` 추가 (동전 던지기·대체 단계 무작위). 기존 값은 바꾸지 않는다 |
| RoadToPro.Core | Core/Runtime/Serialization/StateWriter.cs, StateReader.cs | 리틀엔디언 바이너리 기록기·판독기(고정 버퍼 재사용, 할당 0). bool/byte/int/long/ulong/float(비트 그대로)/Vec2/Vec3/RngState. 형식 버전 바이트와 길이 검사 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/KinematicsSystem.cs (수정) | ① `WriteState/ReadState`(상태·큐·기록·접촉·서브스텝 — 프로필은 불변이라 제외) ② `ResetSlot(id, attributes, position)`(교체 투입: 프로필 재계산, W=1·C=0, 큐 비움) ③ `SetActive(id, bool)`(퇴장·교체 대기 선수는 적분·Separation에서 빠짐). 기본은 전원 활성이라 005 해시·테스트는 그대로다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/CommandQueue.cs, MotionStats.cs (수정) | 상태 직렬화 메서드 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/MatchSetup.cs | FR-10 경기 설정: 시드, `TeamSetup`×2(팀 id, 선수 목록 = id + `KinematicAttributes`, 선발 11·벤치·주장·세트피스 키커·승부차기 순서 선택값), 대형 슬롯 좌표(대체 단계용 정규화 좌표 11개), 심판 성향(int, 012 자리), `PitchCondition`, `PitchSpec`, `CompetitionRules`, 중요도, 튜닝 해시, 내 선수 id. `Validate()` — 선발 ≠ 11, id 중복, 벤치 인원 초과는 `MatchSetupException` |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/CompetitionRules.cs | 연장·승부차기 유무, 교체 수·창 수·벤치 인원, 연장 추가 1명·1창, 뇌진탕 교체 1회. `Default` = 5명/3창 (FR-12, 020 데이터가 오면 덮어씀) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/MatchSim.cs | 경기 하나의 루트. 상태 소유, `StepTick()`, 단계 실행, 서브스텝 루프 위임, 체크포인트, 예외·NaN → 실패 결과(FR-72). 정적 가변 상태 없음 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/MatchRunner.cs | FR-70 실행 제어: `Step()`, `StepTicks(n)`, `RunUntilTick(t)`, `RunUntil(in EventCondition)`(종류·선수·구역 사각형 — 람다 없음), `RunToEnd()`. 일시정지 = 호출하지 않음. 상태 조회는 `IMatchView` |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/IMatchView.cs | 읽기 전용 조회(선수·공 상태, 스코어, 진행 상태, 시각). 구독자·테스트·프레젠테이션이 쓴다 (FR-35) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Pipeline/StageId.cs | FR-01 단계 번호 0~9 (TeamTactics, Phase, Shape, RoleAdjust, Tendency, ScenarioOrUtility, SteeringTargets, Physics, Referee, Publish) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Pipeline/IMatchStage.cs | `StageId Id`, `Execute(MatchContext)`, `WriteState/ReadState`(단계 내부 상태 — 스냅샷용), `AddTo(ref StableHasher)` |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Pipeline/TickBoard.cs | 틱 단위 슬롯: 대형 목표(선수별 Vec2), 국면, 결정 대상 목록, Steering 명령, 공 명령(킥·컨트롤), 물리 사건(서브스텝 시각 포함, FR-03), 심판 판정. 슬롯마다 쓰는 단계가 고정이다. `Read(slot, current)`는 `writer < current`인지, `Write`는 `writer == current`인지 정수 비교로 검사하고, 어기면 `StageOrderViolation`(단계·슬롯). 틱 시작에 비운다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Pipeline/IDecisionScheduler.cs, FixedPeriodScheduler.cs | FR-05: 010이 "이번 틱 결정 대상"을 준다. 대체 구현은 선수별 주기(기본 매 틱) + 테스트에서 주기 변경. 006은 대상 목록을 id 오름차순으로 정렬해 실행한다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Pipeline/IStageTimer.cs | 단계별 시간 측정 훅 (Begin/End(StageId)). null이면 호출 0. 구현(Stopwatch)은 Verification에 둔다 — 코어에 시계 API를 들이지 않는다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Stages/PhysicsStage.cs | 서브스텝 10회: 공 명령 적용 → `KinematicsSystem.Step()` → `BallStepper.Step` → 컨트롤 공 위치 갱신 → 공 이벤트를 (서브스텝, 순서)로 보드에 기록 → 리플레이 기록기·100 Hz 진단 싱크에 서브스텝 상태 전달 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Stages/StandIn/*.cs | 대체 단계(FR-04): `HoldShapeStage`(009 자리 — 대형 슬롯 + 공 쪽으로 10 m 이동, 하프마다 좌우 반전), `NoOpStage`(국면·역할·성향 자리), `SimpleDecisionStage`(013·010 자리 — 공 가장 가까운 선수가 컨트롤, 일정 간격으로 가장 가까운 앞쪽 동료에게 패스 또는 사거리 안이면 슈팅, 입력 주입 처리), `SimpleSteeringStage`(결정 → `SteeringCommand`), `SimpleRefereeStage`(012 자리 — 라인 아웃 → 재시작 이벤트·배치, 골 판정, 가짜 사건 주입 훅: 7명 미만·정지 시간·페널티 선언·부상·뇌진탕). 대체 수치는 `Data/tuning/match.json`의 `standin.*`(태그 `[대체]`, 게임 디자인 값 아님) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Flow/MatchFlow.cs | FR-11·14·15·21·22 진행 상태 기계: `FlowState {Ready, KickoffPending, InPlay, DeadBall, HalfTime, BeforeExtra, ExtraHalfTime, Penalties, Finished}` + 결과 사유(정상·중단·실패). 동전 던지기(`MatchFlow` 스트림), 후반 반대 팀 킥오프·진영 교체, 실점 팀 킥오프(자책골 포함), 기간 끝 판정(공식 아래), 페널티 유예 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Flow/StoppageClock.cs | FR-20·21: 하프별 정지 틱 누적(요인별: 골 세리머니·교체·부상 처치는 006 이벤트, VAR·카드 지연·항의는 012 이벤트의 정지 시간). 가산 계수 테이블(기본 0). 발표값 = ⌈누적/60 s⌉분 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Flow/Substitutions.cs | FR-12·13: 요청 큐(팀, 나감, 들어옴, 사유: 일반/뇌진탕), 데드볼·하프타임에서만 실행. 한도·창(같은 데드볼의 여러 교체 = 1창, 하프타임·연장 전 휴식은 창 미소모), 연장 진입 시 +1명·+1창(미사용분과 합산), 뇌진탕 1회 별도, 나간 선수 재투입 거부. 거부 → `SubstitutionRejected` 이벤트(사유 코드) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Flow/PenaltyShootout.cs | FR-15: 5명씩 교대, 수학적 확정 시 조기 종료, 이후 서든데스. 키커 순서는 팀 설정(없으면 출전 선수 id 순), 11명을 모두 차면 처음부터 다시. 킥 결과는 `IPenaltyJudge`(대체: 고정 확률, 008·011이 나중에 바꿈). GK 부상: 교체가 남았으면 교체, 아니면 키커 목록 첫 선수가 GK |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Squad.cs | 경기 선수 ↔ 운동 슬롯(0~21) 매핑, 출전 상태(벤치·출전·나감·퇴장), 출전 시간(틱). 운동 시스템은 슬롯 id로 돈다. 교체하면 같은 슬롯에 새 선수를 `ResetSlot`으로 넣는다 (id 정렬 순서가 바뀌지 않는다) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Events/SimEvent.cs | FR-30·82 값 타입(64 B 안): Seq(long), Tick(long), Substep(sbyte, 틱 단위 사건은 −1), Stage(byte), Kind(`SimEventKind` ushort), Team(sbyte), PlayerId, OtherId, X·Y(정규화 0~1)·Z(m), BallPhase, RelatedSeq, ScenarioId, BodyPart(byte), A·B(int)·F0·F1(float) 종류별 데이터. `AddTo(ref StableHasher)` |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Events/SimEventKind.cs | FR-31 종류 목록과 소유 범위: 006 = 1~99(킥오프, 기간 시작·종료, 추가시간 발표, 경기 종료, 중단, 실패, 교체, 교체 거부, 승부차기 킥, 입력 적용), 009 = 100~, 008 = 200~(패스·받음·크로스·드리블·캐리·슈팅·블록·태클·인터셉트·클리어·경합·골·골대), 011 = 300~, 012 = 400~(파울·카드·오프사이드·재시작 5종·VAR·7명 미만), 027 = 500~(부상), 013 = 600~, 003 = 700~, 019 = 800~. 006은 대체 단계가 쓰는 것만 정의하고 나머지 번호는 예약한다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Events/BodyPart.cs | FR-82 사용 부위: LeftFoot, RightFoot, Head, Chest, Thigh, Hand. 대체 단계는 공이 몸 방향 기준 어느 쪽에 있는지로 발을 정한다 (008이 나중에 정한다) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Events/SimEventLog.cs | 미리 잡은 배열(초기 용량 32k건, 넘치면 2배 — 워밍업 밖에서만 생긴다), 일련번호 부여, 정렬 불변식 검사(디버그), 누적 해시, `TruncateTo(seq)`(재시뮬), 스키마 버전 `SchemaVersion = 1` (FR-33) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Events/EventPublisher.cs | 단계 10: 같은 틱의 단계별 대기 이벤트를 (서브스텝, 단계, 단계 내 순서)로 안정 정렬(삽입 정렬, 할당 0)한 뒤 로그에 넣고 구독자에게 전달. 구독자 예외는 잡아서 세고 시뮬은 계속한다 (AC-18) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Events/ISimEventSubscriber.cs | `OnEvent(in SimEvent, IMatchView)` — 읽기 전용 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Replay/ReplayFrame.cs | FR-50 프레임: 절대 프레임 번호, 엔티티 26개 고정(슬롯 22 + 공 + 심판 3) × {x, y, z, yaw, MotionHint(byte), 선수 id}, 공 상태. float 그대로(버퍼는 무손실, 압축은 041이 재생 쪽에서) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Replay/MotionHint.cs | 이동 상태 코드: Idle, Walk, Jog, Run, Sprint, Kick, Tackle, Fall, Celebrate(예약). 속도 구간은 005 기록 임계(고강도·스프린트)를 쓴다. 043과 합의할 때까지 이 목록이 기본값이다 (스펙 [미정]) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Replay/ReplayRecorder.cs | 서브스텝 직전·직후 상태로 시각 k/30 s 프레임을 보간한다. 절대 서브스텝 s(시각 s/100 → (s+1)/100)를 처리할 때 `3s < 10k ≤ 3(s+1)`인 k마다 α = (10k − 3s)/3으로 보간한다 — 정수 계산이라 시작 지점과 무관하다. 링 버퍼 1800프레임(60 s). 클립 모드에서는 별도 버퍼에 기록 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Replay/StateCheckpoints.cs | FR-51·55: 보관 모드(All/KeyMomentsOnly/None). 10초마다 상태 해시는 모드와 상관없이 `CheckpointLog`에 기록한다. All이면 스냅샷 바이트도 보관(체크포인트용 바이트 풀 — 미리 540개 × 16 KB를 잡지 않고 필요할 때 할당, 성능 할당 측정은 None 모드) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Replay/Reconstructor.cs | FR-52·53: [t1, t2] → t1 이하 가장 가까운 체크포인트에서 복원한 별도 `MatchSim`을 t2까지 돌려 클립 기록. `ExtractClip(t1, t2)`는 0 미만·경기 끝을 넘는 구간을 잘라내고 구간 안 이벤트를 붙인다(구간 규칙은 018이 정함) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Snapshot/MatchSnapshot.cs | FR-60: 바이트 배열 + 틱 + 이벤트 일련번호 + 형식 버전. `MatchSim.Capture()`/`Restore()` 순서: 진행 상태 → 스코어·스쿼드 → 교체 → 정지 시계 → RNG 스트림들 → 공 → 운동 시스템 → 단계별 내부 상태 → 리플레이 링 버퍼 위치(링 내용은 제외 — 다시 채워진다) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Input/ExternalInput.cs, InputLog.cs | FR-62: (틱, 대상 선수, 종류, 값). 결정 단계(6)에서 해당 틱에 적용하고 `InputApplied` 이벤트를 낸다. 입력 로그는 결과에 포함된다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Input/KeyMomentResim.cs | FR-63: 선택 틱 t의 스냅샷(KeyMoments 보관 또는 가장 가까운 체크포인트 → t까지 진행)에서 복원 → 입력 추가 → 끝까지. 원래 로그는 t 이전 일련번호까지 유지하고 이후를 교체한다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Result/MatchResult.cs | FR-40·41: 스코어(정규·연장·승부차기), 득점·도움·시각, 카드, 교체, 부상, 출전 시간, 스탯 입력 자리(015), 평점 자리(015), 이벤트 로그(선택), 체크포인트 해시, 시드, 튜닝 해시, 엔진 버전, 종료 사유(정상/중단/실패 + 원인 틱·단계·메시지). `Validate()` 형식 검사 (016도 같은 검사, AC-20 보류) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/Diagnostics/IRawTrajectorySink.cs | FR-83 100 Hz 원본 출력. null이면 호출 0. 켜고 꺼도 상태는 바뀌지 않는다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Match/MatchTuningIds.cs, MatchParams.cs | 튜닝 id 상수와 불변 파라미터(정지 가산 계수, 대체 단계 값) |
| 데이터 | Data/tuning/match.json | `match.stoppage.*` 가산 계수(기본 0, `[미정]`), `match.goalCelebration.s`·`match.sub.s`·`match.injuryTreatment.s` 정지 시간 기본값(`[미정]` — 대체 단계에서만 쓴다, 027·012가 실제 값을 줌), `standin.*`(태그 `[대체]`) |
| 데이터 | Data/perf/006-match-loop-simevent.json | 성능 예산 사본. `runtime: il2cpp` 판정 항목 + `editmode` 회귀 항목, `deferred: {AC-20, AC-21 전체 정밀}` |
| RoadToPro.Verification | Verification/Runtime/Bench/SimBench.cs, MatchBenchWorkloads.cs, StopwatchStageTimer.cs | IL2CPP 플레이어에서 `--sim-bench 006`이면 워크로드(뼈대 경기, 물리 포함 경기 + 단계별 시간, 체크포인트 크기·복원, 클립 재구성, 메모리)를 돌리고 `bench.json`을 쓰고 종료. 성능 테스트도 같은 워크로드 클래스를 부른다 (측정 대상이 어긋나지 않게) |
| RoadToPro.Verification.Editor | Verification/Editor/FarmBuild.cs (수정) | `BuildMacIl2cppBench()`: 빌드 동안만 스크립팅 백엔드를 IL2CPP로 바꾸고 `finally`에서 되돌린다. 출력은 `Builds/farm-il2cpp/` |
| 도구 | Tools/devicefarm/farm/{bench.py, cli.py, build.py, perf.py} (+ tests) | `farm.py bench --spec NNN`: IL2CPP 빌드(캐시) → 실행 → `bench.json`을 `Data/perf`의 `runtime: il2cpp` 예산으로 판정(+10% 회귀는 `il2cpp/NNN/<profile>` 기준). `logic --spec`이 bench 결과가 있으면 리포트에 합친다 |
| RoadToPro.Tests.EditMode | Tests/EditMode/MatchEngine/Match/*Tests.cs, MatchTestKit.cs | 로직·결정론 |
| RoadToPro.Tests.Performance | Tests/Performance/MatchEngine/MatchPerfTests.cs | EditMode(Mono) 회귀 기준선 |

- 의존: `MatchEngine → Core`만, `Verification → MatchEngine`(이미 있음). 위반 없음.
- 운동 시스템 변경은 005 소유 코드다. 기본값(전원 활성)에서 005 테스트·해시가 그대로인 것을 회귀 조건으로 둔다.

## 데이터 · 스키마
**기간 끝 판정** (FR-21·22): 기간 p의 공식 끝 틱 `E_p = 시작 + 명목 + 실제 추가`. 명목 끝 틱(45:00 등)을 처리하는 틱에서 `AddedTimeAnnounced(분 = ⌈누적 정지 s / 60⌉)`를 낸다. `실제 추가 = max(발표값 × 600틱, 지금까지 누적 정지 틱)`이고, 발표 이후의 정지로 늘어날 수 있다. 틱 t를 처리한 뒤 `t + 1 ≥ E_p`이고 페널티 대기가 아니면 종료 휘슬을 분다. 서브스텝 시각 `(t, k)`의 사건은 그 틱에 있으므로 종료 시각 이하로 친다 (AC-09: 마지막 틱 마지막 서브스텝의 골라인 통과는 골, 다음 서브스텝은 시뮬되지 않음 = 무효). `MatchTimeline`은 종료 후 실제 추가 틱으로 만들어 표기(001 FR-41)에 쓴다. 진행 중 표기는 기간 + 기간 안 틱으로 `MatchTime`을 직접 만든다.

**SimEvent 정렬 키** (FR-32): (Tick, Substep(틱 단위 사건 = 10, 서브스텝 사건 뒤), Stage, 단계 안 발생 순). 물리 사건 → 심판 판정 순서(FR-03): 같은 서브스텝의 사건은 단계 번호로 정렬된다. 같은 서브스텝 안에서는 선수 id 순(물리 단계가 id 순으로 낸다). 진행 이벤트(기간 종료 등)는 단계 10 뒤의 가상 단계 번호 10으로 둔다.

**스냅샷 형식 v1**: `[ver u8][tick i64][seq i64][섹션 길이 u32 + 내용]…`. 섹션: Flow, Squad, Subs, Stoppage, Rng(×n), Ball, Kinematics, Stage[0..9], Recorder. 크기 목표 ≤ 16 KB (22명 × (상태 ~50 B + 대기 명령 ≤ 8 × 41 B + 기록 ~40 B) ≈ 9 KB 최악).

**`Data/tuning/match.json`**: 001 튜닝 형식. 디자인 값이 아닌 대체 단계 값은 태그 `[대체]`. 정지 시간 기본값·가산 계수는 `[미정]` (스펙 FR-20).

## 결정론 · 성능
- RNG: `RngStreams.Create(seed, MatchFlow)`(동전·대체 승부차기), `PlayerDecision`(대체 패스·슈팅 선택), `Referee`(대체 심판). 스트림은 `MatchSim`이 소유하고 단계에 주입하며, 상태는 스냅샷에 넣는다. 정적 RNG는 없다.
- 순회: 선수는 슬롯 id 순, 결정 대상은 id 오름차순 정렬(삽입 정렬), 이벤트는 정렬 키 순. Dictionary 순회 없음.
- 할당 0 경로: 보드·이벤트 대기열·로그·링 버퍼·스냅샷 버퍼를 생성 시 할당한다. 체크포인트 All 모드의 스냅샷 보관과 로그 2배 확장만 예외이고, 할당 측정은 None 모드 + 로그 용량 충분한 상태에서 한다.
- 성능 (스펙 성능 예산·FR-80, 판정 IL2CPP): `farm.py bench --spec 006`이 IL2CPP 릴리스 플레이어에서 측정한다 — ① 뼈대 경기(물리 단계를 정지 상태 대체로 바꾼 경기, 아래 스펙 수정 제안) ② 물리 포함 경기 끝까지 + `IStageTimer` 단계별 평균 (물리 몫 ≤ 67 µs 판정, 나머지는 기록만) ③ 체크포인트 크기·복원 ④ 11초 클립 재구성 ⑤ 이벤트 로그·링 버퍼 메모리(배열 크기 계산). 같은 워크로드를 EditMode 성능 테스트로도 돌려 Mono 회귀 기준선으로 쓴다.

## 테스트 전략
| AC | 테스트 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `MatchLoopTests.StandInMatch_RunsToEnd_ScoreAndLog` | 로직 | — |
| AC-02 | `PipelineOrderTests.RecordingStage_OrderMatchesFr01`, `.PhysicsSubsteps_ExactlyTenPerTick` | 로직 | — |
| AC-03 | `PipelineOrderTests.ReadingLaterStageSlot_Throws` | 로직 | — |
| AC-04 | `DecisionLodTests.ThreeTickPeriod_Count`, `.PeriodSwitch_NoMissOrDouble`, `.SameTick_IdAscending` | 로직 | — |
| AC-05 | `MatchFlowTests.Kickoff_AfterGoal_Conceding`, `.OwnGoal_ConcedingKicksOff`, `.SecondHalf_OtherTeam_SidesSwap` | 로직 | — |
| AC-06 | `SubstitutionTests.*` (6번째·4번째 창 거부, 하프타임 미소모, 뇌진탕 별도, 재투입 거부, 연장 합산, 휘슬과 같은 서브스텝 요청) | 로직 | — |
| AC-07 | `MatchFlowTests.BelowSeven_AbandonsSameTick` (양 팀 동시 포함) | 로직 | — |
| AC-08 | `StoppageTests.Announced_GeqAccumulated`, `.Actual_GeqAnnounced`, `.PenaltyDelaysWhistle`, `.VarDisallowed_DoesNotShrink` | 로직 | — |
| AC-09 | `StoppageTests.GoalOnLastSubstep_Counts_NextIsVoid` (고정 배치) | 로직 | — |
| AC-10 | `ShootoutTests.*` (연장 → 승부차기, 5-5 이후 서든데스, 11명 순환, 연장 없음 동점 종료, 정규시간 승부 시 연장 없음, GK 부상) | 로직 | — |
| AC-11 | `SimEventTests.SeqMonotonic_SortKeyMatchesOutput`, `.TimeFormat_90Plus3` | 로직 | — |
| AC-12 | `MatchDeterminismTests.SameSetup_TwoRuns_SameHashes` | 결정론 | — |
| AC-13 | `MatchDeterminismTests.StepByStep_EqualsRunToEnd`, `.EightConcurrent_EqualsSequential` (테스트에서만 스레드) | 결정론 | — |
| AC-14 | `SnapshotTests.RestoreAt60_EqualsUninterrupted` (+ 틱 0, 체크포인트 경계, 마지막 틱) | 결정론 | — |
| AC-15 | `KeyMomentResimTests.Before60Unchanged`, `.SameInputTwice_Same` | 결정론 | — |
| AC-16 | `ReconstructionTests.Random100Ranges_BitEqual` | 결정론 | — |
| AC-17 | `ClipTests.GoalClip_330Frames_WithEvents`, `.EarlyGoal_ClampedAtZero` | 로직 | — |
| AC-18 | `SubscriberTests.ThrowingSubscriber_SimContinues_SameHash`, `.Subscriber_ReadOnly` | 로직 | — |
| AC-19 | `FailureTests.NaNInStage_FailedResult_OthersOk` | 로직 | — |
| AC-20 | 보류 (016) | 로직 | — |
| AC-21 | `farm.py bench --spec 006` (IL2CPP) + `MatchPerfTests.*` (Mono 회귀) | 성능 | mac-m1-1600p |
| AC-22 | `CheckpointModeTests.None_Equals_All` | 로직 | — |
| AC-23 | `ContactEventTests.TenThousand_NoMissingBodyPartOrSubstep_Repeatable` | 로직 | — |
| AC-24 | `RawTrajectoryTests.OnOff_SameHash_ResampleEqualsFrames` | 결정론 | — |

## 버그 방지
- 정확히 45:00·90:00 틱, 추가시간 0분(발표 0, 바로 종료), 추가시간 중 VAR 취소(발표값 유지) → `StoppageTests`.
- 같은 틱의 파울+골, 같은 서브스텝 두 선수 접촉 → 정렬 키 테스트(`SimEventTests.SameSubstep_OrderedByStageThenId`).
- 체크포인트 경계(정확히 10초 배수)에서 복원: 체크포인트는 "틱 t 처리 후" 상태로 정의하고, 복원 후 첫 틱은 t+1이다. 틱 0 = 킥오프 전 상태 → `SnapshotTests`.
- 복원 직후 같은 틱 입력 주입 → `KeyMomentResimTests.InjectAtRestoreTick`.
- 결정 LOD 주기 전환 순간 → AC-04 테스트.
- 일련번호 64비트, 150분 경기 메모리(로그 확장 1회 이내) → `SimEventTests.LongMatch_150Min`.
- 입력 검증: 선발 11명 미만, 중복 id, 벤치 0명(교체 요청 거부) → `MatchSetupTests`.
- 운동 시스템 변경 회귀: 005 테스트 전체 + 005 결정론 해시 그대로 (`KinematicsSnapshotTests.WriteRead_RoundTrip_SameHash`, `.Inactive_SkippedInSeparation`, `.ResetSlot_NewProfile`).
- 스냅샷 누락 탐지: 복원 후 즉시 상태 해시 == 캡처 시 상태 해시, 그리고 1분 진행 후 해시 비교 (누락된 필드는 진행 후 갈라진다).
- 구독자 재진입·예외 → 격리 테스트.

## 스펙 수정 (2026-11-03 플랜 승인 때 반영)
- **성능 예산 "경기 1개 끝까지 (대체 단계만, 루프 뼈대) ≤ 100 ms"**: 005 실측 이후 물리 단계만 IL2CPP 몫으로 틱당 최대 67 µs라, 54,000틱이면 물리만 약 3.6 s다. 그래서 100 ms는 물리를 포함하면 지킬 수 없다. 제안: 이 항목을 **"루프 뼈대 = 물리 단계를 정지 대체로 바꾼 경기(단계 실행·보드·이벤트·체크포인트 해시·진행 판정)" ≤ 100 ms**로 명확히 한다 (틱당 ≈ 1.85 µs, FR-80 "이벤트 발행·체크포인트 해시 2 µs" 몫과 맞음). 물리를 포함한 경기는 FR-80 물리 몫(≤ 67 µs/틱)으로 판정한다.

## 위험 · 미정
- **IL2CPP 모듈 미설치**: 이 Mac의 Unity 6000.3.27f2에는 `MacStandaloneSupport`의 Mono 변형만 있다. Unity Hub에서 **Mac Build Support (IL2CPP)** 모듈을 설치해야 한다 (Xcode 16.3은 있음). 설치 전에는 bench 태스크(T22)에서 멈춘다.
- **스크립팅 백엔드 전환**: `ProjectSettings/ProjectSettings.asset`의 백엔드 값을 빌드 동안만 IL2CPP로 바꾸고 되돌린다. 커밋되는 파일은 바뀌지 않게 하고(되돌린 뒤 `git diff`로 확인), 규칙상 ProjectSettings를 건드리는 일이라 이 플랜 승인으로 허락을 받는다.
- 대체 단계 결과(득점 분포 등)는 의미가 없다. 테스트는 구조(순서·규칙·결정론)만 본다. 평균 득점 2.5~3.0은 017 몫이다.
- 링 버퍼 + 체크포인트 All 모드의 메모리(약 8.6 MB + 1.2 MB)는 017 경기 메모리 피크 32 MB 안이다.
- 심판 3명 위치는 012 전까지 대체 단계(주심: 공 뒤 10 m 대각, 부심: 터치라인 위 공 x 추적)로 채운다.
- MotionHint 목록은 043과 합의 전 기본값이다 (스펙 [미정]).
- 결정론 리뷰(T17) 참고 항목 — 반영 안 함: ① 진행 판정·기록기의 고정 상수(킥오프 압축 0.47·센터서클 9.6 m, 퇴장 위치, 페널티 대기 3 s, 이동 힌트 속도 구간, 심판 7 m/s, 대체 잡기 규칙)는 012·043·008이 대체할 때 튜닝 데이터로 옮긴다. ② 스냅샷에 시드·튜닝 해시 지문이 없다 — 이벤트 로그 앞부분 해시 검사로 다른 경기 복원은 거부되지만, 같은 로그·다른 튜닝은 잡지 못한다(경기 중 저장이 없어 메모리 안에서만 쓰므로 위험 낮음). ③ `MatchParams`의 초 → 틱 반올림은 float 곱 뒤 double 반올림 — 같은 빌드 결정론은 지키며, 값이 .05초 경계에 걸리면 직관과 1틱 다를 수 있다. ④ 결정 단계에서 재시작 키커가 공을 잡는 것·퇴장 때 공을 놓는 것은 이벤트가 없다(재구성은 재시뮬이라 결정론 문제 아님).
- 설계 변경 (구현 중): 교체 요청은 외부 입력(`InputKind.Substitute`·`SubstituteConcussion`)으로 기록해 틱 시작에 대기열에 넣는다 (리뷰 B2). 같은 틱 요청은 (틱, 나가는 선수 id) 순.

