# 선수 컨디션·피로·체력·부상 위험·회복 모델링 (게임 + 스포츠과학)

조사일: 2026-11-11. 공개 자료만 사용 (디컴파일·추출 데이터 없음). 게임 관련 수치는 대부분 커뮤니티 테스트·팬 사이트라 **비공식 추정치**로 취급해야 한다.

## Q1. Football Manager: 컨디션 vs 경기 감각 vs 피로(jaded), Natural Fitness·Stamina, 부상 경향, 로테이션

### Takeaway
FM은 FM21부터 하나의 %였던 값을 **경기 컨디션(Condition)**과 **경기 감각(Match Sharpness)**으로 분리하고, 별도의 장기 누적 **피로(Fatigue/Jadedness)**와 부상 위험 툴팁(경기 부하·훈련 부하·부상 취약성·피로)을 추가했다. 커뮤니티 테스트는 "감각 영향 큼 > 컨디션 중간 > 피로는 과하지만 않으면 됨"으로 요약한다.

### Cited Findings
- FM21에서 처음으로 Match Condition과 Match Sharpness가 별개 항목이 됨. 감각은 실전 출전으로 오르고 결장 시 떨어지며, 장기 부상·장기 결장 시 35%까지 떨어질 수 있음 — [RealSport101 (FM21 conditioning overhaul)](https://realsport101.com/article/football-manager-202-conditioning-overhauled-ahead-of-release-match-sharpness-draining-fatigue)
- FM21 컨디션 툴팁 구성: Overall Risk of Injury, Match Load, Training Load, Injury Susceptibility, Fatigue. 피로(Fatigue)는 "부상 위험뿐 아니라 피로로 기량이 떨어지는 것"을 보여주는 신규 요소. 감각·컨디션 표시는 %에서 "Excellent/Great" 같은 서술형 라벨로 바뀜 (2020-11-05 FM21 퍼스트룩 스트림 공개) — [RealSport101 (FM21 condition/fatigue tooltip)](https://realsport101.com/football-manager/football-manager-21-condition-fatigue-sharpness-changes-tooltip-injury-risk/)
- 커뮤니티 테스트: 완전 휴식 1일이 Jadedness 값을 20포인트 줄이고, 1경기분 jadedness를 없애려면 약 3일 완전 휴식이 필요 (비공식, fm-arena) — [WebSearch 요약, fm-arena / Steam 토론](https://steamcommunity.com/app/2252570/discussions/0/4029095281637786919)
- EBFM 인용 커뮤니티 요약: "경기 감각은 성적에 매우 크게, 컨디션은 중간 정도로 영향, 피로는 과하지만 않으면 걱정 불필요". 감각 90% vs 100%가 승률 큰 하락과 연관 (구체 수치는 원문 미확인) — [fm-arena 댓글](https://fm-arena.com/find-comment/45840/)
- 실전 가이드: 보통 3경기 중 2경기 출전이 이상적이고 Natural Fitness에 따라 가감. 친선전·짧은 교체 출전으로 감각 유지. 휴식 외 훈련은 감각 감소를 약간 늦춤. 피로 데이터는 스포츠 사이언티스트를 통해 확인 — [Steam FM24 토론](https://steamcommunity.com/app/2252570/discussions/0/4029095281637786919)
- Natural Fitness(1~20, 공개): 커리어 전반·경기 사이의 기본 체력 수준. Stamina: 경기 중 최고 수준을 더 오래 유지. 둘은 직접 연결됨 — [footballmanagerblog 신체 능력치 가이드 (2024)](https://www.footballmanagerblog.org/2024/09/football-manager-physical-attributes-guide.html); [passion4fm 능력치 설명](https://www.passion4fm.com/football-manager-player-attributes/)
- Injury Proneness는 숨김 능력치(1~20). 14 이상이면 부상 가능성이 높음. 스카우팅 도구나 부상 이력·스태프 리포트로 추정 — [FMScout 부상 관리 가이드](https://www.fmscout.com/a-fm-guide-keeping-injuries-under-control.html?pg=9)
- FM19 가이드: Bravery와 Natural Fitness가 부상 경향에 영향을 주는 주요 능력치로 언급됨. 피지오 능력치는 피트니스 테스트·메디컬·부상 경향 평가에 사용 — [FMBrotherhood FM19](https://fmbrotherhood.com/football-manager-guides/injury-proneness-all-you-need-to-know-fm19/)
- 공식 블로그에 "Fighting Fatigue and Preventing Injuries in FM21" 글이 존재하나, 페치 시 본문을 확보하지 못함 — [footballmanager.com/node/474](https://footballmanager.com/node/474)

### Inferences
- FM 구조를 요약하면 3층: ①**단기 컨디션**(경기 중 소모, 며칠 내 회복) ②**경기 감각**(출전 빈도로 오르내리는 느린 변수, 수주 단위) ③**누적 피로/jaded**(연속 부하로 쌓이고 휴식일로만 감소). 부상 위험은 여기에 부하·취약성(숨김)을 더해 계산되는 파생값으로 보인다.
- "감각 > 컨디션 > 피로"라는 커뮤니티 결론은 게임 밸런스상 "결장하면 손해"를 크게 만들어 로테이션 딜레마를 형성한다. 선수 1인 커리어 게임에서는 "출전 시간 확보 vs 몸 관리" 갈등으로 쓰기 좋다.

### Gaps
- FM24~FM26/FM27의 피로 표시 변경(예: 신체 상태 아이콘 체계) 공식 설명은 확보하지 못함. SI 공식 수치(회복률·감각 감소율)는 공개되지 않음.
- 피로가 경기 중 의사결정·능력치에 주는 정량 효과는 공식 자료 없음. 커뮤니티 승률 테스트의 원 수치도 미확인.

## Q2. EA SPORTS FC(FIFA): 스태미나 바, 스프린트 피로, 샤프니스(FC 25 커리어), 피트니스·부상

### Takeaway
FC는 경기 중 **스태미나 바**(Stamina 능력치가 감소 속도 결정)와 커리어 모드의 경기 간 **Fitness**·**Sharpness** 2축을 쓴다(샤프니스는 FC 25 훈련 개편과 함께 강조). 공식 수치는 거의 공개되지 않았고, 저스태미나가 패스·슈팅 정확도에 미치는 효과도 공식 확인 자료가 없다.

### Cited Findings
- Stamina 능력치는 경기 중 피트니스가 떨어지는 속도를 결정, 높을수록 느리게 감소 (FUT 팬 가이드, 커리어에 그대로 적용되는지는 불확실) — [fifauteam FC 25 Fitness](https://fifauteam.com/fc-25-fitness-guide/)
- FC 27(팬 사이트): Competitive Gameplay에서 **직접 조작 선수**는 스태미나와 무관하게 스프린트 속도·가속을 유지하고, AI 동료는 피로에 비례해 감소. Authentic Gameplay는 전통 방식(스태미나 저하가 직접 영향) — [fifauteam FC 27 Stamina/Fatigue](https://fifauteam.com/fc-27-stamina-fatigue/)
- FC 26/27 Pro Clubs 이용자들이 "피로가 스프린트·압박·패스에 거의 영향 없다"며 의미 있는 스태미나 관리 복원을 요구 (이용자 의견) — [EA Forums FC 26 피드백](https://forums.ea.com/discussions/fc-26-feedback-en/restore-meaningful-stamina-management-in-pro-clubs/13583130)
- FC 25 커리어: 퍼포먼스 중심 훈련 플랜이 샤프니스를 올리고, 완전히 샤프한 선수는 경기마다 약간의 스탯 부스트. 대가로 피트니스 소모 — [Dexerto FC 25 훈련 플랜](https://dexerto.com/ea-sports-fc/best-training-plans-ea-fc-25-career-mode-sharpness-fitness-2913374)
- 자동 훈련 가이드: 샤프니스 70 미만이면 퍼포먼스 훈련, 피트니스 80 미만이면 스태미나 훈련으로 자동 전환되는 설정 예 (팬 가이드) — [mitchcactus FC 25](https://mitchcactus.co/blog/ea-sports/best-fc-25-training-plans-to-enhance-sharpness-fitness/)
- 비공식 조언: 샤프니스 60 아래로 떨어뜨리지 말 것 — [Exitlag 블로그](https://www.exitlag.com/blog/fc25-career-mode-potential/)
- FC 25 버그 보고: "fixed rate" 회복 설정이 샤프니스를 회복시키지 못해 시즌 내내 낮게 고정; 샤프니스 10에 고정돼 선수가 굼뜬 사례; FC 26에서 전원 80 고정 사례 — [EA Forums FC 25 sharpness](https://forums.ea.com/discussions/fc-25-game-modes-en/fc25-sharpness/7856987); [EA Forums fixed rate](https://forums.ea.com/discussions/fc-25-bug-reports-archive-en/players-struggling-to-regain-fitness-with-fixed-rate-training-plans/7865790); [EA Forums FC 26 80 sharpness](https://forums.ea.com/discussions/fc-26-game-modes-en/80-sharpness-bug-in-career-mode/12820232)
- EA 공식 FC 25 Career Mode Deep Dive는 Live Start Points, Snapshots, 여자 커리어, Tactical Presets, Development Plans 2.0을 다루며, 검색 발췌에서 샤프니스 수치 설명은 확인되지 않음 — [EA Pitch Notes FC 25 Career Deep Dive](https://www.ea.com/en/games/ea-sports-fc/fc-25/news/pitch-notes-fc-25-career-mode-deep-dive)

### Inferences
- FC의 Fitness(경기 간 회복되는 체력)와 Sharpness(출전·훈련으로 쌓이는 경기 감각)는 FM의 Condition/Sharpness와 거의 같은 2축 구조. 업계 표준 패턴으로 볼 수 있다.
- 액션 게임은 조작감 때문에 "조작 선수에는 피로 페널티 약화"를 택한다. 하이라이트만 보는 Road To PRO는 이런 타협이 필요 없고, 시뮬 쪽에서 사실적 피로를 적용해도 된다.
- 샤프니스 고정 버그 사례들은 "자동 회복 규칙"이 경계 조건에서 쉽게 고착된다는 교훈 — 결정론 테스트로 장기(시즌 단위) 수렴·발산을 검증할 필요.

### Gaps
- EA 공식 스태미나 감소율, 저스태미나 시 능력치 감쇠 공식, 커리어 부상 확률 공식은 공개 자료 없음.

## Q3. eFootball: 컨디션 화살표(폼)와 스태미나

### Takeaway
eFootball은 경기 전 **컨디션 화살표 5단계**(경기 단위로 고정, 다수 능력치에 소폭 가감)와 경기 중 **스태미나 게이지**를 분리한다. 화살표는 휴식·로테이션이 아닌 확률 추첨이며 "Condition Fitness"(폼 유형 등급)가 변동성을 정한다.

### Cited Findings
- 화살표 5단계: 녹색↑(최고, 능력치 소폭 상승) / 연두↗ / 노랑→(기준, 효과 없음) / 주황↘ / 빨강↓(카드보다 저조). 정확한 %는 공개 안 됨, 커뮤니티는 양 극단 차이를 "능력치당 몇 포인트"로 추정. 여러 능력치에 동시 적용 (2026-09-05 기사, 버전 명시 없음) — [GameMarket eFootball 컨디션 화살표](https://gamemarket.gg/news/efootball/efootball-condition-arrows-explained-how-player-form-works)
- 화살표는 가중 범위에서 추첨되며 최근 결과·휴식·로테이션·훈련 영향 없음. Condition Fitness 등급이 높을수록 좋은 컨디션 빈도 ↑, 최악 빈도 ↓. 컨디션은 킥오프 전 고정·경기 중 불변, 스태미나는 꽉 찬 상태에서 달릴수록 감소 — 같은 출처
- 폼 유형: Inconsistent는 주황·빨강이 잦고, Unwavering은 가장 안정적. Legends·Epic·Big Time 등 일부 카드는 폼 영향 없이 항상 B — [gamingonphone eFootball 폼·컨디션 가이드](https://gamingonphone.com/guides/efootball-2024-a-guide-to-live-update-rating-player-form-and-conditioning/)
- PES 2019 공식 매뉴얼: 폼은 화살표, 스태미나는 게이지로 표시 — [Konami PES 2019 매뉴얼](https://dds.konami.com/games/manual/pes2019/PC/en/plan.html)

### Inferences
- eFootball의 "컨디션 = 무작위 + 성격(일관성) 등급"은 FM의 숨김 Consistency와 유사. Road To PRO의 성향·버릇 체계에 "기복(consistency)" 축이 있으면 경기 전 컨디션 추첨 분산에 쓸 수 있다(시드 기반이면 결정론 유지).

### Gaps
- Konami 공식 화살표별 능력치 보정 수치, 스태미나 감소율 미공개.

## Q4. 스포츠과학: 경기 내 피로 곡선, 기술 저하, 회복 시간, ACWR, 일정 밀집, 질병

### Takeaway
엘리트 축구에서 고강도 달리기는 **마지막 15분이 첫 15분보다 약 18~45% 적고**, 가장 격렬한 5분 직후 **일시적 피로**(평균 대비 −12%)가 나타난다. 경기 후 회복은 지표별로 24~72시간+ (스프린트는 빠르고, 점프·근력·CK는 72시간까지 잔존). 주 2경기 시 부상률은 주 1경기 대비 약 6배(25.6 vs 4.1/1000h), 회복 ≤4일 시 근육 부상 RR 1.32. ACWR >1.5는 위험 지표로 널리 쓰이지만 "0.8~1.3 스위트 스팟" 근거는 논쟁 중.

### Cited Findings
**경기 내 피로 (시간대별)**
- Mohr, Krustrup, Bangsbo (2003, J Sports Sci 21:519–528): 수준·포지션과 무관하게 고강도 달리기가 마지막 15분에 첫 15분보다 35~45% 적음. 고강도가 최고였던 5분 직후 5분은 경기 평균보다 12% 감소(일시적 피로). 톱 레벨은 중간 레벨보다 고강도 28%, 스프린트 58% 많음. 교체 투입 선수(n=13)는 마지막 15분 고강도 거리가 다른 선수보다 25% 많음. 고강도 거리는 Yo-Yo 테스트와 r=0.71 상관(VO2max와는 무관) — [QxMD/PubMed 12848386](https://read.qxmd.com/read/12848386/match-performance-of-high-standard-soccer-players-with-special-reference-to-development-of-fatigue)
- Bradley et al. (2009, EPL 2005–06, 28경기, n=370): 경기당 고강도 거리 — 윙 미드 3,138m, 중앙 미드 2,825m, 풀백 2,605m, 공격수 2,341m, 센터백 1,834m. 첫 15분 대비 마지막 15분 고강도 거리: 윙 미드 467→344m(−20%), 중앙 미드 534→429m, 풀백 481→389m, 공격수 438→348m, 센터백 278→276m. 초고강도 구간 사이 평균 회복 72초, 마지막 15분에 28% 더 김. 최고 강도 5분 직후 감소: 공격수 216→113m, 센터백 182→96m — [Sunderland SURE 3478](https://sure.sunderland.ac.uk/id/eprint/3478/)
- 별도 엘리트 연구: 경기 수준과 무관하게 마지막 15분 고강도 거리가 첫 15분보다 18% 적음(391 vs 478m), 경기 전체 고강도 약 2,520m — [academia.edu 12782253](https://www.academia.edu/12782253)

**기술·의사결정 저하**
- Rampinini et al. (2008, MSSE 40:934–942): 경기 피로와 짧은 고강도 활동 후 Loughborough 패스 테스트에서 오류 수·소요 시간 증가 → 짧은 패스 능력 저하 — [PubMed 18408603](https://www.pubmed.gov/18408603/); [PMC4208825 리뷰](https://pmc.ncbi.nlm.nih.gov/articles/PMC4208825)
- Rampinini 등 세리에A 분석: 전반 대비 후반에 볼 관여, 짧은 패스, 성공한 짧은 패스가 유의하게 감소(신체 지표 감소와 동반). 상위 팀 선수가 관여·짧은 패스 성공이 더 많음 — [Technical performance… Serie A](https://data.zhenzhi.com/details/1/868702635)
- 반대 근거: Harper et al. (2014, PLoS One)은 신체 피로가 있어도 세리에A 기술 지표의 약 3/4은 전후반이 비슷했다고 언급 → 기술 저하 증거는 혼재 — [Harper et al. 2014](https://eprints.hud.ac.uk/29346/1/Harper%20et%20al%202014%20-%20Plos%20One.pdf)

**경기 후 회복 시간**
- Nédélec et al. (2012, Sports Med 42:997–1015): 경기 후 피로 원인 = 탈수, 글리코겐 고갈, 근손상, 정신적 피로. 완전 회복 시간은 지표에 따라 24~72시간으로 크게 다름 — [HAL hal-01660950](https://hal-insep.archives-ouvertes.fr/hal-01660950); [Frontiers 2017 인용](https://www.frontiersin.org/articles/10.3389/fphys.2017.00831/pdf)
- Silva et al. (2018, Sports Med 메타분석): 급성 피로(<3h)와 잔존 피로(최대 72h) 구분. 스프린트는 72h에 회복, 점프(CMJ)는 72h에도 저하. 근손상 지표(CK) 72h에도 상승, 실제 11대11 경기의 CK 변화 효과크기 1.8 vs 시뮬 프로토콜 0.7. 햄스트링 기능·CMJ·DOMS·CK 모니터링 권고 — [Silva et al. PDF (up.pt)](https://cifi2d.fade.up.pt/files/acuteandresidual.pdf)
- 시뮬레이션 경기 연구(세미프로): CMJ는 72h에도 저하, RSI는 48h 저하·72h 회복, 스프린트는 24h에 회복, 최대 수의 근력은 72h에도 미회복(중추 피로 지속) — [lida 4044292](https://lida.sport-iat.de/ta/Record/4044292?lng=en)
- 후속(2019): 5.5 m/s 이상 달린 거리가 경기 후 24h 생화학·신경근 반응을 가장 잘 설명하는 외부 부하 변수 — [Sports Med Open s40798-019-0219-7](https://sportsmedicine-open.springeropen.com/track/pdf/10.1186/s40798-019-0219-7)

**부상률 기준값**
- Ekstrand, Hägglund, Waldén (2009/2011 BJSM, UEFA 부상 연구 7시즌): 4,483건 / 약 566,000시간 → 전체 8.0/1000h, 경기 27.5/1000h vs 훈련 4.1/1000h. 선수당 시즌 약 2.0건(25인 스쿼드 ≈ 시즌 50건은 파생 추정) — [BJSM 2009](https://bjsm.bmj.com/content/early/2009/06/23/bjsm.2009.060582.full.pdf)
- 2001–02 UCL 데이터: 전체 9.4/1000h, 경기 30.5, 훈련 5.8 — [Ovid BJSM 2005](https://www.ovid.com/journals/bjsme/pdf/00002412-200508000-00022~uefa-champions-league-study-a-prospective-study-of-injuries)

**일정 밀집**
- Dupont et al. (2010, AJSM 38:1752–8): 주 2경기 시 부상률 25.6/1000h vs 주 1경기 4.1/1000h (P<.001), 신체 퍼포먼스(거리·고강도)는 유의한 차이 없음 — [PubMed 20400751](https://pubmed.ncbi.nlm.nih.gov/20400751/)
- Carling et al. (2015)은 다른 분석에서 경기 부상(27.2 vs 33.8/1000h)·훈련 부상(4.1 vs 6.2)에 유의차 없음 보고 → 밀집 효과는 연구마다 다름 — [Thieme 인용](https://thieme-connect.de/products/ejournals/html/10.1055/a-1028-7600)
- Bengtsson, Ekstrand, Hägglund (2013, BJSM, UCL 27팀 11시즌): 리그 경기에서 회복 ≤4일이 ≥6일보다 근육 부상률 높음 RR 1.32 (95% CI 1.15–1.51), 특히 햄스트링·대퇴사두. 팀 성적에는 영향 없거나 매우 제한적 — [PubMed 23851296](https://pubmed.ncbi.nlm.nih.gov/23851296/)
- 후속(BJSM 2017, 14시즌, 13만+ 경기 관찰): 총 경기 부상률은 밀집과 무관, 그러나 경기 간 ≥6일이면 근육 부상률이 낮음. 4~5일 회복은 ≤3일 대비 보호 효과 없음 — [BJSM 51/4/294.2](https://bjsm.bmj.com/content/51/4/294.2)

**ACWR (급성:만성 부하비)**
- Gabbett (2016, BJSM 50:273–280) "훈련-부상 예방 역설": 높은 부하에 적응한 선수가 낮은 부하 선수보다 부상이 적음 — [PMC4789704](https://pmc.ncbi.nlm.nih.gov/articles/PMC4789704)
- 코칭 자료: "스위트 스팟" 0.8~1.3, "위험 구간" >1.5 (2차 출처, 원문 수치 직접 확인 못함) — [SimpliFaster](https://simplifaster.com/articles/train-more-without-injuries/)
- 비판적 리뷰: 0.8~1.3 스위트 스팟이 부상 위험 감소와 관련된다는 근거 없음. 다만 ACWR ≥1.5를 준비 부족·부상 위험 증가로 보는 관례는 남아 있음 — [PMC8138569](https://pmc.ncbi.nlm.nih.gov/articles/PMC8138569/)
- 비판: ACWR을 훈련 개입으로 적용해 실제 부상이 줄었는지 검증한 연구는 없음 — [Sports Injury Bulletin](https://www.sportsinjurybulletin.com/acute-to-chronic-workload-ratio-fact-or-fiction/)

**질병**
- 남자 프로축구(노르웨이·스웨덴·잉글랜드·UCL, BJSM 2016): 선수당 시간손실 질병은 약 2시즌에 1회(부상은 시즌당 약 2회). 호흡기 58%, 위장관 38%. 4주 초과 중증 질병은 2% — [OSTRC 뉴스](https://klokavskade.no/en/about-us/news/illness-is-not-a-major-contributor-to-illness-in-male-professional-football); [scite BJSM 2015-095921](https://scite.ai/reports/role-of-illness-in-male-5k0pkV)
- 81명 3년 연구: 질병의 약 3/4이 상기도 증상, 출전 가능성에 미치는 영향은 작음 — [lida 4019239](https://lida.sport-iat.de/twm/Record/4019239?lng=en)
- 국제 남자 대회: 질병 7.7~16.5/1000 player-days, 약 40%가 결장으로 이어짐 — 위 scite 요약
- 육상 1,095명: 대회 전 4주 내 부상 증상이 대회 중 부상 위험을 2배 이상 높임(21.6%가 질병 증상 보고, 질병의 OR은 발췌에서 확인 못함) — [DOAJ BMJ Open SEM 2025](https://doaj.org/article/6b39e339c39f4ebcb944b1ca128ba97a)
- 2025 연구(165명, 39주): 한 주의 부상이 다음 주 건강 상태 악화 위험 약 5배 → "악순환" — 같은 검색 결과 [PMC9368705 등](https://pmc.ncbi.nlm.nih.gov/articles/PMC9368705)

### Inferences
- 경기 내 피로는 ①점진적 누적(90분에 걸쳐 고강도 출력 −20~40%) ②일시적 피로(격렬한 구간 직후 회복 가능) 두 성분이 필요. 교체 선수가 마지막 15분에 +25% 더 뛴다는 결과는 "교체 = 체력 리셋"의 근거.
- 포지션별 기본 고강도 부하(윙 미드 ≈ 센터백의 1.7배)는 경기 소모량 계수로 바로 쓸 수 있다.
- 회복 곡선은 단일 값보다 **빠른 성분(스프린트·컨디션, ~24~48h)**과 **느린 성분(근손상·근력, 72h+)** 2개 지수 감쇠로 모델링하는 것이 문헌과 맞다. 느린 성분이 남아 있을 때 출전하면 부상 위험 가중.
- 일정 밀집은 퍼포먼스보다 **부상(특히 근육)** 쪽에 효과가 뚜렷하다(Dupont, Bengtsson). 게임에서 "밀집 일정 = 능력치 크게 하락"보다 "부상 확률 상승"으로 표현하는 것이 현실적.
- 질병이 이후 부상 위험을 높인다는 축구 특화 정량 근거는 약하다. 이미 확정된 "감기 → 부상 확률 상승"은 게임 디자인 결정으로 두고, 배수는 근거 수치로 인용하지 말 것.

### Gaps
- 질병 후 부상 위험 배수(OR/RR)의 축구 특화 수치는 찾지 못함.
- 15분 구간별 스프린트 횟수 전체 표, Silva 2018의 시점별 효과크기 표는 원문 미확보.
- ACWR 원 연구(Hulin 등)의 구간별 상대위험 수치는 1차 출처로 확인하지 못함.
- 피로가 의사결정(판단 정확도)에 주는 정량 효과는 축구 경기 데이터로 확인된 수치를 찾지 못함.

## Road To PRO에 적용할 점

(전제: 순수 C# 결정론 엔진, 결정 10Hz·물리 100Hz, 내부 능력치 0~100, 감기→부상 위험 상승·드물게 질병 은퇴 확정, 건강 스펙 027. 아래 수치는 모두 `[제안]`이며 기획서 반영 전 사용자 승인 필요.)

1. **상태 변수 3층 분리 (FM/FC 공통 패턴)**: ① 경기 컨디션(0~100, 경기 중 소모·며칠 내 회복) ② 경기 감각(0~100, 출전 시간으로 상승·결장 시 감소, 수주 단위) ③ 누적 피로(연속 부하로 증가, 휴식일로만 감소). 부상 위험은 저장하지 말고 이 셋 + 숨김 취약성 + 일정 밀집에서 **파생 계산**(세이브 단순화·결정론 유리).
2. **경기 내 피로 = 점진 + 일시**: 10Hz 결정 틱마다 스태미나 소모를 해당 틱의 활동 강도(스프린트·압박)에 비례시키고 Stamina 능력치로 감속. 문헌 목표값으로 대량 시뮬을 보정: 마지막 15분 고강도 출력이 첫 15분 대비 −20~40%(Bradley/Mohr), 격렬 구간 직후 일시 −12% 후 회복. 포지션 부하 계수는 Bradley 2009 비율(윙 미드 3,138m ~ 센터백 1,834m) 참고. 교체 선수가 마지막 구간에 +25% 출력하는지 검증 테스트로 활용.
3. **피로의 효과 범위**: 피로는 주로 물리(최고 속도·가속·회복 시간 +28%)에 크게, 기술(짧은 패스 정확도)에는 작게-중간, 근거가 혼재하므로 기술 감쇠는 보수적으로. 의사결정 저하는 정량 근거가 약하므로 Utility 폴백의 잡음(temperature)을 약간 키우는 정도로 제한. 감쇠는 정수/고정소수점 곡선 테이블로 구현해 결정론 유지.
4. **회복은 2성분 감쇠**: 빠른 성분(컨디션, 24~48h)과 느린 성분(근손상, 72h+ 잔존 — Silva 2018·Nédélec 2012). 느린 성분이 남은 상태로 출전 시 부상 위험 가중. 일 단위 시뮬(커리어 루프)에서 계산하므로 경기 엔진과 분리 가능.
5. **일정 밀집 → 부상 위험(근육) 중심**: 경기 간 ≤4일이면 근육 부상 위험 ×~1.3(Bengtsson 2013 RR 1.32), 주 2경기 지속 시 더 크게(Dupont 25.6 vs 4.1). 퍼포먼스 하락은 작게(Dupont: 신체 퍼포먼스 유의차 없음). 기준 부상률 앵커: 경기 27.5/1000h, 훈련 4.1/1000h, 선수당 시즌 약 2건(Ekstrand) — 대량 시뮬 시즌 부상 수 검증 기준으로 사용.
6. **ACWR은 표시·경고용**: 최근 7일/28일 부하비 ≥1.5이면 "부하 급증" 경고 및 위험 가산. 0.8~1.3 "안전 구간"을 보상으로 설계하면 근거 논쟁이 있음을 기획서에 명시.
7. **경기 감각은 커리어 드라마 장치**: 결장·부상 복귀 후 감각 하락(FM: 장기 부상 시 35%까지)으로 "출전 시간 vs 몸 관리" 선택지를 만든다. 선수 1인 시점이므로 감독의 로테이션 결정(3경기 중 2경기 등)이 사기 이벤트와 연결되면 좋다.
8. **경기 전 컨디션 추첨(eFootball식)**: 시드 기반 추첨 + 성향/버릇의 "기복" 축이 분산을 결정 → 결정론 유지하면서 경기마다 변화. 휴식·수면·라이프 선택이 평균을 이동시키도록 하면 라이프 시스템과 연결된다.
9. **질병(027)**: 프로 축구 질병 빈도는 선수당 약 2시즌 1회, 호흡기 58%·위장관 38%, 4주 초과는 2% — 질병 발생률·기간 분포 앵커로 사용. "질병 → 부상 위험 상승" 배수는 축구 특화 근거가 없으므로 `[미정]`으로 두고 게임 디자인 값임을 명시. 질병 은퇴는 중증(2%) 중에서도 극히 일부로 제한.
10. **결정론·테스트**: 모든 피로·회복·위험 계산은 시드 RNG + 고정 순서, float 누적 대신 정수/고정소수점. FC 샤프니스 고착 버그 사례처럼 장기 루프가 고착·발산하지 않는지 "시즌 시뮬 N회 후 분포" 회귀 테스트를 둔다.
