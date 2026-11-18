# 085 — 놓친 공중볼 경합 점프 시각 보정 · 082 E2E 장면 재확인 · 플랜

상태: 완료 (2026-11-18)
스펙: ./spec.md

## 접근
083 구현 조사 결과: 놓친 경합 점프는 `AnimPlan.BuildMissedJumps`가 `HighlightData.AerialJumps`(071 기록을 `ClipConverter`가 재생 시각으로 옮긴 것)마다 `MissedJumpMotion.Plan`을 불러 `header_jump`의 접촉 시각을 **기록 정점 `ApexAt`**에 맞춘다. 시뮬·변환은 그대로 두고(Q1 ①), **정점 시각만 `BuildMissedJumps` 단계에서 다시 정한다.**

1. **목표 높이 (FR-01, Q2)**: 그 선수의 점프 머리 높이 `H = 점프 클립 접촉점 Y × 키 배율 + 리프트`(리프트 = 083 규칙 그대로: 기록 점프 높이를 082 `JumpLift` 범위로 자름). 공이 **`H + HeadClearance`(083 상수 0.15 m = 054 머리 접촉 허용치 0.05 + 계획↔화면 여유 0.10)를 내려오며 지나는 순간**을 정점으로 한다. 머리 높이 자체로 맞추면 공이 수평으로 가까울 때 머리에 닿으므로, 여유만큼 위로 지나는 순간으로 정의한다 → 그 순간 머리–공 거리 ≥ 0.15 m > 허용치가 구성으로 보장되고(FR-04), 공 높이 ≥ 서서 이마 높이도 따라온다(리프트 하한 = 서서 이마 높이 + `JumpMin`).
2. **찾는 구간 (Q4)**: `[ApexAt − 0.5, ApexAt + 0.5] ∩ [장면 시작, 장면 끝]`. 장면 공 프레임(`HighlightData.Ball`, 30 Hz)을 앞에서부터 훑어 `Ball[f].Y ≥ 목표 > Ball[f+1].Y`인 첫 구간을 선형 보간 → 기록 프레임 ±1 안(AC-01). 바운드 뒤 다시 뜨는 경우도 첫 하강 통과(경계).
3. **없을 때 (FR-02, Q3)**: 구간 안에서 공이 한 번도 목표 높이 위에 없으면 점프 없음 + 사유 `ReasonBallLow`. 구간 내내 위에 있어 내려오지 않으면(→ 스펙 보완 제안 ①) 점프 없음 + 사유 `ReasonBallHigh`.
4. **나머지 083 규칙 (FR-03)**: 찾은 시각을 `ApexAt`로 바꾼 `AerialJump` 사본을 기존 `Plan`에 넘긴다 → 장면 경계·FR-02 겹침·054 FR-82 자르기·GK 박스(변환 단계 `Skip`)는 코드 변경 없이 새 정점 기준으로 동작. 선수별 머리 높이(키 배율·리프트)가 달라 승자·상대 정점은 따로 나온다.
5. **진단 (FR-04)**: 082·083 `LiveContactAudit`의 `MissedJump` 행에 `PassTime`(찾은 정점), `BallHeightAtApex`, `BallHeadGap`(공 Y − 머리 Y), `StandForehead`를 추가. `ApexError`는 기록 정점이 아니라 `PassTime` 기준으로 바꾼다.
6. **E2E (FR-05·06)**: `live.freeze083.missed`가 기록 정점이 아니라 **계획된 점프 접촉 시각**에 멈추게 바꾼다(083 장면도 같은 기준). 새 시나리오 `Data/e2e/085-aerial-reshoot.json` = 놓친 경합 · 점프 헤딩슛 · 늦은 슬라이딩 3장면. 087 시뮬 변경 뒤 각 시드를 플레이어 빌드에서 확인하고, 장면이 없으면 플레이어 빌드 768시드 탐색(083 FR-05·082 T11과 같은 기준)으로 다시 고른다.

