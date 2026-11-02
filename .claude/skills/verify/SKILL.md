---
name: verify
description: 스펙 NNN의 완료 증거를 만든다 — 로직 테스트 리포트 스크린샷, 디바이스팜 프로필 매트릭스 E2E 스크린샷과 기준 이미지 비교, 성능 측정. 구현 후 "검증", "증거", "/verify NNN".
---
# /verify — 디바이스팜 증거 리포트

도구: `python3 Tools/devicefarm/farm.py` (설계 `specs/000-verification-harness/`). 종료 코드 0 통과 · 1 검증 실패 · 2 사용법 · 3 실행기 오류(도구 없음·에디터 열림·빌드 실패 — 검증 결과가 아님).

1. `specs/NNN-*/spec.md`의 AC 목록·검증 종류·성능 예산·E2E 시나리오 id·대상 프로필을 읽는다. Unity 에디터가 이 프로젝트를 열고 있으면 닫아 달라고 한다(배치모드 불가, 강제 종료 금지).
2. 로직: `farm.py logic --spec NNN [--filter <테스트 이름;…>]` → `evidence/report.html` + 로직 리포트 PNG.
3. E2E·성능: `farm.py smoke --spec NNN` (스펙이 요구하면 `full`). 빌드는 캐시가 있으면 건너뛴다. 시나리오는 `Data/e2e/<id>.json`, 시나리오의 `acs`가 AC 연결표에 들어간다. 성능 예산은 시나리오 `budgets`.
4. 기준 이미지가 없으면 결과가 "기준 후보"다. 캡처를 Read로 직접 열어 확인하고, **사용자 승인 후에만** `farm.py approve-baseline --scenario <id>` → 다시 실행. 의도한 화면 변경이면 `--include-failed`도 사용자 승인 후. 성능 기준 갱신(`approve-perf`)도 승인 후에만.
5. 스펙 000 자체는 `farm.py self-test all`이 AC-01~16 증거를 만든다.
6. 리포트 발행: `specs/NNN-*/evidence/report.html`을 Artifact로 발행한다. `files`에 `evidence/assets/` 아래 파일을 같은 상대 경로로 넣는다. 처음 발행하면 링크를 `evidence/artifact-url.txt`에 저장하고, 다음부터는 그 URL로 같은 아티팩트를 갱신한다. 링크를 사용자에게 알린다.
7. 하나라도 실패하면 `완료`로 바꾸지 않는다. 실패 항목·스크린샷·수치를 그대로 보고한다. 리포트 상단에 "작업 트리 변경 있음"이 뜨면 커밋한 뒤 다시 돌려야 완료 증거가 된다.
8. 생성된 스크린샷은 Read로 직접 열어 눈으로 확인한다. 깨진 레이아웃·빈 화면·잘린 텍스트가 보이면 차이 임계값과 무관하게 실패.
