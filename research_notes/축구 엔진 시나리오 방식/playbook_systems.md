# 스포츠 게임·시뮬레이션의 저작형 플레이 패턴(플레이북·시나리오) 시스템

> 조사일 2026-11-11. 공개 자료만 사용(논문, 팀 기술 문서, 공식 개발 블로그·패치 노트). 디컴파일·리버스 엔지니어링 자료는 쓰지 않았다. 항목마다 **[확인]**(1차 출처로 확인)과 **[추정]**(저자 추론)을 구분했다.
> 조사 범위의 한계: 이번에 GDC Vault 영상은 회원 전용이라 보지 못했고, Game AI Pro에서는 스포츠 플레이북을 다룬 장을 찾지 못했다. 그래서 상용 게임(Madden, NBA 2K, FC, FM)은 내부 구조가 아니라 **공식 마케팅·패치 노트 수준**까지만 확인했다. 가장 자세한 메커니즘 근거는 RoboCup 학술 자료(CMDragons 플레이북, FC Portugal Setplay, HELIOS)다.

## 1. 미식축구·농구·하키 게임(Madden, NBA 2K, NHL): 플레이 저작·선택·실행, 깨진 플레이 처리

### Takeaway
Madden과 NBA 2K는 "저작된 플레이북 + 상황별 선택 + 플레이가 깨지면 규칙 기반 자유 행동" 구조를 공개적으로 설명한다. 다만 선택 모델의 내부(가중치·입력)는 공개되지 않았다. 최근 Madden은 플레이 **선택**을 실제 NFL 데이터로 학습한 ML 모델에 맡긴다. 반면 깨진 플레이 대응("plaster")은 트리거와 강도 프리셋을 가진 **규칙**으로 설명한다.

