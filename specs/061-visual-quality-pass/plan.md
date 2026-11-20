# 061 — 그래픽 품질 2차 · 플랜

상태: 완료 (2026-11-20 T22 검증): AC-01·03·04·05·06(평균 L*·채도)·08·09·11·12·13·14 통과, 로직 2031개 중 실패 0, Blender 35/35, 60 fps 통과(mac-m1-1600p 세 프리셋·두 모드). AC-13 육안 승인 2026-11-20 "일단 ㅇㅋ". AC-07 계단 현상·FR-06 대비 폭 일부·FR-83 High 선명도·AC-10 화면 1곳은 Q14대로 후속 스펙(D32)으로 넘겨 완료 조건에서 뺌. 리포트 https://claude.ai/artifact/VsX4BPo4AXGZ27UrA7oxfz
스펙: ./spec.md

## 접근
두 층으로 간다. ① **공통 룩**(모든 프리셋·두 모드가 같이 씀): 볼륨 프로필 하나(`Look`)에 톤매핑·색 보정(채도·녹색 차분하게·하이라이트 접기·그림자 푸른 기)을 담고, 044 조명 프리셋에 하늘·환경광·반사 값을 더한다. 하늘은 **절차 생성 스카이박스 셰이더**(그라데이션 + 시뮬 시각으로 흐르는 구름 + 지평선 원경 실루엣 띠)로 만들어 외부 HDRI 라이선스·용량 문제를 없애고, 시간대·구름이 시뮬 시각의 함수라 결정론이 쉽다. 환경광은 하늘 색에서 뽑은 3색 환경광 + 하이라이트 로딩 때 한 번 굽는 반사 큐브맵(Low는 하늘 평균색만)을 쓴다. 경기장 구조물 AO는 044 Blender 생성기가 정점 색에 구워 넣고, 가벼운 자체 셰이더 `StadiumSurface`가 곱한다. 선수는 공용 셰이더 include에 림 라이트 항을 넣고, 발밑 접촉 어둡기는 기존 블롭 그림자를 모든 프리셋·시간대에서 켠다. ② **연출 장면 고품질 모드**: 순수 로직 `CinematicModeLogic`이 카메라 트랙(샷 종류·카메라 위치·시뮬 시각)만 보고 "모드 구간 + 블렌드 가중치"를 계산하고, 런타임 컨트롤러가 그 가중치로 **두 번째 볼륨(피사계 심도만 들어 있음)**·그림자 거리 축소(같은 해상도로 근거리 해상도 올림)·SSAO 기능(High만)·근거리 셸 잔디를 켠다. 룩 볼륨은 모드와 상관없이 그대로라 FR-82(색·톤·조명 불변)가 구조로 보장된다.

Medium·High는 지금 Mobile/PC 대역이다. 이 스펙이 Medium/High 시각 기능을 맡으므로(Q6) Low처럼 `Medium_RPAsset`·`High_RPAsset`을 새로 만들어(기존 Mobile/PC 에셋은 안 건드림) 실제 프리셋으로 바꾸고, 세 프리셋 모두 STP(High는 렌더 스케일 1.0 = TAA 역할)로 AA를 통일한다.

