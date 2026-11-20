# 080 — GK 동작 클립 확장 · 손 IK · 플랜

상태: 승인됨 (2026-11-20 사용자 "ㄱㄱ")
스펙: ./spec.md

## 접근
세 겹으로 손을 공에 붙인다. ① **클립을 늘린다**: bpy 생성기에 `clips/keeper.py`를 새로 만들어 011 FR-90 목록 클립 ≈ 30개를 만들고, 접촉 태그에 어깨 위치를 더한다(IK 도달 한계 계산용). ② **동작 종류로 후보를 좁힌다**: 지금 GK 이벤트는 이벤트 종류(Catch·Parry…)로만 후보를 받아 `gk_dive_l/r`만 고른다. 011·073·074 이벤트가 이미 담고 있는 `A = KeeperMotionKind`, `B = 높이 구간`으로 매핑 표의 새 `keeper` 구획에서 후보를 고르고, 그 안에서 기존 `ActionClipSelector` 점수(손 접촉점 오차 + 공 높이 범위)로 하나를 고른다. ③ **남는 오차를 손 IK가 흡수한다**: `AnimPlan`이 접촉마다 손 이동량(≤ 30 cm, 어깨–손 ≤ 팔 길이)을 결정론으로 계산해 타임라인에 넣고, 런타임은 `FootGrounding`과 같은 2본 해석 IK로 팔을 푼다. 순서는 루트 보정(≤ 10 cm, 054 FR-02) → 손 IK(≤ 30 cm) → 화면 공 보정(≤ 15 cm, 054 FR-35)이다.

