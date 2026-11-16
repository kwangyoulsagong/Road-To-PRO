# 081 육안 승인

- 날짜: 2026-11-16
- 승인자: 사용자 ("네" — T20 질문 3개 전부)
- 대상 (AC-07, mac-m1-1600p, 플레이어 빌드, 대체 장면 없음):
  - `081-match-feel-scenes` (시드 20261382): `assets/approved/takeon_ok.png`, `assets/approved/takeon_lost.png`, `assets/approved/tackle_stand.png`, `assets/approved/tackle_slide.png`, `assets/approved/defensive.png`
  - `081-match-feel-header` (시드 20261195): `assets/approved/header.png` — 헤더 접촉 순간 머리에 공. 머리–공 거리 로그(1.66 m)는 화면과 맞지 않아 측정 코드 신뢰 불가 → 육안 판정
  - `081-match-feel-cutback` (시드 20261267): `assets/approved/cutback_goal.png`
- 함께 승인:
  1. FR-31 개정 — 결투 줌을 장면 안 모든 테이크온·태클 순간으로, 줌 배율 0.45 [대체]
  2. 교체 투입 순간 속도 기록 초기화(058 "배치" 경고 2건 → 0) — `HighlightData.Swap`
- 콘솔: 세 시나리오 + `058-live-variety`·`071-cross-shot` 재촬영 경고 0·에러 0

## 2026-11-16 재촬영 (커밋 7b577c7 뒤 — 사용자 확인 대기)
- 승인한 캡처는 커밋 78abc53 이전 빌드의 경기다. 7b577c7(터치라인 밖 공 최소 안쪽 속도) 뒤 플레이어 경기가 바뀌어 시드를 다시 고정했다:
  `081-match-feel-scenes` 시드 20261247(돌파 성공·실패·스탠딩·헤딩슛·수비), `081-match-feel-slide` 시드 20261220 → `assets/final/`
- 직접 확인: 헤딩슛 장면은 공이 선수 머리 약 1 m 위(점프 동작 없음), 늦은 슬라이딩 장면은 슬라이딩 동작이 보이지 않음 — 043·054 동작 연결 몫으로 보임
- 컷백 슛 골은 7b577c7 뒤 256시드에 없음 → 승인 캡처(`assets/approved/cutback_goal.png`, 시드 20261267)만 남김

## 2026-11-16 최종 결정
- 사용자 결정 "A": 재촬영본(`assets/final/`, 시드 20261247·20261220)을 지금 상태로 승인. 헤더 점프 동작·늦은 슬라이딩 동작 연결은 043·054, 컷백 슛 골 빈도는 071 후속(specs/README D21·D22). 081 완료.
