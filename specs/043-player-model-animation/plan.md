# 043 — 선수 모델 · 애니메이션 매핑 · 플랜

상태: 완료 (2026-11-04, 리포트 https://claude.ai/artifact/2xMJcHgU79JDxB7HARnbdz)
스펙: ./spec.md

## 접근
선수 에셋은 044 경기장처럼 **Blender 스크립트 패키지 하나**(`Art/Blender/scripts/player/`)가 원본이다(FR-30, 2026-11-04 결정: 외부 모델·모션·텍스처 파일 없음). 진입점 `gen_player.py`가 ① 로우폴리 베이스 메시 LOD0·1·2(같은 뼈대, 해상도만 다르게 절차 생성) ② Unity Humanoid 이름 규칙의 뼈대와 자체 가중치 ③ 체형·헤어 셰이프키 ④ 절차 키프레임 클립 약 80개 ⑤ 텍스처(키트 마스크·피부 디테일·얼굴 프리셋 아틀라스·헤어)를 만들고, `player.glb` + `player.clips.json`(클립 태그) + 미리보기 PNG를 낸다. 텍스처 배치(앞판·뒤판·소매·반바지·양말, 번호·이름 영역)는 **우리 메시의 UV 펼침에서 직접 정한다**. 외부 템플릿은 참고하지 않는다.

Unity 쪽 애니메이션은 **시뮬 시각의 함수**로 평가한다. 하이라이트는 이미 시뮬된 데이터(006 `ReplayFrame` 30 Hz + `SimEvent`)를 재생하므로, 접촉 시각을 미리 알고 클립을 고를 수 있다(research 06 § 3.2). 클립 선택·접근 보정·루트 보정·공 화면 보정은 Unity 비참조 순수 C# 계층(`Players/Logic`)에서 결정하고, 그 결과(클립 id·클립 시각·가중치)를 **PlayableGraph 수동 평가**로 포즈에 적용한다. 렌더 프레임 간격은 입력이 아니다. 그래서 30 fps·60 fps·배속이 클립 선택을 바꾸지 않는다(AC-12, FR-50~52).

검토한 대안:
1. Animator Controller + Blend Tree: 스펙 제외 항목이 "Animator + Blend Tree 기준"이라고 적었지만, Animator는 프레임 델타로 상태를 진행해서 30/60 fps에서 전환 시점이 달라진다(AC-12 위반). **Blend Tree와 같은 역할(속도·방향 가중치 블렌드)을 AnimationMixerPlayable로 구현**하고 가중치는 순수 C#이 계산한다. (2026-11-04 승인, 스펙 문구 수정됨)
2. 자동 가중치(`ARMATURE_AUTO`): 결과가 메시 해상도에 따라 흔들려서 LOD 간에 포즈가 어긋난다. 뼈 선분 거리 기반 가중치를 직접 계산한다(LOD 공통 규칙).
3. Decimate로 LOD 만들기: 셰이프키·UV 경계가 깨진다. 같은 파라미터 생성기에 분할 수만 바꿔 LOD를 만든다.

## 모듈 · 파일
| 모듈 | 파일 | 역할 |
|---|---|---|
| Blender 스크립트 | Art/Blender/scripts/gen_player.py | 진입점. `--out`, `--lods 0,1,2`, `--clips all|<목록>`, `--seed`, `--no-preview`. 044와 같은 규칙(인자 검증 → 종료 코드 2, 임시 폴더에서 다 만든 뒤 옮김, 검사 실패 → 3) |
| Blender 스크립트 | player/body.py | 파라미터 인체(머리·몸통·팔다리 단면 링 + 손발 블록). LOD별 링·분할 수. 머티리얼 슬롯 2개: `Body`(피부·얼굴·헤어, 마스크로 구분), `Kit`(셔츠·반바지·양말·축구화). 단일 메시 |
| Blender 스크립트 | player/uv.py | UV 펼침 규칙: Kit 0~1 공간에 앞판·뒤판·소매 L/R·반바지·양말·축구화 고정 사각 영역, 번호(앞·뒤)·이름(뒤) 영역은 `layout`으로 내보낸다. Body는 얼굴·피부·헤어 영역 |
| Blender 스크립트 | player/rig.py | Humanoid 이름 뼈대(Hips, Spine, Chest, UpperChest, Neck, Head, L/R Shoulder·UpperArm·LowerArm·Hand·UpperLeg·LowerLeg·Foot·Toes) + 뼈 선분 거리 가중치(최대 4본, 정규화) |
| Blender 스크립트 | player/shapes.py | 셰이프키: `Thin`, `Muscular`, `Heavy`(체형 3축, FR-01), 헤어 스타일 `Hair_Short/Long/Curly/Bun`(두피 캡이 기본 = 삭발). 극단 조합 관통 검사용 |
| Blender 스크립트 | player/textures.py | 1K 텍스처를 numpy로 그려 PNG 저장(결정론): 키트 마스크 RGB(주·보조·포인트, 패턴 6종: 단색·세로줄·가로줄·반반·어깨띠·사선 — 실존 클럽 디자인 재현 아님), 원단 노말(직조 노이즈), 피부 디테일(밝기만, 톤은 셰이더), 얼굴 프리셋 아틀라스 8종(눈·눈썹·입 도형 조합, 가상), 헤어 색 마스크 |
| Blender 스크립트 | player/clips/*.py | 절차 클립: `gait.py`(걷기·조깅·달리기·전력질주 8방향 — 보폭 = 속도 × 주기, 발 접지 구간 고정), `poses.py`(키 포즈 라이브러리), `actions.py`(패스·슛·크로스·트래핑·헤딩·태클·몸싸움·넘어짐 8방향·GK·세리머니·심판 등). 클립마다 태그(FR-32): 접촉 프레임, 부위, 접근 각도 범위, 공 높이·속도 범위, 루트 이동량, 접지 구간 |
| Blender 스크립트 | player/checks.py | 삼각형·머티리얼·텍스처 크기(AC-03), 극단 체형 4종 발 접지 높이·자기 관통(BVH 겹침, AC-02), 클립 태그 완결성, 걷기 사이클 접지 미끄러짐(AC-09 원본 쪽) |
| Blender 스크립트 | player/export.py | 044 `export.py` 축 변환 규칙 재사용(Blender (x,y,z) → Unity (−x,z,−y)). glb(스킨·셰이프키·애니메이션 포함), `player.clips.json`·`player.layout.json`(UV 영역) 키 정렬·6자리 |
| Blender 테스트 | Art/Blender/tests/test_player.py | 백그라운드 통합 테스트: 예산, 극단 체형, 2회 바이트 동일, 클립 목록·태그, 생성 시간 |
| 도구 | Tools/devicefarm/farm/art.py (수정) | `farm.py art --spec 043`: 044와 같은 흐름 + 미리보기 격자(LOD 3단·극단 체형 4종·키트 패턴 6종) |
| 도구 | Tools/fonts/bake_glyphs.py | OFL 폰트(Noto Sans KR Black) → 글리프 아틀라스 2장(이름: ASCII·Latin-1·KS X 1001 한글 2,350자 36 px, 2048²; 번호: 0~9 128 px) + 메트릭 JSON. 같은 폰트 → 같은 바이트 |
| 서드파티 | Assets/_Project/Presentation/Players/Fonts/NotoSansKR-Black.ttf | OFL. `Art/LICENSES.csv` 한 줄 (FR-21). 받을 수 없으면 멈추고 보고 |
| 산출물 (LFS) | Assets/_Project/Presentation/Players/Generated/player.glb, *.json, Textures/*.png | 생성물. 손으로 고치지 않는다 |
| RoadToPro.Presentation | Presentation/Runtime/Players/Logic/*.cs (Unity 비참조, Core 타입만) | `AppearanceGenerator`(FR-01~03), `KitColorResolver`(ΔE2000, 2nd·3rd, GK·심판, FR-22), `KitTextLayout`(번호·이름 배치·축소·자르기, FR-23·65·66), `ClipLibrary`(clips.json), `AnimationMap`(매핑 표, FR-40·AC-06), `LocomotionBlend`(속도·방향 → 8방향 × 4보법 가중치·재생률, 블렌드 상한 FR-41·62), `ActionClipSelector`(FR-42·43·61), `ActionTimeline`(선수별 액션 큐·우선순위, FR-60), `RootCorrection`(FR-46), `BallVisualCorrection`(FR-44), `HitDirection`(FR-47), `OutcomeClipRule`(FR-48), `AnimPlan`(선수별 시각 → 클립 가중치 목록, 순수 함수) |
| RoadToPro.Presentation | Presentation/Runtime/Players/PlayerRig.cs | 선수 1명: SkinnedMeshRenderer·LODGroup(크로스페이드, FR-13), 루트 균등 스케일 = 키/기준키, 셰이프키 가중치, 머티리얼 인스턴스(Body·Kit) |
| RoadToPro.Presentation | Presentation/Runtime/Players/PlayerAnimationSystem.cs | 25명 PlayableGraph(수동 평가) 관리. 매 프레임 재생 시각 t → `AnimPlan` → 믹서 가중치·클립 시각 → `Evaluate(0)`. 이후 발 접지 IK. 할당 없음 |
| RoadToPro.Presentation | Presentation/Runtime/Players/FootGrounding.cs | 2본 해석 IK로 접지 구간 발 높이 0(평평한 피치, FR-45) |
| RoadToPro.Presentation | Presentation/Runtime/Players/KitCompositor.cs | 팀당 1회: 마스크 + 3색 + 번호 SDF + 이름(TextCore) → 팀 아틀라스(선수 칸별 번호·이름). 하이라이트 로딩 중에만, 재생 중 호출 시 진단 로그 + 카운터(AC-17) |
| RoadToPro.Presentation | Presentation/Runtime/Players/IPlaybackClock.cs, FixedPlaybackClock.cs | 재생 시각 공급(041 소유). 041 전까지 E2E·테스트는 고정 클럭 |
| RoadToPro.Presentation | Presentation/Shaders/PlayerBody.shader, PlayerKit.shader | URP Lit 단순화: Body = 피부 톤 × 디테일, 얼굴 아틀라스 칸, 헤어 색 마스크. Kit = 마스크 3색 + 원단 노말 + 팀 아틀라스 칸(번호·이름) |
| RoadToPro.Presentation | Data/animation/043-anim-map.json | 매핑 표: `MotionHint`·`SimEventKind`·`BallActionKind`·`BodyPart` → 클립 후보·기본 클립 |
| RoadToPro.Presentation.Editor | Presentation/Editor/Players/PlayerImportSettings.cs | glTFast 임포트: 애니메이션 = Mecanim(Generic) 클립, 머티리얼 → PlayerBody/PlayerKit 교체 |
| RoadToPro.Verification | Verification/Runtime/Samples/PlayerLineupScreen.cs, PlayerPerfScreen.cs | E2E: 22명 + 심판 3명 정면·후면 일렬(캡처 영역: 팀별 주색 영역, 번호 영역), 25명 합성 질주·액션 장면(성능 구간) |
| RoadToPro.Verification.Editor | Verification/Editor/PlayerSceneSetup.cs, FarmBuild.cs (수정) | `Player043.unity` 생성(044 경기장 + 선수 25명), 빌드 씬 추가 |
| 도구 | Tools/devicefarm/farm/imagechecks.py (수정) | `deltaE`(영역 평균색 쌍 ΔE2000 ≥ 임계), `glyphInside`(번호 영역 글자 높이 ≥ px, 영역 밖 글자 픽셀 0) |
| 데이터 | Data/e2e/043-kit-lineup.json, 043-anim-perf.json | AC-13(전체 프로필), AC-16(low-igpu-1080p). AC-14·15는 보류(041 이후)라 이번엔 만들지 않음 |
| RoadToPro.Tests.EditMode | Tests/EditMode/Presentation/Players/*Tests.cs | 아래 테스트 전략 |
| RoadToPro.Tests.Performance | Tests/Performance/Players/KitCompositorPerf.cs | AC-17 팀당 ≤ 50 ms |

- 의존: Presentation → Core·MatchEngine(`ReplayFrame`, `SimEvent`, `BodyPart` 읽기). 코어 모듈 변경 없음 → 결정론 리뷰 대상 아님. 다만 `Players/Logic`은 결정론 요구(AC-12)가 있어 `determinism-reviewer`에 한 번 보인다. Packages 변경 없음(glTFast·animation 모듈 이미 있음, Animation Rigging 패키지 안 씀).

## 데이터 · 스키마
`player.clips.json` (schemaVersion 1):
```
{ "schemaVersion":1, "fps":30, "generator":{...},
  "clips":[{"id":"pass_inside_r","group":"pass","frames":24,"contact":11,"part":"RightFoot",
            "approach":[-30,30],"ballHeight":[0,0.3],"ballSpeed":[3,18],"root":[dx,dz],"rootYaw":deg,
            "stance":[[from,to,"L"|"R"],...],"loop":false,"speed":null}] }
```
로코모션 클립은 `speed`(m/s, 루트 속도)와 `dir`(0~315°)이 있고 `contact`가 없다.

`043-anim-map.json`: `{"locomotion":{"Idle":[...],...}, "events":{"Pass":{"candidates":["pass_*"],"default":"pass_inside_r"},...}, "outcome":{"TrapFail":"trap_fail_*","TackleWin":"tackle_win_*"}, "hit":{"N":"fall_n",...8}}`. 매핑에 없는 종류 → 진단 로그 + `default` 전역 클립(AC-06).

외형(`PlayerAppearance`): 키 cm, 체중 kg, 체형 0~2, 피부 톤 0~7, 얼굴 0~7, 헤어 스타일 0~4, 헤어 색 0~5. 입력은 002가 없으므로 `BodyInput {id, heightCm?, weightKg?}`, 값이 없으면 id 시드에서 유도. 시드 = `StableHasher(id, "appearance")` → `SplitMix64`.

## 결정론 · 성능
- Blender: 044와 같은 규칙(`read_factory_settings(use_empty=True)`, 시드 하나, 고정 순서, glTF 옵션 전부 명시, JSON 정렬). 텍스처는 numpy로 그려 Pillow 없이 Blender 이미지 저장 → 바이트 비교.
- Unity 재생: 포즈 = f(t). 선수별 상태는 **리플레이 프레임 경계(30 Hz)에서만** 갱신한다(로코모션 위상 적분, 액션 시작 확정). 렌더 프레임 사이는 그 상태에서 해석적으로 계산한다. 가중치·클립 시각은 float이지만 입력이 같으면 같은 연산 순서 → 30/60 fps 같은 t에서 같은 값(AC-12). 포즈 해시는 본 로컬 회전을 1e-4로 양자화해서 잰다.
- 성능: 25명 × PlayableGraph 1개, `Evaluate`는 시스템 루프 하나에서 호출. 선수별 가중치 배열·후보 버퍼는 미리 할당. 머티리얼 2개/명 → 렌더 드로우콜 ≤ 50 + 그림자(LOD2는 그림자 끔). LOD 화면 높이 비율 [제안] 0.25 / 0.08 / 0.02(LOD2 이하 컬링 없음). 측정: E2E `043-anim-perf`에서 프로파일 마커 `Players.Anim`(CPU p95), 드로우콜·삼각형, GC.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `AppearanceTests.SameId_SameAppearance`, `NoDuplicateCombos_InMatch`, `Collision_NextCombo` | 로직 | — |
| AC-02 | `test_player.ExtremeBodies_FootGround_NoSelfIntersect`, `PlayerRigTests.ExtremeBodies_FootHeight` | 로직 | — |
| AC-03 | `test_player.Budgets_Lod_Materials_Textures`, `PlayerImportTests.Budgets` | 로직 | — |
| AC-04 | `KitColorResolverTests.SimilarPrimaries_AwaySwitches`, `ThirdKit_WhenSecondAlsoClose`, `GkAndReferee_Distinct` | 로직 | — |
| AC-05 | `KitTextLayoutTests.Numbers_1_7_10_99_Names_0_3_12_25_InsideArea`, `KitCompositorTests.Glyphs_InsideCell` | 로직 | — |
| AC-06 | `AnimationMapTests.AllKinds_Mapped`, `Unknown_LogsAndDefault` | 로직 | — |
| AC-07 | `ContactAccuracyTests.ThirtyScenes_ContactFrameAndBall`(합성 장면 30개: 패스 12·슛 10·크로스 8, 실제 클립 샘플링) | 로직 | — |
| AC-08 | `ActionClipSelectorTests.StrideAdjust_Within15Pct_OrOtherClip` | 로직 | — |
| AC-09 | `LocomotionSlideTests.Stand_Jog_Sprint_Turn_SlideUnder3cm`, `test_player.Gait_StanceFootFixed` | 로직 | — |
| AC-10 | `RootCorrectionTests.SyntheticHighlight_NoFrameOver03m` | 로직 | — |
| AC-11 | `ActionClipSelectorTests.OneTouch_NeverLate` | 로직 | — |
| AC-12 | `AnimDeterminismTests.Fps30vs60_SameClipAndTime`, `TwoRuns_SamePoseHash` | 결정론 | — |
| AC-13 | `e2e/043-kit-lineup` + `deltaE`·`glyphInside` | E2E | 전체 |
| AC-14 | `e2e/043-contact-closeup` + `contactDistance` (041 대신 FixedPlaybackClock, 2026-11-04) | E2E | smoke |
| AC-15 | `e2e/043-speed-motion` + `sameImage` | E2E | smoke |
| AC-16 | `e2e/043-anim-perf`(GC·드로우콜·삼각형) + `farm.py bench --spec 043`(IL2CPP CPU p95) | 성능 | low-igpu-1080p · mac-m1-1600p(IL2CPP) |
| AC-17 | `KitCompositorPerf.PerTeam_Under50ms`, `KitCompositorTests.DuringPlayback_Zero` + `043-anim-perf` 로그 카운터 | 성능 | low-igpu-1080p |

하네스 확장 테스트: `Tools/devicefarm/tests/test_imagechecks.py`에 `deltaE`·`glyphInside` 통과·실패 합성 이미지. 기존 셀프테스트 회귀 없음.

## 버그 방지
- 축·방향: Blender → Unity 축 변환은 044 실측 규칙. 클립 루트 방향(정면 = +Z 로컬)과 시뮬 yaw 부호를 `PlayerRigTests.Yaw_FacesSimDirection`으로 고정한다.
- 경계값: 키 160·200, 체중 극단, 번호 0·100(표시 안 함 + 진단 로그), 이름 0자·25자·한글 혼합, 남은 시간 0인 원터치, 정지 상태 최고속도 요청(블렌드 상한 + 진단 로그), 장면 중 교체·퇴장(`Absent` 힌트 → 화면 밖 처리).
- ΔE2000: 표준 테스트 데이터(Sharma 2005 표 34쌍)로 C#·Python 구현을 둘 다 검증한다.
- 액션 겹침: 같은 틱 두 이벤트는 `SimEvent.Seq` 순서. 피격은 즉시 끊기.
- 공 보정은 화면 위치만 바꾼다: 테스트가 시뮬 `ReplayFrame` 값을 보정 전후로 비교해 불변을 확인한다.
- 캡처 안정성: 일렬 장면은 정지 포즈(클립 시각 고정)라 연속 2프레임 동일 규칙이 그대로 통한다.
- 성능 카운터 null → 실패(044와 같은 규칙).

## 구현 중 바뀐 점 (2026-11-04)
- T02: Unity `FontEngine`에 CPU 래스터화 공개 API가 없다(`TryAddGlyphToTexture`는 내부). TextCore 동적 아틀라스 대신 **오프라인 구운 글리프 아틀라스**(위험 항목의 대체안)를 쓴다. KS X 1001 밖 한글 음절은 표시하지 않고 진단 로그(이름 영역은 남은 글자로 배치). 아틀라스 PNG는 `PlayerImportSettings`가 읽기 가능 R8로 임포트.
- `PlayerImportSettings`(T18 파일)를 T02에서 먼저 만들었다(글리프 임포트 규칙).
- 생성기: 키는 프레임 0부터(glTF 내보내기가 0부터 샘플링 — 1부터면 Unity 클립이 1프레임 길어 발이 미끄러졌다). 스윙은 Hermite(경계 속도 연속) + 들기 sin^0.5. 접촉 키 포즈는 다리 각도 격자 탐색으로 공 중심 높이에 맞추고, 모든 샘플에 지면 보정. 셰이프키 기본값 0 명시(Blender 5 기본 1.0).
- glTFast 블렌드셰이프 프레임 가중치는 1(100 아님) → PlayerRig는 비율 × 프레임 가중치.
- 매핑 표 위치: `Data/animation/` 대신 `Presentation/Players/Data/043-anim-map.json`(빌드에 들어가는 TextAsset).
- 외형(2026-11-04 사용자 요청 '사람답게'): 머리 조형·귀·얼굴 텍스처 보강(흰자 = body.png A), 앞뒤 비대칭 단면(가슴·엉덩이·종아리·이두), 헤어 10종(버즈·크롭·페이드·곱슬·아프로·장발·번·모히칸·드레드·삭발), 키는 역할별 정규분포(GK 188·수비 183·미드 179·공격 181 cm, 표준편차 5.2~6.4 — 공개 연구: World Cup 736명·EURO 2008 분석), BMI 23.2 ± 1.1. LOD0 9,860 / LOD1 2,858 / LOD2 952 tris.
- 키트 배정: GK·심판은 ΔE ≥ 30(스펙 20보다 엄격, 렌더 후 색차가 5~8 줄어드는 실측 반영), GK 팔레트에서 파랑~보라를 뒤로.
- LOD: 크로스페이드 대신 히스테리시스 ±15%(FR-13 허용) — 전환 중 두 단계를 같이 그리는 비용 제거. 그림자는 LOD2를 서브메시 하나로 합친 그림자 전용 프록시.
- 애니메이션: 25명 공유 PlayableGraph 1개(한 번 평가) + 선수별 슬롯 7개 믹서(클립 상시 연결 82개보다 빠름).
- 키트 셰이더: 글자 샘플을 분기 밖·LOD 0 명시로(분기 안 암시적 미분이 정의되지 않아 프레임마다 값이 달라졌다).
- E2E: 정지 라인업은 포즈를 정적 메시로 굽고(Freeze) LOD0 고정. 등번호 검사는 글자만 키 색(마젠타)으로 그린 BackKey 캡처로. 렌더 카운터는 구간 평균으로 판정(프레임 페이싱 '0 → 두 배' 기록).
- AC-16 CPU 판정 런타임은 IL2CPP(2026-11-04 사용자 결정, 006 D17과 같은 규칙). E2E(Mono 개발 빌드)의 animCpuMs는 기록만 — IL2CPP 애니메이션 벤치는 후속 T27.

## 위험 · 미정
- **절차 모션 품질**: 키프레임 생성 모션은 모캡보다 딱딱하다. 스타일라이즈드 목표에는 맞지만 1.5x 자연스러움(054 기준)을 못 맞추면 054에서 튜닝한다. 뼈대를 Humanoid 이름 규칙으로 두어 나중에 외부 모션으로 바꿀 수 있다.
- glTFast의 스킨·셰이프키·여러 애니메이션 임포트(Mecanim Generic)를 첫 태스크에서 확인한다. 안 되면 FBX 내보내기로 바꿔야 하므로 멈추고 보고한다(044는 생성 시각 때문에 FBX를 버렸다).
- TextCore 동적 아틀라스로 한글 이름을 CPU 합성할 수 있는지(읽기 가능 텍스처) 확인이 필요하다. 안 되면 이름 글리프를 오프라인으로 굽는 방식(KS X 1001 2,350자)으로 바꾸고 보고한다.
- 이 기기에는 내장 GPU가 없어서 `low-igpu-1080p`는 해상도·품질만 흉내 낸다(000·044와 같은 한계).
- 054가 소유하는 수치(공 보정 상한, 미끄러짐 p95, 0.3 m)는 054 확정 전까지 스펙의 [제안] 값을 튜닝 상수로 둔다.
