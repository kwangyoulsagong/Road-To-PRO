---
name: unity-test
description: Unity EditMode/PlayMode 테스트를 배치모드로 돌리고 실패를 요약한다. 테스트 실행, 결정론 검증, 컴파일 확인이 필요할 때 사용.
---
# Unity 테스트 실행

1. 실행: `.claude/scripts/unity-test.sh [EditMode|PlayMode] [필터] [카테고리]`
   - 기본 실행은 느린 테스트 제외: `.claude/scripts/unity-test.sh EditMode "" "!Slow"`
   - 특정 클래스: 두 번째 인자에 전체 이름(`RoadToPro.Tests.EditMode.XxxTests`).
   - 대량 시뮬은 사용자가 요청할 때만: 카테고리 `Slow`.
2. 첫 실행·패키지 변경 후에는 임포트 때문에 수 분 걸린다. 타임아웃을 넉넉히(10분) 잡는다.
3. exit 3 = 에디터가 프로젝트를 열고 있음 → 사용자에게 에디터를 닫을지, 에디터 Test Runner로 돌릴지 묻는다. 에디터를 강제 종료하지 않는다.
4. 결과 파일 없음 = 대부분 컴파일 에러. 스크립트가 `error CS` 줄을 보여준다. 전체 로그: `Temp/ClaudeTests/log-<플랫폼>.txt`.
5. 실패를 보고할 때는 테스트 이름 + 메시지를 그대로 옮긴다. 고친 뒤 같은 필터로 다시 돌려 통과를 확인한다.