대안: 목표 = 머리 높이 그대로(여유 없음)는 수평으로 가까운 참여자가 공에 닿아 083의 "리프트 하한으로 낮춤"이 다시 정점을 옮기는 순환이 생겨 버렸다. 공–머리 최근접 순간(Q2 ②)은 스펙 결정으로 제외.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.Presentation | `Presentation/Runtime/Players/Logic/MissedJumpMotion.cs` | `Window` 상수 0.5 s(= `highlight.missedJumpWindow`), `TargetHeadY(lib, jump, scale)`, `FindPassTime(HighlightData, targetY, center, window, out t, out reason)` 순수 함수, 사유 상수 2개 추가 |
| RoadToPro.Presentation | `Presentation/Runtime/Players/Logic/AnimPlan.cs` | `BuildMissedJumps`: 기록 정점 대신 `FindPassTime` 결과로 `AerialJump` 사본을 만들어 `Plan`에. 못 찾으면 `_jumpSkips`에 사유. 진단용 `MissedJumpPassTime(seq, entity)` 조회 |
| RoadToPro.Presentation | `Presentation/Runtime/Players/Logic/HighlightData.cs` | `AerialJump.WithApex(float)` 사본 생성 (필드 추가 없음) |
| — (데이터) | `Data/tuning/highlight.json` | `highlight.missedJumpWindow` 0.5 s (085 Q4, 071 FR-06 자름 폭과 같음) |
| Tests | `Tests/EditMode/Presentation/Highlight/LiveContactAudit.cs` | `MissedJump` 행 칸 추가·`ApexError` 기준 변경 |
| RoadToPro.Verification | `Verification/Runtime/Samples/Highlight/HighlightScreen.cs` | `FreezeMissedAerial`: 정지 시각 = 계획된 점프 접촉 시각, 점수의 공 높이 조건도 그 시각. `Seed085*` 상수 + `livePlay085`·`livePlay085header`·`livePlay085slide` 화면 |
| — (데이터) | `Data/e2e/085-aerial-reshoot.json` | E2E 3장면 (FR-05·06) |
| — (데이터) | `Data/e2e/seeds/*` | 바꾸지 않음 (진단 묶음은 082 10 + 083 6경기 그대로) |
| Tests | `Tests/EditMode/Presentation/Players/MissedJumpMotionTests.cs`, `Tests/EditMode/Presentation/Highlight/LiveContactAuditTests.cs` | 아래 테스트 전략 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 — Presentation 안에서만 바뀌고 MatchEngine·Core는 손대지 않는다.

## 데이터 · 스키마
- 튜닝 1개 추가 `highlight.missedJumpWindow` (default 0.5, min 0, max 1.0, unit s, `[설계]`, source "specs/085 Q4 · 071 FR-06"). 코드 상수와 같은지 테스트로 확인(083 `slideRootMax`와 같은 방식).
- `AerialJump` 필드·`ReplayClip`·세이브 스키마 변경 없음.
- 진단 `evidence/live-contact-audit.json`의 `MissedJump` 행에 `passTime`·`ballHeightAtApex`·`ballHeadGap`·`standForehead` 추가.

