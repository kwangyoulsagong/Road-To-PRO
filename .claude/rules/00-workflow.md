# 작업 흐름 (항상 적용)

- 대화·주석·커밋 본문·문서는 **한국어**. 식별자(클래스·메서드·변수)는 영어.
- 코드는 사용자가 요청한 범위만 쓴다. 요청 없이 기능을 확장하거나 "겸사겸사" 리팩터링하지 않는다.
- 수치·공식·규칙의 근거는 `docs/index.html`(기획서)과 `docs/scenarios.html`(시나리오 카탈로그). 문서와 코드가 어긋나면 임의로 정하지 말고 사용자에게 묻는다.
- `CLAUDE.md`의 "결정 대기" 항목(예: 실명 데이터)은 결정된 것처럼 구현하지 않는다.
- 새 문서는 Markdown이 아니라 HTML 아티팩트로 만들고 `docs/*.html`에 저장한다.

## Git
- `main`에 직접 커밋·푸시 금지 (훅이 막는다). 강제 푸시·`reset --hard`·`clean -f`·`branch -D`·`lfs prune`도 훅이 막는다. 훅을 고치면 `bash .claude/hooks/tests/run.sh` 통과가 필수. 작업은 `<종류>/<주제>` 브랜치에서: `setup/`, `feat/`, `fix/`, `docs/`, `art/`, `test/`.
- 커밋·푸시·PR은 사용자가 요청할 때만.
- 커밋 메시지: 첫 줄은 영어 명령형 요약(50자 내외), 본문은 한국어로 "왜".
- Unity `.meta` 파일은 항상 짝 파일과 함께 커밋한다. `.meta`만 지우거나 새로 만들지 않는다.
- 바이너리(fbx, glb, png, wav, ttf 등)는 LFS. `.gitattributes`에 없는 바이너리 확장자를 추가하면 먼저 LFS 패턴을 넣는다.

## 건드리지 않는 곳
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/` — Unity 생성물. 편집 금지 (훅이 막는다).
- `ProjectSettings/*.asset`, `Packages/manifest.json` — 바꿀 땐 이유를 먼저 말하고 확인받는다.
- `*.csproj`, `*.sln(x)` — Unity가 재생성한다. 수정해도 의미 없다.
