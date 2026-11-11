# 매치 엔진 의사결정 아키텍처 선택: 시나리오 라이브러리 vs 상황 판단(Utility) vs 런타임 LLM

작성일 2026-11-11. 공개 자료만 사용. 상용 게임 코드·데이터 분석 없음. "확인됨"은 1차 출처(개발사 공식 글, 논문), "추정"은 출처를 근거로 한 추론이다.

## 1. Football Manager·EA FC·eFootball은 어떻게 결정하나

### Takeaway
FM은 공식적으로 **선수마다 상황을 보고 결정하는(situational, per-player) 엔진**이라고 설명한다. 0.25초마다 각 선수가 주변을 평가하고 능력치를 바탕으로 최선의 행동을 고른다. 미리 짜 둔 플레이 패턴을 고른다는 공식 언급은 찾지 못했다. EA FC는 ML을 주로 **애니메이션**(HyperMotion)과 일부 에이전트(FC26 골키퍼 RL)에 쓰고, 전술 AI는 데이터로 만든 "역할(Role)" 체계(FC IQ)로 움직인다.

### Cited Findings
**Football Manager (확인됨, 공식 블로그)**
- FM21: "선수와 심판은 0.25초마다 각자 결정하며, 이를 'slice'라 부른다." 각 선수는 "주변 상황을 계속 평가하고, 자기 능력치를 바탕으로 최선이라 생각하는 행동을 한다". FM21부터는 slice 도중에도 상황이 바뀌면 결정을 고칠 수 있게 해서 선수가 더 빠르게 반응한다. — [Match Engine AI in FM21](https://www.footballmanager.com/news/match-engine-ai-fm21)
- FM21: 슈팅 선택과 공간 인지가 능력치에 묶여 있다. 기술이 좋은 선수는 슈팅 선택지가 더 많다. 위험 감수 정도는 전술과 개인 성향에 따라 다르다(좁은 각도에서 슛 vs 패스). — 같은 출처
- FM21 팀 단위 동작: 수비 라인이 "하나의 단위로 움직인다". 마크 대상 넘겨주기, 패스 길목 차단("공의 움직임을 따라가기보다 패스 길목을 막으려 한다"), 공격형 미드필더·스트라이커가 볼 운반자를 지원하는 위치 조정, 명백한 오프사이드 위치를 피함, 날씨에 반응하는 골키퍼. 미리 짠 시퀀스 언급은 없다. — 같은 출처
- FM23(2022): 선수 행동을 "능력치를 바탕으로 선수가 내리는 결정"으로 설명. K-블록 세이브, 첫 터치 바운드, 강한 발로 꺾기, 역할(윙어·메짤라·전진 플레이메이커) 위치 수정, 능력치에 따라 수비 복귀가 들쭉날쭉. 애니메이션은 IK(역운동학)로 발 미끄러짐을 줄이고 "새로운 선택 기준 기술"로 핵심 동작을 고른다. 애니메이션 데이터는 크기가 약 1/30로 줄었다. — [Match AI and Animation (FM23)](https://www.footballmanager.com/features/match-ai-and-animation)
- FM23: AI 감독 로직을 개편해 경기 상황에 따라 추격·포메이션·역할을 고른다. 즉 **팀 수준 결정(감독 AI)**과 **선수 수준 결정(slice)**이 계층으로 나뉜다. — 같은 출처
- FM26(첫 Unity 버전, 2025): Hawk-Eye Innovations의 실제 경기 볼류메트릭 데이터로 볼 터치 애니메이션 수백 개를 추가했다. 단 이 데이터는 그냥 끼워 넣은 게 아니라 SI의 블렌딩 기술이 "경기 상황 맥락에 따라 애니메이션을 섞어야" 쓸 수 있었다. 드리블 AI는 수비수를 세워 두고 유인한 뒤 돌파하도록 바뀌었고, 패스 위험 평가가 개편됐다. Dynamic Highlight Mode는 경기의 중요도·극적 정도에 따라 하이라이트 수를 바꾼다. 매치 AI 상세 딥다이브는 예고만 됐다. — [FM26 Match Day Experience](https://www.footballmanager.com/fm26/features/where-storytelling-evolves-fm26s-match-day-experience)
- FM26은 자체 엔진을 버리고 Unity로 옮긴 첫 버전이다. — [FRVR 비교 기사](https://frvr.com/blog/football-manager-26-vs-fm-24-all-new-features-and-upgrades-explained) (2차 출처)
- FM27(2026-10): Hawk-Eye 데이터로 이동·속도 시스템을 "전면 개편". 실제 경기 수천 시간을 분석해 현대 공격 트렌드와 공격 시 선수 의사결정을 반영. 영리한 선수는 동료·상대의 움직임을 더 잘 읽는다. Anticipation·Off The Ball이 침투 질에, Flair·Dribbling이 개인기에, Marking·Anticipation이 수비에, Stamina가 스프린트·회복에 영향. 박스 침투 방식(늦게 도착, 라인 공략, 니어·파·중앙) 개인 지시 추가. 샤우트는 능력치 소폭 보정이 아니라 선수의 멘탈 상태에 영향을 준다. — [FM27 Tactical Tweaks and Match Engine Upgrades](https://www.footballmanager.com/nl/node/1108)
- 학술 논문도 "공식 매뉴얼이 일부 설명을 주지만 핵심 세부는 공개되지 않았다"고 지적한다. — [arXiv 2504.09499](https://arxiv.org/pdf/2504.09499)

**EA SPORTS FC (확인됨, 공식 Pitch Notes·발표)**
- FC 25 FC IQ = Player Roles + Team Tactics + Smart Tactics. 역할 31개 × 포커스 1~3개 = 출시 시 52개 조합. 역할이 공 없을 때 선수가 어떻게 움직이고 생각하는지를 결정한다. 역할 숙련도(Role / Role+ / Role++)는 대부분 Opta 실데이터를 쓰는 자체 AI 모델의 도움으로 배정. Work Rate·개인 지시는 없앴다. — [FC 25 FC IQ Deep Dive](https://www.ea.com/es/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-fc-iq-deep-dive), [FC 25 Gameplay Deep Dive](https://www.ea.com/ko/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-gameplay-deep-dive)
- HyperMotion V(FC 24): 프로 경기 180경기 이상의 볼류메트릭 영상 → ML 학습 → 실시간 애니메이션 생성. FIFA 22 HyperMotion은 11대11 Xsens 모션캡처 + ML로 보폭·주기를 실시간 생성. 즉 EA의 ML은 **결정이 아니라 동작 생성** 쪽이다. — [Trusted Reviews](https://www.trustedreviews.com/explainer/what-is-hypermotion-v-4352934), [80.lv](https://80.lv/articles/ea-uses-machine-learning-for-animations-in-fifa-22)
- FC 26: GDC 강연 "Reinforcement Learning in FC26: Shipping Human-Like Goalkeepers with a Designer-First Approach". 밤새 학습시키고, 기존 CPU 골키퍼보다 선방률이 높으며, 300개 이상의 "유닛 테스트" 시나리오로 검증한다. — [GDC 세션](https://schedule.gdconf.com/session/reinforcement-learning-in-fc26-shipping-human-like-goalkeepers-with-a-designer-first-approach/914241)

### Inferences
- (추정) FM 계열은 "**팀 전술/감독 AI(의도) → 선수별 상황 평가(Utility에 가까운 점수 선택) → 맥락 기반 애니메이션 선택**"의 3층 구조로 보인다. 공식 글의 "능력치 기반으로 최선의 행동 선택", "선택 기준 기술(selection criteria)"이라는 표현은 점수 기반 선택(Utility)과 맞지만, 정확한 알고리즘은 공개되지 않았다.
- (추정) FM이 "패턴처럼 보이는" 플레이(오버로드 후 반대 전환, 박스 침투 타이밍)를 내는 것은 저작한 시나리오가 아니라 **전술 지시·역할이 선수별 효용 점수를 바꿔서** 생긴 결과로 보인다. FM27이 "실제 경기 수천 시간 분석"을 했다는 것은 데이터를 **튜닝·검증**에 썼다는 뜻으로 읽히며, 런타임 패턴 DB를 썼다는 근거는 없다.
- 상업 축구 게임 어디에서도 런타임 LLM으로 경기 중 결정을 내린다는 공개 자료는 찾지 못했다.

### Gaps
- FM 2D/3D 엔진의 내부 알고리즘(Utility인지, 규칙 트리인지)은 비공개. FM26 매치 AI 딥다이브 본문은 찾지 못했다.
- eFootball(Konami) 의사결정 구조에 대한 신뢰할 만한 공개 자료를 찾지 못했다(이번 조사 범위에서 검색 예산 부족).
- 공식적인 SI 엔지니어 인터뷰·GDC 강연은 찾지 못했다.

## 2. 업계·학계 방법: Utility, BT, HTN/GOAP, 플레이북, RoboCup, RL

### Takeaway
실전 팀 스포츠 AI의 주류는 "**포메이션 + 동적 역할 배정 + 상황 트리거 세트플레이 + 개인 반응 규칙**"이다. 오픈플레이 전체를 저작 패턴으로 돌리는 사례는 찾지 못했고, 저작 패턴(세트플레이)은 데드볼·특정 트리거 상황에 쓰인다. RL은 연구에서 강하지만 상용 축구 게임에서는 골키퍼 같은 좁은 범위에만 출시됐다.

### Cited Findings
- RoboCup(CMU): 과제를 역할로 분해하고, 에이전트가 포메이션 안에서 역할을 바꾸며, "런타임에 평가되는 미리 정의된 트리거"에 따라 포메이션을 바꾼다. 세트플레이는 심판 메시지로 시작되고, 포메이션 역할을 세트플레이 역할로 매핑할 때 홈 포지션에서의 이동을 최소화한다. 세트플레이가 성능을 "크게" 올렸다. — [CMU, Task Decomposition and Dynamic Role Assignment](https://www.ri.cmu.edu/publications/task-decomposition-and-dynamic-role-assignment-for-real-time-strategic-teamwork), [Stone & Veloso 1999](https://cs.utexas.edu/~pstone/Papers/99aij/node19.html)
- UT Austin Villa(2011 3D 우승): 고정된 선수-역할 매핑은 경기의 동적 성격 때문에 성능이 나빴다. 킥오프·골킥·코너킥·스로인에서 포메이션·행동을 바꾸는 세트플레이를 쓴다. — [MacAlpine et al.](https://www.eecs.ucf.edu/~gitars/cap6671/Papers/macalpine.pdf)
- magmaOffenburg(2019): 세트플레이는 상대가 대응 전략을 만들 수 있어서, 대회 중에도 쉽게 바꿀 수 있게 유연하게 유지한다. — [magmaOffenburg TDP2019](https://magma.hs-offenburg.de/fileadmin/Sonstige_Unterseiten/magma/files/downloads/magmaOffenburg_TDP2019.pdf)
- RoboCup 3D 단순 결정 방법: 포메이션마다 전략 영역·홈 포지션이 있고, 공 위치가 주 요인이며, 퍼지 규칙으로 전략을 고른다. — [arXiv 1212.1570](https://arxiv.org/pdf/1212.1570)
- Google Research Football(Kurach et al., AAAI 2020): 벤치마크 상대는 "직접 설계한 고정 규칙 기반 봇"이고, 난이도 0~1 파라미터로 반응·결정 속도를 조절한다(쉬움 0.05, 보통 0.6, 어려움 0.95). 조작하지 않는 동료도 기본적으로 규칙 봇이 조종한다. 결정론/확률 모드를 둘 다 지원. 처리량은 블로그 기준 하루 약 2,500만 스텝, arXiv 기준 하루 약 1억 4천만 스텝(6코어 1대)으로 출처마다 다르다. — [Google Research 블로그](https://research.google/blog/introducing-google-research-football-a-novel-reinforcement-learning-environment/), [arXiv 1907.11180](https://arxiv.org/abs/1907.11180)
- DeepMind, Liu et al.(Science Robotics 2022, arXiv 2021): 모션캡처 모방 → 드릴 → 2대2 멀티에이전트 RL로 관절 제어부터 팀 플레이까지 학습. 저자들은 인간 수준 운동 지능을 향한 "작은 한 걸음"이라고 평했다. 범위는 2대2. — [arXiv 2105.12196](https://arxiv.org/abs/2105.12196)
- TacticAI(DeepMind + 리버풀 FC, Nature Communications 2024-03): 선수를 그래프 노드로 두는 GNN, 경기장 대칭성 활용. 프리미어리그 코너킥 7,176개로 학습. 코너킥 첫 수신자·결과 예측과 배치 조정 제안. 전문가 보조 도구이며 실시간 게임 AI가 아니다. — [DeepMind 블로그](https://deepmind.google/blog/tacticai-ai-assistant-for-football-tactics/), [The Register](https://www.theregister.com/2024/03/20/googles_deepmind_football/)
- Utility AI: GDC 2024 AI Summit "Practical Utility AI"는 고려 요소(consideration), 효용 곡선, 결정 계층, 튜닝에서 데이터의 중요성을 다룬다. GDC 2026 AI 라운드테이블도 BT·Utility를 여전히 기본 기법으로 다룬다. — [GDC 2024 Practical Utility AI](https://schedule.gdconf.com/session/ai-summit-practical-utility-ai/902394), [GDC 2026 Roundtable](https://schedule.gdconf.com/session/game-ai-roundtable-day-1/917354)

### Inferences
- RoboCup의 "포메이션(팀 형태) + 동적 역할 + 트리거 세트플레이" 구조는 Road To PRO의 "팀 형태 레이어 + 시나리오 + Utility 폴백"과 거의 같다. 다만 RoboCup에서 저작 플레이는 **데드볼/명확한 트리거**에 집중되고, 오픈플레이는 역할·위치 함수·개인 규칙이 담당한다.
- "고정 매핑은 성능이 나쁘다"(UT Austin Villa)는 교훈은 시나리오 쪽에도 적용된다. 시나리오의 역할 슬롯에 **누가 들어갈지를 런타임에 배정**해야 하고, 특정 선수 번호에 묶으면 깨진다.

### Gaps
- Madden·NBA 2K 플레이북 AI(공격 플레이 선택, 수비 커버리지 자동 조정)에 대한 GDC 강연은 이번 검색에서 찾지 못했다. 미식축구·농구 플레이북은 "멈췄다 다시 시작"하는 구조라 축구 오픈플레이와는 다르다는 점은 일반 상식 수준의 추론이다.
- Game AI Pro의 스포츠 AI 장(章)과 HTN/GOAP 스포츠 적용 사례는 이번 범위에서 1차 출처를 확인하지 못했다.

## 3. 런타임 LLM: 지연·비용·결정론·환각, 출시 사례

### Takeaway
런타임 LLM은 10Hz × 22명 결정에 쓸 수 없다. 지연이 수백 ms~수 초라 한 틱(100ms)보다 길고, 비결정적이라 리플레이·재시뮬·대량 시뮬과 충돌한다. 출시 사례는 대화·동반자·생활 시뮬(inZOI, PUBG Ally)에 한정되고, 실시간 스포츠 결정에 쓴 사례는 찾지 못했다. **오프라인 저작 도구로만** 의미가 있다.

### Cited Findings
- "Large Language Models and Games: A Survey and Roadmap": 계산 비용과 지연 때문에 소비자용 실시간 LLM 통합은 어렵고, 유한한 컨텍스트 창이 장기 상태 추적을 제한한다. — [alphaXiv 2402.18659](https://alphaxiv.org/overview/2402.18659v5)
- VR LLM 리뷰: 로컬 배포에서도 NPC 응답이 평균 3.2초인 연구, 20초 넘는 지연 사례를 인용. 실시간·고부하 환경에서 LLM 동작은 잘 이해되지 않았다. — [arXiv 2508.00737](https://arxiv.org/pdf/2508.00737)
- 실무 자료(비동료심사): 클라우드 API 첫 토큰까지 보통 200~800ms. 결정론 비교: 전통 게임 AI는 "완전 결정론, QA·디버깅용으로 재현 가능", LLM NPC는 "본질적으로 비결정적, 통계 테스트·가드레일 검증 필요". 멀티플레이 동기화는 비결정 생성 때문에 깨지며 캐싱·사전 생성으로 우회. "LLM은 실시간 전투 로직에는 너무 느려서 서사·대사에 쓴다." — [Metavert 비교](https://metavert.io/compare/game-ai-vs-agent-npcs), [abratabia 지연·비용](https://abratabia.com/llm-npcs/latency-and-cost.php) (실무 블로그, 수치는 참고용)
- 출시 사례: NVIDIA ACE 자율 캐릭터는 inZOI(2025-03-28 얼리 액세스, RTX GPU 전용 **옵트인 실험 기능** "Smart Zoi")와 NARAKA: BLADEPOINT 모바일 PC판에 들어갔다. PUBG Ally는 Mistral-Nemo-Minitron-8B 계열 소형 모델을 쓰는 동반자로 발표됐다(모델 크기는 출처마다 다름). — [NVIDIA 발표](https://www.nvidia.com/en-eu/geforce/news/nvidia-ace-naraka-bladepoint-inzoi-launch-this-month/), [GamesBeat](https://gamesbeat.com/krafton-and-nvidia-team-up-to-create-smarter-ai-characters-for-pubg-and-inzoi/)

### Inferences
- 수치 비교(추정): 10Hz × 22명 = 초당 220회 결정. 대량 시뮬 1경기 90분 = 54,000틱 × 22명 ≈ 119만 결정. 첫 토큰 200ms만 잡아도 한 경기에 수 일, 수천 경기면 불가능. RTX 전용 옵트인이라는 inZOI 사례는 "저사양 내장 GPU 60fps" 목표와도 맞지 않는다.
- 시드 고정·temperature 0으로도 GPU 커널·배치·모델 버전에 따라 출력이 바뀔 수 있어 "같은 시드 = 같은 경기"를 보장하기 어렵다(일반적 지식, 이번 검색에서 1차 출처 미확인).
- 오프라인 활용은 유효: 시나리오 초안·변주 이름·설명 생성, 실경기 서술에서 패턴 후보 추출, 테스트 시나리오 작성. 결과는 사람이 검토하고 데이터(YAML/JSON)로 고정되므로 결정론을 해치지 않는다.

### Gaps
- 실시간 스포츠 결정에 LLM을 쓴 출시 게임은 찾지 못했다(없다는 증명은 아님).
- 로컬 소형 모델의 내장 GPU 지연에 대한 신뢰할 벤치마크는 찾지 못했다.

## 4. 세 방식 비교와 실제 쓰이는 하이브리드

### Takeaway
A(저작 시나리오 4만 개) 단독은 "축구처럼 보이는" 연출과 튜닝에 강하지만 반복·깨짐·저작 비용 위험이 크다. B(상황 판단 Utility) 단독은 FM 방식에 가깝고 개성·견고성이 좋지만 "작전처럼 보이는" 연계가 약하고 튜닝 공간이 넓다. C(런타임 LLM)는 성능·결정론에서 탈락. 실전 사례(FM의 전술→선수 판단, RoboCup의 포메이션+역할+세트플레이)는 모두 **"의도(팀·패턴) + 상황 판단(개인)" 하이브리드**다.

### Cited Findings
- FM: 팀 지시·역할(의도)이 선수별 판단을 바꾸고, 선수는 0.25초마다 능력치 기반으로 결정. — [FM21](https://www.footballmanager.com/news/match-engine-ai-fm21), [FM27](https://www.footballmanager.com/nl/node/1108)
- RoboCup: 포메이션·역할(팀 형태) + 트리거 세트플레이(저작 패턴) + 동적 역할 배정. — [CMU](https://www.ri.cmu.edu/publications/task-decomposition-and-dynamic-role-assignment-for-real-time-strategic-teamwork)
- EA FC: 역할(데이터 기반 의도) + ML 애니메이션 + RL 골키퍼(좁은 범위 학습 에이전트, 300개 이상 시나리오 테스트). — [FC IQ](https://www.ea.com/es/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-fc-iq-deep-dive), [GDC FC26 GK](https://schedule.gdconf.com/session/reinforcement-learning-in-fc26-shipping-human-like-goalkeepers-with-a-designer-first-approach/914241)
- 데이터에서 패턴 추출: TacticAI는 실제 코너킥 7,176개에서 학습 → 데이터 기반 세트피스 패턴 생성·평가 사례. FM27은 실제 경기 수천 시간 분석을 공격 의사결정에 반영. — [DeepMind](https://deepmind.google/blog/tacticai-ai-assistant-for-football-tactics/), [FM27](https://www.footballmanager.com/nl/node/1108)

### 비교표 (평가는 위 근거를 바탕으로 한 추론. ◎ 강함 / ○ 보통 / △ 약함 / ✕ 부적합)

| 항목 | A. 저작 시나리오 4만 개 선택 | B. 순수 상황 판단(Utility) | C. 런타임 LLM | A+B 하이브리드(의도=시나리오, 실행=Utility) |
|---|---|---|---|---|
| 축구처럼 보임 | ◎ 연계·삼각 패스·오버래핑이 "작전처럼" 보임. 단 맥락과 안 맞으면 어색 | ○ 개별 판단은 자연스럽지만 팀 연계가 우연에 맡겨짐 | △ 그럴듯한 말은 하지만 공간·물리 근거가 약함, 환각 | ◎ |
| 다양성·반복 | △~○ 4만 개라도 "같은 장면" 인지 위험. 하이라이트만 보므로 반복이 더 잘 보임 | ○ 상황이 매번 달라 자연 다양성, 대신 "평탄한" 경기 | ○ 다양하지만 통제 불가 | ◎ 패턴 골격 + 상황별 실행 차이 |
| 선수 개성(능력치·역할·버릇) | ○ 슬롯 배정·성공률에만 반영되기 쉬움 | ◎ 효용 가중치에 역할 27개·성향 14축·버릇 15개를 직접 넣을 수 있음 | △ 프롬프트로만, 일관성 낮음 | ◎ 패턴 선택 가중치 + 실행 효용 둘 다 |
| 결정론·리플레이·재시뮬 | ◎ | ◎ | ✕ | ◎ |
| 성능(10Hz·22명·헤드리스 수천 경기) | ◎ 매칭 비용만 신경 쓰면 됨(4만 개 인덱싱·메모리 주의) | ○ 22명 × 후보 행동 평가. 후보 수 제한·LOD로 해결 가능 | ✕ | ○~◎ 공 주변만 정밀 평가 |
| 저작 비용 | △ 268 기본형 × 변주 생성은 자동이지만 검증·QA 부담 큼 | ○ 효용 함수·곡선 수십 개 | ○ 프롬프트는 싸지만 가드레일·검증이 비쌈 | ○ 기본형 수 축소 가능 |
| 튜닝(평균 2.5~3.0골) | ○ 패턴별 성공률·선택 빈도로 직관적 튜닝 | △ 상호작용이 많아 결과가 창발적, 대량 시뮬 필수 | ✕ | ○ 두 손잡이(패턴 빈도·실행 성공률) |
| 모드 지원 | ◎ YAML/JSON 패턴 추가가 쉬움 | ○ 가중치 파일 수준 | △ | ◎ |
| 깨짐 대응 | △ 상대 개입·공 튐에서 쉽게 깨짐 → 폴백 품질이 전체 품질 결정 | ◎ 매 틱 재평가라 깨질 게 없음 | △ | ◎ 깨지면 Utility가 자연스럽게 이어받음 |

### Inferences
- A의 가장 큰 위험(추정): 시나리오가 깨지는 비율이 높으면 실제 경기 대부분을 **폴백 Utility가 결정**하게 된다. 그러면 Utility 품질이 사실상 엔진 품질이고, 4만 개 저작 투자는 일부 장면에만 돌아온다. 깨짐률을 대량 시뮬로 먼저 측정해야 한다.
- 4만 개라는 숫자 자체는 다양성을 보장하지 않는다. 사용자가 구분하는 단위는 "기본형 268개"이고, 변주(위치·각도·속도)는 B식 실행 차이로도 만들 수 있다. 즉 4만 개 사전 생성 대신 **기본형 + 런타임 파라미터화**로 같은 효과를 낼 수 있다.
- B 단독의 약점(연계 부족)은 FM도 "역할·지시 → 침투 타이밍·오버로드"로 보완한다. 시나리오를 **의도(목표 위치·타이밍·패스 순서 힌트)**로 바꾸고 Utility가 그 의도에 보너스 점수를 주는 구조가 두 장점을 합친다.

### Gaps
- 시나리오 기반 엔진과 Utility 엔진을 같은 조건에서 비교한 공개 실험은 찾지 못했다.
- 상용 축구 게임의 "시나리오 깨짐률"류 수치는 공개되지 않았다.

## Road To PRO 판단

### 결론
**현재 결정(시나리오 기반 + 팀 형태 레이어 + Utility 폴백)의 큰 틀은 유지하되, 무게 중심을 "시나리오가 조종 → Utility가 땜질"에서 "Utility가 항상 실행 → 시나리오는 의도(intent)를 준다"로 옮기는 것**을 권한다. 런타임 LLM은 쓰지 않는다. 양 팀 22명(교체 포함) 전원에게 오픈플레이 시나리오를 주지 않는다. 세트피스만 관여 선수 전원 시나리오.

### 이유
1. **근거 사례가 모두 하이브리드다.** FM은 선수별 상황 판단(0.25초 slice, 능력치 기반)이 실행을 맡고 전술·역할이 그 판단을 기울인다. RoboCup은 포메이션·역할 + 트리거 세트플레이. 오픈플레이 전체를 저작 패턴으로 돌리는 공개 사례는 찾지 못했다.
2. **깨짐 문제.** 상대가 끼어드는 오픈플레이에서 시나리오는 자주 깨진다. 깨지는 순간 Utility가 이어받는다면, Utility는 "폴백"이 아니라 **항상 돌아가는 기본 두뇌**여야 장면이 끊기지 않는다(하이라이트만 보여 주므로 끊김이 바로 보인다).
3. **선수 개성.** 역할 27개·성향 14축·버릇 15개는 Utility 가중치로 넣을 때 가장 자연스럽다(FM도 "능력치 기반 결정"). 시나리오 안에서는 슬롯 배정·성공률로만 반영되기 쉽다.
4. **튜닝·결정론·성능.** A+B 모두 순수 C# 결정론에 문제없다. 튜닝은 "패턴 선택 빈도"와 "실행 성공률" 두 손잡이로 평균 2.5~3.0골을 맞추기가 B 단독보다 쉽다.
5. **LLM 런타임 불가.** 지연(첫 토큰 200~800ms, 로컬에서도 초 단위 사례) > 틱 100ms, 비결정성은 리플레이·재시뮬·"경기 전 저장 복구"와 충돌, 한 경기 약 119만 결정이라 대량 시뮬 불가, 출시 사례는 RTX 전용 옵트인 대화형 동반자뿐.


### 전제: 시나리오는 개발 때 전부 만들어 두고, 게임은 골라 쓰기만 한다 (사용자 확정 2026-11-11)
- 빌드된 게임은 시나리오를 생성하지 않는다. 268 기본형 × 변주 → 약 4만 개를 **개발 파이프라인에서 생성·검증해 데이터로 고정**하고, 런타임은 "상황 키 → 후보 조회 → 결정론적 가중 선택"만 한다.
- 이 전제는 결정론·QA에 유리하다(출시된 시나리오 집합이 곧 테스트 대상 집합). EA도 FC26 RL 골키퍼를 오프라인으로 학습하고 300개 이상 고정 시나리오로 검증한 뒤 출시했다는 점에서 "개발 때 만들고 출시물은 고정"이라는 흐름은 업계 관행과 맞다. — [GDC FC26 GK](https://schedule.gdconf.com/session/reinforcement-learning-in-fc26-shipping-human-like-goalkeepers-with-a-designer-first-approach/914241)
- 따라서 앞선 초안의 "변주를 런타임 파라미터로 풀자"는 안은 **철회**한다. 대신 런타임에서 허용되는 것은 (a) 고정된 시나리오의 슬롯에 누구를 넣을지 배정, (b) 시나리오가 준 목표 지점·타이밍을 실제 선수 위치에 맞춰 **보간·정렬**(생성이 아니라 적용), (c) 깨지면 Utility 이어받기, 세 가지뿐이다.
- LLM도 같은 선 안에 둔다: 개발 파이프라인에서 기본형 초안·변주 설명·검증 케이스 작성 보조까지만. 출시물에는 LLM이 없다.

### 22명 전원(양 팀 + 교체 선수) 시나리오 vs 공 주변 3~5명 시나리오 + 팀 형태·Utility

사용자 질문의 "11명 전원"은 **우리 팀 11명 + 상대 팀 11명 = 22명, 교체 투입 선수 포함**이다.

#### 공개 사례: 수비 팀 조직은 어떻게 하나
- **FM(확인됨)**: 수비는 패턴 재생이 아니라 팀 단위 규칙 + 개인 판단이다. "수비 라인이 하나의 단위로 움직인다", 마크 대상 넘겨주기, "공을 따라가기보다 패스 길목을 막는다". — [FM21](https://www.footballmanager.com/news/match-engine-ai-fm21). 팀 지시는 블록 높이(Low Block / Mid Block / High Press), Drop Off / Hold the Line, 구역별 압박·형태 지시 4개로 표현된다. — [FM23](https://www.footballmanager.com/features/match-ai-and-animation). FM27은 수비에서 Marking·Anticipation 비중을 키웠다. — [FM27](https://www.footballmanager.com/nl/node/1108)
- **EA FC(확인됨)**: Team Tactics의 Defensive Approach + "공 있을 때/없을 때" 별도 형태, 그리고 역할이 공격·수비 양쪽의 오프볼 행동을 정한다. 압박 트리거 세부는 비공개. — [FC 25 Gameplay Deep Dive](https://ea.com/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-gameplay-deep-dive)
- **RoboCup(확인됨)**: 포메이션별 홈 포지션 + 공 위치 기반 위치 조정 + 동적 역할 배정. 저작된 다인원 플레이는 데드볼 세트플레이(심판 신호로 시작)에 쓴다. — [CMU](https://www.ri.cmu.edu/publications/task-decomposition-and-dynamic-role-assignment-for-real-time-strategic-teamwork), [UT Austin Villa](https://www.eecs.ucf.edu/~gitars/cap6671/Papers/macalpine.pdf)
- **Google Research Football(확인됨)**: 조작하지 않는 선수 전원을 규칙 봇이 맡는다. — [arXiv 1907.11180](https://arxiv.org/abs/1907.11180)
- 정리: 조사한 어떤 공개 사례도 **양 팀 22명 전원에게 오픈플레이 패턴을 동시에 재생**하지 않는다. 수비 조직은 공통적으로 "팀 형태(블록·라인·간격) + 개인 규칙(마크·커버·길목 차단·압박)"이다. (공개 자료 범위 내 결론. 비공개 구현은 알 수 없음.)

#### 비교 (평가는 위 근거와 계산에 기반한 추론)

| 항목 | X. 22명 전원 시나리오 동시 부여 (공격·수비·전환·세트피스·GK 패턴) | Y. 공 주변 3~5명 시나리오 + 나머지 팀 형태·Utility (현재 결정) |
|---|---|---|
| 성능 10Hz | 매 결정마다 공격 패턴 × 수비 패턴 × 전환 상태의 **짝 맞추기**가 필요. 한쪽 패턴이 바뀌면 상대 쪽도 재선택 → 연쇄 재매칭. 조회 자체는 싸도 재선택 빈도가 높아짐 | 시나리오 조회는 공 소유 변화·트리거 때만. 나머지 17~19명은 팀 형태 목표점 + 짧은 Utility 평가(후보 수 제한)로 상수 비용 |
| 헤드리스 대량 시뮬 | 4만 개가 "한 팀 3~5명" 기준이라면, 22명 동시 부여는 상대 패턴과의 **조합 공간**(공격 패턴 × 수비 대응)을 사전 생성해야 해서 데이터량이 곱으로 커짐. 사전 생성 전제와 정면 충돌 | 데이터량은 현재 그대로. 상대 대응은 런타임 팀 형태·Utility가 만들므로 사전 생성 불필요 |
| 충돌 해결 | 양 팀 패턴이 같은 공간·같은 공을 두고 각자 "대본"을 가짐 → 누가 이기는지를 결정할 중재 규칙이 별도로 필요하고, 결국 그 중재가 Utility/확률 판정이 됨. 한 명만 어긋나도 22명 대본이 동시에 무효화될 위험 | 대본은 공격 쪽 소수에만. 수비는 매 틱 반응하므로 충돌이 곧 "경합"으로 자연 처리(태클·인터셉트 판정). 깨지면 그 소수만 Utility로 |
| 반복감 | 22명이 같은 그림으로 움직이면 **화면 전체가 똑같은 장면**이 됨. 하이라이트만 보여 주는 게임에서 반복이 가장 잘 드러나는 형태 | 공 근처 패턴은 반복돼도 주변 선수 배치·수비 반응이 매번 달라 장면이 달라 보임 |
| 결정론 | 가능(고정 데이터 + 시드). 단 중재 규칙이 복잡해 디버깅 비용 큼 | 가능. 결정 경로가 짧아 재현·디버깅 쉬움 |
| 선수 개성 | 대본이 개인 판단을 덮음. 상대 수비수의 Marking·Anticipation 차이가 잘 안 드러남 | 수비수·먼 선수의 능력치·역할·버릇이 Utility에 직접 반영 (FM27이 수비에서 Marking·Anticipation을 키운 방향과 같음) |
| 교체 선수 | 투입 즉시 패턴 슬롯에 들어갈 수 있어야 함 → 슬롯 배정만 하면 되지만, 진행 중인 22인 대본 중간 투입은 불가(데드볼 때만) | 교체는 데드볼에서만 일어나므로 팀 형태에 역할만 꽂으면 끝. 다음 시나리오부터 후보로 참여 |
| 저작·QA | 사전 생성 집합이 곱으로 커져 검증 불가 수준(추정) | 현재 268 × 변주 범위 유지 |

#### 22명 질문에 대한 판단
**22명 전원 오픈플레이 시나리오는 하지 않는다.** 대신 상황별로 시나리오가 닿는 범위를 다르게 둔다.

| 상황 | 시나리오 대상 | 나머지 |
|---|---|---|
| 오픈플레이 공격 | 공격 팀 공 주변 3~5명(상황 따라 2~6명 동적 배정) | 공격 팀 나머지: 팀 형태 + 역할 Utility(지원·폭·침투 준비) |
| 오픈플레이 수비 | **수비 시나리오 없음**. 대신 수비 팀 "형태 프리셋"(블록 높이·라인·압박 구역)을 데이터로 미리 만들어 고름 | 공 근처 수비 1~3명: 압박·커버·마크 넘기기 Utility. 먼 수비수: 라인·간격 유지 |
| 전환(볼 탈취·상실 직후 수 초) | 시나리오 없음 또는 "역습 시작" 같은 짧은 공격 기본형만 | 양 팀 모두 전환 규칙(재압박 vs 복귀)을 Utility로 |
| 세트피스(코너·프리킥·스로인·킥오프·골킥·페널티) | **관여 선수 전원**(양 팀, 공격·수비 배치 모두) 시나리오. 경기가 멈춘 상태에서 시작하므로 짝 맞추기·충돌이 작음. RoboCup 세트플레이·TacticAI 코너킥 사례와 같음 | 킥 이후는 오픈플레이로 복귀 |
| GK | 세트피스·킥오프 배치는 시나리오, 플레이 중 위치·선방은 Utility(+ 애니메이션 선택) | — |
| 교체 투입 선수 | 투입 직후 별도 시나리오 없음. 역할을 팀 형태에 꽂고, 다음 시나리오부터 슬롯 후보 | — |

정리하면 "시나리오 = 공격의 의도 + 세트피스 전체", "수비 = 데이터로 만든 형태 프리셋 + Utility"다. 상대 팀도 같은 규칙을 따른다(대칭). 이렇게 하면 사전 생성 데이터는 "공격 기본형 268개 변주 + 세트피스 패턴 + 수비 형태 프리셋"으로 셋 다 개발 때 만들어 두고 런타임은 고르기만 한다는 전제를 지킨다.

### 교체·전술 변경·AI 감독 결정 (FM/FC 공개 자료)과 시나리오 체계의 관계

#### 공개 자료
- FM23: SI가 "AI 감독을 움직이는 로직을 개편"했고, AI 감독이 경기의 각 측면을 평가해 추격 여부·폼·경기 상황에 반응하고, 포메이션·역할을 팀 형태와 균형을 고려해 더 신중히 고른다. — [FM23 Match AI](https://www.footballmanager.com/features/match-ai-and-animation) (확인됨)
- FM27: Tactical Tweaks로 팀·개인 수준 전술 결정을 경기 중 바로 내리고, 꺼질 때까지 유지된다. 샤우트는 선수 멘탈 상태에 영향. — [FM27](https://www.footballmanager.com/nl/node/1108) (사람 감독 쪽 도구. AI 감독이 같은 도구를 쓰는지는 명시 없음)
- FM26 어시스턴트는 xG·xA와 상황 맥락으로 조언(예: 크게 이기고 있는 후반이면 템포를 낮추라). — [FM26 Match Day](https://www.footballmanager.com/fm26/features/where-storytelling-evolves-fm26s-match-day-experience) (확인됨)
- AI의 교체 트리거(카드·체력)는 **공식 문서로 확인하지 못했다**. 팬 블로그는 FM26 AI가 경고·체력에 반응해 선제 교체한다고 주장(미검증). — [footballmanagerblog.org](https://www.footballmanagerblog.org/2026/01/fm26-new-ai-coach-algorithm-forces.html). FIFA 계열 CPU가 주로 점수·체력/부상·퇴장에만 반응한다는 기사도 있으나 오래된 비공식 평가. — [OSFTW](https://www.osftw.com/features/1589/the-next-frontier-in-fifa-soccer-ai-management)
- EA FC 25: Smart Tactics가 경기 중 전술 제안과 새 교체 메뉴를 준다. — [FC 25 Gameplay Deep Dive](https://ea.com/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-gameplay-deep-dive)

#### 추론: 시나리오 체계와의 관계
- 공개 사례에서 감독 결정은 **선수 결정과 다른 층, 다른 주기**다(FM23의 AI 감독 로직 개편이 선수 결정 AI와 별도 항목으로 서술됨). Road To PRO도 3층으로 나눈다.
  1. **감독 층(AI 감독, 저주기)**: 데드볼·하프타임·일정 간격(예: 경기 시간 5분마다 — 수치는 `[미정]`)에만 평가. 입력 = 점수·남은 시간·체력·카드·부상·xG 흐름. 출력 = 교체, 포메이션, 팀 지시(블록 높이·템포·폭), 시나리오 **선택 가중치 프로필**(예: 지고 있으면 직선적·위험한 기본형 가중치 ↑, 이기고 있으면 점유·지연형 ↑). Utility(또는 규칙 표)로 구현하고, 결정론 시드 사용.
  2. **팀 층(팀 형태 레이어, 매 틱)**: 감독 층이 고른 형태 프리셋으로 라인·간격 유지.
  3. **선수 층(10Hz)**: 공 주변은 시나리오 의도 + Utility 실행, 나머지는 Utility.
- 즉 **감독 결정은 시나리오를 새로 만들지 않고, 미리 만든 시나리오 집합 위의 "선택 분포"를 바꾼다.** 이것이 "개발 때 전부 만들고 런타임은 고르기만" 전제와 맞는 연결 방식이다. 교체 선수는 팀 형태에 역할을 꽂는 것으로 끝나고, 시나리오 슬롯 배정은 다음 선택 때 자동으로 반영된다.
- 주인공 선수(사용자 커리어 선수)가 교체로 들어가거나 빠지는 것도 같은 경로를 탄다. 감독 AI가 주인공 교체 여부를 정할 때의 기준(체력·평가·감독 신뢰도)은 커리어 쪽 스펙과 연결해야 하며 수치는 기획서에 없으므로 `[미정]`.

### 바꿀 것 (제안, 사용자 승인 필요)
1. **역할 전환**: 시나리오 = 의도(역할 슬롯, 목표 지점, 타이밍 창, 다음 패스 대상 힌트). Utility = 매 결정(10Hz)마다 실행. 의도와 맞는 행동에 보너스 점수, 상황이 의도와 너무 어긋나면 의도 폐기. "깨짐 → 폴백"이 "의도 약해짐 → 자연스러운 Utility"로 바뀐다. 시나리오 데이터는 그대로 사전 생성·고정.
2. **시나리오 범위 명시**: 오픈플레이는 공격 팀 공 주변 2~6명(동적), 수비는 시나리오 대신 사전 제작 "수비 형태 프리셋" + Utility, 세트피스는 양 팀 관여 선수 전원 시나리오. 22명 동시 오픈플레이 시나리오는 하지 않는다.
3. **감독 층 분리**: AI 감독(교체·전술·시나리오 선택 가중치 프로필)을 별도 스펙으로. 평가 주기·트리거 수치는 `[미정]`.
4. **먼저 측정**: 헤드리스 대량 시뮬에서 (a) 시나리오 깨짐률, (b) 시간 중 시나리오 vs Utility 점유율, (c) 패턴별 득점 기여, (d) 하이라이트 반복 체감(같은 기본형 연속 등장)을 지표로.
5. **Utility 품질 투자**: 폴백이 아니라 주력이므로 고려 요소·곡선·역할 가중치를 스펙으로 (GDC "Practical Utility AI" 방식).
6. **LLM은 개발 파이프라인에서만**: 기본형 초안·변주 설명·검증 케이스 작성 보조. 산출물은 사람이 검토한 YAML/JSON으로 고정. 출시 빌드에는 LLM 없음.

1·2·3은 CLAUDE.md "확정된 결정"(경기 엔진 행)과 기획서를 구체화·일부 변경하는 내용이므로 SDD 규칙에 따라 기획서 수정 → 스펙 수정 제안으로 진행해야 한다. 4는 기존 결정 안에서 바로 스펙화 가능.
