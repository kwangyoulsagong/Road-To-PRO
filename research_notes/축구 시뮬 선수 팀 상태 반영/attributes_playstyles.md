# 축구 게임 시뮬에서 능력치·플레이 스타일이 경기 판단·실행에 연결되는 방식 (FM / EA FC / eFootball / 오픈소스)

> 조사일 2026-11-11. 공개 자료만 사용(공식 Pitch Notes, 공식 포럼 직원 글, 커뮤니티 가이드·공개 테스트). 디컴파일·데이터 추출 자료는 쓰지 않았다.
> 출처 신뢰도: EA 공식 Pitch Notes > SI 직원 포럼 글 > 커뮤니티 가이드(passion4fm, fmscout 등) > 포럼 개인 테스트(FM-Arena). FM은 **공식 속성 설명 문서를 찾지 못했다** — FM 속성 설명은 대부분 커뮤니티 가이드에서 온 것이다.
> 도구 한계: fm-arena.com 페이지는 직접 가져오기에서 404가 나서 검색 스니펫만 사용했다.

## Q1. Football Manager — 속성 그룹, 트레잇(PPM), 역할·임무가 매치 엔진에 미치는 영향, 커뮤니티 테스트 결과

### Takeaway
FM에서 정신 속성은 "무엇을 언제 할지(판단·위치 선정·지시 준수)", 기술 속성은 "실행 정확도", 신체 속성은 "가능 범위(속도·지속력)"를 정하는 구조로 설명된다. 트레잇은 능력 보너스가 아니라 특정 상황의 **선택 확률 편향**이고, Teamwork가 "전술 지시 vs 개인 트레잇" 중 무엇을 따를지 정한다. 공개 테스트에서는 Pace·Acceleration이 결과를 압도적으로 좌우하고 Teamwork·Off the Ball은 영향이 거의 없다는 결과가 반복된다(테스트 전술 하나 기준이라는 한계 있음).

