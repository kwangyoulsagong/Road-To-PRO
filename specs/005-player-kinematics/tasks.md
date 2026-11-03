# 005 — 선수 운동학 · Steering · 태스크

상태: 구현 완료 · AC-14 보류 (승인 2026-11-03, 플랜 승인과 함께 바로 진행)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다. C# 테스트는 `/unity-test` (`.claude/scripts/unity-test.sh EditMode <필터>`). Unity 배치 1회가 길어서 테스트 선작성 태스크는 같은 블록의 구현과 한 배치로 돌린다. 코어(MatchEngine) 변경 블록 끝에서 `determinism-reviewer` 리뷰.

## A. 데이터 · 매핑
- [x] T01 튜닝 정의 파일 — 파일: `Data/tuning/kinematics.json` (플랜 데이터 표 전부, 출처 태그, k_a·만성 계수는 임시값 `[제안]`) — 검증: `TuningLoader.ParseDefinitions` 로드 성공 (T02 테스트) — 결과: 정의 62개(출처 태그, [제안] 표시). 플랜 표 외 추가 3개 — `kin.sep.pushSpeed`(밀어내기 속도), `kin.jockey.minApproach`(공격수 정지 시 접근 하한), `kin.follow.gain`(움직이는 목표점 보정 이득) — 모두 [제안], 기획서에 값 없음. T12에서 `kin.intercept.controlRadius` 0.8·`kin.tti.turnCost` 1.5 추가
- [x] T02 매핑 테스트 — 파일: `Tests/EditMode/MatchEngine/Kinematics/{KinematicsTestKit,KinematicsMappingTests}.cs` — 검증: AC-01, 능력치 범위 밖 거부(AC-11 일부) — 결과: `KinematicsMappingTests` 9개(AC-01, B-7 범위, 단조성, 범위 밖·NaN 거부, 튜닝 조합 거부). Unity 배치가 길어 블록 A~F를 한 배치로 돌림 — 구현 전 실패 단계는 생략, 대신 스크래치(.NET) 하네스로 수치를 먼저 확인
- [x] T03 입력·파라미터·프로필 — 파일: `MatchEngine/Runtime/Kinematics/{KinematicAttributes,KinematicsTuningIds,KinematicsParams,PlayerMotionProfile}.cs` — 검증: `KinematicsMappingTests` 통과 — 결과: 통과. 능력치 입력은 float(내부 0~100) — 표시값 N의 대표값 100·(N−1)/19가 정수가 아니라서

## B. 한계 풀이 · 적분
- [x] T04 스프린트·곡선·Arrive 테스트 — 파일: `Tests/EditMode/MatchEngine/Kinematics/{SprintTests,CurveTests,ArriveTests}.cs` — 검증: AC-02~05 (구현 전 컴파일 실패) — 결과: 스플릿 능력치 15: 2.01/3.23/4.37/5.49 s, 16: 1.99/3.19/4.32/5.42 s (전부 ±0.1 안). 95% 도달 3.32 s vs 3τ 3.29. AC-04는 반경 5 m 원을 √(a_lat·R)로 제한해 도는 경우와 전력으로 노리는 경우(실제 반경 6.4 m로 커짐) 둘 다 확인
- [x] T05 상태·명령·큐 — 파일: `MatchEngine/Runtime/Kinematics/{PlayerMotionState,MotionStats,SteeringCommand,CommandQueue}.cs` — 검증: 큐 지연 활성화·넘침 처리 단위 테스트 — 결과: `CommandQueueTests` 4개 (지연 활성화, 10 Hz 재발행, 넘침 시 오래된 것 버림 + 진단, 즉시 적용)
- [x] T06 한계 풀이·피벗·적분 + Seek/Arrive — 파일: `MatchEngine/Runtime/Kinematics/{MotionSolver,Steering}.cs`(Seek·Arrive만), `KinematicsSystem.cs`(단일 선수 경로) — 검증: AC-02~05 통과 — 결과: AC-02~05 통과, 위반 0. 감속 시작 6.16 m (기대 6.31 ±10%), 오버슈트 0. 횡가속은 방향 회전으로 적용(곡선에서 속력이 부풀지 않음)

