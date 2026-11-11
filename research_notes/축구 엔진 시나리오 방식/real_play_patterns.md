# 실제 축구 플레이 패턴의 식별·분류·정량화 — 오프라인 시나리오 라이브러리의 근거

> 조사일 2026-11-11. 공개 자료만 사용. 결론에는 실존 선수 이름을 넣지 않는다(인용 출처 제목 제외).
> 도구 호출 예산(약 15회) 때문에 일부 논문은 원문 대신 초록·2차 요약만 확인했다. 확인 수준을 항목마다 적었다.

## (1) 분석·코칭에서 쓰는 패턴 분류 체계 (공격·수비·전환·세트피스)

### Takeaway
분석 데이터 쪽 분류는 "경기 상황(오픈 플레이/역습/세트피스/페널티)"과 "이벤트 타입(패스·크로스·드리블·슛…)"의 두 축이 사실상 표준이다. 코칭 쪽의 "4국면(공격·수비·공→수 전환·수→공 전환) + 세트피스" 틀은 널리 쓰이지만, 이번 조사에서는 UEFA 1차 문서로 확인하지 못했다.

### Cited Findings
- Wyscout 공개 데이터셋의 이벤트 분류: 7개 상위 타입(pass, foul, shot, duel, free kick, offside, touch). 서브타입 예시는 pass 2개(cross, simple pass), duel 4개(air duel, dribbles, tackles, ground loose ball), free kick 6개(corner, shot, goal kick, throw in, penalty, simple kick), touch 계열 3개(acceleration, clearance, simple touch) — [Pappalardo et al. 2019, Scientific Data](https://pmc.ncbi.nlm.nih.gov/articles/PMC6817871)
- 리그 집계에서는 상황을 "세트피스(페널티 제외)·코너·롱스로·역습(fast break)·하이 턴오버" 같은 단위로 나눠 보고한다. 예: 하이 턴오버 경기당 11.5회, 그중 슛으로 끝난 비율 14.9%. 패스 시퀀스 평균 9.6초, 시퀀스당 평균 전진 12.1m — [Premier League / Opta Analyst 2025-26 트렌드](https://www.premierleague.com/en/news/4426039/opta-analyst-on-long-balls-long-throws-key-tactical-trends-spotted-in-2025-26-season)
- 수비 전환 하위 패턴 "카운터프레싱"은 분석 전문가가 정의에 합의한 뒤 2만 건 이상을 라벨링해야 할 만큼 정의가 까다로운 패턴이다 — [Bauer & Anzer 2021, DMKD](https://link.springer.com/article/10.1007/s10618-021-00763-7) (초록·요약 기준)
- 학술 연구에서 국제 경기 득점의 약 70%가 오픈 플레이, 그중 약 20%가 역습이라는 오래된 기준치를 인용한다(2022 월드컵 득점 패턴 논문의 배경 서술, 1차 수치 아님) — [Frontiers in Sports and Active Living 2024](https://www.frontiersin.org/journals/sports-and-active-living/articles/10.3389/fspor.2024.1394621/pdf)

### Inferences
- 우리 카탈로그의 상위 분류(빌드업·역습·프레스·세트피스·수비 등)는 데이터 쪽 "상황 레이블"과 1:1로 대응시키면 검증 지표(상황별 득점 비중)를 바로 계산할 수 있다.
- 세부 패턴(컷백, 오버래핑, 3자 패스 등)은 이벤트 타입 + 구역 + 순서 조합으로 정의하는 것이 데이터 쪽 관행과 맞다.

### Gaps
- UEFA 코칭 교재·기술 보고서(Technical Report)의 국면·원칙 분류를 1차 출처로 확인하지 못했다. "4국면 + 세트피스" 틀은 통념으로만 남긴다.
- 아카데미 커리큘럼별 원칙 목록(폭·깊이·침투·지원·이동성 등)도 출처 미확보.

## (2) 이벤트·트래킹 데이터에서 패턴을 자동으로 찾는 방법

### Takeaway
주요 접근은 다섯 갈래다: ① 점유 구간(phase)을 잘라 궤적 거리(DTW)로 군집 → 군집 안에서 빈발 순차 패턴 채굴(Decroos 2018), ② 연속 패스의 "선수 순서" 모티프(flow motif), ③ 액션을 위치·방향 혼합모델로 원형(prototype)화(SoccerMix), ④ 세트피스 주자 런을 토픽모델식 특징 + 계층 군집으로 루틴화(Shaw & Gopaladesikan), ⑤ 전문가 라벨 + 지도학습으로 특정 패턴 탐지(카운터프레싱, Bauer & Anzer). 그래프 신경망 기반 생성·예측(TacticAI)도 코너에 적용됐다.

### Cited Findings
- **Decroos, Van Haaren, Davis (KDD 2018)** "Automatic discovery of tactics in spatio-temporal soccer match data": 이벤트 스트림을 플레이 국면으로 분할 → 공간·시간 성분으로 군집(DTW) → 각 군집에서 빈발 순차 패턴 채굴. 데이터는 2015/16 잉글랜드 1부 이벤트. 골킥·코너·세트피스에서 팀별 전략을 찾았고 도메인 전문가가 검토 — [the morning paper 요약](https://blog.acolyer.org/2018/10/12/automatic-discovery-of-tactics-in-spatio-temporal-soccer-match-data/), [포스터(DTW 군집)](https://tomdecroos.github.io/reports/tactics-poster.pdf), [원문 KU Leuven](https://lirias.kuleuven.be/retrieve/510838)
- **Flow motifs (Bekkers & Dabadghao 2019, JSA)**: 모티프 = 연속 패스에 관여한 선수 순서(예: ABAC). 선행 연구(Gyarmati 등)는 3패스 모티프 = 서로 다른 5가지 패턴. 데이터: 6개 유럽 리그 4시즌(2012–15), 8,219경기, 선수 3,532명, 팀 155개. 점유 모티프와 슈팅 직전 모티프를 구분 — [IOS Press](https://content.iospress.com:443/articles/journal-of-sports-analytics/jsa190290) (검색 요약 기준)
- 후속(Håland et al., JQAS 2020): 관여 선수가 적은 모티프는 덜 효과적, 패스가 넓은 면적을 쓸수록 슈팅으로 이어질 확률이 높다. Barbosa et al. 2022는 패스 구역을 더한 "공간 flow motif"로 확장 — [NTNU Open](https://ntnuopen.ntnu.no/ntnu-xmlui/handle/11250/2727710), [Barbosa CNA 2022](https://www.dcc.fc.up.pt/~pribeiro/pubs/pdf/barbosa-cna2022.pdf)
- **SoccerMix (Decroos, Van Roy, Davis, ECML PKDD 2020)**: 액션 타입별로 시작 위치를 가우시안 혼합으로 군집 → 각 위치 군 안에서 방향을 von Mises 혼합으로 군집 → "원형 액션" 집합. 코드 공개(github.com/ML-KULeuven/soccermix) — [DTAI 블로그](https://dtai.cs.kuleuven.be/sports/blog/soccermix:-representing-soccer-actions-with-mixture-models), [논문 PDF](https://tomdecroos.github.io/reports/ecml_2020.pdf)
- **Shaw & Gopaladesikan (MLSA 2020) "Routine Inspection: A Playbook for Corner Kicks"**: 공격 선수들의 동기화된 런(루틴)을 처음으로 상세 분석. 토픽모델링 방식으로 런을 특징화해 코너를 분류하고, 팀별 루틴을 식별·검색. 후속 논문은 이 방법이 응집형(Agglomerative) 군집을 쓴다고 서술. 수비 전략(대인·지역)도 다룸 — [Sloan 논문 페이지](https://www.sloansportsconference.com/research-papers/routine-inspection-a-playbook-for-corner-kicks), [Springer](https://www.springerprofessional.de/en/routine-inspection-a-playbook-for-corner-kicks/18671052)
- **Bauer & Anzer (DMKD 2021) 카운터프레싱 탐지**: 포지션+이벤트 데이터, 97경기에서 수비 전환 상황 2만 건 이상 라벨, 특징 134개, XGBoost AUC 87.4%. 분데스리가 6시즌 전체에 적용해 팀별 효과 측정. 검증한 코칭 경험칙 예: "터치라인 근처에서 카운터프레싱이 더 잘 된다" — [Springer](https://link.springer.com/article/10.1007/s10618-021-00763-7) (원문 접근 실패, 초록·검색 요약 기준)
- **TacticAI (DeepMind + 리버풀 FC, Nature Communications 2024)**: 코너킥 7,176개(잉글랜드 1부)로 학습한 기하 딥러닝 모델. 첫 수신자 예측·슈팅 여부 예측·대안 배치 생성. 전문가 평가에서 제안 배치가 실제와 구분되지 않았고 기존 배치보다 90% 선호(프리프린트·언론 기준, 최종 논문 수치 미확인) — [arXiv 2310.10553](https://arxiv.org/pdf/2310.10553), [The Register](https://www.theregister.com/2024/03/20/googles_deepmind_football/)
- 트래킹 데이터 기반 자동 이벤트 탐지(패스·슛 등)도 공개 연구가 있다 — [arXiv 2202.00804](https://arxiv.org/pdf/2202.00804)

### Inferences
- 우리 목적(패턴 → 템플릿)에는 ① Decroos식 "국면 분할 + 궤적 군집 + 순차 패턴"이 가장 직접적이다. 군집 = 기본 패턴 후보, 군집 안 변이 = 변주 축.
- SoccerMix의 "위치 군 × 방향 군" 원형 액션은 시나리오 단계(step)의 어휘(예: '우측 하프스페이스→박스 안 낮은 패스')로 바로 쓸 수 있다.
- 세트피스는 Shaw 방식(런 타입 사전 → 루틴 = 런 조합)이 우리 S-CK 계열 템플릿 구조(역할별 런 + 킥 타깃 구역)와 같은 모양이다.
- Flow motif는 "누가 몇 번 관여하나"만 보므로 3D 재생용 템플릿에는 공간 정보가 붙은 공간 모티프가 더 적합하다.

### Gaps
- Power et al. 2017(KDD, 패스 위험·보상) 원문·수치를 이번 검색으로 확인하지 못했다(기억상 트래킹 기반 패스 위험·보상 모델이지만 미검증).
- Fernández & Bornn(공간 가치·피치 컨트롤), 오프볼 런 분류 논문(런 타입 사전)의 원문 수치 미확보.
- Decroos 2018의 군집 수·패턴 수 등 정량 결과는 원문 미확인.

## (3) 흔한 패턴의 빈도·성공률 (수치)

### Takeaway
1부 리그에서 페널티 제외 세트피스는 득점의 약 20~27%, 그중 코너가 약 60%. 오픈 플레이 크로스의 득점 전환율은 1~2%대로 낮고, 페널티 스폿 앞쪽으로 들어가는 크로스(컷백 성격)는 5.8%로 약 3배. 하이 턴오버의 약 15%가 슈팅으로 끝난다. 평균 득점은 경기당 약 2.5~3.0.

### Cited Findings
- **세트피스 비중(잉글랜드 1부)**: 2025-26 초반 50경기 124골 중 31골(25%)이 페널티 제외 세트피스, 2024-25 20.6%, 2023-24 19.8%. 코너 득점 경기당 0.44(직전 0.35, 그 전 0.45). 124골/50경기 → 경기당 약 2.48골 — [Premier League / Opta Analyst](https://www.premierleague.com/en/news/4426039/opta-analyst-on-long-balls-long-throws-key-tactical-trends-spotted-in-2025-26-season)
- 다른 시점 기사는 2025-26 세트피스 비중 27.4%(34시즌 중 2위)로 보도 — 시점(경기 수) 차이로 25%와 다름 — [Premier League 뉴스(검색 요약)](https://www.premierleague.com/news/4193966)
- 2024-25 세트피스 득점 187골 중 58.8%가 코너 — [Premier League(검색 요약)](https://premierleague.com/news/4193966); 팀 단위로는 한 시즌 리그 득점의 57.1%가 세트피스였던 극단 사례도 있음 — [Sky Sports](https://www.skysports.com/football/story-telling/11095/13257398/art-of-the-dead-ball-how-premier-league-teams-approach-set-pieces)
- **롱스로**: 경기당 3.44회(직전 1.52), 롱스로 득점 12.5경기당 1골, 경기당 xG 0.11 — [Premier League / Opta Analyst](https://www.premierleague.com/en/news/4426039/opta-analyst-on-long-balls-long-throws-key-tactical-trends-spotted-in-2025-26-season)
- **역습**: 2025-26 fast break 경기당 1.56회(득점 비중은 미보고) — 같은 출처. 2023/24 fast break가 전 시즌 대비 32.8% 증가 — [Premier League(검색 요약)](https://www.premierleague.com/news/2638312). 2018 월드컵: 득점 2골 중 1골이 세트피스 또는 역습 — [FIFA](https://www.fifa.com/tournaments/mens/worldcup/2018russia/news/why-this-has-been-a-world-cup-of-set-pieces). 클럽월드컵 2025 조별 48경기 역습 득점 17골(2022 월드컵 같은 단계 11골) — [FIFA Training Centre](https://www.fifatrainingcentre.com/en/game/tournaments/fcwc/2025/team-analyses/group-stage-review.php)
- **하이 턴오버(전방 압박 탈취)**: 경기당 11.5회, 슈팅으로 끝난 비율 14.9%(직전 14.6회, 16.4%) — [Premier League / Opta Analyst](https://www.premierleague.com/en/news/4426039/opta-analyst-on-long-balls-long-throws-key-tactical-trends-spotted-in-2025-26-season)
- **크로스**: 잉글랜드 1부 3시즌 33,954개 크로스, "6초 안 득점"을 성공으로 보면 약 2%(666개). 박스 뒤 깊은 곳 크로스 0.5%, 더 전진한 위치 1.7%. **페널티 스폿 앞쪽으로 배달된 크로스 5.8% vs 뒤쪽 2%** — [Stats Perform "The art of crossing"](https://www.statsperform.com/resource/the-art-of-crossing). 빅5 리그 오픈 플레이 크로스 평균 전환율 1.76% — [StatsBomb](https://blogarchive.statsbomb.com/articles/soccer/how-low-can-you-go-assorted-thoughts-about-crosses/). 오픈 플레이 크로스 정확도 약 18.8~21.7%(2020/21~2024/25), 90분당 오픈 플레이 크로스 42.0(2003/04) → 24.2(2018/19) — [Premier League Talking Tactics(검색 요약)](https://www.premierleague.com/en/news/4148090)
- **컷백 vs 크로스 xG**: 직접 비교한 공개 수치를 찾지 못함. 학생 프로젝트는 컷백 표본이 너무 적어 검정 불가라고 보고 — [capstone PDF](https://docs.google.com/document/d/1PeytnNl9ZLkPi8tanw_IT_ZuoPgl4aw7LUhrJuOxRpg/export?format=pdf). SciSports는 크로스와 컷백 크로스를 별도 xA 지표로 둔다 — [SciSports](https://support.scisports.com/en/articles/7330575-what-are-the-definitions-of-the-expected-metrics-goalkeeper-stats)
- **슈팅 기본 분포**: 약 37.5만 슈팅 기준 전체 전환율 9.875%, 평균 xG 0.098, 중앙값 xG 0.053, 비페널티 슈팅의 약 70%가 xG 10% 미만, 페널티 xG 약 0.77, 스루볼로 이어진 슈팅 전환율 약 30% — [American Soccer Analysis](https://americansocceranalysis.com/home/2021/8/12/where-goals-come-from-using-past-goals-to-create-future-goals)
- **이벤트 밀도**: 경기당 이벤트 1,682±101개, 이벤트 간격 3.59±7.42초, 패스가 이벤트의 약 50%, 슛 약 1.5%(→ 경기당 약 25슛은 계산치) — [Pappalardo et al. 2019](https://pmc.ncbi.nlm.nih.gov/articles/PMC6817871)
- **월드컵 득점률**: 2018 경기당 2.54골, 2026 조별 2.99골(제공사 불명) — [Northeastern](https://news.northeastern.edu/2026/06/28/world-cup-group-stage-standings/)

### Inferences
- 경기당 슈팅 약 25개 × 평균 xG 0.1 ≈ 2.5골. 우리 목표(평균 2.5~3.0)는 "경기당 슈팅 22~28, 슈팅당 평균 xG ≈ 0.10~0.11" 조합과 맞다.
- 상황 비중 목표치(잠정): 페널티 제외 세트피스 20~27%(코너가 그중 약 60%), 페널티 약 8~10%(아래 Gaps), 역습 약 10~15%(70% × 20% = 14% 추정), 나머지 오픈 플레이 빌드업 약 50~60%.
- 컷백(스폿 앞 낮은 크로스)은 일반 크로스 대비 약 3배 효율 — 템플릿의 결과 확률에 반영.

### Gaps
- 시즌 전체 "역습 득점 비중"을 1차 제공사 수치로 확인하지 못함(70%×20%는 오래된 학술 기준치의 곱).
- 페널티 득점 비중 1차 수치 미확보(일반적으로 약 10% 안팎으로 알려졌지만 이번 조사에서 출처 없음).
- 카운터프레싱 탈취 후 5초 내 탈취율·슈팅 연결률(Bauer & Anzer 원문) 미확보 — Springer 원문 접근 실패.
- 패턴별 경기당 평균 슈팅 수(예: 오버래핑 → 슈팅) 공개 통계는 찾지 못함.

## (4) 실제로 구별되는 패턴은 몇 개인가 / 롱테일

### Takeaway
"패턴 수"에 대한 합의된 숫자는 없다. 정의 수준(3패스 모티프 5종 → 위치·방향 원형 수십~수백 → 궤적 군집 수백)에 따라 달라지고, 빈도 분포는 소수 패턴이 다수 상황을 차지하는 롱테일이다.

### Cited Findings
- 3패스 flow motif는 정의상 5종뿐(ABAB, ABAC, ABCA, ABCB, ABCD) — 너무 거칠어서 공간 정보를 붙인 확장이 나옴 — [IOS Press](https://content.iospress.com:443/articles/journal-of-sports-analytics/jsa190290), [Barbosa 2022](https://www.dcc.fc.up.pt/~pribeiro/pubs/pdf/barbosa-cna2022.pdf)
- 이벤트 데이터는 희소(sparse)해서 비슷한 액션을 묶어야 한다는 것이 SoccerMix의 출발점 — [DTAI 블로그](https://dtai.cs.kuleuven.be/sports/blog/soccermix:-representing-soccer-actions-with-mixture-models)
- 슈팅 xG 분포 자체가 롱테일: 최빈 xG 약 0.02, 비페널티 슈팅 70%가 0.1 미만 — [American Soccer Analysis](https://americansocceranalysis.com/home/2021/8/12/where-goals-come-from-using-past-goals-to-create-future-goals)
- 코너는 팀마다 소수의 반복 루틴을 갖고, 이를 시즌 단위로 식별·검색할 수 있다(= 팀 단위로는 루틴 수가 적고 리그 전체로는 다양) — [Shaw & Gopaladesikan](https://www.sloansportsconference.com/research-papers/routine-inspection-a-playbook-for-corner-kicks)

### Inferences
- 우리 268개 기본 패턴은 "궤적 군집 수백 개" 수준과 같은 자릿수라 과하지도 모자라지도 않다. 4만 개 변주는 "군집 내 변이(측면·역할 교체·패스 타입·결과 분기)"로 정당화된다.
- 롱테일 특성상 상위 30~50개 패턴이 상황의 대부분을 차지할 것으로 예상 — 이 패턴들을 먼저 다듬고, 꼬리 패턴은 하이라이트 다양성용으로 둔다.

### Gaps
- 공개 데이터로 "상위 N개 군집이 점유의 몇 %를 덮는가" 같은 누적 분포 수치를 직접 찾지 못함. 우리 채굴 파이프라인에서 직접 측정해야 한다.

## (5) 채굴한 패턴을 매개변수 템플릿으로 만들고 검증하기

### Takeaway
선행 연구의 공통 흐름은 "분할 → 정규화(측면 반전·좌표 표준화) → 군집 → 군집 대표(원형) 추출 → 전문가 검토 → 탐지기로 재검색해 빈도·성과 측정"이다. 템플릿화는 군집 대표를 역할·구역·트리거·분기로 추상화하는 단계이고, 검증은 (a) 전문가 판별, (b) 실제 분포와의 통계 비교로 나뉜다.

### Cited Findings
- Decroos 2018: 군집 결과를 도메인 전문가가 검토, 자문 회사에 스카우팅·상대 분석용으로 제시 — [the morning paper](https://blog.acolyer.org/2018/10/12/automatic-discovery-of-tactics-in-spatio-temporal-soccer-match-data/)
- Shaw & Gopaladesikan: 런 타입 사전 → 루틴 분류 → 같은 루틴의 코너를 데이터에서 검색(템플릿을 탐지기로 재사용) — [Springer](https://www.springerprofessional.de/en/routine-inspection-a-playbook-for-corner-kicks/18671052)
- TacticAI: 생성한 배치를 전문가에게 실제 배치와 섞어 보여주는 블라인드 평가(구분 불가 + 90% 선호) — [arXiv 2310.10553](https://arxiv.org/pdf/2310.10553)
- Bauer & Anzer: 전문가 합의 정의 → 라벨 → 분류기(AUC 87.4%) → 대량 적용으로 경험칙 검증 — [Springer](https://link.springer.com/article/10.1007/s10618-021-00763-7)
- Håland et al.: 모티프를 슈팅 결과와 연결해 효과 평가(관여 인원·사용 면적이 클수록 슈팅 확률↑) — [NTNU Open](https://ntnuopen.ntnu.no/ntnu-xmlui/handle/11250/2727710)

### Inferences
- 템플릿 구조 제안: `역할 슬롯(볼 보유자·지원·침투·박스 타깃 3~5명)` + `구역 시퀀스(18~30칸 격자 또는 SoccerMix식 위치 군)` + `트리거(탈취 위치·수비 라인 높이·수적 우위)` + `분기(성공·차단·파울·슈팅 결과)` + `결과 확률(데이터 xG·전환율로 보정)`.
- 검증 2단: ① 구조 — 우리 탐지기로 실제(공개) 데이터에서 같은 패턴을 다시 찾아 빈도 순위가 비슷한지, ② 결과 — 대량 시뮬의 상황별 득점 비중·크로스 전환율·하이 턴오버→슈팅 비율이 위 (3) 범위에 드는지.

### Gaps
- 패턴 템플릿을 게임 엔진용으로 변환한 공개 사례(게임 업계)는 찾지 못함.

## 부록: 상업적 채굴에 쓸 수 있는 공개 데이터셋 라이선스

### Cited Findings
- **Wyscout 공개 데이터셋 (Pappalardo et al. 2019)**: 데이터 CC BY 4.0, 메타데이터 CC0. 2017/18 빅5 리그 + 2018 월드컵 + 유로 2016, 1,941경기, 이벤트 3,251,294개 — [Scientific Data / PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC6817871), [figshare](https://figshare.com/collections/Soccer_match_event_dataset/4415000/2). 상업 이용 가능(저작자 표시 필요). **단, 선수 실명이 포함돼 있다** — 채굴 단계에서 즉시 익명화 필요.
- **SkillCorner Open Data**: 방송 기반 트래킹 10경기(2024/25 호주 A리그로 보고됨) + Dynamic Events, 수동 보정 없음. 라이선스는 인덱스 기준 MIT(LICENSE 파일 직접 미확인) — [GitHub](https://www.github.com/SkillCorner/opendata), [ecosyste.ms](https://awesome.ecosyste.ms/projects/github.com%2FSkillCorner%2Fopendata)
- **StatsBomb Open Data**: "공개 비상업적 이용"용, 사용자 동의서(LICENSE.pdf) 적용, 출처·로고 표기 요구, 상업 이용은 별도 계약 — [GitHub statsbomb/open-data](https://github.com/statsbomb/open-data). **우리 제품용 채굴에는 쓰지 않는다.**
- **Metrica Sports 샘플**: 이번 조사에서 라이선스를 확인하지 못함 → 미확인 상태로는 사용 보류.

---

## Road To PRO에 적용할 점

현재 `Data/scenarios/`에는 6개 파일(CT02, D-PR-01, FT01, PR04, S-CK-01, wing_solo_run_cutback)이 있다. 아래는 268개 기본 패턴 → 약 4만 개 변주로 키우는 제안이다(수치는 위 출처 기반 [제안], 기획서 반영은 사용자 승인 필요).

### 1. 먼저 손으로 만들 패턴 (빈도·득점 기여 순)
1. **코너킥 루틴 묶음 (S-CK)** — 세트피스 득점의 약 60%, 전체 득점의 약 12~16%. 니어 포스트 플릭, 파 포스트, 숏 코너, 페널티 스폿 앞 컷백, 존 수비 대 대인 수비 분기. 루틴 = 역할별 런 조합이라 변주 효율이 가장 좋다.
2. **측면 크로스 / 컷백 (현재 wing_solo_run_cutback)** — 크로스는 경기당 수십 회로 하이라이트에 자주 나오지만 전환율은 1~2%. 컷백(스폿 앞 배달)은 5.8%로 약 3배 → 결과 확률을 크게 다르게 준다.
3. **하이 턴오버 → 빠른 슈팅 (PR 계열)** — 경기당 약 11~15회, 약 15%가 슈팅으로 끝남.
4. **역습 (CT 계열)** — 득점의 약 10~15%(추정). 수적 우위 2v1·3v2, 롱패스 스루볼 분기. 스루볼 슈팅 전환율 약 30%는 결과 쪽 기준이 된다.
5. **프리킥(직접·간접), 롱스로, 페널티** — 롱스로는 최근 경기당 3.44회로 늘었다(경기당 xG 0.11).
6. **빌드업 → 하프스페이스 침투 / 3자 패스 / 오버래핑** — 오픈 플레이 득점의 본체. 변주 축(측면·인원·패스 타입)이 가장 많다.
7. 수비 패턴(D-PR 등): 카운터프레싱(터치라인 근처 성공률이 높다는 경험칙 반영), 라인 브레이크 대응.

### 2. 익명화 채굴 파이프라인 (오프라인, Python, `Tools/`)
1. **입력**: Wyscout 공개(CC BY 4.0) 이벤트 + SkillCorner(MIT로 보고됨, LICENSE 직접 확인 후) 트래킹. StatsBomb Open Data는 비상업 조건이라 제외, Metrica는 라이선스 확인 전 보류.
2. **익명화 먼저**: 로드 즉시 선수·팀 ID를 해시 슬롯(`P1..P22`, `TeamA/B`)과 포지션·역할 라벨로 바꾸고 이름 필드는 버린다. 결과물(JSON 템플릿)에 원본 ID가 남지 않게 검사(spec-lint 실명 검사 확장).
3. **분할**: 점유 단위(탈취 → 상실/슈팅/세트피스 재개)로 자르고 상황 라벨(오픈 플레이·역습·세트피스 종류·하이 턴오버)을 붙인다.
4. **정규화**: 공격 방향 통일, 좌우 반전(측면은 변주 축으로 분리), 좌표 105×68 표준화.
5. **군집**: 이벤트는 SoccerMix식 원형 액션(위치 GMM × 방향 von Mises)으로 기호화 → 원형 시퀀스를 DTW 또는 편집 거리로 군집(Decroos 방식). 코너는 Shaw 방식(런 타입 → 루틴).
6. **템플릿화**: 군집 대표 → `역할 슬롯 3~5 + 구역 시퀀스 + 트리거 + 분기 + 결과 확률`. 군집 내 분산을 변주 축으로 뽑는다(측면 ×2, 패스 타입, 관여 인원, 결과 분기 3~5개, 수비 대응 2~3개 → 기본 패턴당 약 150개 변주 ≈ 268 × 150 ≈ 4만).
7. **사람 검토**: 상위 군집부터 카탈로그(`docs/scenarios.html`) ID와 대응. 데이터는 "어떤 패턴이 얼마나 자주"의 근거로만 쓰고, 최종 템플릿은 우리가 저작(원본 궤적 복제 아님).

### 3. 검증 목표 (대량 헤드리스 시뮬)
| 지표 | 목표 범위 [제안] | 근거 |
|---|---|---|
| 경기당 평균 득점 | 2.5~3.0 (확정) | CLAUDE.md, 잉글랜드 1부 약 2.48, 월드컵 2.54~2.99 |
| 경기당 슈팅 | 약 22~28 | 이벤트 1.5% × 1,682 ≈ 25 (계산치) |
| 슈팅당 평균 xG | 약 0.10 (중앙값 약 0.05) | ASA 37.5만 슈팅 |
| 페널티 제외 세트피스 득점 비중 | 20~27% | 잉글랜드 1부 2023-24~2025-26 |
| 세트피스 중 코너 비중 | 약 55~60% | 2024-25 58.8% |
| 역습 득점 비중 | 약 10~15% | 오픈 플레이 70% × 역습 20% (오래된 기준치, 재확인 필요) |
| 오픈 플레이 크로스 → 득점 | 1.5~2.2% | Stats Perform, StatsBomb |
| 컷백(스폿 앞) 크로스 → 득점 | 약 5~6% | Stats Perform 5.8% |
| 하이 턴오버 → 슈팅 | 약 15% | Opta 2025-26 14.9% |
| 패턴 빈도 분포 | 상위 패턴 집중 롱테일, 채굴 데이터의 순위와 비슷 | (4) |

- 이 수치들은 기획서에 없는 값이 많다. 스펙에 넣을 때는 `[제안]`으로 달고 사용자 승인 후 확정한다.
- 남은 확인: 페널티 득점 비중, 시즌 단위 역습 득점 비중(1차 출처), 카운터프레싱 탈취 후 슈팅률(Bauer & Anzer 원문), SkillCorner LICENSE 파일, Metrica 라이선스.