대안: Unity Animator IK(`SetIKPosition`)는 지금 재생 경로가 Playables에서 IK를 끄고(`SetApplyPlayableIK(false)`) 직접 본을 쓰는 구조라 맞지 않는다. IK 양을 런타임에서만 정하면 로직 테스트(측정기)가 같은 값을 재현하기 어려워, 양은 `AnimPlan`(순수 로직)에서 정하고 런타임은 푸는 일만 한다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| (Blender) | `Art/Blender/scripts/player/clips/keeper.py` (새) | GK 클립 생성: 다이빙 12 (좌/우 × 낮음/중간/높음 × 손 옆 거리 짧음 0.5 m/긺 1.0 m, 몸은 시뮬 루트 위 — 스펙 FR-01 개정 2026-11-20), 잡기 땅볼(무릎)·머리 위, 쳐내기 측면 좌/우·앞, 넘기기 좌/우, 펀칭 한 손·두 손, 클레임 점프, 스마더 좌/우(옆으로 덮기), 스프레드, 셔플 좌/우, PK 조기 이동 좌/우, 굴리기, 오버암 던지기, 일어나기. 기존 `gk_ready`·`gk_catch_chest`·`gk_punch`는 옮겨 오고 `gk_dive_l/r`는 `gk_dive_mid_long_l/r`로 이름을 바꾼다(맵·측정기 참조 함께 갱신) |
| (Blender) | `Art/Blender/scripts/player/clips/reactions.py` | GK 함수 제거 → `keeper.py` 호출 |
| (Blender) | `Art/Blender/scripts/player/clips/base.py` | 접촉 태그에 `shoulders`(접촉 프레임 좌/우 어깨, 루트 기준)·`armLength` 추가 (손 접촉 클립만) |
| (Blender) | `Art/Blender/scripts/player/checks.py` | 새 클립 검사: 접촉 손이 어깨에서 팔 길이 안, 지면 관통 0, 접촉 프레임 범위 |
| RoadToPro.Presentation | `Presentation/Players/Generated/player.clips.json`·`player.glb` | 생성물 (LFS) |
| RoadToPro.Presentation | `Presentation/Players/Data/043-anim-map.json` | 새 `keeper` 구획(동작 종류 이름 → 후보, `Dive`는 높이 구간별), 이벤트 후보 갱신(Catch·Parry·Punch·Smother·ClaimFail·DiveStart), Pass 후보에 굴리기·던지기 추가(부위 Hand 필터로만 뽑힘), Distribution `none` 사유 갱신 |
| RoadToPro.Presentation | `Presentation/Runtime/Players/Logic/ClipLibrary.cs` | `ClipInfo`에 어깨 2점·팔 길이 읽기 |
| RoadToPro.Presentation | `Presentation/Runtime/Players/Logic/AnimationMap.cs` | `keeper` 구획 파싱, `KeeperCandidates(KeeperMotionKind, DiveHeight?)` |
| RoadToPro.Presentation | `Presentation/Runtime/Players/Logic/KeeperMotion.cs` (새) | 순수 로직: GK 이벤트 해석(`A`·`B` 디코드 — DiveStart `A = dir*10 + height` 부호 포함), 후보 좁히기, 손 IK 이동량 계산(`HandReach`: 상한 30 cm·어깨 구 안으로 자름), 실점 다이빙 비켜 가기(≥ 15 cm)·손끝(≤ 10 cm) |
| RoadToPro.Presentation | `Presentation/Runtime/Players/Logic/AnimPlan.cs` | `BuildActions`에서 GK 이벤트면 `KeeperMotion`으로 후보·손 이동량 결정. 손 IK가 맡은 만큼 화면 공 보정(`_ballPulls`)에서 뺀다. 타임라인에 손 IK 키(접촉 시각·이동량·대상 손) 기록, `Evaluate`가 섞기 가중치(±0.2 s 평활)와 함께 돌려준다 |
| RoadToPro.Presentation | `Presentation/Runtime/Highlight/Logic/ClipConverter.cs` | 실점 DiveStart에 손끝 플래그 옮기기(같은 슛의 Goal 이벤트 `EventCodes.Fingertip`) |
| RoadToPro.Presentation | `Presentation/Runtime/Players/HandReachIk.cs` (새) | 런타임: 어깨–팔꿈치–손 2본 IK (`FootGrounding.SolveTwoBone` 재사용), 두 손 잡기는 두 손을 같은 이동량으로 |
| RoadToPro.Presentation | `Presentation/Runtime/Players/PlayerAnimationSystem.cs` | `ApplyAt`에서 `FootGrounding` 뒤 `HandReachIk` 적용 (GK만, 가중치 > 0일 때만) |
| RoadToPro.Presentation | `Presentation/Runtime/Players/Motion/MotionMeter.cs` | 지면 접촉 예외 목록을 `gk_dive_*`·스마더·스프레드 접두사로 일반화, 실점 다이빙 손–공 거리 기록 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/Presentation/Players/KeeperMotionTests.cs` (새) | 디코드·후보·IK 양·실점 비켜 가기 단위 테스트 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/Presentation/Players/Motion/GkContactMeterTests.cs` | `Explicit` 해제(AC-02), 실점 다이빙(AC-03), 073·074 장면(AC-04) 추가 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/Presentation/Players/KeeperClipCoverageTests.cs` (새) | AC-01 매핑 완전성 |
| RoadToPro.Tests.Performance | `Tests/Performance/Presentation/HandReachIkPerf.cs` (새) | AC-07 IK 1회 시간 |
| RoadToPro.Verification | `Verification/...`·`Data/e2e/080-gk-contact.json` (새) | E2E 장면 3개 (011-gk-highlight와 같은 장면 종류, `PlayFresh`) |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 — Presentation이 MatchEngine 타입(`KeeperMotionKind`·`DiveHeight`·`EventCodes`)을 읽기만 한다. MatchEngine 코드는 바꾸지 않는다.

## 데이터 · 스키마
- `player.clips.json` 클립 `contact`에 선택 필드 추가 (스키마 버전 그대로, 없으면 IK 없음):
  ```json
  "contact": { "frame": 14, "part": "Hand", "point": [...], "rule": {...},
               "shoulders": [[xL,yL,zL],[xR,yR,zR]], "armLength": 0.58 }
  ```
- `043-anim-map.json` 새 구획:
  ```json
  "keeper": {
    "Dive": { "Low": ["gk_dive_low_short_l", ...], "Mid": [...], "High": [...] },
    "CatchGround": ["gk_catch_ground"], "Smother": ["gk_smother_l","gk_smother_r"], ...
  }
  ```
  `KeeperMotionKind`의 모든 값(`None` 제외)이 키로 있어야 한다(AC-01). 골킥·펀트·드롭킥은 055 킥 원형 클립을 가리킨다.
- 클립 이름 규칙: `gk_<동작>[_<높이>][_<도달>]_<l|r>` — 좌우는 GK 기준.
- 세이브 스키마 변경 없음.

## 결정론 · 성능
- 클립 선택·손 이동량은 `AnimPlan` 생성 때 이벤트 시각·`Seq` 순으로 한 번 계산(지금 경로 그대로). RNG 없음. 같은 이벤트 로그 → 같은 타임라인(AC-08, 기존 054 AC-15 해시에 손 IK 키 포함).
- 런타임 IK는 포즈 = f(t): 가중치는 재생 시각에서만 계산, 이전 프레임 상태 없음 → 측정 순서와 무관(측정기 전제 유지).
- 할당: `HandReachIk`는 본 참조를 GK 리그 생성 때 캐시, 프레임 경로 할당 0. GK 2명 × 양손 = 최대 4회 2본 해석.
- 성능 측정: `HandReachIkPerf` (Performance 테스트, 1,000회 평균 ≤ 0.05 ms, 기록). 클립 메모리: `player.glb` 크기 차와 `player.clips.json` 크기 차 합 ≤ 2 MB (빌드 리포트 대신 생성물 크기 비교, 증거에 기록). 프레임 시간은 `farm.py shot` 측정값 기록만.
- 시뮬 틱 경로 변경 없음 → IL2CPP 벤치 안 돌림. 코어 모듈 변경 없음 → `determinism-reviewer` 해당 없음.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `KeeperClipCoverageTests.EveryKeeperMotionKind_HasClip`, `DiveClips_TwoSidesThreeHeightsTwoReaches` | 로직 | — |
| AC-02 | `GkContactMeterTests.GkContacts_HandBallWithin10cm` (Explicit 해제), `GkContacts_Timing33ms_NoContradiction` (유지) | 로직 | — |
| AC-03 | `GkContactMeterTests.MissedDive_HandClearOfBall15cm`, `KeeperMotionTests.MissedDive_Fingertip_Within10cm` | 로직 | — |
| AC-04 | `GkContactMeterTests.SmotherClaimDistribution_HandBallWithin10cm` (073·074 시드에서 장면 탐색) | 로직 | — |
| AC-05 | `e2e/080-gk-contact` (다이빙 선방·펀칭·실점) | E2E | mac-m1-1600p |
| AC-06 | 기존 054 미끄러짐·순간 회전 측정에 새 GK 클립 장면 추가: `GkContactMeterTests.KeeperClips_NoSlideNoSnap_AllRates` (1x·1.5x·2x·3x) + `farm.py art` 생성기 검사 | 로직 | — |
| AC-07 | `HandReachIkPerf.Solve_Under50us` + 클립 메모리 크기 기록 | 성능 | mac-m1-1600p |
| AC-08 | `KeeperMotionTests.SameLog_SameChoiceAndReach`, 기존 054 포즈 해시 테스트(GK 장면 포함), `farm.py art` 생성 결정론 | 결정론 | — |

로직 범위: `farm.py logic --spec 080 --scope auto` → Presentation 범위. 아트: `farm.py art --spec 080`. 장면: `farm.py shot --scenario 080-gk-contact`.

## 버그 방지
- **이벤트 필드 디코드**: `ClipConverter`가 `Flags = (A & 0x7FFF) | FromSim | (B << 16)`로 담는다. DiveStart `A = dir*10 + height`는 dir = −1이면 음수라 `& 0x7FFF`에서 부호가 깨질 수 있고, Parry `B`에는 `ParryDanger` 비트가 같이 있다. `KeeperMotion.Decode`가 이 둘을 처리하고, `KeeperMotionTests.Decode_NegativeDirection`·`Decode_ParryDangerMasked`로 잡는다. 필요하면 변환기 포장 방식을 고친다(Presentation 안, 시뮬 무변경).
- **후보가 비는 경우**: 동작 종류에 맞는 클립이 없거나 부위 필터로 다 빠지면 지금 이벤트 후보로 돌아간다(회귀 없음). `KeeperMotionTests.UnknownKind_FallsBackToEventCandidates`.
- **IK가 닿지 않는 경우**: 목표가 어깨 구 밖이면 구 표면으로 자르고 남는 차이는 화면 공 보정(≤ 15 cm)으로. 그래도 넘으면 측정기가 위반으로 잡는다(AC-02가 실패로 드러냄 — 숨기지 않는다).
- **IK 튐**: 접촉 ±0.2 s 평활 가중치. 연속 접촉(쳐낸 뒤 다시 잡기)이 0.4 s 안이면 두 IK 키를 겹치지 않게 앞 키를 줄인다. `KeeperMotionTests.ConsecutiveContacts_WeightsDoNotOverlap`.
- **실점 다이빙**: `MissesBall`이면 루트 보정 0(지금 규칙) + 손 이동량을 "공에서 멀어지는 쪽"으로만 써서 ≥ 15 cm. 손끝 플래그면 반대로 ≤ 10 cm.
- **클립 이름 변경**: `gk_dive_l/r` 참조(맵·`MotionMeter` 예외·테스트·E2E)를 함께 바꾼다. 맵 로딩은 없는 클립 이름을 오류로 내므로(`AnimationMap.Ids`) 빠뜨리면 바로 실패한다.
- **배급**: 굴리기는 손 접촉점이 지면 근처, 던지기는 머리 위. Pass(Part = Hand)만 두 클립을 받고 발 패스는 부위 필터로 막힌다. `KeeperMotionTests.FootPass_NeverPicksThrowClip`. 시뮬 공이 배급 순간 GK 발밑(0.11 m)이라 던지기 손–공은 기록만(스펙 FR-06 개정, D31) — 던지기는 화면 공 보정·손 IK를 하지 않는다(15 cm 상한 안에서 억지로 당기지 않음).
- **D30**: E2E는 `PlayFresh`로만 재생.

## 위험 · 미정
- 클립 ≈ 30개를 손 키프레임으로 만드는 양이 크다. 다이빙 12개는 높이·도달 매개변수 하나로 생성하는 함수 1개로 묶는다.
- 기준 경기(시드 20261103)의 GK 접촉 6장면이 지금 엔진(084~087 보정 뒤)에서도 나오는지 T01에서 먼저 잰다 — 손–공 거리 기준선을 다시 기록한다.
- 073·074 장면(스마더·클레임·배급)을 담은 짧은 실경기 구간을 찾는 시드 탐색이 필요하다 (073·074 테스트 시드 7400·8400부터).
- `armLength`는 기준 키(1.80 m) 값이고 선수 키 배율(054 FR-87)로 키운다.