## C. 조향 행동 · 합성 · 겹침
- [x] T07 행동·겹침 테스트 — 파일: `Tests/EditMode/MatchEngine/Kinematics/{JockeyTests,SeparationTests,SteeringBehaviorTests}.cs` — 검증: AC-07·08, Pursue·Evade·마킹 기하 — 결과: 저킹 3케이스(2·3·3.8 m/s) 거리 2.01~2.02 m, 백페달 상한·접근 상한, 22명 혼잡 2시드, Pursue·Evade·마킹·합성 기하. 저킹 0.9 규칙은 "접근할 때만 공격수 속도 × 0.9(하한 1.5 m/s), 후퇴는 4 m/s 상한"으로 해석(리서치 02 §3.4 "접근 시 감속") — 문자 그대로 "항상 0.9배"면 AC-07(거리 유지)과 모순
- [x] T08 나머지 행동·합성·겹침 해소·경계 — 파일: `Steering.cs`(Pursue·Evade·OffsetPursuit·OffsetArrive·Formation), `SteeringCombiner.cs`, `Separation.cs`, `KinematicsSystem.cs`(22명·스냅숏·경계·ApplyContact) — 검증: AC-07·08 통과 — 결과: 통과. 마킹·저킹은 대상 속도 앞먹임 + 위치 오차 보정(서브스텝마다 대상 스냅숏을 읽음 — 인지 지연은 명령 전환에만 적용). 저킹 백페달 상한은 Separation 밀어내기를 합친 뒤 다시 자른다(첫 배치 실패 수정). 겹침 해소는 목표 간격 0.84 m(여유 0.04)·반복 4회·선수당 투영 총량 0.35 m — "22명이 한 점으로 Arrive" 가혹 케이스에서 0.79 m로 남던 압축 수정

## D. 피로 · 기록
- [x] T09 피로·기록 테스트 — 파일: `Tests/EditMode/MatchEngine/Kinematics/{FatigueTests,MotionStatsTests}.cs` (표준 활동 프로필은 테스트 킷) — 검증: AC-09·10, FR-50 횟수 판정 — 결과: `FatigueTests` 5개, `MotionStatsTests` 2개
- [x] T10 피로·기록 구현 + 계수 보정 — 파일: `Fatigue.cs`, `MotionStats.cs`, `KinematicsSystem.cs`, `Data/tuning/kinematics.json`(k_a·c_d·c_h 확정값) — 검증: AC-09·10 통과 — 결과: k_a 0.16 → 30 m 스프린트 4회 후 W 0.30, 60 s 후 0.73. c_d 6.3e-5·c_h 2.1e-4 → 표준 프로필(약 10.6 km·고강도 1.3 km) 지구력 5/10/20 = C 0.88/0.74/0.49. 튜닝 데이터 [제안] 갱신

## E. TTI · 공 인터셉트
- [x] T11 TTI·인터셉트 테스트 — 파일: `Tests/EditMode/MatchEngine/Kinematics/{TimeToInterceptTests,InterceptTests}.cs` — 검증: 정지·먼 거리 근사식 일치(FR-40), AC-06 — 결과: `TimeToInterceptTests` 5개(점근식, 실제 직선 주행과 ±0.03 s, 방향·피로 효과, d=0), `InterceptTests` 2개
- [x] T12 TTI·인터셉트 구현 — 파일: `TimeToIntercept.cs`, `Steering.cs`(InterceptBall) — 검증: AC-06 ≥ 98% — 결과: AC-06 99.4%(1,000/1,000 탐색 성공 케이스 중). 바꾼 점: 인터셉트 행동은 TTI 가정과 같게 "전력 Seek, 컨트롤 반경 안에서만 Arrive"(Arrive만 쓰면 감속 때문에 늦음), TTI에 반대 방향 정지 거리·옆 속도 제거 비용(`kin.tti.turnCost`) 추가 — 0.5 → 86%, 1.0 → 93%, 1.5 → 99.4%

