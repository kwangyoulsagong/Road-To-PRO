# 사기·폼·조직력·전술 숙련도·리더십·감독·경기 맥락이 경기 결과에 미치는 영향 (게임 모델 + 실제 데이터)

조사일: 2026-11-11. 공개 자료만 사용 (디컴파일·추출 데이터 없음). 웹 검색 스니펫·2차 요약에서 온 수치는 그렇게 표시함.

## Q1. Football Manager: 사기·조직력·전술 숙련도·숨은 능력치·팀 토크의 효과

### Takeaway
SI(개발사)는 사기·조직력·전술 숙련도가 경기 엔진에 영향을 준다고 **정성적으로만** 밝히고 공식 수치·공식은 공개하지 않는다. 대표 커뮤니티 테스트(FM-Arena)는 오히려 사기·컨디션을 **최대로 고정**해서 변수에서 제거하고 테스트한다. 사기 단계별 효과 크기를 측정한 공개 수치는 찾지 못했다.

### Cited Findings
- FM27 공식 매치 엔진 소개: 팀 조직력(cohesion)이 맞지 않으면 "선수들이 같은 구역에 겹치고 잘못된 판단을 하는" 모습이 경기에 드러날 수 있다고 설명. 또 FM27 "Tactical Effectiveness"는 스쿼드 내 관계가 조직력을 돕거나 해치는 것까지 고려한다고 함 (구체 공식 없음) — [FM27: Tactical Tweaks and Match Engine Upgrades](https://www.footballmanager.com/node/1108)
- FM23 공식: 다이내믹스 센터(위계·사회 그룹·드레싱룸 분위기)로 "행복한 스쿼드"를 관리하는 기능 소개 — [Maintain a happy squad in FM23 with the Dynamics Centre](https://footballmanager.com/node/722); FM20 매뉴얼 "Squad and Team Report / Dynamics" — [SI Community manual](https://community.sports-interactive.com/manual/football-manager-2020/your-squad-team-report-and-dynamics-r560)
- 전술 숙련도(커뮤니티 가이드): 숙련도는 멘탈리티·템포·폭 등 **8개 전술 특성**별로 따로 추적되며, 낮으면 "전술이 최대 성능을 내지 못한다" — [Passion4FM: How to maximize tactical familiarity](https://www.passion4fm.com/how-to-maximize-tactical-familiarity/)
- 커뮤니티 해석: 팀 조직력 수준은 경기 중 선수 **포지셔닝**에 더 큰 영향; 짧은 기간 다수 영입 시 조직력 하락; 선수 간 파트너십 선이 끊겨 있으면 둘 다 함께 뛸 때 부진; 승리 → 조직력 상승 → 드레싱룸 분위기 상승 (모두 비공식) — 위 Passion4FM 및 검색 요약(같은 검색: [Passion4FM](https://www.passion4fm.com/?p=19442), [Football Handbook](https://footballhandbook.com/?p=39980), [GameSpot FM2018 신규 기능](https://www.gamespot.com/articles/football-manager-2018s-new-features-revealed/1100-6453892))
- 사기 일반: 행복한 스쿼드는 능력치 이상으로, 불행한 스쿼드는 이하로 플레이한다는 설명(커뮤니티) — [footballgpt FM morale](https://footballgpt.co/fm/fm-morale); 사기 낮으면 "헌신 없이 뛰거나 전술을 제대로 수행하지 못함" — [Gamepressure FM22 초보 가이드](https://www.gamepressure.com/football-manager-2022/beginners-guide/zef0e3); 사기는 경기 도중에도 변함 — [FMScout 팀 토크 가이드](https://www.fmscout.com/a-team-talks-guide-managing-morale-and-confidence.html)
- FM21 UI: 사기 최고는 녹색 위 화살표, 최악(devastated)은 빨간 아래 화살표로 표시 — [Gamepur FM21](https://vip-develop.gamepur.com/?p=116149)
- 개인 대화(private chat)는 사기를 올리는 가장 쉬운 수단; 연패 탈출 팁 — [FMInside 41 tips to end a losing streak](https://fminside.net/guides/tactical-guides/41-tips-to-end-a-losing-streak); 성격(personality)은 사기에 간접 영향, 경기력에도 영향 — [FMScout 성격 가이드](https://www.fmscout.com/a-guide-to-player-personalities-football-manager.html)
- FM-Arena 테스트 방법론: 테스트 리그는 매 경기 전 **모든 선수의 사기·컨디션을 최대로 고정**하여 양 팀 동일 → 결과 계산에서 사기·컨디션 요인이 제거됨. 단 경기 중 체력 저하는 남음 — [FM-Arena 댓글 5014](https://fm-arena.com/find-comment/5014/), [FM-Arena 댓글 20560](https://fm-arena.com/find-comment/20560/)
- FM-Arena: 불행한 선수를 다른 선수들이 지지하면서 사기 문제가 **전염**됨; 수석코치 능력이 이 처리에 영향 — [FM-Arena 댓글 19348](https://fm-arena.com/find-comment/19348/)
- FM-Arena RNG 잡음: 5,760경기 테스트 기준 약 ±1점(FM22), FM23은 약 1.5점 — [FM-Arena 댓글 33673](https://fm-arena.com/find-comment/33673/)
- FM-Arena에 "FM23 Condition & Morale Testing" 표가 존재하나(https://fm-arena.com/table/20-condition-morale-testing/) 현재 404로 내용 확인 불가.

### Inferences
- FM의 설계 철학: 사기·조직력·숙련도는 "능력치 실효값과 판단·포지셔닝 품질"을 깎는 **승수형 보정**이며, 플레이어에게는 화살표·단계 라벨로만 보여 주고 수치는 숨긴다.
- FM-Arena가 사기를 고정해야 할 만큼 영향이 있다고 보는 것 자체가 "효과가 무시할 수준은 아니다"라는 간접 증거이나, 크기는 미확인.

### Gaps
- 사기 단계(Abysmal~Superb)별 수치 효과, Leadership/Determination/Pressure/Important Matches(Big Matches) 숨은 능력치의 정량 효과, 팀 토크·샤우트의 측정치: 공식·공개 테스트 수치를 찾지 못함. 공식 FM 문서는 정성 설명뿐.
- "mentality"(전술 멘탈리티)의 결과 영향은 FM-Arena 전술 테스트가 다수 있으나 이번 조사에서 수치를 수집하지 못함.

## Q2. EA SPORTS FC: Ultimate Team 케미스트리, 커리어 모드 사기·샤프니스

### Takeaway
FUT 케미스트리(FIFA 23부터 0~3점 체계, FC 24·25 유지)는 **케미스트리 스타일의 능력치 보너스를 열어 주는 장치**이며, 0점이어도 기본 능력치는 그대로다(FIFA 22까지의 "케미 낮으면 능력치 하락"에서 바뀜). 커리어 모드는 FC 24까지 샤프니스+사기가 경기 중 능력치를 조정했고, FC 27은 폼·사기·경기 체력으로 OVR 자체가 변하는 "Dynamic OVR"로 바뀌었다(3자 가이드 기준).

### Cited Findings
- FC 24 케미: 선수당 최대 3점, 선발 11명 합 최대 33점. 클럽 링크 2/4/7명 → 1/2/3점, 리그 3/5/8명 → 1/2/3점, 국적 2/5/8명 → 1/2/3점(가이드 한 곳). 감독은 국적 또는 리그가 같으면 +1(최대 1). 아이콘은 모든 리그에 리그 케미 +1, 같은 국적에 +2 — [GGRecon FC 24 chemistry explained](https://ggrecon.com/guides/fc-24-chemsitry-explained), [Charlie INTEL FC 24](https://charlieintel.com/ea-sports-fc/ea-sports-fc-24-ultimate-team-chemistry-system-explained-261204), [FIFAUTeam FC 24](https://fifauteam.com/fc-24-chemistry/). 한 가이드는 클럽 임계값을 다르게 적어 출처 간 불일치 있음.
- 케미 점수 1/2/3점에 따라 활성 케미 스타일의 능력치가 소/중/대(full) 부스트 — [GamesRadar FC 24 chemistry styles](https://gamesradar.com/fc-24-chemistry-styles), [Charlie INTEL FIFA 23 chem styles](https://www.charlieintel.com/fifa-23/fifa-23-ultimate-team-chemistry-styles-explained-all-chem-style-stat-increases-207241/)
- **교체 투입 선수는 케미 능력치 상승 없이** 플레이 — 위 검색 요약(FC 24 가이드들) 및 [FUT.GG FIFA 23 new chemistry system](https://fut.gg/news/new-fifa23-chemistry-system-explained-how-to-gain-chemistry-points-and-more)
- FC 24 커리어: "샤프니스와 선수 사기가 경기 중 능력치에 영향", 샤프니스가 높을수록 경기력이 좋음; 코치 지식에 따라 증가폭이 정해지고 능력치 증가는 경기 중에만, 배정 포지션에 따라 적용 — 검색 요약(EA Pitch Notes FC 24 Career deep dive 계열; 본문 직접 fetch 시 해당 내용 미확인) [EA Pitch Notes FC 24 Career deep dive](https://www.ea.com/games/ea-sports-fc/pitch-notes/news/fc-24-career-deep-dive)
- FC 27 커리어 "Dynamic OVR": 폼·사기·경기 체력 3요소가 OVR을 변동, 사기 변동성 증가(시즌 내내 행복 유지 불가), 포지션별 보정 — [Dexerto FC 27 Dynamic OVR](https://www.dexerto.com/wikis/ea-fc-27/dynamic-overall-explained/), [The Spike FC 27 Dynamic OVR guide](https://www.thespike.gg/fifa/ea-fc-27/dynamic-ovr-guide), [AllThings.how](https://allthings.how/ea-fc-27-career-mode-how-dynamic-ovr-ratings-work/) (모두 3자 가이드)
- FC 26 사용자 보고: 체력 100%여도 샤프니스 80% 이상 불가 버그 의심 (공식 확인 아님) — [EA Forums](https://forums.ea.com/discussions/fc-26-game-modes-en/fc26-career-mode---sharpness-bug/12713697)

### Inferences
- EA의 방향은 "숨은 감쇠"보다 **보이는 보너스**(케미 = 보너스 해금, Dynamic OVR = 보이는 숫자 변화)다. 플레이어가 원인을 이해하기 쉬운 구조.
- 케미가 AI 행동(포지셔닝·움직임)을 바꾼다는 공식 근거는 찾지 못함. 현 체계에서 케미의 경로는 능력치 보너스뿐으로 보임.

### Gaps
- 케미 스타일 소/중/대 부스트의 정확한 수치(EA 공식 표)는 이번에 확인 못 함.
- 케미 → AI 행동 영향 여부: 공식 자료 없음.
- FC 27에서 샤프니스 존속 여부, 사기의 정확한 OVR 영향 폭: 공식 자료 없음.

## Q3. eFootball(PES): 팀 스피릿, 컨디션·폼 화살표

### Takeaway
컨디션 화살표(5단계)는 선수 능력치 전체에 걸친 **완만한 승수**로 설명되고, 팀 스피릿(최대 99)은 전술 지시 수행·연계 품질에 영향을 주는 팀 단위 수치로 설명된다. 대부분 팬 가이드이며 버전마다 다르다.

### Cited Findings
- 컨디션 5단계: 최상(청록 화살표) 보너스, 노랑 변화 없음, 빨강 최악 — [GamingOnPhone eFootball 가이드](https://gamingonphone.com/guides/efootball-a-guide-to-live-update-rating-player-form-and-conditioning)
- 컨디션은 경기별 샤프니스, 한 능력치의 큰 보너스가 아니라 **능력치 전반의 완만한 배율**; 매일 리셋된다는 주장(미검증) — [GameMarket eFootball condition arrows](https://gamemarket.gg/news/efootball/efootball-condition-arrows-explained-how-player-form-works)
- PES 시절(마스터 리그): 연속 좋은 결과 → 사기 상승 → 화살표 상승, 좋은 피지컬 트레이너가 낮은 화살표를 줄임 — [PESMastery Condition](https://pesmastery.com/pes-condition/), [PESMastery](https://pesmastery.com/?p=500)
- 팀 스피릿(최대 99): 전술 지시 수행도와 연계에 영향, 높을수록 결과가 좋음 — [Operation Sports PES 2021 Team Spirit Guide](https://operationsports.com/pes-2021-team-spirit-guide-understand-chemistry-on-a-new-level), [PESMastery MyClub guide](https://pesmastery.com/?p=7548)
- 역할에 맞는 선수 배치 시 팀 스피릿 상승, Team Strength 수치에 반영(미검증) — [GameMarket Team Strength](https://gamemarket.gg/news/efootball/efootball-team-strength-explained-how-squad-rating-is-calculated)

### Inferences
- PES/eFootball은 컨디션을 **실제 선수 폼(라이브 업데이트)**과 연결해 "현실 반영" 감각을 준다. 개인 = 컨디션 배율, 팀 = 스피릿(지시 수행 품질)으로 층이 분리되어 있다.

### Gaps
- Konami 공식 수치 없음. 화살표별 배율(%)과 팀 스피릿의 엔진 영향 경로는 확인 불가.

## Q4. 실제 데이터: 홈 어드밴티지 크기·추세, 무관중(COVID) 경기

### Takeaway
홈팀 승점 점유율은 잉글랜드 1부에서 19세기 말 약 68%에서 1990~2000년대 약 61%로 하락(2차 요약). 분데스리가 2016/17~2023/24 홈승 45.5%·원정승 30.2%·무 24.3%, 홈 득실 +0.38골. 무관중 경기는 대체로 홈 어드밴티지를 줄였고(특히 분데스리가 2019/20), 핵심 경로는 **심판 판정(카드) 편향 감소**이지만 리그별·연구별로 결과가 엇갈린다.

### Cited Findings
- Pollard 방식(홈팀이 얻은 승점 비율, 50% = 어드밴티지 없음): 잉글랜드 1부 1888~1900 67.9% → 1992~2002 60.6% (2차 블로그 요약, 원문 미확인); Pollard(1986)는 1984년까지 매우 안정적이었다고 보고, 이후 유의하게 감소 — [The Factball: How relevant is home advantage](https://thefactball.substack.com/p/home-advantage), 감소 원인으로 TV 중계 등장 일부 기여 주장 — [Reading Univ. 경제 토론문](https://www.reading.ac.uk/web/files/economics/emdp202014.pdf), 4개 리그 모든 수준에서 유의한 감소 — [Tinbergen 20025](https://papers.tinbergen.nl/20025.pdf)
- 분데스리가(Schank·Voigt·Orthey 2024, arXiv 2411.12509): 2016/17~2023/24 홈승 45.5%, 원정승 30.2%, 무 24.3%, 홈팀 평균 +0.38골. 2019/20 무관중 마지막 9라운드: 홈-원정 득실차가 2018/19 대비 약 0.45~0.52골 감소(평균 약 −0.2, 즉 홈 열세), 홈승 확률 −13.1%p, 원정승 +10.1%p. 그러나 2020/21(거의 무관중)은 2018/19와 거의 동일(유의한 변화 없음). 2021/22 관중 25~75% 경기는 75% 초과 경기 대비 홈 득점 +0.38, 홈승 +11.8%p. 2022/23·2023/24는 코로나 이전 수준(2023/24 득실 약 +0.4)으로 복귀. VAR 도입 시즌 홈승 −5.8%p였으나 비유의 — [arXiv 2411.12509](https://arxiv.org/html/2411.12509v1)
- 같은 논문 인용: Fischer & Haucap(2021)은 1부에서만 홈승 감소, 2·3부는 아님; 리그를 따로 분석한 연구는 대부분 분데스리가 등 일부에서만 감소를 발견 — [arXiv 2411.12509](https://arxiv.org/html/2411.12509v1)
- Bryson·Dolton·Reade·Schreyer·Singleton(2020, Economics Letters): 17개국 6,481경기(무관중 1,498경기). 무관중이면 원정팀이 받는 카드가 줄어 홈·원정 경고 차가 약 **1/3장** 좁혀짐 → 홈 어드밴티지 감소 — [UCL Discovery PDF](https://discovery.ucl.ac.uk/10115454/1/Bryson%20et%20al%202020%20Econ%20Letters.pdf)
- 다른 연구: 무관중 시 원정팀에 대한 파울·경고·퇴장 편향은 사라졌지만, "관중이 홈 어드밴티지의 주된 요인은 아닌 것 같다"고 결론 — [PMC8011816](https://pmc.ncbi.nlm.nih.gov/articles/PMC8011816)
- 체계적 문헌고찰(Management Review Quarterly, 2023, 26편): 무관중 경기가 홈 어드밴티지에 상당한 영향, 경로는 심판 편향 감소와 응원 부재. 연구 간 불일치는 방법론·리그 차이·작은 표본 때문 — [Springer s11301-021-00254-5](https://link.springer.com/10.1007/s11301-021-00254-5); Krawczyk(2020) 등은 변화 없음 보고 — 같은 검색 요약
- Leitner & Richlan: 유럽 리그 무관중 경기에서 홈팀 경고가 유의하게 증가 — 검색 요약(원문 미확인) [Frontiers 2021](https://www.frontiersin.org/journals/sports-and-active-living/articles/10.3389/fspor.2021.720488/epub)

### Inferences
- 게임용 근사: 정상 관중 홈 어드밴티지 ≈ 득실 +0.3~0.4골/경기, 홈 승점 점유율 ≈ 58~61%. 그 일부는 **판정(카드·파울)** 경로라 엔진에서 "능력치 보정"보다 "판정 확률 보정"으로 넣는 것이 현실과 맞다.
- 관중 수(점유율)에 비례하는 연속 보정이 2021/22 결과와 일치.

### Gaps
- 리그 전체를 통합한 COVID 메타분석 효과크기(승점·득실)는 확인 못 함(PMC8416626 접근 차단).

## Q5. 실제 데이터: 날씨·피치·고도

### Takeaway
고도는 강한 효과(1,000m 차이당 약 0.5골)가 보고되지만 팀 전력 혼입 논쟁이 있다. 날씨가 득점 총량에 미치는 효과에 대한 고품질 동료평가 연구는 찾지 못했다.

### Cited Findings
- McSharry(2007, BMJ): 남미 10개국 1,460개 A매치, 팀 전력 통제 후 고도 차 1,000m당 고지대 팀 득실 약 +0.5골. 같은 고도 홈승 확률 0.537 → 고도 차 3,695m(볼리비아 vs 브라질) 0.83. 고지대 팀은 저지대 원정에서도 우위 — [ScienceDaily 2007](https://www.sciencedaily.com/releases/2007/12/071221094837.htm), [EurekAlert](https://www.eurekalert.org/pub_releases/2007-12/bmj-haf121907.php)
- 반론(Williams, NESN 요약): 결과는 고도보다 팀 전력 차 반영; 같은 팀 비교 시 볼리비아 승률이 라파스에서 45%p 상승 — [NESN 2011](https://nesn.com/2011/03/how-high-is-too-high-for-soccer)
- 기상(2024, Biology of Sport, 챔스): 기온·습도·바람 중 "역습 슈팅"이 기온에만 유의하게 영향(OR 0.55, 95% CI 0.30~0.98). 쾌적 기온대 10.6~22°C 제안 — [PMC11474995](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11474995/)
- 잉글랜드 하부 리그: 강우 후 득점 증가, 따뜻하고 젖은 날 골·코너 최다, 건조·추운 날 최소 (언론 분석, 비동료평가) — [Racing Post, Kevin Pullein](https://www.racingpost.com/sport/kevin-pullein/some-football-forecasts-should-wait-until-you-have-studied-the-weather-forecast/407819)

### Inferences
- 날씨는 결과 승수보다 **플레이 스타일(템포·패스 정확도·체력 소모)** 보정으로 작게 넣는 것이 근거 수준에 맞다.

### Gaps
- 비·눈·바람의 득점·패스 정확도 정량 효과(동료평가) 미확인.

## Q6. 실제 데이터: 퇴장·모멘텀·핫핸드

### Takeaway
퇴장은 명확한 효과(상대 득점 강도 대폭 상승)가 있다. 반면 "득점 후 모멘텀"·핫핸드는 축구 데이터에서 대체로 확인되지 않으며, 베팅 시장(사람)은 모멘텀이 있다고 믿는다.

### Cited Findings
- Ridder·Cramer·Hopstaken(1994): 네덜란드 1·2부 1991/92 340경기, 퇴장 후 11명 팀 득점 강도 **+88%**, 10명 팀 감소(후속 논문 인용) — [Červený·van Ours·van Tuijl, IZA DP10174](https://docs.iza.org/dp10174.pdf)
- Caliendo & Radic(2006, 월드컵): 퇴장 시점에 따라 효과가 다르고 전반 종료 이후 퇴장은 효과 소멸 — [IZA DP2158](https://repec.iza.org/dp2158.pdf); Červený 외: 퇴장은 해당 팀 득점률 하락, 상대 득점률 상승 — [Springer s00181-017-1287-5](https://link.springer.com/10.1007/s00181-017-1287-5)
- 분데스리가 베팅(2022): 동점골 직후 팀에 베터들이 약 40% 더 베팅하지만, 득점 순서가 평균 승률·배당에 영향을 준다는 증거 없음 — [arXiv 2211.06052 Gambling on Momentum](https://www.arxiv.org/pdf/2211.06052)
- EPL(Parsons & Rohde): 연승 후 오히려 반전, 연무·연패·경기 내 득점에는 모멘텀 증거 없음; "모멘텀은 사후 라벨에 가깝다" — [Griffith repository](https://research-repository.griffith.edu.au/items/0675b98f-a106-4a29-812f-6eaed7800ee3)
- Baert: 하프타임 직전 골은 승리 확률을 높이지 않음 — [Skeptoid 요약](https://skeptoid.com/blog/2016/06/19/goal-before-break-myth/)

### Inferences
- 결정론 엔진에서 "득점 후 사기 버프" 같은 경기 내 모멘텀 보정은 실제 근거가 약하므로 **작게 하거나 없애고**, 연출(관중 소리·하이라이트 선택)로만 체감시키는 편이 현실적.

### Gaps
- 경기 내 연속 공격 기반 모멘텀(xT 흐름)의 정량 연구는 이번에 수집 못 함.

## Q7. 실제 데이터: 감독 교체 효과

### Takeaway
시즌 중 감독 교체 직후 "반등"은 대부분 **평균 회귀**로 설명되며, 비교군(부진했지만 교체하지 않은 팀)도 비슷하게 회복한다.

### Cited Findings
- Bruinshoofd & ter Weel(2003, 에레디비시): 평균 회귀 통제 후 교체 효과 없음; Curtis 외(1986), Cannella & Rowe(1995), Koning(2003)도 같은 결론 — [Silesian Univ. Management Papers 2025 문헌 정리](https://managementpapers.polsl.pl/wp-content/uploads/2025/04/218-Stachniak-Wolak.pdf)
- ter Weel 1986~2004 18시즌: 경질 전후 하락·회복 패턴이 경질하지 않은 경우에도 거의 동일 — [Soccer Analytics Substack (2차)](https://socceranalytics.substack.com/p/is-the-new-manager-bounce-really)
- van Ours & van Tuijl(2016, 네덜란드): 평균 회귀에 지배됨 — [De Economist s10645-016-9277-0](https://link.springer.com/article/10.1007/s10645-016-9277-0)
- Besters·van Ours·van Tuijl(2016, EPL): 성공·역효과 교체가 섞여 평균적으로 시즌 중 교체는 효과 없음; 프랑스·독일·이탈리아·스페인 8개 리그 연구도 평균 효과 없음 — 같은 검색 요약 ([Tinbergen 25054](https://papers.tinbergen.nl/25054.pdf))

### Inferences
- 게임에서 "새 감독 바운스"를 넣는다면 단기·소폭(선발 재경쟁에 따른 개인 동기)으로 하고, 팀 결과의 큰 보정은 근거가 약하다.

### Gaps
- 바운스의 단기(첫 3~5경기) 효과 크기 수치는 원문 미확인.

## Q8. 실제 데이터: 조직력(함께 뛴 시간)·안정성

### Takeaway
"함께 뛴 시간" 자체는 전력 통제 후 경기 결과에 독립 효과가 거의 없고, **성공적인 상호작용(패스 성공 이력)**이 있는 공유 경험이 패스 성공을 예측한다(2025 연구). 타 종목 포함 연구는 "과거 함께 거둔 성공"이 승리 확률을 높인다고 보고.

### Cited Findings
- Le Coz·Carling·Houde·Imbach(2025): 19,721경기에서 FIFA 랭킹·전력 포함 시 공유 경험의 독립 영향 없음; 1,602경기 패스 수준에선 공유 경험+과거 성공 상호작용이 패스 성공 예측; 단기(20일)·장기(12개월) 창 모두 예측력, 지수 감쇠 가중의 추가 이득 없음 — [Reassessing the impact of shared experience in football](https://library.kab.ac.ug/Record/doaj-art-73fa02071c92416dad6ac592ea1c885d)
- 농구·축구·야구·크리켓: 팀원 간 과거 공동 성공이 개인 재능을 넘어 승리 확률을 유의하게 높임 — [Northwestern NOSH prior shared success](https://nosh.northwestern.edu/wp-content/uploads/2020/10/prior-shared-success.pdf)
- 럭비 월드컵: 누적 공동 선발로 측정한 집단 경험이 성과와 연결 — [Frontiers 2019 fspor.2019.00044](https://www.frontiersin.org/journals/sports-and-active-living/articles/10.3389/fspor.2019.00044/pdf)
- 유럽 축구 팀 안정성: 독립적이지만 약한 효과(스니펫 기반) — [Van Vugt, Does stability foster team performance](https://www.professormarkvanvugt.com/images/files/DoesStabilityFosterTeamPerformance.pdf)
- 패스 기반 케미: 선수 간 공유 패스가 승리와 연관, 설명력은 작음 — [American Soccer Analysis Chemistry 101 (2025)](https://www.americansocceranalysis.com/home/2025/1/12/chemistry-101)

### Inferences
- FM식 "조직력 = 함께한 기간"보다 "**선수 쌍별 성공 상호작용 누적**"이 실증과 맞다. 효과 위치도 결과 승수가 아니라 **패스·연계 성공 확률**의 작은 보정.

### Gaps
- 효과 크기(%p)는 초록 수준이라 미확인.

## Q9. 실제 데이터: 큰 경기 압박·초킹

### Takeaway
압박은 승부차기에서 가장 뚜렷하다: 실축 = 패배인 킥 성공률 약 52% vs 성공 = 승리인 킥 약 93%. 대회 중요도가 높을수록 성공률이 낮다.

### Cited Findings
- Jordet 외(2007): 1976~2004 월드컵·유로·코파 41개 승부차기 409킥, 실축 시 패배 상황 52%, 성공 시 승리 상황 93%; 전체 약 78.9% — [Yahoo/The Conversation: How to win a penalty shoot-out](https://sg.news.yahoo.com/win-penalty-shootout-according-science-173327986.html)
- 대회 중요도: 월드컵 71% vs 유로 85%, 코파 82% — 같은 출처 및 [BPS Research Digest](https://www.bps.org.uk/research-digest/how-practise-penalty-shoot-outs)
- 준비 시간: 0.5초 미만 정지 후 출발 63% vs 더 긴 정지 81% — [Yahoo/The Conversation](https://sg.news.yahoo.com/win-penalty-shootout-according-science-173327986.html)
- 유럽 약 5만 개 페널티(최근 11시즌): 경기 중 PK 성공률 약 82.06%, 승부차기는 이보다 낮음 — [arXiv 2310.04797](https://arxiv.org/pdf/2310.04797v1)

### Inferences
- 압박은 "모든 행동 −X%"가 아니라 **고압 순간의 단일 행동(PK·결정적 슈팅)**에 집중되고, 개인 차(침착성)로 크게 갈린다.

### Gaps
- 일반 경기(오픈 플레이)에서 큰 경기 압박 효과를 측정한 연구는 이번에 수집 못 함.

## Road To PRO에 적용할 점

(선수 1명 커리어, 사기·평판·라이프 이벤트가 경기력에 연결되는 설계, 결정론 엔진 기준. 아래는 리서치 기반 제안이며 기획서 확정 수치가 아님 — 스펙에선 `[제안]`/`[미정]`으로.)

1. **보정은 승수·작게·보이게**: FM·eFootball 모두 사기·컨디션을 능력치 실효값의 완만한 배율로 다루고, EA FC 27은 Dynamic OVR로 아예 숫자를 보여 준다. 우리 UI(큰 숫자 카드)에 "오늘 실효 능력치 = 기본 × 사기·컨디션·라이프 보정"을 보여 주면 원인 이해가 쉬워진다. 내부 0~100 기준 보정 폭 상한을 스펙에서 정해야 함([미정]).
2. **판정 경로 분리**: 실제 홈 어드밴티지(득실 +0.3~0.4골, 홈승 약 45% vs 원정 30%)의 상당 부분이 카드·파울 판정 편향(무관중 시 경고 차 약 1/3장 축소). 엔진에서 홈 보정을 "능력치"와 "판정 확률(파울 콜·카드)"로 나누고, 관중 점유율에 비례시키면 COVID·관중 변화 이벤트도 자연스럽게 표현된다. 대량 시뮬 평균 득점 2.5~3.0 목표와 함께 홈승/무/원정승 비율(약 45/25/30)도 검증 지표로 추가할 만함.
3. **모멘텀 버프는 약하게**: 실데이터상 득점 후 모멘텀·핫핸드 근거가 약하다. 결정론 엔진에서 경기 내 사기 급변 보정은 최소화하고, 체감은 하이라이트 연출(관중 소리)로. 반대로 **퇴장 효과(상대 득점 강도 약 +88%, 이른 퇴장일수록 큼)**는 확실하므로 팀 형태 레이어(10명 라인 재배치)로 반영.
4. **압박은 순간 집중형**: 압박·큰 경기 보정은 PK·결정적 찬스 같은 단일 고압 행동에만, 침착성/버릇(성향 14축)으로 개인 차를 크게. 승부차기 성공률(전체 약 79%, 상황별 52~93%)을 회귀 기준으로 쓸 수 있다.
5. **조직력 = 쌍별 성공 상호작용**: "같은 팀 기간"보다 주인공과 동료 간 성공 패스·연계 누적(20일·12개월 창)을 저장해 패스·연계 성공 확률을 소폭 보정. 이적 직후 적응 스토리렛과 자연스럽게 연결되고 결정론적으로 계산 가능.
6. **감독 교체는 개인 서사로**: 팀 결과 바운스는 평균 회귀 수준. 새 감독 = 주전 재경쟁·신뢰도 리셋 같은 **선수 개인 이벤트**로 다루는 편이 현실과 게임성 모두에 맞다.
7. **날씨·고도는 스타일 보정**: 근거가 약하므로 결과 승수보다 체력 소모·패스 정확도·볼 속도 같은 작은 물리/스타일 보정. 고도는 효과 크다는 연구(1,000m당 약 0.5골)와 반론이 공존 — 쓰더라도 체력 소모 경로로만.
8. **결정론 유의**: 사기·컨디션·관중·날씨 등 모든 보정 입력은 경기 시작 전 스냅샷으로 고정해 시드와 함께 저장(경기 전·후 저장 규칙과 일치). 경기 중 사기 변동을 넣는다면 10Hz 결정 틱에서 정수/고정소수 계산.
9. **FM-Arena식 검증 방법 차용**: 보정 효과를 확인할 때 다른 변수를 고정한 대량 시뮬(예: 5,760경기, RNG 잡음 약 ±1~1.5점 수준 참고)으로 "사기 최고 vs 최저" 승점 차를 측정해 의도한 크기인지 확인하는 테스트를 스펙 AC로 둘 수 있다.
