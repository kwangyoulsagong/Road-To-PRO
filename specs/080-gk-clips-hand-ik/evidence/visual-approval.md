# 080 AC-05 육안 승인

- 날짜: 2026-11-20
- 시나리오: `Data/e2e/080-gk-contact.json` (mac-m1-1600p-light-ko, 011 GK 경기와 같은 고정 시드 20261109, `livePlay080` = `PlayFresh`)
- 장면: ① gkdive — 다이빙 선방 접촉 순간, GK가 옆으로 누워 몸을 던지고 공이 두 손 바로 앞(080 전 기준선 손–공 134~162 cm) ② gkpunch — 이 경기에 펀칭이 없어 대체 장면(가슴 높이 잡기), 공이 손 앞 ③ gkconceded — 근거리 슈팅 실점, 공이 골라인을 넘기 직전 프레임에서 GK 손이 공에 닿지 않음
- 첫 촬영에서 플레이어 예외(`PlayerAnimator.Prepare` IndexOutOfRange) → E2E 씬이 121개 클립 배열로 남아 있었음, 씬 재생성(T16a) 뒤 재촬영
- 승인 문구: 사용자 "승인" (AskUserQuestion 응답, 승인한 캡처 = `.farm/runs/20261120-120324-shot`; 증거 리포트의 캡처는 같은 커밋·같은 시드 재촬영)

# AC-07 성능 (로직 리포트 밖 측정)
- 손 IK 1회(GK 1명 양손): 1.2 µs ≤ 50 µs (`HandReachIkPerf.Solve_Under50us`, 에디터, T14)
- 클립 추가 크기: `player.glb` + `player.clips.json` ≈ +1.2 MB ≤ 2 MB
- 에디터 클립 메모리: 41.43 MB = 043 기준 +75.9 % ≤ +80 % (`PlayerImportTests.ClipMemory_Within180Percent`, 로직 리포트 안)
- 하이라이트 프레임 시간: shot 측정값 기록(판정은 실기기 확보 후, 05-sdd 완료 정의 4)
- IL2CPP 벤치: 시뮬 틱 경로 변경 없음 → 실행 안 함

# 아트
- `farm.py art --spec 080`: Blender 생성 테스트 29/29 (생성 시간 68 s — 팔 맞춤 캐시 `keeper_fits.json`), `evidence/art.html`
