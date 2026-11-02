---
paths:
  - "Data/**"
  - "Assets/_Project/Data/**"
---
# 데이터 · 세이브

- 시나리오 정의: `Data/scenarios/` (YAML 또는 JSON). 패턴 ID는 `docs/scenarios.html` 카탈로그 ID와 일치시킨다. 변주는 파일을 복제하지 말고 변주 축으로 표현한다.
- 실제 시즌 일정: `Data/schedules/`. 첫 시즌 전용. 2시즌부터는 WorldGen이 생성.
- 로스터·이름 데이터에 실존 선수·감독 이름 금지 (`01-legal.md`).
- 세이브: SQLite. 스키마 변경 시 버전 번호를 올리고 마이그레이션을 같이 쓴다. 이전 세이브를 깨뜨리지 않는다.
- 모드 데이터는 같은 스키마를 쓴다. 스키마가 바뀌면 문서(`docs/index.html`)도 같이 고친다.
- JSON/YAML은 LF, 2칸 들여쓰기, 키는 `camelCase`.