## F. 예외 · 진단 · 한계 모니터 · 결정론
- [x] T13 예외 상황 테스트 — 파일: `Tests/EditMode/MatchEngine/Kinematics/KinematicsEdgeCaseTests.cs` — 검증: AC-11 전 항목 통과 (필요한 수정 포함) — 결과: `KinematicsEdgeCaseTests` 7개 통과. 발견·수정: 피로로 Vmax_eff가 내려가는 중 횡가속을 한계까지 쓰면 마찰원이 F-V 초과 속도 감속까지 막아 속도가 Vmax_eff 위에 머묾 → 초과 속도 감속을 마찰원에서 먼저 확보
- [x] T14 진단·한계 모니터 — 파일: `IKinematicsDiagnostics.cs`, `MotionLimitMonitor.cs`, `Tests/EditMode/MatchEngine/Kinematics/{KinematicsDiagnosticsTests,MotionLimitTests}.cs` — 검증: AC-15, AC-16 (합성 1,000경기 × 5분 위반 0, 강제 위반 정확 보고) — 결과: AC-15 통과(진단·모니터 켬/끔 해시 동일). AC-16 합성 1,000경기 × 5분 위반 0(스크래치 .NET 87 s, Mono 에디터는 수 분 → `Timeout` 20분), 강제 위반은 그 서브스텝·선수만 보고. 모니터 절대 여유 0.05 m/s²(float 해상도 1e-4 m/s² 수준 오탐 제거)
- [x] T15 결정론 테스트 — 파일: `Tests/EditMode/MatchEngine/Kinematics/KinematicsDeterminismTests.cs` `[Category("Determinism")]` — 검증: AC-12 — 결과: 90분 22명(54만 서브스텝) 2회 해시 일치, 입력 순서 섞어도 일치
- [x] T16 결정론 리뷰 — `determinism-reviewer`로 `MatchEngine/Runtime/Kinematics/**` 리뷰, 지적 반영 + 회귀 테스트 — 검증: 차단 0 — 결과: 차단 0, 권장 6 반영 — R1 해시에 접촉·검증 결함 플래그, R2 Separation 후보 반경을 튜닝에서 계산(몸 지름·Vmax 상한·투영 총량), R3 Facing 정리(움직이면 속도 방향, 0이면 +x), R4 NaN/무한대 입력 즉시 거부, R5 명령 활성화 시각 단조, R6 TTI 회전 비용 튜닝 데이터로. 회귀 테스트 `KinematicsReviewRegressionTests` 5개. 반영 안 한 참고(진단 콜백 재진입, 같은 서브스텝 접촉 2회 곱, 프로필 해시 미포함, `InjectVelocityFault` 공개)는 plan "위험"

## G. 성능 · 마무리
- [x] T17 성능 테스트 — 파일: `Tests/Performance/MatchEngine/KinematicsPerfTests.cs`, `Data/perf/005-player-kinematics.json` (예산 표 사본, `deferred: {AC-14}`) — 검증: AC-13 예산 안, 할당 0 — 결과: Mono 에디터 실측 — 서브스텝 22명 평균 11.8 µs·p95 14.0 µs, TTI 203 ns, 인터셉트 9.0 µs, 할당 0. 원래 예산(1.1 µs/3 µs/50 ns/5 µs)을 크게 넘어 사용자에게 보고 → "최적화 후 예산 재설정" 결정(2026-11-03). 최적화: 흔한 행동·합성 스칼라 인라인, Separation float 배열, 피벗 회전 삼각함수 제거(프로필에 cos·sin 미리 계산), TTI 컨텍스트(인터셉트 탐색에서 선수 불변값 1회 계산)·뉴턴 3회(AC-06 99.0% 유지). 15.2 → 11.8 µs, TTI 265 → 203 ns, 인터셉트 13 → 9 µs. `-releaseCodeOptimization`도 거의 같음(Mono JIT 한계, CoreCLR에서는 같은 코드 약 3 µs·70 ns). 예산을 실측 + 약 30%로 수정(spec·`Data/perf`), 006 FR-80 물리 몫 주의 추가, README D17. 최적화 후 결정론 테스트·합성 1,000경기 다시 통과
- [x] T18 전체 테스트 — EditMode 전체 실패 0, 콘솔 에러·경고 0(기대 로그 제외), 컴파일 경고 신규 0 — 결과: EditMode 312개 중 통과 309·실패 0·건너뜀 3(000 셀프테스트 전용). 컴파일 경고 0. 005 테스트 74개(로직·결정론·성능 5)
- [x] T19 `/verify 005` — `farm.py logic --spec 005` 리포트, 첫 성능 기준은 사용자 승인 후 `approve-perf`, 리포트 아티팩트 발행. 상태 "구현 완료 · AC-14 보류", README 표 갱신 — 결과: `farm logic --spec 005` 74개 통과(로직·결정론·성능 5/5), AC 15개 증거 통과, AC-14 보류(판정 "보류 1건 · 완료 아님"). 깨끗한 트리(커밋 9a41345) 재실행에서도 같은 판정 — 서브스텝 평균 12.0 µs·p95 13.8 µs·TTI 196 ns·인터셉트 9.5 µs·할당 0. 성능 기준 `editmode/005/mac-m1-1600p` 채택(사용자 승인 2026-11-03). 리포트 https://claude.ai/artifact/HUe3c6KaSNhcLagPW5QoXs