### Cited Findings
**정신 속성 (커뮤니티 가이드 기준, 2025-01 게시, FM24까지의 1–20 척도 설명)** — [passion4fm: Football Manager Player Attributes Explained](https://www.passion4fm.com/football-manager-player-attributes/)
- Decisions: 공 유무와 관계없이 올바른 선택(패스·스루패스·압박·슈팅·드리블)을 고르는 능력. 전술 지시·트레잇 수행의 중심.
- Anticipation: 상황을 예측하고 남보다 빨리 반응, 공간·위험을 먼저 감지. 모든 포지션·국면에 중요.
- Off the Ball: 위험 지역에서 공을 받을 위치로 움직이는 능력. 강한 러닝을 소화할 신체 속성이 뒷받침돼야 함.
- Positioning: 수비 위치 선정·마킹 선택. **공격 상황에서는 쓰이지 않음**.
- Composure: 압박 하 침착함, 공을 가질 시간 확보, 판단 개선. 마무리 일관성·페널티에 영향.
- Concentration: 경기 내내 집중 유지, 실수(오프사이드 라인 이탈, 엉성한 패스) 감소. 다른 가이드는 "높으면 Decisions·Anticipation을 경기 내내 더 잘 쓴다"고 설명 — [fm-base: player attributes explained](https://fm-base.co.uk/threads/player-attributes-explained.48793)
- Flair: 창의성·위험 감수(드리블, 중거리 시도, 기술) — 지시 밖으로 벗어날 수 있음.
- Teamwork: 지시를 따르고 동료와 협력하는 정도. 낮으면 동료를 찾기보다 공을 끄는 경향. 트레잇 vs 지시 선택에 영향.
- Vision: 스루패스·침투 기회를 "보는" 능력, Technique·Decisions와 함께 작동.
- Work Rate: 올바른 위치로 가려는 급박함. Stamina가 있어야 효과.
- Aggression: 압박·강한 태클·파울·항의 증가(반드시 더티함은 아님). Bravery: 위험한 경합·세트피스 헤더에 뛰어드는 의지. Determination: 뒤질 때의 반응·성격. Leadership: 동료 경기력 상승.
- 오래된 FM2011 가이드는 Decisions·Anticipation을 "1차(prime) 속성", 나머지를 그 아래 2차로 설명 — [fm-base: player attributes explained](https://fm-base.co.uk/threads/player-attributes-explained.48793)

**신체 속성** — [passion4fm](https://www.passion4fm.com/football-manager-player-attributes/)
- Acceleration: 정지에서 최고 속도까지. Pace: 최고 속도, **공을 가졌을 때보다 없을 때 더 빠름**. Agility: 출발·정지·방향 전환. Balance: 압박·회전 중 넘어지지 않음.
- Jumping Reach: 키를 반영해 보정된 공중 도달 높이, Heading·Balance·Strength와 공중 경합.
- Stamina: 90분 지구력. 낮으면 빨리 지치고 **지칠수록 실행 품질 저하**.
- Natural Fitness: 경기 간 회복·부상 회복·체력 향상, 노화에 따른 속성 하락 속도 — 경기 중 직접 효과는 아님.
- Strength: 몸싸움, 롱스로에도 사용.

**골키퍼 속성** — 같은 가이드는 Reflexes·One on Ones(선방), Handling·Aerial Reach(공중), Communication·Command of Area(지휘), Throwing·Kicking(배급), Eccentricity·Rushing Out·Punching(성향)으로 묶기만 하고 엔진 효과는 상세 설명하지 않음 — [passion4fm](https://www.passion4fm.com/football-manager-player-attributes/)

**숨은 속성** — Consistency 등은 에디터·스카우팅 도구에서만 보이며 평소 수준을 얼마나 꾸준히 내는지를 나타냄 — [passion4fm hidden attributes](https://www.passion4fm.com/?p=14158)

**트레잇(PPM)·Teamwork·지시의 관계**
- SI 공식 포럼 직원 글: "Teamwork가 높은 선수는 팀 지시를 따를 가능성이 높고, 낮은 선수는 전술 지시보다 자기 PPM을 택할 가능성이 높다" — [SI Community: team instructions vs player instructions](https://community.sports-interactive.com/forums/topic/555046-team-instructions-vs-player-instructions)
- 트레잇은 "추가 능력이 아니라 특정 상황에서의 의사결정에 영향"이며, 팀 지시·선수 지시·멘탈리티·역할·임무에 따라 발현이 달라짐. 지시와 맞는 트레잇은 그 행동을 더 자주 하게 만들고, 충돌하는 트레잇은 지시 준수를 떨어뜨림(의도적으로 한 선수만 다르게 움직이게 할 때 유용) — [passion4fm: Player Traits guide](https://www.passion4fm.com/football-manager-player-traits/)
- 예: "Gets Into Opposition Area" 트레잇은 전진 침투 빈도를 주로 정하지만 선수 지시로 덮어쓸 수 있음 — [magicgameworld FM21 Player Traits Guide](https://guides.magicgameworld.com/football-manager-2021-player-traits-guide/)
- FM24 커뮤니티 일부는 트레잇이 기대에 못 미친다며 개인 지시로 흡수하자고 제안 — [Steam FM24 discussion](https://steamcommunity.com/app/2252570/discussions/0/565870623632404146)

**역할·임무 (FM26 변경)**
- FM26에서 기존 Defend/Support/Attack 임무가 없어지고 선수마다 **공격 시(IP) 역할과 수비 시(OOP) 역할을 따로** 지정. 역할 수가 약 32개 → 70개 이상으로 늘었다는 보도. OOP 역할은 압박 강도·수비 위치·태클 적극성을 정함. 공·수 포메이션도 분리(예: IP 4-3-3 / OOP 4-1-4-1) — [RealSport101: FM26 tactics overhaul](https://realsport101.com/article/fm-26-tactics-overhaul-in-possession-out-of-possession-formations-explained); [passion4fm: in/out of possession formations](https://www.passion4fm.com/explaining-in-out-of-possession-formations/)
- 예: Advanced CB는 빌드업 때 DM 위치로 올라섬(현대식 리베로), No-Nonsense CB는 걷어내고 위험한 패스 회피 — [footballgpt FM26 guide](https://footballgpt.co/guides/master-fm26-tactics-new-ipoop-roles-pressing-explained) (상업 가이드, 신뢰도 중간)

**공개 테스트 수치 (FM-Arena, 포럼 기반)**
- FM-Arena 해설: "Acceleration·Pace 같은 속성은 큰 차이를 만들고, Teamwork·Off The Ball 같은 속성은 거의 차이가 없다" — [FM-Arena FM2021 Player Attributes Testing](https://fm-arena.com/find-tactic-result/9/852/)
- FM2024 테스트 페이지: Acceleration·Pace의 결과 영향이 "매우 높음" — [FM-Arena FM2024 Player Attributes Testing](https://fm-arena.com/find-tactic-result/26/7525/)
- 커뮤니티 해석: 필드 플레이어 전원 Acceleration +1 → 38경기 승점 약 +5.3 — [FM-Arena comment](https://fm-arena.com/find-comment/40437/) (개인 해석)
- Zippo(FM-Arena 운영자) 글: Acceleration·Pace·Work Rate 포함 7개 속성을 각 +3 → 승점 약 64 → 94(+30). 단 "이 테스트 전술 기준으로" pace/acceleration이 가장 중요하며 그 이상은 추측이라고 단서 — [FM-Arena Zippo posts](https://fm-arena.com/profile/6-zippo/find-posts/page-15/)
- FM24 결과표 스니펫: 기준 약 72.4점(114점 만점 리그), +5 Jumping Reach 71.6, +5 Finishing 61.1 등 — 행·라벨 매칭이 스니펫상 불확실 — [FM-Arena FM24 table](https://fm-arena.com/table/26-player-attributes-testing/)
- FM26 개인 테스트 글: 중요도 순 Pace·Acceleration > Work Rate·Jumping Reach·Agility > Finishing·Dribbling. Finishing 가중이 FM24 대비 약 2배라는 주장(개인 의견, 공개 표 없음) — [FM-Arena: I tested the attributes of FM26](https://fm-arena.com/thread/15933-i-tested-the-attributes-of-fm26-and-some-other-things/)
- FM26 요약 스레드: Dribbling 8 → 10~18에서 득점 +13%, 승점 +3%라는 단일 테스트 보고 — [FM-Arena: Summary of recent findings FM24 & FM26](https://fm-arena.com/thread/15934-summary-of-recent-findings-for-optimal-play-in-fm24-amp-fm26/)

### Inferences
- FM 구조는 "정신 = 선택·타이밍·지시 준수 확률", "기술 = 실행 성공률", "신체 = 이동·경합 가능 범위·피로에 따른 감쇠"의 3층으로 정리할 수 있다.
- 공개 테스트에서 신체(속도)가 지배적이라는 것은 실행 판정이 공간·도달 시간 계산에 크게 의존한다는 뜻으로 보이며, 정신 속성 효과가 작게 측정되는 것은 효과가 "선택 다양성·타이밍"이라 승점으로 잘 드러나지 않거나 실제로 가중이 작기 때문일 수 있다(판별 불가).
- Teamwork가 "지시 vs 개인 성향" 혼합 비율을 정하는 방식은 개성(성향·버릇)과 전술 일관성 사이 균형을 잡는 직접적인 장치다.

### Gaps
- SI의 공식 속성 설명(인게임 툴팁 원문) 출처를 찾지 못함. 속성별 엔진 가중치는 비공개.
- FM-Arena 원문 표를 직접 열지 못해(404) 속성별 전체 수치표를 확보하지 못함.
- GK 속성의 엔진 내 구체 효과는 공개 자료 부족.

## Q2. EA SPORTS FC — 능력치, PlayStyles/PlayStyles+, Work Rate, FC IQ 역할·숙련도

### Takeaway
EA는 FC 25부터 선수 표현을 4개 시스템으로 나눈다고 공식 설명: HyperMotionV(동작), OVR(신체·정신 능력치), PlayStyles(공 있을 때 기술), Roles(공 없을 때 행동). Work Rate와 선수 지시는 FC 25에서 제거되고 역할(31개, 포커스 포함 52조합)·숙련도(+/++)로 대체됐으며, 숙련도가 위치 선정 계산의 10~40%를 차지한다는 수치가 공개됐다.

### Cited Findings
**FC 25 FC IQ (공식 Pitch Notes, 2024)** — [EA: Pitch Notes FC 25 FC IQ Deep Dive](https://www.ea.com/en/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-fc-iq-deep-dive)
- 역할 31개, 각 1~3개 포커스, 출시 시 52개 조합. 역할은 "모든 선수가 공 없이 생각·행동·움직이는 방식"과 공 유무별 정렬 위치를 정함.
- 예: Advanced Forward(수비 라인 근처, 빌드업 때 내려오고 뒷공간 침투), Poacher(전방 대기·침투 득점), False 9(깊이 내려와 플레이 지휘, 포커스 Build-Up만), Holding(백라인 보호, 공격 지원 제한), Stopper(CB, 튀어나가 태클·인터셉트·압박 주도), Sweeper Keeper(GK, 빌드업 패스 옵션).
- 포커스 = 역할의 특성 1~2개를 약간 조정. 각 특성을 +, =, −로 표시. 예: Poacher Attack 포커스 "Attacking Runs(+), Support Centrally(+), Limited Width(=), Defence Support(−)".
- 숙련도: 기본(수행 가능), Role+(능숙, **모든 선수가 최소 1개 Role+ 보유**), Role++(월드클래스), 포지션 밖은 "단순화된 버전의 역할".
- 숙련도 효과: Poacher++는 공격에만 집중, 포지션 밖 Poacher는 공격 임무를 게을리할 수 있음. **위치 선정 계산에서 Positioning 또는 Defensive Awareness와 함께 쓰이며 결과의 10~40%를 차지**. 높을수록 침투·지원 범위·타이밍, 수비 마킹 반응 시간·커버 거리·압박 밀착이 좋아짐. "숙련도가 낮을수록 공수 전환에 더 오래 걸림".
- CF·LWB/RWB 포지션, Work Rate, Player Instructions는 FC 25에서 대체·제거.
- 수비 라인 높이 수치: Deep 1–30(기본 25), Balanced 31–60(50), High 61–90(70), Aggressive 91–100(95).
- 다른 지역판 공식 글은 숙련도가 "선수별·역할 단위"라고 써 있어 포지션 기반 설명과 어긋남(검색 요약 기준) — [EA FC 25 FC IQ (id)](https://www.ea.com/id/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-fc-iq-deep-dive). 역할 철학 글은 실데이터(Opta)로 숙련도를 배정한다고 언급했다는 검색 요약이 있으나, en판 본문에는 "real-world data로 학습한 독자 AI 모델"로만 적혀 있음.

**FC 25 Gameplay Deep Dive (공식, 2024)** — [EA: Pitch Notes FC 25 Gameplay Deep Dive](https://www.ea.com/en/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-gameplay-deep-dive)
- 패스 정확도 시스템 재설계로 "더 개성 기반" — 조작 선수의 능력치·스킬이 결과에 더 큰 영향. 슛 정확도는 실제 능력·PlayStyle·상황을 더 반영.
- 페널티: Composure 능력치에 따라 Composure Circle 속도가 달라짐. 쉴딩 효과는 피로에 연동(지치면 약해짐).
- 새 GK PlayStyle(Footwork, Rush Out, Deflector, Cross Claimer, Far Reach, Far Throw) — 기본은 "빠르게/범위 확장", +는 "더 빠르게/더 넓게"로 정성 설명만, 수치 없음.
- AI 수비: 돌파당하기 직전 수비수는 골문 중앙 쪽으로 물러남, 박스 안에서는 컷백 대비 지역 마킹(볼 소유자를 덜 정확히 따라감). CPU 슬라이더(Tackle Aggression, Buildup Speed, Shot/Crossing/Dribble/Skill Move Frequency 등) 추가, Tactical 프리셋은 상대 스타일에 따라 CPU 결정을 바꾸고 Dynamic 프리셋은 약간의 변동을 줌.

**FC 24 PlayStyles (공식, 2023)** — [EA: FC 24 PlayStyles deep dive](https://www.ea.com/games/ea-sports-fc/pitch-notes/news/fc-24-playstyles-deepdive)
- PlayStyles는 "Opta 실데이터 등으로 최적화"되며 OVR을 넘어 경기장 위 능력을 표현. PlayStyles+는 같은 효과를 "월드클래스 수준"으로 강화(별도 명시 없으면 효과 동일·수치만 증가).
- GK 전용 PlayStyle은 FC 24에서 Be A Goalkeeper 모드에서만 작동하고, 그 외에는 "과거 Traits와 비슷한 CPU AI 행동 효과"만 가짐 → Traits가 원래 CPU AI 행동 편향 장치였음을 공식적으로 시사.
- 34개 PlayStyle, 6분류(Scoring, Passing, Ball Control, Defending, Physical, Goalkeeping) — [Sportskeeda](https://sportskeeda.com/esports/ea-fc-24-gameplay-deep-dive-complete-list-playstyles-confirmed) (2차 출처)
- 2차 출처들은 PlayStyles를 이전 Traits·X-Factor 개념의 진화로 설명 — [RealSport101](https://realsport101.com/article/ea-fc-playstyles-everything-you-need-to-know)

**FC 26 (2025-07 사전 공개)**
- 새 PlayStyle 5개(Enforcer, Aerial Fortress, Precision Header, Gamechanger, Inventive), 제거 4개(Power Header, Aerial, Trivela, Flair), 역할 추가 예고. AI GK 위치 선정에 강화학습 도입. Competitive/Authentic 두 프리셋(어느 쪽이 AI 수비가 강한지는 보도 간 상충) — [Beebom: FC 26 gameplay features](https://beebom.com/ea-fc-26-gameplay-features); [ESPN: FC 26 gameplay deep dive](https://africa.espn.com/gaming/story/_/id/45803292/ea-sports-fc-26-gameplay-deep-dive)

### Inferences
- EA는 "공 없는 판단 = 역할 × 숙련도 × 인지 능력치(Positioning/Def. Awareness)", "공 있는 실행 = 능력치 + PlayStyle 보정 + 상황"으로 분리. 숙련도를 위치 계산의 일정 비율(10~40%)로 섞는 방식은 단순하고 결정론적으로 구현하기 쉬운 가중합 모델이다.
- Work Rate 같은 단일 수치를 없애고 역할 포커스(+/=/−)로 행동 특성을 표현한 것은 "능력치 1개로 행동 성향을 섞지 않는다"는 설계 선택.
- PlayStyle은 능력치와 별도 축의 "희소한 특기 플래그"로, +는 같은 효과의 강화판이라 데이터 구조가 단순(효과 ID + 레벨 0/1/2).

### Gaps
- 필드 PlayStyle별 정량 효과(정확도 %, 속도 %)는 공식 문서에 수치가 없음.
- FC 25 이전 Work Rate(High/Med/Low)가 AI 위치에 미친 정량 효과는 공개 자료 미확보.
- FC 26 공식 Pitch Notes 원문은 이번에 직접 열지 못함(2차 보도 기준).

## Q3. eFootball — 플레이 스타일·스킬이 AI 움직임을 바꾸는 방식

### Takeaway
eFootball(구 PES)은 선수별 "Playing Style"(Goal Poacher, Box-to-Box, Orchestrator 등)이 공 없는 움직임·위치를 정하고, "Player Skills"가 특정 기술 동작 가능 여부를, 별도의 "AI Playing Styles"가 CPU가 조종할 때 공 가진 행동 빈도(드리블·침투·크로스 등)를 정하는 3층 구조로 설명된다. 다만 Konami 공식 해설은 찾지 못했다.

### Cited Findings
- Goal Poacher: 최종 수비수 뒤에서 득점 기회를 노리는 CF 스타일(eFootball 2026) — [fifplay: eFootball 2026 Playing Styles](https://www.fifplay.com/efootball-2026-playing-styles/)
- Box-to-Box: 가장 높은 활동량으로 넓은 범위에서 공수 지원; Orchestrator: 깊이 내려와 공을 받아 공격 시작 — [gamingonphone: midfielder playstyle guide](https://gamingonphone.com/guides/efootball-playstyle-guide-for-midfielders)
- AI Playing Styles는 일반 스타일과 별개로, CPU 조종 시 공을 가진 행동(드리블, 침투, 크로스, 패스, 슈팅)을 정함. 일반 스타일은 주로 움직임·위치, 스킬은 특정 기술 동작 담당 — [fifplay: eFootball 2027 Playing Styles](https://www.fifplay.com/efootball-2027-playing-styles/)
- 포지션별 스타일 개수는 출처마다 다름(예: 공격수 7개, 미드필더 7~8개) — [sportsdunia](https://www.sportsdunia.com/esports/efootball-all-playing-styles); [gamingonphone](https://gamingonphone.com/guides/efootball-playstyle-guide-for-midfielders)

### Inferences
- "공 없는 움직임 성향(스타일) / 공 있는 행동 빈도 성향(AI 스타일) / 실행 가능 기술(스킬)"의 분리는 우리 게임의 역할·성향·버릇 분리와 거의 같은 구조.

### Gaps
- Konami 공식 매뉴얼의 스타일·스킬별 AI 효과 설명은 확보 못함. 2차 출처 간 상충(fifplay가 Goal Poacher를 GK 스타일로 표기하는 오류 등) 있음.

## Q4. 모든 선수가 똑같이 움직이지 않게 하는 장치 (개인 자율성)

### Takeaway
세 게임 모두 (1) 역할/스타일로 공 없는 기본 행동을 다르게, (2) 트레잇·PlayStyle·AI 스타일로 특정 선택에 편향을, (3) 정신 속성으로 판단 질·타이밍을 다르게, (4) FM은 Teamwork·Flair로 "지시를 벗어날 확률"을 개인화한다.

### Cited Findings
- FM: Teamwork 낮음 → 지시보다 PPM 선택(SI 직원) — [SI Community](https://community.sports-interactive.com/forums/topic/555046-team-instructions-vs-player-instructions); Flair 높음 → 지시 밖 창의 플레이 — [passion4fm](https://www.passion4fm.com/football-manager-player-attributes/)
- FM: 충돌 트레잇은 한 선수만 시스템과 다르게 행동하게 만드는 데 쓸 수 있음 — [passion4fm traits](https://www.passion4fm.com/football-manager-player-traits/)
- EA FC 25: 역할 포커스의 +/=/− 특성과 숙련도 차이로 같은 역할도 선수마다 침투 타이밍·커버 거리·전환 속도가 달라짐 — [EA FC IQ Deep Dive](https://www.ea.com/en/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-fc-iq-deep-dive)
- EA FC 25: CPU Dynamic 프리셋은 결정에 약간의 변동을 줌 — [EA Gameplay Deep Dive](https://www.ea.com/en/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-gameplay-deep-dive)
- eFootball: 스타일 + AI 스타일 + 스킬 조합 — [fifplay 2027](https://www.fifplay.com/efootball-2027-playing-styles/)

### Inferences
- 공통 패턴: "팀 구조(역할·포메이션)가 기본값을 주고, 개인 성향이 그 위에 확률 편향을 더하며, 정신 능력치가 '편향이 상황에 맞는지' 판단 질을 정한다".

### Gaps
- 각 게임에서 편향의 정량 크기(예: 트레잇이 선택 확률을 몇 % 올리는지)는 비공개.

## Q5. 오픈소스 축구 시뮬의 능력치→행동 모델

### Takeaway
Google Research Football은 선수 능력치·상대 AI 난이도를 설정 가능한 파라미터로 노출하고, RoboCup 2D 서버(rcssserver)는 "이종 선수 타입(heterogeneous player types)"으로 속도·스태미나·킥 파워 등 신체 파라미터를 무작위 생성한 타입을 코치가 배정하는 방식이다. 둘 다 "정신 속성·성향"보다는 신체·제어 파라미터 위주이고, 판단은 에이전트(RL 또는 팀 코드)가 맡는다.

### Cited Findings
- GRF 원 논문 저자: 엔진이 게임 파라미터, 상대 AI 난이도, **선수 스탯** 커스터마이즈를 허용 — [Google Research Football paper (emergentmind)](https://www.emergentmind.com/papers/1907.11180.md)
- GRF 관측값에 선수별 피로도(tired factor), 옐로카드, 활성 여부, 역할 코드(GK, CB, LB 등) 포함 — [DI-engine docs: gfootball](https://di-engine-docs.readthedocs.io/en/latest/13_envs/gfootball.html)
- GRF 기반 연구에서 선수 타입(CM/CB)과 능력 수준(예: 1.0 vs 0.7)을 바꿔 과제 동역학을 다르게 함 — [arXiv 2210.12262](https://arxiv.org/pdf/2210.12262)
- rcssserver 서버 기본값 예: stamina_inc_max 45, kick_power_rate 0.027, player_speed_max 1 — [RoboCup Soccer Simulator: Getting Started](https://rcsoccersim.readthedocs.io/en/latest/gettingstarted.html)
- rcssserver 15.1.1에서 이종 킥 파워(kick_power_rate_delta_min/max) 등 `_delta` 계열 선수 타입 파라미터 추가 — [Softpedia changelog](https://mac.softpedia.com/progChangelog/The-RoboCup-Soccer-Simulator-Changelog-28403.html)
- 온라인 코치가 `change_player_type`으로 선수에게 이종 타입을 배정 — [rcsoccersim: coach](https://rcsoccersim.readthedocs.io/en/latest/coach.html)
- 이종 선수 활용 전략 논문 — [Stone et al., ATTUnited-2001: Using Heterogeneous Players](https://www.cs.utexas.edu/~pstone/Papers/bib2html-links/LNAI2001-team.pdf)

### Inferences
- 오픈소스 쪽은 "신체 파라미터 = 실행 한계" 모델의 좋은 참고(속도 상한, 스태미나 회복, 킥 파워 계수 같은 연속 파라미터). 정신 속성·성향 모델은 상용 게임 공개 설명 쪽이 더 참고가 된다.

### Gaps
- rcssserver 이종 타입 파라미터의 정확한 정의(player_speed_max_delta 등)와 트레이드오프 공식은 매뉴얼 해당 절을 직접 확인하지 못함.
- 정신 속성·성향을 명시적으로 모델링한 오픈소스 축구 시뮬은 찾지 못함.

## Road To PRO에 적용할 점

### Takeaway
"정신 = 선택·타이밍·지시 준수", "기술 = 실행 성공률", "신체 = 도달 범위·피로 감쇠", "역할 = 공 없는 기본 위치", "성향 14축 = 선택 확률 편향", "버릇 15개 = 희소한 강한 편향(트레잇/PlayStyle 대응)"으로 층을 나누면 세 상용 게임의 공개 설계와 맞는다.

### Cited Findings
- (위 Q1~Q5의 인용 근거를 종합한 설계 제안이며 새 사실 인용은 없음)

### Inferences
- **역할 숙련도 가중합**: EA처럼 위치 선정 = (역할 목표 위치) 보정에 숙련도를 10~40% 비율로 섞는 식은 결정론 C#에 그대로 옮기기 쉽다. 단 수치는 우리 기획서에 없으므로 스펙에서 `[미정]`으로 두고 대량 시뮬로 정할 것.
- **지시 준수 확률**: FM의 Teamwork 메커니즘을 "시나리오 지시 vs 개인 버릇" 선택에 적용 — 시나리오가 조종하는 3~5명도 Teamwork(또는 유사 속성)가 낮으면 버릇 행동을 택할 확률↑. 시나리오가 깨졌을 때 Utility 폴백에서 성향 14축을 고려 가중치로 쓰면 개성이 자연스럽게 드러난다.
- **버릇 = 레벨 있는 플래그**: PlayStyle/PlayStyle+처럼 버릇에 강도 레벨(0/1/2)을 두면 데이터가 단순하고 표시도 쉽다.
- **공 없는 행동과 공 있는 행동 분리**: EA(Roles vs PlayStyles), eFootball(Style vs AI Style) 모두 분리. 우리 역할 27개는 공 없는 위치·침투 성향, 성향 14축·버릇은 공 있는 선택 빈도 담당으로 나누면 중복이 줄어든다.
- **신체 지배 주의**: FM 공개 테스트에서 Pace·Acceleration이 결과를 압도한다는 점은 "속도가 너무 강한 엔진" 위험 신호. 대량 시뮬(평균 득점 2.5~3.0 목표)과 함께 속성별 +N 민감도 테스트(FM-Arena 방식: 한 속성만 올리고 리그 시즌 반복)를 회귀 테스트로 두면 밸런스 감시에 쓸 수 있다.
- **피로→실행 감쇠**: FM(Stamina 낮으면 실행 저하), EA(쉴딩이 피로에 연동)처럼 피로를 실행 성공률 계수에 곱하는 방식이 커리어 게임(체력·컨디션·라이프 시스템)과 연결하기 좋다.
- **Composure·Concentration을 상황 계수로**: 압박 강도·경기 시간 경과에 따라 실행·판단 품질을 깎는 계수로 쓰면 "큰 경기에 약한 선수" 같은 개성이 생긴다(EA는 페널티 Composure Circle로 같은 생각을 구현).
- **정신 속성 효과 측정 어려움**: 정신 속성 효과는 승점으로 잘 안 보이므로, 검증 지표로 결과(승점)뿐 아니라 행동 통계(침투 타이밍, 패스 선택 분포, 지시 이탈률)를 함께 기록해야 한다.

### Gaps
- 우리 기획서(docs/index.html)의 역할 27개·성향 14축·버릇 15개 정의와 위 층 구분의 1:1 대응은 이 노트에서 대조하지 않았다(별도 스펙 단계에서 확인 필요).
