# 059 — 골망 · 플랜

상태: 승인됨 (2026-11-26 사용자 "네 진행" — FR-41 ② 해석: 0.3 s 안 면 바깥 속도 ≤ 0·면에서 0.5 m 안·면 밖 0)
스펙: ./spec.md

## 접근
세 층으로 나눈다. ① **시뮬(MatchEngine)**: 004 감속 영역(`GoalFrame.ApplyNet`)을 "골문 안 상자(뒤·옆·위 면)에 닿으면 면 수직 속도를 거의 죽이고 면 밖으로 못 나감"으로 바꾼다. 공은 그물 면 정지 위치까지 감속 없이 가고, 그물 면을 넘어 들어가지 않는다(관통 허용 스프링 방식은 041 화면 공 보호 상자·FR-02와 충돌해 버림). ② **표현 로직(Presentation, 순수 C#)**: 30 Hz 클립 공 궤적에서 그물 면 접촉을 모두 찾아(`NetContactDetector`, 058 `NetContact.Find` 대체) 접촉 목록을 만들고, 그물 변위를 **시각 t와 접촉 목록만의 해석식**(접촉마다 범프 × 감쇠 출렁임, 합산·포화)으로 계산한 뒤 공 구 바깥으로 밀어내는 제약을 한 번 건다. 상태를 프레임마다 적분하지 않으므로 fps·재생 속도·리플레이 시작 위치와 무관하게 같은 t → 같은 모양이다(FR-28·29 자동 충족). ③ **렌더**: 그물은 칸 0.25 m 거친 격자 메시(뒤 30×11·위 30×8·옆 8×11, 골대당 삼각형 1,324 + 지지대 336 — T12 개정) 위에 **육각 망을 해석적으로 그리는 전용 셰이더**(줄까지 거리 / fwidth로 안티에일리어싱, 셀이 화면에서 [제안] 3 px보다 작아지면 평균 덮임으로 서서히 바뀜 → 모아레·깜빡임·거리 단계 튐 없음)를 쓴다. 대안 — 줄을 실제 형상으로 만들기(골대당 셀 ≈ 3,400개, 예산 6천 삼각형 초과)와 알파 테스트 텍스처(research 06 § 4.4가 피하라고 한 방식, 모아레)는 버렸다. 출렁임은 공이 그물에서 3 m 안일 때만 CPU에서 깨어 정점을 갱신한다.

이미 있는 것(058에서 구현, 재사용): 골 시퀀스 메인 샷 유지(`GoalSequenceDirector` + `camera.netHoldSeconds` 1.0·`noNetHoldSeconds` 0.5·`goalHoldMaxSeconds` 1.5 — FR-30·31), 골 연장 +7 s(`deadball.goalCelebration` 7, `LiveSceneSelector` — FR-32), 첫 그물 접촉 큐(`NetContact.CueKind`). 이 스펙은 접촉 시각 출처만 새 검출기로 바꾸고 AC-06 테스트로 고정한다.

## 개정 (2026-11-26 사용자 "ㄱㄱ" — docs/goal-net-research.html 권장안)
해석식 출렁임(`NetRippleModel`)은 사용자 판정 "철창에다 공 쏘는 느낌"으로 버리고 **처짐 있는 그물 시뮬 + 굽기**로 바꾼다.
- `NetCloth`(Presentation, 순수 C#): `NetRestShape` 격자 정점을 위치로 합쳐 노드 한 장(뒤·옆·위 모서리 공유, 골대당 약 1,200 노드). 거리 제약(가로·세로, 기준 길이 = 기하 간격 × 처짐 — 뒤 `net.slackBack`·옆/위 `net.slackSide`, 줄 거의 안 늘어남), 고정점(포스트·크로스바 앞 모서리, 뒤 기둥·뒤 위 모서리, 바닥 모서리), 정지 그물 상자 안쪽 금지(FR-02 — 면은 바깥쪽으로만), LRA(고정점까지 거리 ≤ 정지 거리 × `net.lra`), 공 = 시뮬 공 위치(30 Hz 클립 보간)를 따라가는 구(반지름 `net.ballReach` — 칸 사이로 공이 비치지 않게 공보다 크게), 중력·속도 감쇠(`net.damping`), 240 Hz 고정 스텝·스텝당 반복 `net.iterations`, 단일 스레드 고정 순서(결정론).
- 정지 모양 = 그물 상자 밖으로 바깥 기울기(`net.hangBias`)를 주고 중력으로 [제안] 3 s 미리 늘어뜨린 모양(로딩 때 1회, 장면마다 같음).
- `NetBake`: 장면을 만들 때(`SceneClipBuilder.Step`, 로딩) 접촉이 있는 골대만 첫 접촉 − 0.1 s부터 정지(정지 모양과 차 < 0.5 mm)까지 최대 [제안] 4 s 구워 30 Hz 프레임을 int16 mm 오프셋으로 저장. 끝은 정지 모양으로 이어 붙인다(튐 없음). `BuiltScene.NetBake`.
- `GoalNetDriver`: 구운 데이터 보간(없으면 정지 모양). 시뮬 그물 주머니(FR-41 ⑥)는 부드럽게: `ball.net.pocket` 0.8 m·`ball.net.stiffness` [제안]·반발 ≈ 0.
- 그대로: 그물 형상·셰이더(T11~T13), 접촉 검출·큐(T05~T07), 골 시퀀스(T16), 바깥 면 막기, 검증 화면 지표(T17).

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `MatchEngine/Runtime/Ball/GoalFrame.cs` (`ApplyNet` 교체) | FR-41: 골 상자 면 접촉 처리. 면 수직 속도 → −e·v⊥, 접선 속도·스핀 × 보존율, 위치를 면 안쪽(면 − 반지름)으로 고정 |
| RoadToPro.MatchEngine | `MatchEngine/Runtime/Ball/BallParams.cs`, `BallTuningIds.cs` | `ball.net.damping` 제거, `ball.net.restitution`·`ball.net.tangentKeep` 추가, `NetDepth` 유지 |
| RoadToPro.MatchEngine | `MatchEngine/Runtime/Ball/BallStepper.cs` | 호출 조건 유지(골라인 뒤 Air·Rolling) — 골 상자 안일 때만 동작 |
| (데이터) | `Data/tuning/ball.json` | 위 튜닝 행 교체(`[제안]`, 출처 059 FR-41) |
| RoadToPro.Presentation | `Presentation/Runtime/Highlight/Logic/NetContactDetector.cs` (새, `NetContact.cs` 대체) | 클립 공 궤적에서 접촉 목록: (시각, 골 쪽, 면, 위치, 수직 속도). 골 상자 기하는 `BallVisualGuard.GoalBox` 재사용 |
| RoadToPro.Presentation | `Presentation/Runtime/Highlight/Logic/ClipConverter.cs` | 접촉마다 큐 1개(FR-34), 시뮬 시각 순 정렬(기존 정렬 재사용) |
| RoadToPro.Presentation | `Presentation/Runtime/Highlight/Broadcast/Logic/CameraTrackBuilder.cs` | `NetContactTime` = 첫 접촉 시각(검출기로 교체). FR-33 하이앵글 리플레이 프레이밍 확인·필요 시 접촉점을 프레이밍 대상에 추가 |
| RoadToPro.Presentation | `Presentation/Runtime/Stadium/Net/NetRestShape.cs` (새) | 정지 격자: 정점 위치·면 id·바깥 법선·고정 가중치(테두리 0). glb 정점 색에서 읽거나 테스트용으로 인자에서 생성 |
| RoadToPro.Presentation | `Presentation/Runtime/Stadium/Net/NetRippleModel.cs` (새, 순수 C#) | FR-20~24·26·27·57: `Evaluate(t, contacts, ballPos, into Vec3[])`. 해석식 + 포화 + 공 구 제약 + 깨움/잠듦 판정. 상태 해시 함수 |
| RoadToPro.Presentation | `Presentation/Runtime/Stadium/Net/NetRippleTuning.cs` (새) + `Data/tuning/goalnet.json` (새) | 출렁임·깨움 수치(`[제안]`) 로드·범위 검사. 잘못된 값 → 정지 모양 + 경고 1회 (FR-56) |
| RoadToPro.Presentation | `Presentation/Runtime/Stadium/Net/GoalNetView.cs` (새, MonoBehaviour) | Net_L·Net_R 메시를 찾아 깨어 있을 때만 `Mesh.SetVertices`(NativeArray, 할당 0). 재생 시각은 `HighlightPlayer` 시각을 읽기만(정지·건너뛰기 FR-53·54) |
| RoadToPro.Presentation | `Presentation/Shaders/GoalNet.shader` (새) | 육각 망 해석 렌더, 거리 페이드, 낮·나이트 포화 억제(FR-10~15). Transparent 큐·ZWrite 끔·양면 |
| RoadToPro.Presentation.Editor | `Presentation/Editor/Stadium/StadiumAssets.cs` | `Net*` 머티리얼을 `GoalNet` 셰이더로 매핑, 메시 Read/Write 켬 |
| (아트) | `Art/Blender/scripts/stadium/goals.py`, `materials.py`, `checks.py` | 면별 격자 그물(처짐·부풂 FR-03), 지지대(FR-04), 정점 색에 면 id·고정 가중치, 셀·줄 인자(FR-01)를 커스텀 속성으로, 잘못된 인자 → 종료 코드(FR-56) |
| (아트 테스트) | `Art/Blender/tests/test_stadium.py` | AC-01 |
| RoadToPro.Verification | `Verification/Runtime/Samples/GoalNetScreen.cs` (새) + `Data/e2e/059-net-closeup.json`·`059-net-ripple.json` | 정지 그물 시점 캡처, 합성 골 6종 출렁임 캡처, 그물 켬/끔·판 대체 캡처(FR-10 비교용) |
| RoadToPro.Tests.EditMode | `Tests/EditMode/MatchEngine/Ball/BallNetTests.cs`, `Tests/EditMode/Presentation/Highlight/NetContactDetectorTests.cs`, `Tests/EditMode/Presentation/Stadium/NetRippleModelTests.cs`, `.../GoalNetSequenceTests.cs` | 아래 테스트 전략 |

의존 방향 확인: MatchEngine 변경은 Core만 참조. 그물 로직은 Presentation 안(순수 C# 폴더 `Logic`과 같은 규칙), Verification은 최상위. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- `ball.json`: `ball.net.damping` 삭제 → `ball.net.restitution` [제안] 0.1 (0~0.5), `ball.net.tangentKeep` [제안] 0.35/접촉 (0~1). `ball.net.depth` 2.0 그대로. 근거: FR-41 ② "닿은 뒤 0.3 s 안에 면 근처에서 정지" — 30 m/s 수직 접촉도 반발 3 m/s 이하, 면에서 0.3 s 안 이동 < 0.5 m.
- 시뮬 골 상자: x ∈ [골라인, 골라인 ± depth], |y − 중심| < 7.32/2, z < 2.44. 공 중심은 각 면에서 반지름만큼 안쪽. 상자 밖(골문 밖으로 나간 공)은 손대지 않는다(바깥 옆그물은 012, Q2 (a)).
- `goalnet.json` (새, 표현 튜닝): `net.cellSize` 0.12 m · `net.strand` 0.004 m (FR-01), `net.sagTop` 0.08 · `net.bulgeBack` 0.10 (FR-03), `net.ripple.maxDisp` 0.6 m (FR-21), `net.ripple.v0` [제안] 12 m/s (A = max·(1 − e^(−v⊥/v0)), 5·15·30 m/s → 0.20·0.43·0.55 m 단조), `net.ripple.radius` [제안] 0.8 m (cos² 범프), `net.ripple.decay` [제안] 2.5 /s · `net.ripple.period` [제안] 0.6 s (H(t) = e^(−γt)·sin²(πt/T): 항상 바깥쪽, 국소 최대 단조 감소, 2 s 뒤 0.6·e^(−5) < 1 cm — FR-23), `net.wakeDistance` 3 m (FR-27), `net.fadePixels` [제안] 3 px. 생성기도 같은 파일을 읽는다(044가 `ball.json`을 읽는 방식).
- (T11 개정) glb 정점 색은 쓰지 않는다 — 내보내기가 정점 순서·복제를 바꿀 수 있어, 런타임은 glb 그물을 끄고 같은 식으로 만든 격자(`NetRestShape`)로 동적 메시를 새로 만든다(T15 개정 — 정점 대응·메시 Read/Write 불필요, glb 그물은 정지 모양 대체용). 격자 인자는 layout `goals[].netGrid`. 고정 가중치는 런타임 계산(`net.pinBand`). 세이브 변경 없음.
- 접촉 레코드(표현 전용, 직렬화 안 함): `NetContact { double Time; sbyte Side; NetFace Face; Vec3 Point; float NormalSpeed; }`.

## 결정론 · 성능
- **시뮬**: `ApplyNet`는 분기·곱셈만, RNG 없음. 공 궤적이 골 뒤에서 바뀌므로 경기 상태 해시가 바뀐다 → 같은 시드 2회 해시 동일 테스트 + 004 결정론 테스트 기준값 갱신(갱신 사유 기록) + `determinism-reviewer`. 골 판정은 골라인 통과(`DetectBoundarySegment`)라 변경 전후 골 시각·스코어 동일을 AC-12로 확인.
- **표현**: 그물 모양 = f(t, 접촉 목록, 그 시각 공 위치). 접촉 목록은 클립(30 Hz 기록)만으로 정해지고, 공 위치는 기존 보간(`HighlightData` 샘플)과 같은 함수. 적분 상태가 없어 재생 속도·fps·시작 위치 무관. 해시는 고정 간격(1/30 s) 시각에서 변위를 0.1 mm 양자화해 계산.
- 접촉 합산은 접촉 목록 순서(시각, 같으면 면 id) 고정 순회.
- **성능**: 깨어 있는 골대 하나만 갱신(정점 ≈ 4면 × 31×16 ≈ 2천, 접촉 ≤ [제안] 8개 캡 → 정점당 상수 연산). `NativeArray` 재사용·`Mesh.SetVertices`·바운드 고정(최대 변위만큼 미리 넓힘)으로 GC 0. 법선은 셰이더가 정점 위치 미분 대신 면 법선 + 근사 사용. 프로파일 마커 `GoalNet.Ripple`·`GoalNet.Sleep`.
- 셰이더: 셀 좌표는 정지 메시 UV(생성기가 m 단위로 굽움)에서 계산 → 변형돼도 망이 천처럼 따라 늘어난다. 드로우콜 = 골대당 그물 1 + 지지대 1(`GoalSupport_`, 044 포스트 측정과 분리 — T12 개정) → 합계 4 (예산 6). 텍스처 0 B(해석식).
- 검증 범위(05-sdd): MatchEngine + Presentation + Verification + Blender 변경 → `farm.py logic --spec 059 --scope auto`, 시뮬 틱 경로(`BallStepper`)를 바꾸므로 `farm.py bench --spec 059`(+10% 회귀 판정), `farm.py art --spec 059`, `farm.py shot`(059 장면 2개 + 회귀 044-stadium-views·042-goal-sequence), `mac-m1-1600p` 1프로필. 스펙에 적힌 smoke·`uhd-4k-200`·low-igpu·compact-1366·기준 이미지 비교·logic.png는 2026-11-11·12 결정대로 `mac-m1-1600p` 1프로필·report.html로 읽는다. GPU 예산(0.3 ms 등)은 개발 맥 측정값 기록만, 판정은 실기기 확보 후.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `test_stadium.py::TestGoalNet` — 기본·최소·최대 인자에서 정점 ⊄ 시뮬 상자+반지름, 지지대 위치·입구, 바닥선 0~0.03 m, 셀·줄 ±10 %, 044 AC-01·03·05·16 회귀, 2회 해시 동일 | 로직·결정론 | — (`farm.py art`) |
| AC-02 | `NetRippleModelTests.Synthetic_FaceSpeedAngleGrid` (3면 × 5·15·30 m/s × 0·30·60°): 최대 변위 위치 ≤ 0.3 m·코사인 ≥ 0.8·단조·≤ 0.6 m·2 s 뒤 < 1 cm·국소 최대 단조 감소, `MultipleContacts_AppliedInOrder` | 로직 | — |
| AC-03 | `NetRippleModelTests.BallNeverOutside_Synthetic`, `NoFramePenetration`, `RestingBall_NoDrift5s`; `GoalNetSeedSetTests.Goals200_NoEscape_NoOutside` (고정 시드 골 200개, 60 fps·0.5x 프레임 샘플, 시뮬 공 상자 이탈 수 0) | 로직 | — |
| AC-04 | `NetRippleModelTests.StateHash_SameAcrossSpeedsFpsLiveReplay`(1·1.5·3·0.5x, 30·60 fps, 접촉 후 시작), `Interpolation_Within1mm`, `Mirror_Within1mm` | 결정론 | — |
| AC-05 | `GoalNetSimInvarianceTests.NetOnOff_SameHashEventsBallPositions`, `NetContactDetectorTests.OneCuePerContact_Ordered_3x`, `FirstCue_EqualsNetContactTime`, `NoContactGoal_NoCue`, `BallOnLine_NoCue` | 로직 | — |
| AC-06 | `GoalNetSequenceTests.NetGoal_NoCutBefore1s`, `NoNetGoal_Cut05`, `CutWithin15`, `Order_Unchanged`(042 AC-04 재실행), `ReplayHigh_ContactInViewport5pct` | 로직 | — |
| AC-07 | `e2e/059-net-closeup` — 메인 FOV 30·15·8°, 하이앵글, 피치 레벨 3·6 m × 낮·나이트, 그물 켬/끔/판 대체 + 팬·거리 단계 전환. 지표(밝기 차·표준편차 배율·구멍 비율·끊긴 줄·포화·모아레·깜빡임)를 캡처 분석으로 계산해 리포트 표에 | E2E | mac-m1-1600p |
| AC-08 | `e2e/059-net-ripple` — 합성 골 6종 × 라이브 1.5x·하이앵글 0.5x, 첫 접촉 +0.1·0.3·0.6·1.5·3.0 s, 0.5x 연속 프레임 변위 비율·멈칫 구간(로직 수치도 같이 기록). 바깥 옆그물은 보류(Q2 (a)) | E2E | mac-m1-1600p |
| AC-09 | 위 두 장면 + 042 골 시퀀스 연속 캡처를 리포트 아티팩트로 → 사용자 육안 승인 기록(`evidence/approval.md`) | E2E | mac-m1-1600p |
| AC-10 | `e2e/059-net-ripple` 프레임 샘플(p95·최대, 첫 접촉 ±0.5 s), 렌더 카운터(드로우콜·삼각형), 마커 `GoalNet.Ripple`/`Sleep`, GC 0 — 개발 맥 측정 기록, GPU·내장 GPU 판정은 실기기 확보 후 보류. 시뮬 쪽은 `farm.py bench` 회귀 ≤ +10 % | 성능 | mac-m1-1600p (IL2CPP) |
| AC-11 | `test_stadium.py::test_bad_net_args_exit_nonzero`, `NetRippleTuningTests.Invalid_RestShape_WarnOnce_NoThrow`, `GoalNetViewLogicTests.KeyMomentPause_NoChange`, `SceneSkip_StartsAtSceneState` | 로직 | — |
| AC-12 | `BallNetTests` — 면별 접촉 정지(0.3 s·면 밖 0), 그물 전 감속 없음, 상자 밖 공 불변; `GoalNetSeedSetTests.Goals200_SameGoalTimesScores`; `BallDeterminismTests` 기준값 갱신; 같은 시드 2회 해시; `determinism-reviewer` 리뷰 기록 | 로직·결정론 | — |

## 버그 방지
- **골라인 위·반만 넘은 공**(FR-51): 검출기는 공 중심이 면에서 반지름 + [제안] 2 cm 안이고 그 면을 향해 수직 속도 > [제안] 0.5 m/s일 때만 접촉. 골라인 면은 그물 면이 아니므로 제외. 테스트로 고정.
- **같은 공이 면에 붙어 있음**(굴러 뒤 그물에 기댐, FR-55): 접촉은 "접근 → 닿음" 전이 때 1회만. 붙어 있는 동안은 새 접촉 0, 공 구 제약만 정적으로 적용 → 떨림 없음.
- **모서리 접촉**(뒤+위·뒤+옆 동시): 수직 속도가 큰 면 하나로 기록, 같은 프레임 두 면이면 둘 다(시각 같음, 면 id 순).
- **크로스바 맞고 들어감**(FR-50): 프레임 근처는 고정 가중치 0 → 그물이 프레임을 뚫을 수 없음. `NoFramePenetration`이 가중치 0 띠 정점을 확인.
- **포화 합산**: 접촉 여러 개 합이 0.6 m를 넘지 않게 tanh형 포화. 공 구 제약 뒤에도 바닥 아래(z < 0)·프레임 안쪽 정점 없음 검사.
- **좌우 골대**: 모델은 골대 로컬 좌표(골라인 원점, 바깥 +x)에서 계산 → 거울상 자동(FR-57). `Side` 부호 실수는 `Mirror_Within1mm`가 잡는다.
- **장면 경계**(FR-54): 접촉 목록을 장면 시작 시각 이후·장면 클립 범위로 자른다. 이전 장면 접촉이 끼지 않음.
- **시뮬 회귀**: 상자 판정을 `|y| < 반폭`·`z < 높이`로 바꾸면 골문 바깥(옆그물 바깥) 공이 상자로 끌려 들어가는 버그 가능 → "상자 밖 공 불변" 테스트, 200골 이탈 수 0.
- **튜닝 제거**: `ball.net.damping` 삭제 시 `TuningProvenanceTests`·튜닝 로더 범위 검사가 깨지지 않게 같이 갱신.
- **나이트 포화**: 셰이더가 노출 후 그물 알베도를 상한(≤ 240/255, 기존 규칙) + 발광 0으로. AC-07 포화 지표.

## 위험 · 미정
- **FR-41 ② "0.3 s 안에 면 근처에서 정지" 해석**: 위 그물·뒤 그물 위쪽에 닿은 공은 중력으로 떨어지는 데 ≈ 0.6 s 걸린다. 플랜은 "닿은 뒤 0.3 s 안에 면 수직 바깥 속도 ≤ 0, 면에서 0.5 m 안" + "면 밖 0"으로 측정한다(완전 정지 아님). 이 해석이 맞는지 확인 필요 — 아니면 스펙 수정 제안.
- **공이 그물을 밀고 나가는 모습**: 시뮬 공은 정지 그물 면에서 멈추므로, 그물이 공보다 바깥으로 부푸는 모양이 된다(공은 항상 안쪽 — FR-22 충족). 실제처럼 공이 그물을 밀고 나가는 모습을 원하면 시뮬 관통 허용 + 041 공 보호 상자 확장이 필요(FR-02·041 FR-04 개정) — 육안 승인(AC-09)에서 부족하면 그때 제안.
- 셀·줄 공개 출처([미정], FR-01)는 이번에도 [제안] 값으로 간다.
- AC-07 지표 기준(6/255·1.5배 등)은 첫 캡처 후 조정할 수 있다고 스펙에 적혀 있음 — 조정하면 스펙 [제안] 값 갱신을 같이 제안.
- 065(경기장 다양성)가 아직 없어 경기장은 044 기본 1종. 최소·최대 인자는 생성기 인자로만 검사.
- 046 그물 소리는 미구현 — 큐만 내고 소리 연결은 046 때.
- 후속(스펙에 이미 적힘): 004 spec FR-42 문구, 042 FR-20 문구, 018 FR-15 +7 s 문구, 044 "High 후보" 메모 정리 — 문서 갱신 태스크로 넣는다.
