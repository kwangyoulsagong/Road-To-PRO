# 004 — 공 물리 · 공 상태 머신 · 태스크

상태: 승인됨
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면). C# 테스트는 `/unity-test` (`.claude/scripts/unity-test.sh EditMode <필터>`). 코어(MatchEngine) 변경 블록 끝에서 `determinism-reviewer` 리뷰. Unity 배치 1회가 길어서 테스트 선작성 태스크는 같은 블록의 구현과 한 번에 돌려 "구현 전 실패"를 확인한다.

## A. 데이터 · 파라미터
- [x] T01 튜닝 정의 파일 — 파일: `Data/tuning/ball.json` (플랜 데이터 표 전부, 출처 태그 포함) — 검증: `TuningLoader.ParseDefinitions`로 로드 성공 (T02 테스트) — 결과: 정의 38개(출처 태그 포함, [제안] 4개). 현대 공 CdLow 0.45는 리서치 A-8 값
- [x] T02 파라미터 테스트 — 파일: `Tests/EditMode/MatchEngine/Ball/BallTestKit.cs`, `BallParamsTests.cs` — 검증: 정의 파일 로드, 공 종류·표면 5개 매핑(플랜 표), β ≈ 0.053, 잘못된 값(a<0, e>1) 로딩 거부 (AC-12 일부) — 컴파일 후 실패 확인 — 결과: `BallParamsTests` 12개(표면 5개 매핑·β·기본 공 종류·잘못된 값 거부). Unity 배치 1회가 길어 블록 A~B를 한 배치로 돌림 — 구현 전 실패 단계는 따로 확인하지 않음
- [x] T03 상태·타입·파라미터 구현 — 파일: `MatchEngine/Runtime/Ball/{BallPhase,BallState,BallType,SurfaceCondition,BallTuningIds,BallParams}.cs` (정지거리 표는 T13에서) + 테스트 asmdef에 `RoadToPro.MatchEngine` 참조 확인 — 검증: `BallParamsTests` 통과 — 결과: 통과. 테스트 asmdef에 MatchEngine 참조가 이미 있음. `BallState`에 생성 팩토리(AtRest·Controlled·InAir·RollingAt) 추가, `BallTransitions.IsAllowed`로 전이 그래프를 한 곳에 정의

## B. 공기역학 · 적분 · 상태 머신
- [x] T04 비행·구름·마그누스·바운스 테스트 — 파일: `Tests/EditMode/MatchEngine/Ball/{BallRollingTests,BallFlightTests,BallMagnusTests,BallBounceTests,BallStateMachineTests}.cs` — 검증: 컴파일 후 실패 확인 (AC-01~08) — 결과: 5파일 + `BallFlightKit` 도우미. AC-07은 √ 관계를 항력 0으로 확인 + 항력 포함 상태에서 바운스 속도비 = e 확인 (항력 포함 반발 높이는 −10%: 낙하·상승 공기저항, AC-03이 항력 검증)
- [x] T05 공기역학 — 파일: `MatchEngine/Runtime/Ball/BallAero.cs` — 검증: Cd(v) 경계값, C_L(Sp), 8 m/s 미만 0·페이드 구간 (`BallMagnusTests.Below8ms_MagnusZero`) — 결과: `Cd` 경계값·C_L·페이드 테스트 통과. exp 오버플로 방지(x > 60 → CdHigh)
- [x] T06 이벤트·명령 — 파일: `MatchEngine/Runtime/Ball/{BallEvent,BallEventBuffer,BallCommands}.cs` — 검증: 킥 클램프 + 경고 이벤트, 버퍼 용량 초과 시 예외 — 결과: 클램프 경고·버퍼 초과 예외 통과. `BallEventBuffer.AddTo`(이벤트 해시) 추가
- [x] T07 서브스텝 적분기 (Air·지면 바운스·Rolling·Rest) — 파일: `MatchEngine/Runtime/Ball/BallStepper.cs` — 검증: `BallRollingTests`(AC-01·02), `BallFlightTests`(AC-03·04), `BallMagnusTests`(AC-05·06), `BallBounceTests`(AC-07·08), `BallStateMachineTests`(AC-08) 통과 — 결과: AC-01(거리 오차 ≤0.6%·시간 ≤0.9%)·02·03(사거리 −1.3~0%)·04(−0.97%)·05·06·07·08 통과. AC-05 베컴 기준 2~4 m → 2~6 m로 스펙 수정(사용자 승인, 모델 5.3 m). 플랜과 달라진 점: `BallSegment.cs` 별도 파일

## C. 골대 · 경계 · 골망
- [x] T08 경계·골대·예외 테스트 — 파일: `Tests/EditMode/MatchEngine/Ball/{BallBoundaryTests,BallEdgeCaseTests}.cs` — 검증: 컴파일 후 실패 확인 (AC-11·12) — 결과: 경계 5개·예외 9개 테스트. 블록 C~E는 구현과 한 배치로 돌려 "구현 전 실패" 단계는 생략
- [x] T09 골대 기하·접촉·경계 통과·골망 — 파일: `MatchEngine/Runtime/Ball/GoalFrame.cs`, `BallStepper.cs` (골대 swept 접촉, 경계 통과 보간, 골망 감속) — 검증: `BallBoundaryTests`, `BallEdgeCaseTests` 통과 (AC-11·12) — 결과: 통과. 골망 영역에서 정지 확인, 포스트 반발 0.6 확인

