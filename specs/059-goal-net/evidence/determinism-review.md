# 059 결정론 리뷰 (determinism-reviewer, 2026-11-26)

대상: `GoalFrame.ApplyNet`(004 감속 영역 → 그물 면 접촉, 안·밖 분류), `BallStepper.cs:54`(서브스텝 시작 위치 전달), `BallParams`·`BallTuningIds`·`Data/tuning/ball.json`(`ball.net.damping` 삭제, `restitution` 0.01·`tangentKeep` 0.35).

## 결과: 결정론 위반 없음
- 금지 API(Unity·RNG·시간·컬렉션 순회·스레드) 없음, 할당 없음, 상수는 튜닝 또는 규격.
- NaN·무한대 새 경로 없음 (NaN 입력은 첫 분기에서 return, 기존 Sanitize가 처리).
- 안·밖 분류는 서브스텝 시작 위치만으로 정해지는 무상태·순서 무관 판정. 고정 위치와 판정 경계 사이 r 여유로 재분류 없음. 동률은 면 0 → 1 → 2.
- 깊이 축 좌표 왕복은 정확(Sterbenz, float32 10만 건 불일치 0), ±1 곱만.
- 필드 안 공(x ∈ [0, L])·골 판정(고정 전 선분으로 판정) 불변.

## 발견 사항과 조치
| # | 심각도 | 내용 | 조치 |
|---|---|---|---|
| 1 | 낮음~중간 | 그물에 안 닿은 골라인 뒤 공도 매 서브스텝 `cy + w`로 다시 써 Y가 1 ulp 바뀜 (코너 쪽, 상자 밖 공 불변과 어긋남) | **수정**: 면에 닿아 위치를 고친 경우에만 상태를 쓴다. `BallNetTests.FarFromNet_StateBitExact`(Y 5.3·63.7, 비트 비교) 추가 |
| 2 | 낮음 | 위 그물 윗면에 멈춘 공이 Air 상태로 고정점에 남음(진동 아님), 수평 속도가 비정규수를 거쳐 0 | **기록**: 재시작 재배치로 경기 영향 없음. docstring에 한계 기록, 012 바깥 면 화면 검증 때 처리 |
| 3 | 의심·낮음 | 공이 골라인을 완전히 넘기 전(0 < d < r)에도 옆·위 고정 → 이론상 다음 서브스텝 통과 판정에 영향 | **수정**: 옆·위 안쪽 고정은 d ≥ r에서만 (넘는 중인 공은 포스트·크로스바 충돌이 담당). 80경기 비교는 첫 골라인 밖 사건(골 이벤트 포함)까지 main과 동일 |
| 4 | 의심·낮음 | 크로스바를 스치고 넘어간 공의 지붕 고정 위치가 크로스바 접촉 범위와 겹쳐 리플레이에서 튈 수 있음 | **기록**: 골 판정 영향 없음(z ≥ H), 012 화면 검증 때 확인 |

## 수정 후 확인
- Ball 전체 + `TuningProvenanceTests` + 그물 접촉·골 시퀀스·클립 변환: 130개 중 129 통과 · 1 건너뜀(기준 생성용 Explicit)
- `GoalNetSeedSetTests`: 80경기 이벤트 3,429줄이 첫 골라인 밖 사건까지 main과 동일, 골 455개에서 자유로운 공 그물 상자 이탈 0 (선수가 몰고 나간 공 15개는 제외·기록)

# 2차 리뷰 (그물 주머니 FR-41 ⑥, 2026-11-26)

대상: `GoalFrame.ApplyNet` 안쪽 주머니(임계 감쇠 스프링 ω·최대 깊이 × 테두리 smoothstep·접선 감쇠), `BallState.NetSide`, `BallStepper` 호출 조건, `ball.net.pocket`·`stiffness`·`edgeBand`·`slide`.

| # | 심각도 | 내용 | 조치 |
|---|---|---|---|
| 1 | 높음 | 스냅샷(`MatchContext.WriteCore/ReadCore`)에 `NetSide`가 없어 그물 안 구간 복원·클립·재시뮬이 원본과 갈림 | **수정**: 스냅샷에 `NetSide` 쓰기·읽기, `MatchSnapshot.FormatVersion` 7 → 8. `GoalNetSeedSetTests.RestoreInsideNet_SameAsOriginal`(골 직후 6 s 안 스냅샷 3개 복원 → 끝 상태·이벤트·체크포인트 해시 원본과 같음) |
| 2 | 중간 | 표시가 남는 경로(피치에서 같은 스텝 Rest, 잡기·차기·보정) → 남은 표시로 바깥 공을 주머니에 잡을 수 있음 | **수정**: (a) 스텝 끝이 피치면 단계 무관 표시 지움, `Kick`·`TakeControl`·`Sanitize`에서 지움 (b) 표시가 있어도 시작이 주머니 최대 상자 안일 때만 안쪽 |
| 3 | 중간 | 스프링 안정 범위(ω < 82.8, 부호 유지 ω·dt ≤ 0.5)보다 튜닝 상한(100)이 넓음 | **수정**: `ball.net.stiffness` max 50, `BallParams`에 ω·dt ≤ 0.5 거부 |
| 4 | 낮음 | 표시만 켤 때 위치를 다시 써 1 ulp 차이 | **수정**: 표시만 바꾸고 위치·속도는 닿았을 때만 씀 |
| 5 | 낮음·참고 | 테두리 근처 허용 깊이 감소로 한 서브스텝에 위치가 크게 당겨질 수 있음 | **기록**: 결정론 무관, 012 화면 검증 때 확인 |
| 6 | 참고 | `exp(−slide·dt)` 매 서브스텝 계산 | **수정**: `BallParams.NetSlideFactor` 생성 시 1회 |

수정 후: Ball 전체·TuningProvenance·SnapshotTests·접촉 검출·클립 변환 139개 중 138 통과·1 건너뜀(Explicit). 80경기 첫 골라인 밖 사건까지 main과 동일(3,429줄), 골 455개 자유로운 공 이탈 0(주머니 포함 상자).