### Cited Findings
- [확인] Madden 26의 Coach Suggestions는 "수십 년치 NFL 플레이콜 성향 데이터로 학습한 예측 ML 모델"로, "게임 상황에서 감독(또는 스태프)이 부를 플레이"를 찾는다고 EA가 설명한다. 학습 데이터 기간은 출처마다 다르다("decades"와 "nearly a decade"). — [EA Gridiron Notes: Welcome to Madden 26](https://www.ea.com/games/madden-nfl/madden-nfl-26/news/gridiron-notes-welcome-to-madden-26); [Madden 26 Gameplay Deep Dive (EA forums)](https://forums.ea.com/blog/madden-nfl-game-info-hub-en/madden-nfl-26-gameplay-deep-dive/12264662)
- [확인] Madden 26은 스크램블 판단에 다운·거리·점수·시계 같은 상황 변수를 넣었다고 밝혔다. — [Madden 26 Gameplay Deep Dive](https://forums.ea.com/blog/madden-nfl-game-info-hub-en/madden-nfl-26-gameplay-deep-dive/12264662)
- [확인] Madden 26의 "Adaptive Counters"는 상대 성향을 근거로 카운터 플레이를 추천한다. — [EA Gridiron Notes](https://www.ea.com/games/madden-nfl/madden-nfl-26/news/gridiron-notes-welcome-to-madden-26)
- [확인] Madden 27 공식 문서의 내용:
  - 코치 프로필은 "실제 데이터로 구축"했고 쉘·스턴트·압박·커버리지 구조가 코치마다 다르다.
  - 상황 로직: 잘 되는 QB·RB에게 공을 몰아 준다. 태클이 밀리면 엘리트 엣지 러셔에게 더블팀을 붙인다. 라인 매치업에 따라 런/패스 비율을 조정한다. 쿼터 길이가 짧으면 4th down에서 더 공격적으로 간다.
  - **시퀀싱**: "아웃사이드 존이 통하면 플레이액션 부트 같은 보완 콘셉트를 열기 시작한다."
  - 사용자 적응: "성향을 보고, 무엇이 통하는지 추적하고, 경기 내내 반응한다." 예를 들어 언더니스 공격을 반복하면 존 수비가 공격적으로 바뀌고, 스트롱사이드 번치 코너 루트를 반복하면 Box 체크가 나온다. — [EA Madden 27 Gameplay](https://www.ea.com/games/madden-nfl/madden-nfl-27/news/madden-27-gameplay)
- [확인] Madden 27의 **plaster**("플레이가 무너진 뒤의 조직된 혼돈"):
  - 플레이가 길어지면 수비수가 빈 공간을 지키는 대신 가까운 리시버에 붙는다.
  - 강도 프리셋: Off(존 유지), Conservative(백사이드 존 수비만 가장 가까운 리시버에 붙음), Aggressive(모든 존 수비가 붙음).
  - 트리거 옵션: 포켓 이탈+시간, 포켓 이탈만, 시간만.
  - "Look For Work": 할 일이 없는 수비수가 "잔디만 지키지 않고" 커버리지를 돕는다. — [EA Madden 27 Gameplay](https://www.ea.com/games/madden-nfl/madden-nfl-27/news/madden-27-gameplay)
- [확인] Madden 27의 볼 캐리어 AI는 숙련 사용자의 러닝을 behavior cloning으로 학습했다. 플레이 선택과는 별개의 기능이다. — [EA Madden 27 Gameplay](https://www.ea.com/games/madden-nfl/madden-nfl-27/news/madden-27-gameplay)
- [확인, 커뮤니티] Madden 24에서는 상황과 상관없이 AI 패스 비율이 65~70%로 쏠렸다는 비판이 있었다. 상황 반영이 약하면 바로 눈에 띄는 반복·비현실로 이어진다는 사례다. — [Operation Sports](https://operationsports.com/playcalling-and-playbooks-must-be-top-priority-for-madden-and-college-football)
- [확인, 2차 출처] NBA 2K18 Visual Concepts AI 프로듀서(Da_Czar) 블로그의 내용. 원문은 확인하지 못했고 요약 기사 기준이다:
  - 플레이 콜은 "freelance(팀 고유의 자유 공격 스타일)"와 "set(지정 세트 플레이)" 두 층으로 나뉘고, 팀마다 기본 freelance가 있다.
  - AI가 고정 세트 대신 변하는 조건에 적응한다.
  - 수비 팀이 상대가 실행 중인 세트·액션을 인식해 즉석에서 조정한다.
  - 볼 핸들러의 아이솔레이션 성향은 수비수 등급(poor/average/good/elite)에 따라 달라진다. — [Operation Sports: NBA 2K18 Offensive Tendencies blog](https://www.operationsports.com/nba-2k18-offensive-tendencies-ai-execution-motion-blog-da_czar); [Player.One](https://player.one/nba-2k18-blog-details-change-offensive-controls-ai-release-119712)
- [확인] NBA 2K25는 freelance의 컷 투 바스켓 로직을 고쳐 "타이밍이 맞지 않는 림 컷"을 줄였다. — [NBA 2K25 Courtside Report: Gameplay](https://nba.2k.com/2k25/courtside-report/gameplay/)

### Inferences
- [추정] 상용 미식축구·농구 게임은 공통으로 3층 구조로 보인다. ① 저작 플레이(루트·세트), ② 상황 변수로 고르는 선택기(Madden은 최근 ML), ③ 플레이가 깨졌을 때의 규칙 기반 자유 행동(plaster, freelance, scramble drill). 우리 구조인 "시나리오 → Utility 폴백"과 같은 모양이다.
- [추정] "시퀀싱"(통한 플레이 다음에 보완 플레이를 연다)과 "상대 성향 추적 → 카운터"는 반복감을 줄이면서 전술적 서사도 만든다. 둘 다 경기 안의 짧은 메모리만 있으면 구현할 수 있다.
- [추정] plaster가 **트리거(조건) × 강도(대상 범위)** 2축 프리셋인 점을 참고할 만하다. 폴백 전환을 "모두 즉시 Utility로"가 아니라 "누가, 언제 구조를 버리는가"로 매개변수화할 수 있다.

### Gaps
- Madden·NBA 2K·NHL의 플레이 선택 가중치, 데이터 구조, 플레이 수를 다룬 GDC 발표나 기술 문서는 찾지 못했다. GDC Vault는 회원 전용이라 확인하지 못했다.
- NHL(EA)의 시스템 플레이·전술 AI에 대한 공개 기술 자료는 이번 조사에서 찾지 못했다.

## 2. 축구 게임: Football Manager, EA FC, eFootball, RoboCup Setplay, 포메이션·역할 전략

### Takeaway
공식 발언 기준으로 FM은 플레이 패턴 라이브러리보다 "0.25초마다 선수 개인이 속성 기반으로 결정"하는 구조다. EA FC 25는 역할(Role)과 포커스로 오프볼 행동을 정한다. 반면 RoboCup의 FC Portugal Setplay는 **단계(step)·전이(transition)·조건·중단 시간**으로 된 명시적 다인 플랜을 런타임에 해석한다. 우리 시나리오에 가장 가까운 공개 모델이 이것이다.

### Cited Findings
- [확인] FM21 공식 블로그:
  - "선수와 심판은 각각 0.25초마다 결정을 내리며, 이를 'slice'라 부른다." 선수는 "자기 속성에 근거해 최선이라 생각하는 행동"을 한다.
  - slice 도중에도 상황이 바뀌면 결정을 바꿀 수 있다.
  - 수비수는 마킹을 다른 수비수에게 넘기고 압박에 나설 수 있고, 수비 라인은 "하나의 유닛처럼" 움직인다. 공을 따라가는 대신 패스 길을 막는다.
  - 팀은 특정 지역에 과부하를 걸어 반대편 공간을 노린다. — [Match Engine AI in FM21 (footballmanager.com)](https://footballmanager.com/node/404)
- [확인] FM 매치 엔진 개발자 Paul Collyer는 "선호 동작(preferred move)"을 "선수가 경기에서 하려는 경향이 있는 행동(중거리 슛 선호, 드리블 선호 등)"으로 정의했다. — [FMScout 인터뷰](https://www.fmscout.com/i-631-Miles-interviews-game-guru-Paul-C.html)
- [확인] FM26 관련 SI 인터뷰: "Decisions 속성은 침투 타이밍에, Passing·Vision은 스루패스 품질에, Stamina는 마지막 몇 분 경기력에 영향을 준다." — [ESPN FM26 인터뷰](https://africa.espn.com/gaming/story/_/id/46845663/football-manager-26-interview-sports-interactive-alan-granger-soccer-influence)
- [확인] FM이 "플레이 패턴 라이브러리를 쓴다"거나 "안 쓴다"고 명시한 공식 발언은 찾지 못했다. 공개된 설명은 모두 개인 단위 결정과 팀 지시 모델이다. — 같은 출처들
- [확인] EA FC 25 FC IQ는 Player Roles, Team Tactics, Smart Tactics 세 축이다.
  - 역할은 "모든 선수가 오프볼에서 어떻게 생각하고 행동하고 움직이는지"를 정한다.
  - 출시 시점 역할 31개, 역할별 Focus 1~3개로 고유 조합 52개.
  - 실제 데이터로 학습한 AI 모델이 다수 선수의 역할 숙련도(Familiarity)를 매긴다.
  - Work Rate와 Player Instructions는 제거됐고, 감독 세팅을 복사하는 전술 프리셋이 추가됐다. — [EA FC 25 Pitch Notes: FC IQ Deep Dive](https://www.ea.com/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-fc-iq-deep-dive)
- [확인] FC Portugal Setplay 프레임워크(Mota & Reis):
  - 리그에 독립적이고, 런타임에 해석되는 유연한 언어로 세트 플레이를 정의·실행한다. 저자들은 RoboCup에서 범용 세트 플레이 정의·실행 프레임워크가 처음이라고 주장한다. — [EUDL ROBOCOMM 2007](https://eudl.eu/doi/10.4108/ICST.ROBOCOMM2007.2209)
  - 구성: XML 기반 명세 언어, 그래픽 편집기 SPlanner, Agent2D 기반 예제 팀. C++ 라이브러리와 함께 자유 소프트웨어로 SourceForge에 공개됐다. 2D·3D 시뮬레이션과 Middle Size League에 적용됐다. — [FCP_GPR_2016 TDP](https://www.ais.uni-bonn.de/robocup.de/2016/TDPs/Simulation2D/RoboCup__Sim2D_TDP_FCP_GPR.pdf)
  - 공개된 명세 예(`CornerKick1`)의 필드:
    - `:invertible true`: 좌우 반전 허용
    - `:players`: 참가 역할 P6·P7·P8
    - `step :waitTime 25 :abortTime 59`: 사이클 단위
    - `:participants`: 역할별 시작 좌표
    - `:condition`: 플레이 모드 = 우리 코너킥, 공 위치 = 좌측 영역
    - `:leadPlayer`
    - `:transitions (nextStep :condition (canPassPl P6→P7) :directives ...)`: P6는 P7에게 패스, P7은 인터셉트, P8은 지정 지역으로 이동 — [FCP_GPR_2016 TDP](https://www.ais.uni-bonn.de/robocup.de/2016/TDPs/Simulation2D/RoboCup__Sim2D_TDP_FCP_GPR.pdf)
  - 시작 상황은 데드볼(킥오프·스로인·직간접 프리킥·코너·골킥·키퍼 캐치)이다. 볼이 살아 있는 "play on" 세트 플레이는 정의할 수 있지만 당시 라이브러리는 지원하지 않았다. 선수는 **매 결정 사이클마다** 현재 상황이 세트 플레이 시작 조건을 만족하는지 프레임워크에 묻는다. 조율은 "say" 통신으로 한다. 시작 단계의 waitTime은 라이브러리가 참가자를 고르고 그들이 자리를 잡을 시간이다. — [FCP_GPR_2016 TDP](https://www.ais.uni-bonn.de/robocup.de/2016/TDPs/Simulation2D/RoboCup__Sim2D_TDP_FCP_GPR.pdf)
  - 성능(2016): Agent2D 3.1.1 상대로 100경기 승률 90% 이상, Gliders 상대 20경기 10승(21득 25실), HELIOS2015 상대 20전 전패(2득, 평균 3실), WrightEagle 상대 20경기 2승(12득 39실). **세트 플레이만으로는 강팀을 이기지 못했다**는 근거다. — 같은 출처
  - 2019 BahiaRT 팀 문서: 세트 플레이는 "가변 인원의 다단계 유연 플랜"이고 구성 단위는 step이며, "언제든 세트 플레이를 중단하는 abort 조건 집합"을 가진다. — [BahiaRT 2019 TDP](https://tdp.robocup.org/wp-content/uploads/tdp/robocup/2019/robocupsoccer-simulation-3d/bahiart-74/robocup-2019-robocupsoccer-simulation-3d-bahiartA5N2IPfkj7.pdf)
- [확인] HELIOS(RoboCup 2D) 포지셔닝:
  - 입력은 초점(보통 공 위치) 하나, 출력은 각 선수의 전략적 위치다.
  - 사람이 공 위치별 배치 예시를 넣으면(GUI 도구 fedit) Delaunay 삼각분할로 보간한다. 이후 Constrained Delaunay로 발전했다. — [HELIOS 2007 TDP](https://wrighteagle2d.github.io/robocup/2007/helios_2d_TDP.pdf); [HELIOS 2010 TDP](https://wrighteagle2d.github.io/robocup/2010/2D_TDP_HELIOS.pdf)

### Inferences
- [추정] 축구 쪽 공개 설계는 둘로 갈린다. "개인 결정 + 역할·팀 지시"(FM, FC IQ)와 "데드볼 중심의 명시적 세트 플레이"(RoboCup)다. 오픈 플레이 전체를 패턴 라이브러리로 돌린다는 공개 사례는 찾지 못했다. 우리 방식(오픈 플레이 포함 268패턴)은 공개 선례가 드문 쪽이다.
- [추정] Setplay의 `invertible`·`waitTime`·`abortTime`·전이 조건(`canPassPl`) 구조는 우리 시나리오 데이터 스키마(트리거, 역할, 단계, 분기 조건, 타임아웃, 미러링)에 거의 그대로 대응한다.
- [추정] HELIOS의 "공 위치 → 배치 보간"은 시나리오가 조종하지 않는 나머지 선수의 팀 형태 레이어와 같은 문제를 푼 공개 사례다.

### Gaps
- eFootball "advanced instructions"의 내부 동작을 다룬 공식 기술 자료는 이번에 확보하지 못했다.
- EA FC 세트피스 루틴의 내부 구조(패턴 수, 선택 방식)에 대한 공식 자료는 찾지 못했다.
- Setplay 원 논문(Mota, Lau & Reis 2010 "Setplays as flexible, multi-robot plans")의 abort 의미론 세부는 core.ac.uk가 403을 반환해 직접 읽지 못했다.

## 3. 게임 AI·멀티에이전트 문헌: 플레이북, CBR, 역할 배정

### Takeaway
CMDragons(Bowling, Browning, Veloso 2004)는 그대로 가져다 쓸 수 있는 정량 설계를 공개했다.
- 플레이 = 적용 조건 + 종료 조건(결과 4종) + 타임아웃 + 순서 있는 역할(우선순위대로 탐욕 배정)
- 선택 = 적용 가능한 후보 중 가중 무작위
- 적응 = sleeping-experts Exp3(성공 ×3/2, 실패 ×2/3, 완료 ×11/10, 중단 ×10/11)
CBR 축구(Ros et al.)는 "상황 스냅샷 → 가장 가까운 케이스 검색 → 위치 적응"을 쓰며, 이동 비용을 검색 점수에 넣는다.

### Cited Findings
- [확인] 플레이 언어 키워드: `APPLICABLE`(여러 개면 OR, 각 줄은 술어의 AND), `DONE <결과> <술어>`, `TIMEOUT`, `OROLE`(상대 역할, 예: `closest_to_ball`), `FIXEDROLES`, `ROLE 1..4`(골키퍼 제외 4대, 각 역할은 tactic 리스트). 결과는 succeeded·completed·aborted·failed 4종이다. 너무 오래 끝나지 않으면 timeout으로 aborted 처리하고, 팀 기본값을 플레이별로 덮어쓸 수 있다(예: `TIMEOUT 15`). — [Bowling et al., ICAPS 2004](https://icaps04.icaps-conference.org/icapspapers/ICAPS04BowlingM.pdf)
- [확인] **좌표계 매개변수화**: 좌표는 절대값 또는 공 기준이다. y축 방향은 "공이 있는 쪽", "상대 다수가 있는 쪽", 또는 둘의 조합을 향하게 할 수 있다. 덕분에 "대칭 상황을 위해 플레이를 중복 작성할 필요가 없다". 미러링을 데이터 복제 대신 런타임 좌표 변환으로 처리한 것이다. — 같은 출처
- [확인] **역할 배정**:
  - 고정이 아니라 동적이고, tactic마다 고유한 적합도 함수로 정한다.
  - 충돌을 막으려고 역할 순서대로 배정한다. 1번 역할(보통 공을 다루는 역할)이 4대 전부를 후보로 먼저 고르고, 다음 역할은 남은 로봇 중에서 고르는 탐욕 방식이다.
  - 실행기는 환경이 변하면 배정을 계속 재검토해 **역할 교환**을 한다. 진동을 막으려고 현재 배정을 유지하는 강한 편향을 둔다. — 같은 출처
- [확인] **시퀀싱·기회주의**:
  - 공 담당(active player)의 tactic이 성공하면 모든 역할이 다음 tactic으로 넘어간다.
  - 실행기는 매번 각 로봇의 "즉시 슛"에 걸리는 시간과 성공 확률을 평가한다. 충분히 빠르고 성공률이 높으면 플레이 명세를 무시하고 즉시 슛한다. 이렇게 하면 "긴 복잡한 시퀀스를 담으면서도 짧은 기회에 반응"할 수 있다. — 같은 출처
- [확인] **선택과 적응**:
  - 적용 조건으로 후보 목록을 만들고 그중 하나를 무작위로 뽑는다. 뽑힐 확률은 가중치로 조정한다.
  - 처음에는 단순 곱셈 가중치를 썼는데 "특정 함정에 빠지는" 문제가 있었다. 그래서 적용 불가 플레이(잠든 전문가)는 확률이 변하지 않는 sleeping-experts 버전 Exp3로 바꿨다.
  - 갱신식: `ŵ = w·m^(1/Pr)` 뒤에 깨어 있는 플레이끼리 합이 보존되도록 정규화한다.
  - 승수: 성공 3/2, 실패 2/3, 완료 11/10, 중단 10/11. 성공과 실패를 대칭으로 두고, 짧은 경기 안에서 효과가 나도록 공격적으로 정했다.
  - 특수 목적 플레이(예: 코너)는 선택성 때문에 불이익도 이익도 받지 않는다. — 같은 출처
- [확인] 운용 규모: 팀 전략은 보통 **약 15개 플레이**였다. 경기 사이에 플레이북이 자주 바뀌었고, 전체를 몇 시간 만에 다시 쓴 적도 있다. 성적은 RoboCup 2002 8강, American Open 2003 우승, RoboCup 2003 4강이다. 이전 통제 실험에서는 공격 플레이 4개 × 단순 수비 3종 조합에서 최적 플레이가 상대 수비에 따라 결정적으로 달라졌다. — 같은 출처
- [확인] 후속 STP(Skills–Tactics–Plays) 아키텍처는 스킬·전술·플레이 3계층이고, RoboCup에서 미지의 상대를 상대로 검증됐다. — [Bowling et al., STP (2005)](https://webdocs.cs.ualberta.ca/~bowling/publications/b2hd-05cse-stp.html)
- [확인] CBR 로봇 축구(Ros, Arcos, López de Mántaras, Veloso, AIJ 2009):
  - 케이스 특징을 통제 가능(아군 위치)과 통제 불가(상대 위치)로 나눈다.
  - 검색 점수에 공 위치, 상대 특징, **아군을 케이스 배치로 옮기는 비용**을 넣는다.
  - 필드 대칭으로 케이스 베이스를 늘린다.
  - 반응형 행동 선택보다 우수했고, 2대2와 2 대 수비수+골키퍼 시나리오에서 시뮬과 실로봇으로 평가했다.
  - 2007년 확장판은 로봇 간 명시적 패스를 포함한 다로봇 케이스를 단일 로봇 케이스보다 우선한다. — [MERIT/URL 초록](https://merit.url.edu/ca/publications/a-case-based-approach-for-coordinated-action-selection-in-robot-s/); [ECCBR 2006](https://merit.url.edu/ca/publications/retrieving-and-reusing-game-plays-for-robot-soccer/). 본문은 읽지 않았고 초록·스니펫 기준이다.
- [확인] Stone & Veloso는 시뮬레이션 축구에서 과제 분해와 동적 역할 배정을 다뤘다(PTS 도메인). — [Stone & Veloso, Task decomposition and dynamic role assignment](https://cse-robotics.engr.tamu.edu/dshell/cs689/papers/stone99task.pdf)

### Inferences
- [추정] CMDragons의 "역할 우선순위 탐욕 배정 + 유지 편향"은 결정론적이고 계산량이 작다(역할 3~5 × 후보 수). 10Hz에서 공 주변 3~5명 캐스팅에 바로 쓸 수 있다. 헝가리안 알고리즘 같은 최적 배정은 필요할 때만 쓰면 된다.
- [추정] "기회주의 오버라이드"(명세 밖이라도 즉시 슛 가치가 크면 실행)는 우리 "Utility 폴백"보다 **한 단계 앞**의 장치다. 시나리오가 깨지지 않았어도 Utility 점수가 시나리오 다음 행동보다 크게 높으면 시나리오를 끊어야 한다.
- [추정] 플레이 결과를 4종(succeeded·completed·aborted·failed)으로 나누는 것은 오프라인 대량 시뮬 태깅에도 그대로 쓸 수 있다. 시나리오별로 "완주율", "중단율", "득점 기여율"을 따로 기록하는 식이다.
- [추정] CBR의 "케이스 배치로 옮기는 비용" 항은 4만 개 중에서 고를 때 **현재 선수 위치와 시나리오 시작 배치의 거리**를 점수에 넣어야 함을 시사한다. 시작 배치로 순간이동하듯 보이는 부자연스러움을 막는다.

### Gaps
- Game AI Pro(무료 공개판)에서 스포츠 플레이북이나 역할 배정 전용 장은 찾지 못했다. HTN을 스포츠 팀 플레이 패턴으로 쓴 공개 게임 사례도 찾지 못했다.
- Utility와 패턴을 결합한 스포츠 게임의 공개 GDC 사례는 이번 조사에서 확인하지 못했다.

## 4. 라이브러리 확장(절차적 변주, 조합, 미러링), 대량 시뮬 검증, 저장 방식

### Takeaway
공개 자료에서 확인되는 확장 기법은 세 가지다. ① 좌표계 매개변수화로 대칭 플레이 중복 제거(CMDragons), ② `invertible` 플래그로 좌우 반전(Setplay), ③ 대칭으로 케이스 베이스 확장(CBR). 수만 개 규모로 미리 펼쳐 놓는 스포츠 게임 사례나, 대량 시뮬로 패턴을 걸러 내는 공개 파이프라인은 찾지 못했다. 공개 사례의 라이브러리는 모두 작다(약 15개 플레이).

### Cited Findings
- [확인] CMDragons는 공·상대 다수 쪽 기준 좌표계로 대칭 상황의 플레이 중복을 없앴다. — [Bowling et al. 2004](https://icaps04.icaps-conference.org/icapspapers/ICAPS04BowlingM.pdf)
- [확인] Setplay 명세에 `:invertible true` 필드가 있고, waitTime·abortTime이 단계별 매개변수다. 서로 다른 id를 가진 세트 플레이 파일을 이어 붙여 하나의 `setplay.conf`로 배포한다. 즉 템플릿 + 매개변수를 텍스트로 저장한다. — [FCP_GPR_2016 TDP](https://www.ais.uni-bonn.de/robocup.de/2016/TDPs/Simulation2D/RoboCup__Sim2D_TDP_FCP_GPR.pdf)
- [확인] FCP_GPR는 세트 플레이 효과를 상대별 20~100경기 시뮬로 평가했다. 상대에 따라 승률이 90% 이상에서 0%까지 갈렸다. 대량 시뮬 평가는 **상대 다양성**이 있어야 의미가 있다는 근거다. — 같은 출처
- [확인] CMDragons의 통제 실험: 공격 플레이 4 × 수비 3 조합에서 최적 플레이가 수비에 따라 달랐다. 플레이 성공률은 고정 속성이 아니라 **상대·상황 조건부**다. — [Bowling et al. 2004](https://icaps04.icaps-conference.org/icapspapers/ICAPS04BowlingM.pdf)
- [확인] CBR은 필드 대칭으로 케이스 수를 늘렸다. — [Ros et al. 2009 초록](https://merit.url.edu/ca/publications/a-case-based-approach-for-coordinated-action-selection-in-robot-s/)
- [확인] Valve(Ruskin, GDC 2012)의 사례. 스포츠는 아니지만 대규모 저작 콘텐츠 선택 시스템이다:
  - 거대한 if/else 대신 "세계에 대한 수백 개의 사실"을 수천 개 대사 DB와 퍼지 패턴 매칭한다.
  - 특수 규칙에서 일반 규칙으로 내려가며 가장 구체적인 규칙을 고른다. 매칭이 실패하면 덜 구체적인 규칙으로 폴백한다.
  - 기억(history)을 사실로 추적한다. — [GDC Vault 세션 페이지](https://gdcvault.com/play/1015528/AI-driven-Dynamic-Dialog-through); [Emily Short 리뷰](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/). 슬라이드 PDF는 크기 제한으로 읽지 못했고 2차 요약 기준이다.

### Inferences
- [추정] 공개 사례는 대부분 **템플릿 + 매개변수 + 런타임 변환(미러링·좌표 기준)**이다. 우리 4만 개(268 × 변주)도 저장은 "기본 패턴 268 + 변주 축 인덱스"로 하고 런타임에 전개하는 편이 메모리상 유리하다. 다만 오프라인에서 대량 시뮬로 걸러 낸 "통과 조합 목록(비트셋·ID 리스트)"과 태그(성공률·사용 조건)는 미리 계산해 둔다. 즉 "검증은 전개해서, 저장은 압축해서" 한다.
- [추정] 걸러 낼 퇴화 패턴 기준 예: 완주율이 낮은 것(대부분 aborted), 득점·전진 기여가 0인 것, 특정 상대 블록에서만 비정상적으로 높은 성공률을 내는 것(익스플로잇), 시작 배치 이동 비용이 과도한 것, 동일 결과 궤적을 내는 사실상 중복 변주.
- [추정] 성공률 태그는 단일 값이 아니라 (상대 블록 유형, 구역, 점수 상황)별 조건부 값으로 저장해야 CMDragons 실험 결과와 맞는다.

### Gaps
- 수만 개 규모의 스포츠 플레이 패턴을 자동 생성·검증하는 상용·학술 공개 사례는 찾지 못했다. 대량 시뮬로 걸러 내는 파이프라인의 공개 선례도 없다.
- 템플릿 대 완전 전개 저장의 메모리·성능 수치를 비교한 공개 자료는 없다.

## 5. 다양성·반복 방지: 측정, 쿨다운, 결정론적 가중 무작위

### Takeaway
공개 스포츠 AI 자료가 반복 방지를 명시적으로 설명한 경우는 드물다. 확인된 장치는 세 가지다. ① 가중 무작위 선택(CMDragons), ② 상대 성향 추적에 따른 카운터(Madden), ③ 기억 사실 기반 규칙 매칭(Valve). 엔트로피·n-gram·시즌 빈도 상한 같은 측정 지표를 쓴 스포츠 게임의 공개 사례는 찾지 못했다.

### Cited Findings
- [확인] CMDragons는 적용 가능한 후보 중 무작위로 고르되 가중치로 확률을 조정한다. 성공한 플레이는 확률이 오르고, 적용 불가 플레이의 확률은 보존된다(sleeping experts). — [Bowling et al. 2004](https://icaps04.icaps-conference.org/icapspapers/ICAPS04BowlingM.pdf)
- [확인] Madden 27 AI는 사용자가 같은 공격을 반복하면 수비 성향을 바꾼다. 언더니스 반복에는 공격적 존, 번치 코너 반복에는 Box 체크로 대응한다. 반복하는 쪽이 손해를 보게 만드는 장치다. — [EA Madden 27 Gameplay](https://www.ea.com/games/madden-nfl/madden-nfl-27/news/madden-27-gameplay)
- [확인] NBA 2K18의 수비 AI는 상대가 돌리는 세트·액션을 인식해 즉석에서 조정한다(2차 출처). — [Operation Sports](https://www.operationsports.com/nba-2k18-offensive-tendencies-ai-execution-motion-blog-da_czar)
- [확인] Madden 24에서는 상황과 상관없는 패스 비율 고착이 비판받았다. 선택 분포가 상황에 둔감하면 체감 반복이 커진다는 사례다. — [Operation Sports](https://operationsports.com/playcalling-and-playbooks-must-be-top-priority-for-madden-and-college-football)
- [확인] Valve 대사 시스템은 기억 사실로 "이미 한 것"을 추적하고, 가장 구체적인 규칙을 고른다. — [GDC Vault](https://gdcvault.com/play/1015528/AI-driven-Dynamic-Dialog-through); [Emily Short](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)

### Inferences
- [추정] 결정론과 다양성은 함께 얻을 수 있다. 선택을 `seed = hash(matchSeed, tick, triggerId)` 같은 결정론적 RNG 스트림으로 하면 재현성을 유지한다. 가중치에는 다음을 곱한다:
  - 기본 적합도
  - 경기 내 최근 사용 감쇠(쿨다운). 예: 사용 후 N번 트리거 동안 ×0.3에서 시작해 점차 회복
  - 시즌 사용 빈도 감쇠
  - 상대 적응 승수(CMDragons식)
- [추정] 반복 측정 지표 제안:
  - 하이라이트 단위 시나리오 ID의 섀넌 엔트로피
  - 기본 패턴 bigram 반복률. 같은 경기에서 A→B가 2회 이상 나오는 비율
  - 시즌당 동일 기본 패턴 하이라이트 노출 상한
  - 플레이어가 보는 하이라이트에 대해서만 별도 집계. 백그라운드 90분 시뮬은 반복돼도 보이지 않는다.
- [추정] 하이라이트만 보여 주는 우리 구조에서는 체감 반복이 "엔진 선택 빈도"보다 "**하이라이트로 선별된 장면**의 빈도"에 좌우된다. 하이라이트 선정 단계에서 중복 패턴을 감점하는 것도 효과적인 반복 방지 장치다.

### Gaps
- 스포츠 게임이 플레이·애니메이션 반복을 정량 측정(엔트로피, n-gram)하거나 시즌 빈도 상한을 둔다는 공개 자료는 찾지 못했다.

## 6. 개인 능력치·스타일에 맞춘 패턴 적응(속성 기반 캐스팅, 속성 의존 분기)

### Takeaway
공개 사례는 세 가지 방식으로 개인차를 반영한다. ① 역할 배정을 역할별 적합도 함수로 동적 결정(CMDragons), ② 역할 숙련도를 선수별로 매김(FC IQ Familiarity), ③ 속성과 "선호 동작"이 결정 확률·품질에 영향(FM). Madden은 잘 되는 선수에게 공을 몰아 주는 경기 내 적응을 공개했다.

### Cited Findings
- [확인] CMDragons는 tactic별 적합도 방법으로 역할을 배정하고, 환경이 변하면 역할을 교환한다. — [Bowling et al. 2004](https://icaps04.icaps-conference.org/icapspapers/ICAPS04BowlingM.pdf)
- [확인] FC 25는 역할 31개 × Focus 52개 조합이다. 선수는 포지션의 모든 역할을 쓸 수 있지만 숙련도 수준이 다르고, 숙련도는 실제 데이터로 학습한 모델이 매긴다. — [EA FC 25 FC IQ Deep Dive](https://www.ea.com/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-fc-iq-deep-dive)
- [확인] FM: Decisions → 침투 타이밍, Passing·Vision → 스루패스 품질, Stamina → 막판 경기력. 선호 동작은 선수가 하려는 경향이 있는 행동이다. — [ESPN](https://africa.espn.com/gaming/story/_/id/46845663/football-manager-26-interview-sports-interactive-alan-granger-soccer-influence); [FMScout](https://www.fmscout.com/i-631-Miles-interviews-game-guru-Paul-C.html)
- [확인] Madden 27: "잘 되는 QB·RB에게 몰아 주기", 라인 매치업 기반 런/패스 조정, 롤 커버리지를 가장 빠른 리시버나 최고 OVR 리시버 쪽으로 기울이기. — [EA Madden 27 Gameplay](https://www.ea.com/games/madden-nfl/madden-nfl-27/news/madden-27-gameplay)
- [확인] NBA 2K18: 볼 핸들러의 아이솔레이션 공격성이 수비수 등급(4단계)에 따라 달라진다(2차 출처). — [Operation Sports](https://www.operationsports.com/nba-2k18-offensive-tendencies-ai-execution-motion-blog-da_czar)

### Inferences
- [추정] 우리 캐스팅 점수 = 역할 적합도(능력치 가중합) + 역할(27개)·성향(14축) 일치도 + 버릇(15개) 보너스 − 시작 위치 이동 비용 − 피로. 이를 CMDragons식 우선순위 탐욕 배정과 유지 편향으로 결정론적으로 푼다.
- [추정] 분기 확률은 시나리오 데이터에 "기본 확률 + 능력치 계수"로 저장하는 편이 좋다. 예: 컷백 분기 확률 = base + k·(Vision−50). 버릇은 특정 분기에 고정 승수를 건다. 예: "크로스 머신"은 크로스 분기 ×1.5. 같은 시나리오가 선수에 따라 다르게 펼쳐지므로 4만 개를 더 늘리지 않고도 다양성이 생긴다.

### Gaps
- 상용 게임이 저작 플레이의 분기 확률을 선수 속성에 어떻게 연동하는지 구체적인 공개 수식은 찾지 못했다.

## Road To PRO에 적용할 점

우리 런타임은 trigger → select → cast → execute → branch → Utility fallback 순서로 돈다. 순수 C#, 결정론, 10Hz이고, 시나리오는 오프라인에서 미리 만들어 런타임에는 고르기만 한다. 아래는 위 조사에서 뽑은 적용 제안이다. 모두 **[제안]**이며 기획서 수치를 대체하지 않는다.

1. **시나리오 스키마 대조 (CMDragons + Setplay)**
   - 들어가야 할 필드: `applicable`(OR-of-AND 술어), `roles[]`(순서 = 배정 우선순위), `steps[]`(역할별 지시), `transitions`(조건 → 다음 단계), `done`(결과 4종: success·complete·abort·fail), `timeout`/`abortTime`, `invertible`, 좌표 기준(공 기준·사이드 기준).
   - 기존 스키마에 빠진 항목이 있으면 스펙 단계에서 보강한다.
2. **trigger/select**
   - 적용 조건으로 후보를 고른 뒤 결정론적 가중 무작위로 선택한다. 가중치 요소: 상황 적합도, 시작 배치 이동 비용(CBR), 최근 사용 쿨다운, 시즌 빈도 감쇠, 경기 내 적응 승수.
   - 적응 승수는 CMDragons 값(성공 3/2, 실패 2/3, 완료 11/10, 중단 10/11)을 출발점으로 삼는다. 확정 전까지는 [미정]이다.
   - 적용 불가 시나리오의 가중치는 보존한다(sleeping experts).
   - RNG는 매치 시드 기반 서브스트림을 쓴다.
3. **cast**
   - 역할 우선순위 탐욕 배정(공 담당 역할 먼저) + 현재 배정 유지 편향(히스테리시스)으로 결정론과 진동 방지를 함께 얻는다.
   - 동점이면 선수 ID로 정렬해 결정론을 지킨다.
4. **execute/branch**
   - 분기 확률을 "기본값 + 능력치 계수 + 버릇 승수"로 데이터화하면 268 × 변주 위에 선수별 다양성이 더해진다.
5. **fallback**
   - 두 단계로 나눈다. ① **기회주의 오버라이드**: 시나리오가 진행 중이어도 Utility 점수가 시나리오 다음 행동보다 임계값 이상 높으면 끊는다(CMDragons 즉시 슛). ② **구조 붕괴 폴백**: abort 조건이나 타임아웃이 걸리면 전환한다.
   - 폴백 대상과 시점을 Madden plaster처럼 "트리거 × 범위" 매개변수로 두면, 붕괴 시 22명이 한꺼번에 흐트러지는 대신 관련 선수만 자유 행동으로 넘어간다.
6. **22명 전원을 시나리오로 돌려야 하나 → 아니오가 맞아 보인다**
   - 공개 근거 어디에도 22명 전원을 패턴으로 조종하는 사례가 없다.
     - CMDragons의 플레이는 골키퍼를 뺀 4대 규모다.
     - Setplay 예제는 참가자 3명이다.
     - FM과 FC IQ는 오프볼 전원을 역할·개인 결정으로 처리한다.
     - HELIOS는 공 위치 → 배치 보간으로 나머지를 처리한다.
   - 따라서 "공 주변 3~5명은 시나리오, 나머지는 팀 형태 레이어(HELIOS식 공 위치 보간 + 라인·간격 유지)"라는 현재 결정이 공개 사례와 맞는다.
   - 다만 상대 수비도 시나리오로 짝지을지(공격 시나리오의 상대 역할 `OROLE` 정의)는 따로 결정해야 한다. CMDragons의 `OROLE closest_to_ball`처럼 **상대 역할 지정**만 두고 수비 행동은 팀 형태와 Utility에 맡기는 방식이 조합 폭발을 피한다.
7. **오프라인 검증**
   - 성공률은 상대·상황 조건부라는 점이 확인됐다(CMDragons 4×3 실험, FCP_GPR 상대별 0~90%). 대량 시뮬은 상대 블록 유형을 다양하게 섞어 돌리고, 태그는 (상대 유형, 구역, 점수 상황)별로 저장한다.
   - 퇴화 패턴 필터 기준: 완주율 하한, 기여 0, 익스플로잇(특정 조건에서 이상 고성공), 중복 궤적, 과도한 이동 비용.
   - 이 필터의 리그 평균 득점 목표는 기획서의 2.5~3.0이다.
8. **저장**
   - 기본 패턴 268개 템플릿 + 변주 축 인덱스 + 오프라인에서 통과한 조합 비트셋·태그 테이블로 저장한다. 미러링은 데이터 복제 대신 런타임 좌표 변환으로 처리한다.
   - 4만 개를 완전히 전개해 저장하는 것은 검증 산출물에서만 한다. 메모리 판단은 014 스펙의 보류 항목과 연결된다.
9. **반복 방지**
   - 플레이어가 실제로 보는 것은 하이라이트뿐이다. 엔진 쿨다운에 더해 **하이라이트 선정 단계**에서 같은 기본 패턴을 감점하고, 시즌 노출 엔트로피와 bigram 반복률을 대량 시뮬 지표로 측정한다. 목표값은 [미정].
10. **선례가 드문 영역이라는 점을 의식한다**
    - 오픈 플레이까지 수만 개 패턴으로 돌리는 공개 선례는 찾지 못했다. FCP_GPR는 세트 플레이만으로 상위 팀을 이기지 못했다(HELIOS2015 상대 0/20).
    - 이는 Utility 폴백과 팀 형태 레이어의 품질이 패턴 수만큼 중요하다는 신호로 읽는 게 맞다. 이 부분은 [추정]이다.
