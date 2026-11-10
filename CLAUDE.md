# Road To PRO

감독이 아니라 **선수 한 명의 커리어를 운영하는** 축구 매니지먼트 게임 (사이드 프로젝트, 1인 개발).

- 기획서 · 아키텍처 · 리서치: `docs/index.html` (아티팩트 원본: https://claude.ai/artifact/NYBKRPQSL1ZrGBDBM4viof)
- 플레이 시나리오 카탈로그: `docs/scenarios.html` (https://claude.ai/artifact/1odFiJZ6CPwmZpzPNrc7f1)
- 세부 수치·공식은 위 두 문서에 있다. 이 파일은 요약과 작업 규칙만 담는다.

## 작업 규칙 (사용자 선호)
- 대화와 문서는 **한국어**.
- **SDD(스펙 주도 개발)**: 스펙 → 플랜 → 태스크 → 구현. 각 단계는 사용자 승인 후 다음으로. 코드는 승인된 `specs/NNN-*/tasks.md`의 태스크로만 쓴다. (`.claude/rules/05-sdd.md`)
- 문서는 Markdown 파일보다 **아티팩트(HTML 페이지)**로 만든다. 레포에는 `docs/*.html`로 저장한다.
- 커밋·푸시는 요청받을 때만. `main`에 직접 커밋하지 않고 브랜치를 만든다.
- 상용 게임(Football Manager, EA FC 등)을 디컴파일·리버스 엔지니어링하지 않는다. 공개 연구·오픈소스만 참고한다.

## 확정된 결정
| 항목 | 결정 |
|---|---|
| 엔진 | Unity 6.3 LTS (6000.3.27f2), URP. 경기 시뮬은 Unity와 분리된 순수 C#, 결정론적 |
| 경기 화면 | **하이라이트만** 3D 재생. 풀경기 시청 없음. 90분은 백그라운드 시뮬 |
| 재생 속도 | 기본 **1.5배속** (1x / 1.5x / 2x / 3x), 리플레이 0.5x 슬로모 |
| 오디오 | **해설 없음**. 관중·휘슬·킥·그물 등 현장 사운드만 |
| 그래픽 | FM급. AAA 아님. **저사양(내장 GPU)에서도 60fps**, 배터리 고려 (메뉴는 3D 언로드) |
| 플랫폼 | **PC 전용** (2026-11-10). 모바일·콘솔 없음 — 디바이스팜도 PC 프로필(노트북·데스크톱·휴대 PC)만 |
| UI | 깔끔한 모바일 금융 앱 스타일: 회색 바탕 + 흰 카드, 큰 숫자, 블루 포인트 1개, 바텀시트 선택지 |
| 경기 엔진 | **시나리오(플레이 패턴) 기반**. 기본 패턴 약 268개 × 변주 조합 → 약 4만 개 자동 생성. 공 주변 3~5명만 시나리오가 조종, 나머지는 팀 형태 레이어가 라인·간격 유지. 시나리오가 깨지면 Utility 규칙(ML 아님)으로 폴백 |
| 선수 개성 | 역할 27개(메짤라, 트레콸르티스타, 박스투박스 등) + 플레이 성향 14축 + 버릇 15개(드리블 중독, 크로스 머신 등) |
| 라이프 | 쇼핑·집·차, 연애·결혼·가족, 유흥·사교, 자기관리. 모두 경기력·사기·평판·돈에 연결. 성적 묘사 없음, 목표 15세 등급 |
| 건강 | 부상 + 질병(감기→부상 확률 상승, 드물게 질병 은퇴) |
| 이벤트 | 스토리렛 방식, 12개 카테고리 300개 이상 |
| 일정 | 첫 시즌은 **실제 시즌 일정**, 2시즌부터 자동 생성 |
| 3D 아트 | 경기장·소품은 Blender Python(bpy) 스크립트로 생성 → glb/fbx. 모션도 bpy 자체 제작(2026-11-04, Mixamo 대체, Unity Humanoid 뼈대). 텍스처는 CC0(Poly Haven, ambientCG, Kenney). 출처는 `Art/LICENSES.csv`에 기록 |
| 실명 데이터 | **A안** (2026-11-02): 클럽·경기장은 변형 이름, 선수·감독은 가상, 실명 로스터는 커뮤니티가 에디터·가져오기로. "살짝 바꾼" 선수 이름 금지 |
| 우선순위 | **3D 먼저** (2026-11-02): ① 3D 선수 움직임 ② 매치 엔진 ③ 시나리오 생산 파이프라인(계속). 단계 P1~P6은 `specs/README.md` |
| 시뮬 주기 | 결정 10Hz · 물리 100Hz · 리플레이 기록 30Hz(화면 보간). 슬로모 0.5x만 |
| 능력치 | 내부 0~100, 표시 1~20 |
| 시나리오 | 기본 패턴 268개(카탈로그) 기준. 대량 시뮬 평균 득점 2.5~3.0 |
| 저장 | 경기 전·후만. 경기 중 종료 시 경기 시작 직전으로 복구 |
| 선수 폴리곤 | LOD0 6~10k / LOD1 2~3k tris. 예외: 클로즈업(세리머니·컷신) 머리 LOD ≤ 6k tris, 동시 최대 2명 (2026-11-04, specs/056) |
| 연령 하한 | 연애·유흥·도박 이벤트 18세, 선수 최소 15세 |

## 결정 대기
- `specs/README.md` "스펙 간 결정 필요" 표의 남은 항목과 각 스펙의 `[제안]` 수치.

## 레포 구조
```
Assets/_Project/
  Core/          도메인 모델 (UnityEngine 비참조)
  MatchEngine/   경기 시뮬 코어 (UnityEngine 비참조, 결정론)
  Career/        커리어 루프, 이적, 계약, 스토리 디렉터
  WorldGen/      월드·일정 생성
  Data/          SQLite 세이브, JSON 로스터·모드
  Presentation/  하이라이트 3D 재생, 카메라, 컷신, 사운드
  UI/            UI Toolkit
  Verification/  디바이스팜 드라이버 (E2E 단계 실행·캡처·성능 측정, specs/000)
  Tests/         EditMode, Performance, 헤드리스 대량 시뮬
Art/Blender/scripts/   bpy 생성 스크립트
Data/scenarios/        시나리오 정의 (YAML/JSON)
Data/schedules/        실제 시즌 일정
Data/e2e/              디바이스팜 프로필·E2E 시나리오·기준 이미지(LFS)·성능 기준
Tools/devicefarm/      디바이스팜 오케스트레이터 (Python)
docs/                  기획 아티팩트 HTML
specs/NNN-<slug>/      SDD 스펙·플랜·태스크 (Markdown, _templates/ 참고)
.claude/               하네스: rules/, hooks/, skills/, agents/, scripts/, settings.json
```

## 하네스 (`.claude/`)
- **rules/**: 상세 규칙. 00 작업흐름·git, 01 법·라이선스, 05 SDD는 항상, 나머지는 경로별(C#, 코어 결정론, 프레젠테이션·UI, 데이터, 아트, 테스트, 문서).
- **hooks/**: Unity 생성물·`.meta`·csproj 편집 차단 / main 커밋·푸시, 강제 푸시, `reset --hard` 차단 / 코어 모듈의 Unity 참조·비결정 API 검사 / 새 바이너리 에셋 시 `LICENSES.csv` 상기.
- **skills/**: `/spec` `/plan` `/tasks` `/implement` `/verify` (SDD 5단계), `/pipeline`(승인된 태스크 끝까지 연속), `/unity-test`, `/blender-gen`, `/new-scenario`.
- **scripts/**: `unity-test.sh`(배치 테스트), `blender-gen.sh`, `spec-lint.py`(스펙 형식·태그·실명 검사). 훅 회귀 테스트: `bash .claude/hooks/tests/run.sh`.
- **완료 = 증거**: 로직 테스트 리포트 스크린샷 + 디바이스팜(기기 프로필 매트릭스) E2E 스크린샷·기준 이미지 비교 + 프로필별 성능 측정. `specs/000-verification-harness` 참고. 리포트는 아티팩트로 발행.
- **agents/**: `determinism-reviewer`(코어 결정론 리뷰), `spec-checker`(기획서·스펙 대조).
- 개인 설정은 `.claude/settings.local.json` (gitignore).

## 환경
- macOS (Apple Silicon). Unity Hub + Unity 6000.3.27f2 (Windows Build Support Mono 포함).
- Git LFS 설치됨. 3D·텍스처·오디오·폰트는 LFS로 관리 (`.gitattributes`).
- Blender: `/Applications/Blender.app/Contents/MacOS/Blender --background --python <script>`
