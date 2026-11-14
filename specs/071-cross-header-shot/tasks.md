# 071 — 크로스 · 공중볼 · 헤더 · 슈팅 · xG · 태스크

상태: 승인됨 (2026-11-14)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트를 먼저 쓰는 태스크는 같은 블록의 구현과 한 Unity 배치로 돌린다(실패 확인 → 구현 → 통과).
중간 검증은 `.claude/scripts/unity-test.sh EditMode <클래스 필터>`로 좁힌다. `farm.py logic --scope auto`·`bench`·`/verify`는 마지막 태스크에서 각 1회 (`.claude/rules/05-sdd.md` "검증 범위").

경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `A/` = `ME/Actions/`, `D/` = `ME/UtilityAI/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/Actions/`.

## A. 기반
- [x] T01 튜닝·능력치·이벤트·공 동작 필드 — 파일: `Data/tuning/action.json`(`act.xg/shot/aim/block/cross/aerial/header/pen.*`), `A/{ActionParams,ActionTuningIds,TechnicalAttributes,ActionWorld}.cs`(Heading·Jumping·HeightCm, Finishing·LongShots·Crossing·Stamina·Composure 바인딩), `BallAction`·`A/PendingKick.cs`(ShotCell·ShotPower·CrossType·IsHeader, 스냅샷), `ME/Match/Events/SimEventKind.cs`(`Header = 215`), `Presentation/Players/Data/043-anim-map.json`(Header) — 검증: 컴파일, 튜닝 파일=코드 기본값 테스트, `PendingKick` 스냅샷 왕복, `AnimationMapTests`, 008 `Actions*` 필터 회귀 — 결과: 튜닝 80항목(json·Ids·Params 한 스크립트로 생성), `ActionParamsTests|AnimationMapTests|KickContactTests|ActionDetailLogTests` 20/20. 플랜과 다른 점: 공 동작 필드는 `Match/Pipeline/TickBoard.cs`의 `BallAction`에 `HasShot·ShotCell·ShotPower·ShotXg·CrossType`(IsHeader 대신 헤더는 경합 시스템이 직접 실행), 해시는 쓸 때만(안 쓰면 해시 불변). `MotionHint.Header`는 추가하지 않고 Kick 힌트 + `Header` 이벤트 클립 매핑(header_jump·header_stand)으로 처리 — 리플레이 형식 변경 없음. 버릇 레벨(컷인 장인·크로스 머신)도 `ActionWorld.SetFinish`로 바인딩

## B. 평가 · 실행 모델 (FR-01~04·10·11·13·14·17, RNG 없는 함수 + 고정 소비 표본)
- [x] T02 테스트 — 파일: `T/ShotModelTests.cs`, `T/ShotAimTests.cs`, `T/ShotExecutionTests.cs`, `T/CrossModelTests.cs`, `T/AerialModelTests.cs`, `T/ShotAttributeTests.cs` — 검증: AC-01(10,000회 30 % 이상·이항 p < 0.01), AC-02(순서·헤더 0.5~0.6배·블로커 감소·0~1·NaN 0), AC-04(σ 10만 회 ±3 %), AC-05(18 vs 8 F 검정·컷인 0.9배 ±3 %), AC-07(GK 왼쪽 1 m → 오른쪽 셀 이항 p < 0.01, 선택 = argmax), 크로스 타입 파라미터 범위, 평가 함수 RNG 미사용, 표본 RNG 소비 고정(슛 4·크로스 4·경합 1·페널티 방향 1) — 실패 확인 — 결과: 6클래스 23개 + 공용 `ShotTestKit`(z·F 임계값). 테스트와 구현을 한 배치로 써서 실패 단계는 컴파일 전 상태로 갈음. AC-02 헤더 0.5~0.6배는 페널티 지점·박스 안 3지점에서 확인(로지스틱 특성상 6 m 안 고xG 지점은 0.6배를 넘음 — 스펙 "0.5~0.6배로 시작" 범위 해석)
- [x] T03 구현 — 파일: `A/{ShotContext,ShotModel,ShotAim,IShotKeeperModel,StandInKeeperBeat,ShotExecution,CrossModel,AerialModel}.cs` — 검증: T02 통과 — 결과: 23/23. `ActionMath.NormalCdf`(A–S 7.1.26) 추가. 각도는 atan2(7.32x, x²+y²−3.66²) = 스펙 식 + π 규칙. 조준 P_onTarget은 가로 σ를 진입각 코사인으로 보정(하한 0.2), 세로 하한 없음(낮은 공은 바운드)
- [x] T04 경계 · 예외 — 파일: `T/ShotAerialEdgeCaseTests.cs`, T03 파일 보완 — 검증: AC-06 (경합 0명·큰 softmax·골라인 위·골대 뒤·조준 셀 전부 0 → 먼 하단·GK 헤더·극단 능력치 0.001~0.999, NaN·예외 0) — 결과: 6/6 (첫 실행 1건은 테스트 가정 오류 — 최대 입력 xG 0.995가 자르기 전이라 0.999 기대가 틀림, 범위 밖 입력으로 자르기 확인하게 수정)