대안: Poly Haven HDRI 하늘은 사실감이 좋지만 시간대마다 파일이 필요하고 Low 1K에서 뭉개지며, 하늘·방향광 방위를 맞추려면 회전 보정이 따로 필요하다. Unity 라이트맵 베이크는 낮/나이트 두 벌·경기장 인자별 재베이크가 필요해 생성형 경기장(044·065)과 맞지 않는다. 그래서 둘 다 절차 생성·생성기 정점 AO를 골랐다. 피사계 심도는 비용 예산(≤ 0.6 ms) 때문에 URP Gaussian(먼 쪽 흐림, 초점 거리 기준으로 시작·끝 계산)으로 한다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.Presentation | `Runtime/Rendering/Logic/RenderPreset.cs`·`RenderPresetTable.cs` | Medium/High `IsStandIn = false`, 품질 레벨을 새 `Medium`·`High` 레벨로. 061 공통 기능 필드 추가(하늘 해상도·구름 흐름·반사 방식·비네트) |
| RoadToPro.Presentation | `Runtime/Rendering/Logic/VisualFeatureTable.cs` (새) | FR-34 공통 기능 표 + FR-81 고품질 모드 구성 표를 데이터로(프리셋 × 기능 → 켬/끔/값, 끄는 순서). AC-01 ① 대조 대상 |
| RoadToPro.Presentation | `Runtime/Rendering/Logic/CinematicModeLogic.cs` (새) | 순수 로직: 샷 목록(ShotPlan)·카메라 샘플 → 모드 구간. 진입 = Closeup·Celebration·리플레이 저각(ReplayShooter·ReplayReverse·ReplayHigh 중 카메라–피치 거리 ≤ 20 m 구간), 0.5 s 미만 제외, 연속 구간 병합, 컷 없는 전환은 0.3 s 블렌드, 키 모먼트 정지 중 고정. 047 컷신·076 샷은 `ShotKind`이 생기면 같은 표에 추가(지금은 표에 자리만, 연결 때 재확인). 초점 대상(엔티티 ID, 화면 밖이면 공으로 0.3 s 블렌드), 근처 선수 최고 품질 대상 2~3명 신호(062 연결용) |
| RoadToPro.Presentation | `Runtime/Rendering/Logic/LookParameters.cs` (새) | 룩 수치(채도·톤 곡선·그림자 색·블룸·비네트)를 한곳에. 볼륨 생성기와 테스트가 같은 값을 읽는다 |
| RoadToPro.Presentation | `Runtime/Rendering/CinematicModeController.cs` (새) | 런타임: 매 렌더 프레임 시뮬 시각으로 가중치를 얻어 모드 볼륨 weight·DoF 초점 거리·그림자 거리·SSAO 기능 활성·셸 잔디 활성을 적용. 할당 0 |
| RoadToPro.Presentation | `Runtime/Rendering/RenderQualityApplier.cs` | Medium/High도 실제 프리셋 경로(카메라 후처리 켬·STP). 룩 볼륨·모드 볼륨 연결 |
| RoadToPro.Presentation | `Runtime/Stadium/StadiumLighting.cs`·`StadiumLightingPreset.cs` | 프리셋에 하늘 값(위·지평선 색, 구름 양·색, 도시광, 태양 원반 방향 = 방향광), 반사 방식 추가. 카메라 배경 SolidColor → 스카이박스. 환경광을 하늘 색에서 계산. 위치별 환경광(FR-13): 지붕 그늘·터널 볼륨 안 선수에 환경광 배율(라이트 프로브 대신 박스 영역 몇 개 — 경기장 레이아웃에서 계산) |
| RoadToPro.Presentation | `Runtime/Stadium/SkyReflection.cs` (새) | 하이라이트 로딩 때 하늘만 그린 반사 큐브맵 1회 굽기(Medium·High, 128 px), Low는 하늘 평균색. 실패하면 기본 그라데이션 + 경고 1회 |
| RoadToPro.Presentation | `Runtime/Stadium/GrassShellRenderer.cs` (새) | 근거리 셸 잔디: 카메라 아래 40 × 40 m 격자 한 장을 셸 N겹(Low 4겹·반경 10 m · Medium 8 · High 12 [제안 — 측정 후], FR-58: 연출 장면 잔디는 전 프리셋에서 평평하지 않게)으로 인스턴스 그리기. 15~20 m 페이드. 눈 덮임(064)이면 끔 |
| RoadToPro.Presentation | `Shaders/Sky.shader` (새) | 절차 하늘: 그라데이션·구름(시뮬 시각 `_SimTime` 함수, 정지 중 고정)·지평선 원경 실루엣(건물·나무 일반 형태 노이즈, 실존 스카이라인 아님)·나이트 도시광. 하늘 마스크 디버그 키워드(검증용 단색) |
| RoadToPro.Presentation | `Shaders/GrassShell.shader` (새) | 셸 잎: 높이 ≤ 3 cm, 깎기 줄무늬 띠 방향(`PitchLines.hlsl`·`_StripeParams` 재사용)으로 눕힘, 라인 위는 라인 색, 바람 = f(`_SimTime`, 위치), 폭 ≤ 잎 높이 20 % |
| RoadToPro.Presentation | `Shaders/StadiumSurface.shader` (새) | 경기장 구조물용 가벼운 Lit: 기본 색·매끄러움·정점 AO·환경광·반사·메인 라이트 그림자. 044 Stadium_* 머티리얼이 쓴다 |
| RoadToPro.Presentation | `Shaders/PlayerCommon.hlsl`·`Pitch.shader` | 선수: 림 라이트 항(전역 `_RimStrength`, 시선–법선 프레넬 × 환경광 색, 모드 보정은 대상 선수만). 잔디: 저각 회색빛 반사(FR-07 행) |
| RoadToPro.Presentation | `Stadium/Materials/*.mat`, `Lighting_Day/Night.asset` | FR-07 알베도 범위로 값 조정, 하늘·환경광 값 |
| RoadToPro.Presentation | `Runtime/Highlight/HighlightAssets.cs` | 새 셰이더(하늘·셸 잔디·StadiumSurface·DoF 경로)를 셰이더 워밍업 목록에 추가 (FR-85) |
| RoadToPro.Presentation.Editor | `Editor/Rendering/PresetAssets.cs` (`LowPresetAssets.cs` 확장·이름 변경) | Medium/High URP 에셋·렌더러(High 렌더러에 SSAO 기능, 기본 꺼짐) 생성, 룩 볼륨 프로필·모드 볼륨 프로필 생성 (배치 실행). `ProjectSettings/QualitySettings.asset`에 Medium·High 레벨 추가 — **설정 변경, 구현 태스크에서 이유와 함께 확인받는다** |
| (Blender) | `Art/Blender/scripts/stadium/geometry.py`·`export.py`·`checks.py` | 구조물 정점 AO 굽기(색 속성 `ao`, 결정론 샘플·시드 고정) + 내보내기 + 검사(AO 0~1, 관중석 아래·지붕 아래가 더 어두움). 형상 자체는 바꾸지 않는다(065 소유) |
| RoadToPro.Verification | `Runtime/Samples/Visual061Screen.cs` (새) | 기준 장면 S1~S6 구성(고정 시드·시각·카메라, S4·S5는 두 모드 캡처), 위치별 환경광 합성 장면, 마스크 캡처(하늘·선수·경기장 밖·경계 — 같은 프레임을 레이어/디버그 키워드로 단색 재렌더), 4배 슈퍼샘플 기준 캡처, 일반/고품질 강제 토글, 061 기능 끔 토글(기준선용) |
| RoadToPro.Verification | `Data/e2e/061-visual-*.json`, `061-perf-highlight.json`, `061-mode-transition-release.json` (새) | E2E 시나리오 |
| (Tools) | `Tools/devicefarm/farm/imagechecks.py` (+ `tests/test_imagechecks.py`) | 새 검사: 채도 통계(평균·포화 비율·영역 색상), L* 평균·퍼센타일 폭, 하늘 지표, 영역 밝기 비율, 단색 회색 비율, 클리핑, 림 대비, 접촉 어둡기, 낙차(밝기·대비·채도·ΔE), 프레임 간 ΔE, 슈퍼샘플 경계 차이·팬 깜빡임, 지면 경계선 높이 표준편차 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/Presentation/Rendering/*` (새) | 아래 테스트 전략 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 — Presentation 안에서만 바뀌고 MatchEngine·Core는 읽기만 한다. 카메라 데이터는 042 `CameraTrack`·`ShotPlan`을 읽는다.

## 데이터 · 스키마
- `StadiumLightingPreset`(ScriptableObject) 필드 추가: `skyTop`, `skyHorizon`, `cloudCover`(0~1), `cloudColor`, `cityGlow`, `cloudSpeed`(m/s, Low 0), `reflection`(Simple/Cubemap), `rimStrength`, `shadeAmbientScale`(위치별 환경광 배율). 기본값은 낮/나이트 에셋에 넣는다. 잘못된 시간대 값 → 낮 + 경고 1회(AC-03).
- `VisualFeatureTable`: `Feature` 열거형(Sky, Ambient, Reflection, Backdrop, BakedAo, SsaoNormal, Tonemap, Bloom, Vignette, PlayerShadow, ContactDarkening, Rim, FloodlightShafts, AntiAliasing) × `PresetId` → 값. `CinematicFeature`(PlayerTopTier, Dof, ShadowRes, AoQuality, NearGrass, RimBoost) × `PresetId` → 켬/끔 + `CutOrder`. 표 원본은 스펙 FR-34·81 그대로, `[미정]`(조명탑 빛줄기 High)은 끔으로 두고 표에 `Undecided` 표시.
- 모드 구간: `CinematicSpan { double Start, End; bool CutIn, CutOut; int Focus; }`, `CinematicModeLogic.Weight(double simTime)` → 0~1.
- 생성 glb: 구조물 메시에 정점 색 속성(AO) 추가. 044 생성 요약에 AO 통계 한 줄. 경기장 인자·세이브 스키마 변경 없음.
- 새 외부 텍스처·HDRI 없음(절차 생성) → `Art/LICENSES.csv` 변경 없음이 기대값이고 AC-03 검사가 확인한다.

## 결정론 · 성능
- 하늘 구름·셸 잔디 바람·모드 가중치·DoF 초점 블렌드는 모두 **시뮬 시각(재생 시계)·샷 정보·위치만의 함수**. 렌더 프레임 누적값(Time.deltaTime 적분) 없음 → 재생 속도·fps·리플레이와 무관(AC-10). 키 모먼트 정지 중엔 시뮬 시각이 멈추므로 자동으로 정지.
- 모드 판단은 장면 빌드 때 카메라 트랙에서 한 번 계산(트랙과 같은 시점), 재생 중엔 조회만.
- 실행 중 자동 품질 조절 없음(Q9): 프리셋별 고정 구성.
- 할당 0: 컨트롤러·셸 잔디는 버퍼를 로딩 때 만들고 프레임 경로에서 `MaterialPropertyBlock`·미리 만든 행렬 배열만 쓴다.
- 전환 스파이크 방지: 모드 볼륨·SSAO·셸 잔디 리소스는 하이라이트 로딩 때 만들고, 셰이더 변형은 워밍업에 넣는다(DoF·SSAO 켬 상태로 워밍업 1프레임 렌더 포함). 모드 진입 때 생성·로드 0건.
- 그림자 근거리 상향은 URP 에셋 그림자 거리만 줄인다(그림자 맵 재할당 없음).
- **성능 측정·판정** (스펙 Q10, 2026-11-20 사용자 결정 "60은 만들어야 돼"): 프로파일러 마커(룩·하늘·DoF·셸 잔디·SSAO·모드 컨트롤러)와 E2E 성능 구간으로 잰다. **판정**: `mac-m1-1600p`에서 Low·Medium·High 각각, 일반·고품질 모드 모두 p95 ≤ 16.6 ms·최대 프레임 048 AC-09 규칙·GC 0 B, 모드 전환 ±1 s 최대 프레임 규칙·동기 로드·셰이더 컴파일 0건(릴리스), 진입 로딩 ≤ 4 s/2 s. **기록만(잠정)**: 세부 GPU 몫, 흉내 낸 Low 프로필. 못 맞추면 FR-81 끄는 순서(셸 잔디 → DoF → 그림자 → AO → 선수 등급)대로 그 프리셋 구성을 줄이고 표(`VisualFeatureTable`)를 고친다 — 고정 구성이라 실행 중 자동 조절은 하지 않는다.
- 시뮬 틱 경로 변경 없음 → IL2CPP 벤치 안 돌림. 코어 모듈 변경 없음 → `determinism-reviewer` 해당 없음(061 결정론은 AC-10 화면 결정론으로 판정).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `VisualFeatureTableTests.MatchesSpecTables` (FR-34·81 칸별), `PresetTableTests` 048 Low 항목 회귀, `CinematicModeLogicTests.ShotKinds_ModeDecision`·`ShortShot_Under0_5s_Normal`·`ConsecutiveShots_StayOn`·`ReplayFarCamera_Normal`, `LookInvarianceTests.BothModes_SameToneColorExposureLight` (룩 볼륨·조명 값 대조, 모드 볼륨에 DoF 외 항목 없음) | 로직 | — |
| AC-03 | `VisualLicenseTests.NewTextures_AllInLicensesCsv`, `SkyFallbackTests.MissingSky_GradientAndOneWarning`·`BadTimeOfDay_DayAndOneWarning`, imagechecks 하늘 지표 "해당 없음"(하늘 < 5 %) 단위 테스트 | 로직 | — |
| AC-04 | `e2e/061-visual-broadcast` (S1 낮·S2 나이트 + 위치별 환경광 합성 장면 + 056·057 장면 재캡처 육안) — 하늘·그림자 안 환경광·경기장 밖 회색·클리핑·팀 ΔE·블룸·림 대비·실루엣 수·접촉 어둡기 | E2E | mac-m1-1600p (Low·Medium·High) |
| AC-05 | `e2e/061-visual-environment` (S3·S6) — 경기장 밖 회색 비율, 하늘 밝은 쪽–방향광 방위 차(셰이더 파라미터 대조 로직 테스트 `SkySunAlignmentTests`도 같이) | E2E·로직 | mac-m1-1600p |
| AC-06 | `e2e/061-visual-color` (S1~S6, S4·S5 두 모드) — 채도·노출·장면 간 L* 차·기준선 대비 채도 감소. 재질 값 범위(FR-07)는 `MaterialAlbedoTests.SurfacesWithinSpecRanges` (머티리얼 기본 색 × 텍스처 평균 sRGB로 계산 — 알베도 확인 패스 대신 데이터 측정, 선수 행 제외) | E2E·로직 | mac-m1-1600p |
| AC-07 | `e2e/061-visual-aa` — S1~S6 vs 4배 슈퍼샘플 경계 차이(기준선 50 %·6/255), 느린 팬 깜빡임 ≤ 1.5배, `048-upscale`·`044-line-sharpness` 회귀 | E2E | mac-m1-1600p (Low·Medium·High) |
| AC-08 | `e2e/061-visual-grass` — 고품질 모드 근거리 4장면(Low·Medium·High): 지면 경계선 높이 표준편차·공 보임 90 %·공 화면 위치 불변·결 방향·명암 뒤집힘·소멸 경계·라인 끊김 0·S1 켬/끔 ≤ 1/255·무릎 관통 0, Low 고품질 모드도 4겹·10 m로 켜져 같은 기준 통과(FR-58). 결 방향·잎 높이·페이드 거리는 `GrassShellParamsTests`로도 | E2E·로직 | mac-m1-1600p |
| AC-09 | `e2e/061-visual-cinematic` — 메인 → 세리머니(컷) → 슈터 추적(와이프) → 메인: 낙차 ①~④, 프레임당 ΔE ≤ 1.5, DoF 대상 선명도 ≥ 95 %·0.3 s 블렌드, 최고 품질 대상 신호(062 전엔 신호만) | E2E | mac-m1-1600p (Medium·High) |
| AC-10 | `CinematicModeLogicTests.SameTrack_SameSpans`·`Weight_IndependentOfFrameRate`, `SkyCloudTests.CloudOffset_FunctionOfSimTime`, `GrassWindTests.SameTime_SamePose`, `VisualSimInvarianceTests.FeaturesOnOff_SameSimHashEventsBallCamera`, E2E 같은 장면 2회 캡처 동일(061-visual-color 안 반복 단계) + 1x·1.5x·3x·0.5x·30/60fps 상태 해시 | 결정론 | — / mac-m1-1600p |
| AC-11 | `e2e/061-perf-highlight` — Low: 두 모드 기준 장면 + 048 대표 3장면, p95 ≤ 16.6 ms·최대 프레임·GC 0·드로우콜 ≤ 300·삼각형 ≤ 50만·텍스처 ≤ 1 GB 판정, 061 추가 GPU 몫·하늘·후처리 몫 기록(잠정), 044 AC-14 회귀 | 성능 | mac-m1-1600p (Low) |
| AC-12 | 같은 시나리오 Medium·High: 두 모드 p95 ≤ 16.6 ms·GC 0 판정, DoF·셸 잔디 GPU·잔디 삼각형 ≤ 5만 기록 + 배터리 절약 30 ± 1 fps·끄면 1 s 안 60fps + 진입 로딩 4 s/2 s + 언로드 후 061 리소스 0 (048 AC-11·12 회귀) | 성능 | mac-m1-1600p (Medium·High) |
| AC-13 | 리포트: S1~S6 전·후(기준선 대비), Low/Medium/High × 두 모드, 061-visual-cinematic 연속 캡처 → 사용자 육안 승인 ①~④ evidence 기록 | E2E | mac-m1-1600p |
| AC-14 | `e2e/061-mode-transition-release` — 릴리스 빌드, 모드 전환 ≥ 8회: 전환 ±1 s 최대 프레임 규칙, 동기 로드·셰이더 컴파일 0, 모드 구간 p95 ≤ 16.6 ms·GC 0 | 성능 | mac-m1-1600p |

로직 범위: `farm.py logic --spec 061 --scope auto` → `presentation`(+ `verification`). 아트: `farm.py art --spec 061`(정점 AO 생성기 변경, ≈ 7분, 1회). 장면: `farm.py shot --scenario 061-visual-*`. 벤치(IL2CPP 시뮬): 해당 없음. 성능 장면: `farm.py shot --scenario 061-perf-highlight`·`061-mode-transition-release`(릴리스) 스펙 끝 1회.

## 버그 방지
- **기준선 먼저**: 착수 첫 태스크에서 현재 main으로 S1~S6 캡처·지표를 evidence/baseline에 남긴다(FR-02). 이후 "기준선 대비" 지표는 이 파일만 쓴다. 기능 끔 토글로 다시 만드는 기준선은 쓰지 않는다(재현 차이 위험).
- **룩 불변 보장**: 모드 볼륨 프로필에 DoF 외 오버라이드가 들어가면 `LookInvarianceTests`가 실패한다. 림 보정은 대상 선수 머티리얼 프로퍼티 블록에만 들어간다.
- **모드 경계**: 0.5 s 미만 샷, 연속 고품질 샷, 와이프 진입(042 FR-14 0.3 s 안에서 블렌드 끝), 텍스트 타임라인 직후 첫 장면이 고품질인 경우, 장면 건너뛰기, 키 모먼트 정지 — 각각 `CinematicModeLogicTests` 한 케이스.
- **DoF 초점**: 대상이 화면 밖·카메라 뒤면 공으로 0.3 s 블렌드. 초점 거리 0·음수 방지(하한 1 m).
- **STP 히스토리**: 모드 진입은 히스토리를 버리지 않는다(컷일 때만 기존 `ResetHistory`). DoF 켜짐·셸 잔디 등장 프레임 잔상은 048 AC-06 회귀로 잡는다.
- **팀 색**: 채도를 낮추면 팀 ΔE가 줄 수 있다 — 룩 조정 태스크마다 043 `kit-lineup` ΔE 검사를 같이 돌린다.
- **나이트 과노출**: 블룸 임계·흰 키트 ≤ 250/255·포화 ≤ 1 %를 나이트 장면에서 먼저 확인.
- **셸 잔디와 공·라인**: 잎 높이 3 cm < 공 반지름 11 cm라 공은 대부분 보이지만, 저각 슈터 추적에서 보이는 비율을 측정한다. 라인 위 잎은 같은 `PitchLines.hlsl` 함수로 색을 정해 라인 폭이 바뀌지 않는다.
- **Medium/High 실제화 회귀**: 대역 → 실제 프리셋으로 바뀌면 Medium·High 프로필로 찍던 기존 장면(043·054·055·056·057 등)이 달라진다. 기준 이미지 비교는 폐지됐으므로 실패는 아니지만, AC-13 리포트에 "영향받은 기존 장면" 목록과 재캡처를 싣는다.
- **하늘 리소스 실패**: 스카이박스 셰이더를 못 찾거나 반사 큐브맵 굽기 실패 → 그라데이션 단색 하늘 + 경고 1회, 예외 0.
- **화면비**: 21:9·16:10에서 스카이박스는 방향 기반이라 늘어나지 않는다. `ultrawide` 확인은 S1 한 장을 해상도만 바꿔 육안 확인(별도 프로필 매트릭스 없음).

## 위험 · 미정
- **"실사급" 판정은 결국 육안**이다. 지표는 원색·과포화·회색 하늘 같은 확실한 문제를 막는 하한이고, 실사 인상은 룩 수치 조정 반복이 필요하다. 룩 조정 태스크에 "S1·S4 캡처 → 사용자 중간 확인 1회"를 넣는다(최종 AC-13 전에 방향이 맞는지).
- 선수 자체(메시·재질·피부·머리)가 그대로면 연출 장면 실사감에는 한계가 있다 — 062 몫. 061은 062가 쓸 조명·후처리를 먼저 깐다.
- 정점 AO는 경기장 메시 밀도에 따라 품질이 갈린다. 관중석 띠처럼 정점이 적은 곳은 AO가 거칠 수 있다 — 065에서 형상이 바뀌면 같은 굽기 함수를 쓴다.
- `ProjectSettings/QualitySettings.asset`에 Medium·High 레벨을 추가해야 한다(00-workflow: 바꾸기 전 확인). 해당 태스크에서 다시 묻는다.
- 048 FR-21 예산 표·FR-01 DoF 칸, 기획서 5.5.1 셸 잔디·SSAO 문구 갱신은 스펙 Q3~Q6대로 후속(이 스펙 코드 범위 밖).
- 60fps 판정은 개발 맥 기준이다. M1은 내장 GPU보다 빠르므로 내장 GPU 실기기에서는 넘칠 수 있다 — 기기 확보 후 측정에서 넘치면 FR-81 끄는 순서대로 Low 구성을 더 줄이는 후속이 필요할 수 있다. 맥에서도 못 맞추면 구현 중 멈추고 어느 기능을 줄일지 묻는다.