## 결정론 · 성능
- 시뮬(MatchEngine·Core) 변경 없음 → 시뮬 해시 불변(AC-07). 증거는 `git diff main --stat -- Assets/_Project/MatchEngine Assets/_Project/Core`가 빈 것 + 기존 `Determinism` 카테고리. 코어 변경이 아니라 `determinism-reviewer` 대상 아님. IL2CPP 벤치 해당 없음(틱 경로 변경 없음).
- `FindPassTime`은 장면 공 프레임 배열을 앞에서부터 한 번 훑는다(구간 1 s ≈ 30프레임, 할당 0). 순회 순서 고정 → 같은 장면 계획 2회 같음.
- `AnimPlan` 생성(장면 로드) 경로라 매 프레임 비용 없음. 058 하이라이트 Performance 테스트(장면 1개 동작 계획)로 예산 확인, 프레임은 개발 맥 기록.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `MissedJumpMotionTests.PassTime_DescendingThroughHeadPlusClearance_WithinOneFrame` (합성 포물선: 정점 = 통과 시각 ±1 기록 프레임, 그 순간 머리–공 > 허용치) · `ApexAligned_WithinOneFrame_HeadClearOfBall`(통과 시각 기준으로 갱신) · `PerEntityHeadHeight_SeparateApex` | 로직 | — |
| AC-02 | `MissedJumpMotionTests.BallNeverAboveHead_NoJump_Reason` · `BallAboveWholeWindow_NoJump_Reason` | 로직 | — |
| AC-03 | `MissedJumpMotionTests.EdgeCases` 확장: 두 번 통과 → 첫 하강, 통과가 장면 밖 → 경계 사유, 옮긴 정점이 다음 동작과 겹침 → FR-02 사유, 키 150·205 cm, NaN·예외 0 · `WindowMatchesTuning` | 로직 | — |
| AC-04 | `LiveContactAuditTests.MissedAerials_WinnerAndNearestOppJump_NoTouch` 확장 (082 10 + 083 6경기: 점프한 참여자 전부 공 높이 ≥ 서서 이마 높이, 머리–공 > 허용치, `ApexError` ≤ 1 기록 프레임) | 로직 | — |
| AC-05 | `e2e/085-aerial-reshoot` 놓친 경합 정지(계획 점프 접촉 시각) | E2E | mac-m1-1600p |
| AC-06 | `e2e/085-aerial-reshoot` 점프 헤딩슛 접촉 · 늦은 슬라이딩 정지 | E2E | mac-m1-1600p |
| AC-07 | `MissedJumpMotionTests.PlanTwice_Same` + MatchEngine·Core diff 없음 기록 + `--scope auto` 범위의 `Determinism` 카테고리 | 결정론 | — |
| AC-08 | 058 하이라이트 Performance 테스트(동작 계획) + 하이라이트 프레임 개발 맥 기록 | 성능 | mac-m1-1600p |

## 버그 방지
- 장면 공 프레임이 1개뿐이거나 구간이 장면과 안 겹침 → 통과 없음, 경계 사유(인덱스 범위 테스트).
- 통과 구간의 두 프레임 Y가 같음(0 나누기) → `≥ 목표 > 다음` 조건이라 성립하지 않음, 보간 분모 > 0 보장.
- 정점을 앞으로 당기면 점프 시작이 장면 시작보다 앞 → 기존 `Plan`의 `start = max(sceneStart, …)`·rate 보정 그대로, 테스트에서 rate 유한값 확인.
- `HeadClearance` 리프트 하한 낮춤(083) 경로: 목표 높이가 여유를 포함해 정상 경로에서는 안 타지만 남겨 둔다(수평 0·계획↔화면 차 대비). 진단에서 위반이 나오면 멈추고 보고.
- 진단 `ApexError` 기준 변경으로 083 기존 기대값이 바뀌는 테스트는 목록·사유를 결과 줄에.

## 위험 · 미정
- **스펙 보완 (2026-11-18 플랜과 함께 승인, spec.md 반영)**:
  ① 경계 · 예외에 "구간 내내 공이 목표 높이 위(내려오지 않음) → 점프 없음 + 사유" 추가 (Q3 ①과 같은 보수 처리).
  ② FR-01 "점프 머리 높이를 내려오며 지나는 순간"을 "점프 머리 높이 + 머리 여유(083 `HeadClearance` 0.15 m)를 내려오며 지나는 순간"으로 명확히 (접근 1의 이유).
  ③ FR-04 "083과 같은 15경기" → "082 10경기 + 083 시드 묶음(현재 6경기, 087 T13a)" — 086·087에서 083 묶음이 바뀜.
  ④ FR-06·AC-06 대상 시드: 086이 점프 헤딩슛 장면을 20261264로 이미 다시 골랐고 087이 시뮬을 또 바꿨다 → "점프 헤딩슛 = 최신 시드(086 `Seed086` 20261264), 늦은 슬라이딩 = 082 `Seed082Slide` 20261192를 087 뒤 기준으로 확인, 없으면 재탐색 후 `085-aerial-reshoot` 시드로". 082·086 시나리오 파일은 그대로 둔다(085가 대체 확인).
- 087 뒤 놓친 경합 + 공이 머리 위를 지나는 장면이 실경기 하이라이트에 드물 수 있다 → 탐색 768시드에서 못 찾으면 멈추고 보고.
- 목표 높이에 여유를 더해 정점이 기록 정점보다 앞으로(공이 더 높을 때) 당겨지는 경우가 많을 것 — 화면에서 "공이 너무 높이 지나 경합으로 안 읽힘"이면 E2E 육안 단계에서 여유값을 다시 묻는다.