## D. 예측 · 역산
- [x] T10 예측기 테스트 — 파일: `Tests/EditMode/MatchEngine/Ball/BallPredictorTests.cs` — 검증: 컴파일 후 실패 확인 (AC-09) — 결과: 예측 = 실제 오차 0 (0.04·0.1 s, 5종 킥: 골대·골망·크로스바 포함), 상태 불변, 잘못된 인자 예외
- [x] T11 예측기 — 파일: `MatchEngine/Runtime/Ball/{BallPredictor,BallPrediction}.cs` — 검증: `Prediction_EqualsActual_10s_Exact`, `…_DoesNotMutateState`, 잘못된 샘플 간격·버퍼 부족 예외 (AC-09) — 결과: 통과
- [x] T12 역산 테스트 — 파일: `Tests/EditMode/MatchEngine/Ball/BallInverseTests.cs` — 검증: 컴파일 후 실패 확인 (AC-10) — 결과: 땅볼 4가지 공/표면 × 5~45 m(0.5 m 간격), 잔여속도, 도달 불가, 뜬공 3케이스·정점 초과
- [x] T13 역산 — 파일: `MatchEngine/Runtime/Ball/BallInverse.cs`, `BallParams.cs`(정지거리 표) — 검증: `BallInverseTests` 통과 (AC-10) — 결과: 통과. 땅볼은 표 보간만(2026-11-02 결정: 런타임 재검증 제거, FR-33 문구 수정) — `BallRollTable.cs` 별도 파일, 표가 4096칸을 넘는 튜닝은 로딩 거부

## E. 진단 · 결정론
- [x] T14 진단 출력 — 파일: `MatchEngine/Runtime/Ball/IBallDiagnostics.cs`, `BallStepper.cs`(null이면 호출 0) — 검증: `BallDiagnosticsTests.DiagOnOff_SameEventAndCheckpointHash` (AC-16) — 결과: `DiagOnOff_SameEventAndCheckpointHash` 통과(500 초기 조건 + 예측기 진단). `IBallDiagnostics`는 `in` 파라미터로만 받음
- [x] T15 결정론 테스트 — 파일: `Tests/EditMode/MatchEngine/Ball/BallDeterminismTests.cs` (`[Category("Determinism")]`, 고정 시드 초기 조건 10,000개 × 2회) — 검증: 해시 전부 같음 (AC-13) — 결과: 1만 초기 조건(공 2종 × 표면 5종) × 2회, 체크포인트·전체 해시 일치
- [x] T16 결정론 리뷰 — `determinism-reviewer`로 `Assets/_Project/MatchEngine/Runtime/Ball/**` 리뷰, 지적 반영 + 회귀 테스트 — 검증: 리뷰 지적 0 (또는 반영 후 재리뷰 통과) — 결과: 1차 리뷰 차단 1(B-1 `BallParams` 공유 버퍼 → 병렬 경기에서 갈림) + 권장 6(소유자 교체 이벤트, 뜬공 미수렴 Ok, 이벤트 시각 축 불일치, 버퍼 계약, Sanitize 침묵, 경계 이벤트 phase) + 참고 7. 반영 중 새 차단 NB-1(지면이 먼저인데 골대 반발 적용) 발견 → 수정, 3차 확인에서 차단 0. 회귀 테스트 `BallReviewRegressionTests` 9개. 반영 안 한 참고 사항은 plan "위험"

## F. 성능 · 마무리
- [x] T17 성능 테스트 — 파일: `Tests/Performance/MatchEngine/BallPerfTests.cs`, `Data/perf/004-ball-physics.json` (스펙 성능 예산 표 사본 + 테스트 네임스페이스 `RoadToPro.Tests.EditMode.MatchEngine`, `RoadToPro.Tests.Performance.MatchEngine`) — 검증: 서브스텝 상태별·가중 평균(플랜 가중치), 예측 10 s, 땅볼 역산, 할당 0(진단 끔 포함) 예산 안 (AC-14·16) — 결과: 가중 평균 ≈95 ns(예산 100), Air ≈226 ns(300), 예측 10 s ≈218 µs(예산 20→250 µs로 스펙 수정, 사용자 승인), 땅볼 역산 ≈0.05 µs(30), 할당 0. 최적화: 적분 스칼라화, 경계·골대·골망 빠른 배제, 수치 검사 빠른 경로, 꺾인 경로는 골라인 근처에서만
- [x] T18 수용 기준 전체 테스트 통과 — EditMode 전체(`/unity-test`) 실패 0, 콘솔 에러·경고 0, 컴파일 경고 신규 0 — 결과: EditMode 238개 중 통과 235·실패 0·건너뜀 3(000 셀프테스트 전용). 컴파일 경고 신규 0(CS0162 1건 수정). 콘솔 에러는 001 `UnityLoggerTests`의 기대 로그뿐
- [ ] T19 `/verify 004` — `farm.py logic --spec 004` 리포트(로직·결정론·성능), 첫 성능 기준은 사용자 승인 후 `approve-perf`, 리포트 아티팩트 발행. AC-15는 040 이후라 리포트 판정은 AC-15 누락으로 fail이 정상 — 나머지 AC 15개 전부 통과 확인. spec 상태는 "구현 완료 · AC-15 보류", README 표 갱신
