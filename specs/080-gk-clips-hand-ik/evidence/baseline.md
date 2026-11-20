# 080 기준선 (T01, 2026-11-20)

## 011 기준 경기 (시드 20261103, `GkContactMeterTests.GkContacts_HandBallWithin10cm` Explicit 실행)
084~087 보정 뒤에도 GK 접촉 6장면이 나온다. 시각 차·부위·모순은 통과(`GkContacts_Timing33ms_NoContradiction`), 손–공 거리만 실패.

| 장면 | 클립 | 손–공 |
|---|---|---|
| gk-Parry-1675 | gk_dive_l | 160.2 cm |
| gk-Catch-3901 | gk_dive_l | 148.5 cm |
| gk-Parry-4407 | gk_dive_r | 135.3 cm |
| gk-Catch-7318 | gk_dive_r | 140.2 cm |
| gk-Catch-10600 | gk_dive_r | 134.1 cm |
| gk-Catch-16096 | gk_dive_r | 161.7 cm |

## GK 동작 이벤트 기하 (`Diagnose_KeeperContactGeometry`, 010 실경기 시드 7400~7403 + 기준 경기)
접촉 순간 GK 시뮬 위치·정면 기준 공 위치 (m). side = 오른쪽 +, fwd = 앞 +.

| 이벤트/동작/높이 | n | side 범위 (|side| 중앙·최대) | 공 높이 | fwd |
|---|---|---|---|---|
| Catch/CatchChest/Mid | 6 | −0.55~+0.33 (0.32·0.55) | 0.62~1.44 | −0.40~+0.32 |
| Catch/CatchGround/Low | 8 | −0.50~+0.86 (0.33·0.86) | 0.11~0.15 | −2.05~+0.16 |
| Catch/Dive/Low | 24 | −1.00~+1.05 (0.44·1.05) | 0.11~0.58 | −1.06~+1.07 |
| Catch/Dive/Mid | 9 | −1.07~+0.98 (0.75·1.07) | 0.62~1.48 | −1.00~+0.65 |
| DiveStart(실점)/Low | 14 | −1.18~+2.50 (0.81·2.50) | 0.11~0.60 | −1.14~+1.80 |
| DiveStart(실점)/Mid·High | 1·1 | −0.80 · −0.02 | 1.29 · 1.93 | |
| Parry/ParryFront/Low | 3 | ≈ 0 | 0.11 | −0.04~+1.09 |
| Parry/ParrySide/Low | 18 | −1.04~+0.90 (0.57·1.04) | 0.11~0.19 | −1.06~+1.08 |
| Parry/ParrySide/Mid | 1 | +0.94 | 0.77 | −0.50 |
| Punch/PunchTwoHands/High | 1 | −0.23 | 1.88 | +0.15 |
| Smother | 2 | −0.14~0 | 0.11~1.48 | −1.20~+1.84 |
| Pass(Part = Hand, GK 배급) | 40 | **0.00** | **0.11** | **0.00** |

## 관찰
1. **다이빙 이중 이동**: 시뮬은 다이빙 동안 GK 루트를 옆으로 옮긴다(`GoalkeeperSystem` `BeginExternal`). 재생 루트 = 시뮬 위치(`AnimPlan.EvaluateBase` `s.Root = simPos + RootOffset`)인데, `gk_dive_l/r` 클립도 엉덩이를 1.0~1.2 m 옆으로 옮겨 접촉점이 루트에서 1.76 m 옆이다. 실제 공은 시뮬 루트에서 옆으로 중앙 0.44 m·최대 1.07 m(선방). 손–공 134~162 cm는 대부분 이 이중 이동 때문.
2. **배급 공 위치**: GK 손 배급 40건 모두 이벤트 공 위치 = GK 루트 바닥(높이 0.11 m). 오버암 던지기의 손 높이(≈ 2 m)와 1.9 m 차이 — 화면 공 보정 상한 15 cm(054 FR-35)로는 못 메운다.

## 결과 측정 (T06·T13a·T14)
| 항목 | 값 | 예산 |
|---|---|---|
| 손 IK 1회 (GK 1명 양손, `HandReachIkPerf.Solve_Under50us`, 에디터) | 1.2 µs | ≤ 50 µs |
| 생성물 크기 차 (`player.glb` 13.97 → ≈ 15.1 MB, `player.clips.json` 97 → ≈ 134 KB) | ≈ +1.2 MB | ≤ 2 MB |
| 에디터 클립 메모리 (`PlayerImportTests.ClipMemory_Within180Percent`, 154 클립) | 42.15 MB = 043 기준 +78.9 % | ≤ +80 % (055 예산, 2026-11-20 개정) |
| GK 클립 | 35개 (다이빙 12 · 잡기·쳐내기·펀칭·클레임 13 · 스마더·스프레드·이동·일어나기·배급 10 · 앞 덮치기 2 − 기존 2 이름 변경) | — |