## C. 경기 연결 — 슈팅 · 블록 · 페널티 (FR-12·14·15·16·17)
- [ ] T05 테스트 — 파일: `T/ShotStageTests.cs`, `T/ShotBlockTests.cs`, `T/Penalty071Tests.cs` — 검증: 072 Shoot → 예약 슛(셀·파워) → 접촉 서브스텝 실행·Shot 이벤트 F1 = xG, 결정 RNG 대체 3회 소비 없음, 블록(선분–원기둥, 슈터·같은 팀·GK 제외, 팔 영역 → Arm 접촉·핸드볼 입력), AC-03(페널티 xG 0.78 = Shot F1), 페널티 자연 측 비율 0.6 ± 0.02(10,000회) — 실패 확인
- [ ] T06 구현 — 파일: `A/{ShotContextBuilder,ShotBlock}.cs`, `A/ActionSystem.cs`(슛 실행·`OnShotFlight`), `ME/Match/Stages/{UtilityDecisionStage,PhysicsStage}.cs`, `ME/Match/Stages/StandIn/StandInOnBall.cs`(`ShootWithXg` 제거·페널티 071), `D/StandInOnBallEval.cs`(`ShotXg` → `ShotModel.Xg`), `ME/Scenarios/StepExecutor.cs`(Shot 단계) — 검증: T05 통과, `OnBall*`·`Goalkeeper*`·`Scenario*` 필터 회귀(고정 해시 갱신 목록·사유를 결과 줄에)

## D. 경기 연결 — 크로스 · 공중볼 · 헤더 (FR-01·02·03·05·06·20)
- [ ] T07 테스트 — 파일: `T/CrossStageTests.cs`, `T/AerialSystemTests.cs` — 검증: 072 Cross → 타입 선택(결정 RNG 1) → Cross 이벤트 A = 타입, 뜬 공 착지 오차, 경합 예약·경합 서브스텝(공이 접촉 높이 아래로 처음 들어온 순간)·참여 조건(반경 2 m·TTI ≤ t_land + 0.3)·승자 결과(헤딩슛·헤딩 패스·클리어)·Header 이벤트, 공이 먼저 다른 경로로 바뀌면 예약 취소, 다운 선수 제외, GK 승리 → 011 잡기 경로, AC-08(동작 정보·정점 t_land ± 0.5 s·Aerial 접촉 필드 전부) — 실패 확인
- [ ] T08 구현 — 파일: `A/{AerialSystem,AerialDetail}.cs`, `A/ActionDetailLog.cs`(`KindAerial = 3`), `A/ActionSystem.cs`(크로스 실행·`Aerials` 보유·해시·스냅샷), `A/ActionOutcome.cs`, `ME/Match/Stages/{UtilityDecisionStage,PhysicsStage}.cs`, `ME/Scenarios/StepExecutor.cs`(Cross 단계), `ME/Match/Replay/*`(`MotionHint.Header`·스냅샷 섹션) — 검증: T07 통과, `Actions*`·`Duel*`·`Match*` 결정론·스냅샷 필터 회귀

## E. 결정론 · 성능 · 측정
- [ ] T09 결정론 — 파일: `T/ShotAerialDeterminismTests.cs` — 검증: AC-09 (크로스·경합·슈팅·페널티 10만 건 2회 해시 동일, 액션당 RNG 소비 고정 표), `determinism-reviewer` 리뷰(코어 변경)
- [ ] T10 성능 — 파일: `Assets/_Project/Tests/Performance/MatchEngine/ShotAerialPerfTests.cs`, Verification `ShotAerialBenchWorkloads`(`--spec 071`) — 검증: AC-10 (xG ≤ 0.2 µs·조준 ≤ 3 µs·경합 ≤ 1 µs·GC 0 B, Mono 측정)
- [ ] T11 비율 하네스 · 100경기 측정 — 파일: `T/ShotRateHarnessTests.cs`, 측정 결과는 `evidence/` — 검증: AC-11 보류 기록(고정 상황 크로스 성공률·헤딩 전환율·슛 전환율), 100경기 경기당 슈팅·슈팅당 xG·크로스·헤더·블록·득점 표. 득점 > 16/경기 또는 슈팅 증가면 멈추고 보고

## F. 화면 · 검증
- [ ] T12 E2E 장면 — 파일: `Data/e2e/071-cross-shot.json`(058 실경기 경로, 고정 시드 3장면: 컷백 슛 골·크로스 공중볼 헤더·30 m 중거리 슛), 필요하면 `LiveSceneSelector`(Header 장면 점수) — 검증: `farm.py shot --scenario 071-cross-shot`(mac-m1-1600p) 스크린샷 직접 확인 → **사용자 육안 승인**(AC-12), 헤더 접촉 프레임 머리–공 거리 로그
- [ ] T13 `/verify 071` — 커밋 → `farm.py logic --spec 071 --scope auto`(MatchEngine 변경 → `match`) 1회 + `farm.py bench --spec 071`(시뮬 틱 경로 변경) 1회 → `evidence/report.html` 아티팩트 발행. AC-11·13 보류 사유 기록
