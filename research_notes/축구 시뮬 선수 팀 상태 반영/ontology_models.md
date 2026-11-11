# 축구 엔티티 온톨로지·데이터 모델과 시뮬레이션 상태 구조

> 조사일 2026-11-11. 공개 자료만 사용함. 상용 게임 디컴파일·데이터 추출은 하지 않음. FM 관련 내용은 커뮤니티 가이드(공개)를 2차 자료로만 인용함.

## 1. 스포츠·축구 온톨로지/스키마 (BBC, schema.org, SPADL, 이벤트 스키마, OWL, EPTS, CDF)

### Takeaway
공개 축구 스키마는 모두 **경기 사실(Match·Team·Player·Event·좌표·시간)** 을 기록하는 데 초점을 둔다. 선수 능력치·성향·컨디션 같은 **시뮬레이션용 내부 상태를 다루는 표준은 없다**. 따라서 Road To PRO는 "관측 계층(Event/Action)"은 SPADL/CDF를 본뜨고, "상태 계층(Attributes/DynamicState)"은 직접 설계해야 한다.

### Cited Findings
**SPADL (Decroos 외, socceraction)**
- SPADL은 액션 하나를 12개 속성으로 표현한다: game_id, period_id, seconds, player, team, start_x, start_y, end_x, end_y, action_type, result, bodypart — [socceraction SPADL](https://socceraction.readthedocs.io/en/latest/documentation/spadl/spadl.html)
- 액션 타입은 22개다: pass, cross, throw-in, crossed/short free kick, crossed/short corner, take-on, foul, tackle, interception, shot, penalty shot, free kick shot, keeper save/claim/punch/pick-up, clearance, bad touch, dribble, goal kick — [socceraction SPADL](https://socceraction.readthedocs.io/en/latest/documentation/spadl/spadl.html)
- result 값은 success/fail이고, 특수값으로 offside(패스·코너·FK), own goal(슛), yellow/red card(파울)가 있다. bodypart 값은 foot/head/other/none이다 — [socceraction SPADL](https://socceraction.readthedocs.io/en/latest/documentation/spadl/spadl.html)
- 좌표계는 105m×68m 고정이고 원점은 왼쪽 아래다. 홈팀이 오른쪽으로 공격한다고 가정하며, `play_left_to_right()`로 방향을 정규화한다 — [socceraction SPADL](https://socceraction.readthedocs.io/en/latest/documentation/spadl/spadl.html)
- Atomic-SPADL은 result 속성을 없애고 (x, y, dx, dy)를 쓴다. 패스는 pass와 receival로 쪼개고, interception·out 액션을 추가하며, 슛·FK·코너도 두 액션으로 나눈다. 모든 액션은 "성공하고 끊기지 않은 것"으로 다룬다 — [socceraction Atomic-SPADL](https://socceraction.readthedocs.io/en/latest/documentation/spadl/atomic_spadl.html)

**schema.org**
- SportsTeam 고유 속성은 athlete(Person), coach(Person), gender다. sport는 SportsOrganization에서 상속받는다. SportsEvent는 homeTeam/awayTeam/competitor로 팀을 참조한다 — [schema.org SportsTeam](https://schema.org/SportsTeam)

**StatsBomb 이벤트 (공개 문서 기준)**
- `under_pressure`는 액션 시점에 상대가 압박 중이었는지 나타내는 불리언 플래그다. `related_events`는 관련 이벤트 ID 목록이다 — [socceraction StatsBombEventSchema](https://socceraction.readthedocs.io/en/latest/api/generated/socceraction.data.statsbomb.StatsBombEventSchema.html)
- Pressure 이벤트는 수비수가 공 가진 상대의 5야드 안에 들어올 때 기록된다 — [StatsBomb Live API event descriptions](https://live-data-api-guide.statsbomb.com/api-reference/event-descriptions.html) (검색 요약 기준이며 원문 대조는 하지 않음)
- 이벤트별 상세 필드 정의는 open-data 레포의 `doc` 폴더에 있다고 안내된다 — [StatsBomb open-data (mirror)](https://github.com/ivoryshoulder/open-data)

**OWL 축구 온톨로지**
- footbOWL(ESWC 2011)은 2층 구조 OWL KB다. 기본층은 팀·대회 단계·경기·선수와 이벤트(페널티·골·카드)를 담고, 2층은 기본층에서 추가 엔티티·이벤트를 추론한다. 원래 용도는 스페인어 경기 요약 생성이다 — [footbOWL 논문](https://repositorio.uam.es/bitstream/handle/10486/664869/footbOWL_bouayad-agha_LNCS_2011_ps.pdf?sequence=1)
- Calvanese 외(OWLED 2007), "Can OWL model football leagues?"는 리그·라운드·경기를 OWL로 모델링한 초기 사례다 — [OWLED 2007](https://www.inf.unibz.it/~calvanese/papers/calv-etal-OWLED-2007.pdf)
- Serra 외(MTAP 2010)는 OWL과 SWRL 규칙으로 축구 영상 이벤트를 의미적으로 주석한다 — [Serra MTAP 2010](https://ailab.uniud.it/wp-content/uploads/2018/11/Serra-MTAP_2010.pdf)
- **Football CDF Ontology (FCDF)**: 네임스페이스 `https://w3id.org/football-cdf/core#`, 버전 1.0.0-alpha-2(2025-07-31), CC BY 4.0. 클래스 18개(Card, Competition, Event, Goal, Match, MatchResult, MatchStatus, Meta, Misc, Pass, Player, Referee, Season, Shot, Substitution, Team, Vendor, Whistle), 오브젝트 속성 19개(assist_id, receiver_id, related_event_ids, team_home/away 등), 데이터 속성 39개(body_part, is_penalty, is_starter, jersey_number, x/y/x_end/y_end, outcome_type 등) — [FCDF 문서](https://semsys.ai.wu.ac.at/football-cdf/core/)
- FCDF 논문은 이 온톨로지로 추론, SPARQL 질의, SHACL 검증이 가능하다고 주장한다 — [CEUR Vol-4085 paper56](https://ceur-ws.org/Vol-4085/paper56.pdf)

**Common Data Format (CDF, Anzer 외)**
- 클럽·연맹·FIFA·대학 공동 저자 11명이 쓴 arXiv:2505.15820(v4, 2025-08)은 CDF v1.0.0을 정의한다. 다루는 데이터는 5종이다: match sheet, video, event, tracking, match meta — [arXiv 2505.15820](https://arxiv.org/abs/2505.15820), [HTML v4](https://arxiv.org/html/2505.15820v4)
- 오픈소스 JSON Schema 검증기가 있다(UnravelSports, PyPI `common-data-format-validator`) — [ecosyste.ms](https://science.ecosyste.ms/projects/59635)

**FIFA EPTS 표준**
- FIFA와 FC Barcelona가 함께 만든 EPTS(전자 성능·추적 시스템) 데이터 전송 표준 1판이다. 원시 데이터는 제공자가 원하는 포맷으로 둔다. 대신 그 구조를 XSD 준수 XML(Format Specification)로 기술한다. 목적은 "수많은 API 없이 제공자 간 데이터를 비교 가능하게" 하는 것이다 — [FIFA EPTS R&D](https://inside.fifa.com/innovation/standards/epts/research-development-epts-standard-data-format), [Standard Transfer Format 문서(PDF)](https://fifa-quality-prod-1.azurewebsites.net/media/171893/standard-transfer-format-documentation.pdf)

**kloppy (오픈소스 표준화 라이브러리)**
- 경기 데이터셋은 EventDataset, TrackingDataset, CodeDataset 세 종류다. 각각 DataRecord(Event 또는 Frame)의 시퀀스이고 Metadata 객체가 붙는다. Metadata에는 홈/원정 Team 목록이 들어 있다 — [kloppy docs](https://kloppy.readthedocs.io), [kloppy domain models (preview)](https://deploy-preview-546--unruffled-nobel-ed43d4.netlify.app/reference/domain/models)

### Inferences
- 공통 골격은 **Competition → Season → Match → (Team×2, Player 명단, Referee) → Event 시퀀스(시간·좌표·actor·결과·관련 이벤트)** 다. Road To PRO의 Event/Action 레이어(하이라이트 기록, 통계, 30Hz 리플레이와의 연결)는 SPADL의 12필드 + StatsBomb의 `under_pressure`/`related_events` + CDF의 is_starter·Substitution 구조로 충분히 덮인다.
- Atomic-SPADL의 "결과를 별도 액션으로 분리(receival/interception/out)" 방식은 우리 시나리오 엔진에 맞다. 시나리오 단계가 깨지는 지점이 곧 interception/out 이벤트가 되므로, 시나리오 붕괴를 이벤트로 기록할 수 있다.
- EPTS의 "포맷은 자유, 구조는 스키마로 기술" 철학은 모드 친화 JSON 설계에 쓸 수 있다. 데이터 파일과 별개로 스키마 버전 파일을 둔다.
- 표준 이벤트 스키마에 쓰이는 실명 데이터는 우리 실명 정책(A안)과 무관하다. 구조만 차용하면 된다.

### Gaps
- BBC Sport Ontology 페이지(bbc.co.uk/ontologies/sport)는 fetch가 차단돼 클래스·버전을 확인하지 못했다. 2012 올림픽용으로 만들었다는 통념도 검증하지 못했다.
- Opta F24·Wyscout v3 공개 스키마는 이번에 원문을 확인하지 못했다.
- DFL/Sportec 데이터 모델(DFL 공개 스펙)은 찾지 못했다. CDF 논문 저자진에 리그 관계자가 포함된다는 점만 확인했다.
- EPTS XSD의 요소별 상세는 PDF를 열지 못해 미확인이다.

## 2. 스포츠 게임·오픈소스 시뮬이 선수 데이터(능력치·히든·특성·동적 상태)를 구조화하는 방식

### Takeaway
공개된 사례는 공통으로 **정적 능력치(수십 개) + 특성/트레이트 + 동적 상태(피로·카드·역할)** 로 나눈다. 특히 rcssserver는 "선수 타입 파라미터(정적, 이질적)"와 "매 사이클 상태(stamina/effort/recovery)"를 명확히 분리한 좋은 레퍼런스다.

### Cited Findings
**Google Research Football (GRF)**
- 선수별 raw observation 필드: 위치 [x,y], 방향, `tired_factor`(0~1, 0=전혀 안 지침), yellow_card(0/1), active(레드카드면 False), roles(0 GK, 1 CB, 2 LB, 3 RB, 4 DM, 5 CM, 6 LM, 7 RM, 8 AM, 9 CF). 그 밖의 선수별 능력치는 노출하지 않는다 — [GRF observation.md](https://github.com/google-research/football/blob/master/gfootball/doc/observation.md)
- 경기 상태 필드: score, steps_left, game_mode(0 Normal, 1 KickOff, 2 GoalKick, 3 FreeKick, 4 Corner, 5 ThrowIn, 6 Penalty), ball_owned_team/player, sticky_actions(방향 8개 + sprint + dribble). 우측 팀 관측은 좌우 반전해서 준다 — [GRF observation.md](https://github.com/google-research/football/blob/master/gfootball/doc/observation.md)

**RoboCup rcssserver (스태미나·이질 선수)**
- stamina_max 8000이고 하프마다 리셋된다. 매 사이클 stamina += recovery × stamina_inc_max(기본 45)만큼 회복한다. recovery는 1.0에서 시작해 최소 0.5까지 내려가고, 경기 중에는 오르지 않는다 — [rcssserver 문서](https://rcsoccersim.readthedocs.io/en/latest/soccerserver.html)
- 대시 소모는 전진 = power, 후진 = 2×power다. 유효 대시 파워는 effort × dash_power_rate(0.006) × power다. stamina ≤ 0.3×max(2400)이면 recovery가 0.002씩 감소한다. stamina ≥ 0.6×max이면 effort가 0.01씩 증가한다 — [rcssserver 문서](https://rcsoccersim.readthedocs.io/en/latest/soccerserver.html) (문서 표의 effort_dec/effort_dec_thr 값이 의사코드와 뒤바뀐 것으로 보인다는 점에 주의)
- v13부터 stamina_capacity(기본 130600)가 생겼다. 회복할 때마다 용량을 소모하고, 용량이 0이 되면 더는 회복하지 않는다. 즉 경기 전체 피로 누적 모델이다 — [rcssserver 문서](https://rcsoccersim.readthedocs.io/en/latest/soccerserver.html)
- 이질 선수 타입 파라미터: player_speed_max, stamina_inc_max, dash_power_rate, kickable_margin(0.6~0.8), kick_rand, extra_stamina, effort_min/max. 이 값들은 트레이드오프 delta factor로 연동된다. 교체 시 새 타입의 초기값으로 리셋된다 — [rcssserver 문서](https://rcsoccersim.readthedocs.io/en/latest/soccerserver.html)

**EA FIFA (공개 개발자 블로그)**
- FIFA 08 개발 블로그: 선수 하나를 "35개 능력치, 70개 트레이트, 65개 이상의 추가 데이터 필드"로 기술한다. 35개 능력치로 OVR을 계산하며, **포지션마다 고유 공식**이 있다 — [GamesRadar FIFA 08 dev blog #5](https://www.gamesradar.com/fifa-08-360ps3-developer-blog-5/3)

**Madden (공개 기사)**
- 매년 봄에 지정 팀이 전 선수 레이팅을 산정한다. 한 선수가 과도하게 강해지지 않도록 레이팅을 범위 안에 묶는다 — [GamesRadar Madden 17](https://gamesradar.com/how-ps2-era-michael-vick-had-a-big-effect-on-madden-nfl-17)

**Football Manager (공개 커뮤니티 가이드, 2차 자료)**
- 히든 능력치로 Consistency(경기마다 제 능력을 발휘하는지), Important Matches(큰 경기에서의 능력 발휘) 등이 있다. Pressure가 높아도 Important Matches가 낮으면 큰 경기에서 고전할 수 있다는 설명이 있다 — [passion4fm](https://www.passion4fm.com/?p=14158), [fmprojects inconsistency](https://fmprojects.substack.com/p/inconsistency)
- 성격(Personality) 라벨은 Determination·Leadership(표시)과 Professionalism·Pressure·Ambition 등 히든 값의 조합으로 정해진다고 커뮤니티는 설명한다 — [fmscout personality guide](https://www.fmscout.com/a-guide-to-player-personalities-football-manager.html)
- SI는 공개 도구로 Database(pre-game) Editor와 In-Game Editor를 제공하며, 여기서 히든 값을 보고 편집할 수 있다 — [passion4fm](https://www.passion4fm.com/?p=14158)
- 주의: 이 효과 설명들은 커뮤니티 추정이고 출처끼리도 엇갈린다. 예를 들어 Consistency의 중요도를 두고 상반된 주장이 있다 — [fmprojects inconsistency](https://fmprojects.substack.com/p/inconsistency)

**데이터 주도 설계 일반**
- 스프레드시트 튜닝은 기준값을 정한 뒤 원하는 결과가 나오도록 보정치를 역산하는 방식이다 — [UMBC Data Driven Design 강의](https://userpages.cs.umbc.edu/olano/class/493-24-2/ddd.md.html)
- GDC "Gods and Dumps: Attribute Tuning" 세션이 있으나(스포츠 여부 미확인) 원문은 확인하지 못했다 — [GDC Vault](https://gdcvault.com/play/1023481/Gods-and-Dumps-Attribute-Tuning)

### Inferences
- 공통 계층은 다음 다섯이다. (1) 신원·메타(이름·나이·포지션), (2) 정적 능력치(표시), (3) 히든 능력치·성격(비표시, 변동성·큰 경기·성장에 작용), (4) 트레이트/버릇(이산 플래그로 의사결정 가중치를 바꿈), (5) 동적 상태(피로·카드·부상·사기·폼).
- FIFA의 "포지션별 OVR 공식"과 rcssserver의 "이질 타입 파라미터"를 합쳐 보면 한 가지 설계가 나온다. 능력치(0~100)를 엔진이 직접 읽지 않고, **파생 파라미터(최고속도, 킥 노이즈, 스태미나 회복률 등)로 한 번 변환한 뒤** 엔진이 읽는다. 그러면 튜닝 지점이 변환 테이블 하나로 모인다.
- rcssserver의 stamina(단기)·recovery(경기 내 비가역)·capacity(경기 총량) 3단 모델은 "경기 중 피로 vs 시즌 누적 컨디션" 분리에 그대로 쓸 수 있다. 모든 연산이 정수·고정소수점으로 가능해 결정론에도 유리하다.

### Gaps
- EA/2K/SI의 GDC 강연 중 선수 데이터 구조를 직접 다룬 공개 자료는 찾지 못했다.
- FM 히든 능력치가 매치 엔진에 미치는 정확한 효과는 공개 공식 자료가 없다. 리버스 엔지니어링 결과는 의도적으로 배제했다.
- GRF 엔진(C++)의 내부 선수 능력치 구조는 observation 문서 밖이라 확인하지 못했다.

## 3. 선수 퍼포먼스의 인과·지식그래프 모델링 (피로·사기·베이지안 네트워크)

### Takeaway
베이지안 네트워크(pi-football)는 "통계로 잡히지 않는 주관 요인(부상·피로·동기)을 노드로 넣어 팀 강도를 보정"하는 구조의 대표 사례다. 실증 연구에서 경기 일정 밀집(휴식 부족)은 **부상 위험은 확실히 올리지만 경기 결과·총 주행거리에 미치는 효과는 약하거나 불확실**하다. 게임 공식에서 "피로 → 경기력"을 강하게 잡는다면 현실 근거보다는 게임성 선택임을 인지해야 한다.

### Cited Findings
- pi-football(Constantinou 외, 2012)은 홈승/무/원정승 확률을 내는 베이지안 네트워크다. 과거 데이터가 담지 못하는 중요 요인을 주관적 변수로 반영한다. 경기 전에 예측을 공개했고, 장기간 시장 배당 대비 수익을 냈다고 보고했다 — [QMRO 리포지터리](https://qmro.qmul.ac.uk/xmlui/handle/123456789/8404), [cs4fn pi-football](https://www.cs4fn.org/sport/pi-football.php)
- pi-rating(Constantinou & Fenton 2013)은 상대 간 득실 차이만으로 팀 간 상대 우위를 측정한다 — [Springer ML 2018 Constantinou](https://link.springer.com/10.1007/s10994-018-5703-7)
- 2020 하이브리드 BN은 pi-rating을 입력으로 받아 점유→슛→골 관계를 모델링하고, 1X2와 아시안 핸디캡을 예측한다 — [arXiv 2003.09384](https://pdf.arxiv.org/pdf/2003.09384)
- 메타분석(Julian·Page·Harper 2020): 일정 밀집은 총 주행거리에 영향이 없다. 일부 연구에서 저·중강도 달리기 감소(페이싱 가능성)가 보였다. 전술 영향 근거는 연구 1편뿐이다 — [Sports Medicine 2020](https://link.springer.com/article/10.1007/s40279-020-01359-9)
- 챔피언스리그 부상 연구(2013): 일정 밀집은 근육 부상률 증가와 연관됐다. 팀 성적과는 무관하거나 영향이 매우 제한적이었다(유로파리그의 짧은 회복만 패배 증가와 연관) — [PubMed 23851296](https://pubmed.ncbi.nlm.nih.gov/23851296/)
- 라리가 2018-19(선수 346명, 2,971경기): 회복 기간에 따른 신체 수행 차이가 없었다 — [revistas.um.es](https://revistas.um.es/sportk/article/download/543711/380311/2540961)
- 반대 근거로, 시뮬레이션 경기 3회 실험에서 1~2일 간격이 3~4일 간격보다 피로·근손상·수행 저하 지표가 컸다 — [PMC10690515](https://pmc.ncbi.nlm.nih.gov/articles/PMC10690515)
- "준비 2일 팀의 다음 경기 승률 40% 감소"(Verheijen 2012)는 코치 분석이고 동료평가를 거치지 않았다 — [Huddersfield 보도](https://www.hud.ac.uk/news/2020/november/football-fixture-congestion-new-study/)

### Inferences
- BN 구조를 엔진 설계에 옮기면 이렇게 된다. 관측 가능한 "팀 강도 사전값"(능력치 합성)에 **보정 노드**(가용성·피로·사기·홈·중요도)를 곱셈/가산 계수로 붙이고, 결과는 득점 기대값(λ)으로 낸다. 이 형태는 "대량 시뮬 평균 득점 2.5~3.0"(CLAUDE.md) 같은 집계 목표로 보정하기 쉽다.
- 현실 근거에 맞추려면 피로·휴식 부족의 효과를 **경기력보다 부상 확률**에 더 강하게 거는 편이 맞다. 이는 기획의 "감기 → 부상 확률 상승" 방향과도 일치한다.
- 인과 그래프는 사이클 없는 DAG로 정의해야 한다. 정적 → 파생, 동적 상태 → 보정치 → 액션 확률 순서로 흐르게 하고, 경기 결과는 다음 경기 동적 상태로만 되먹임한다. 그래야 틱 순서 의존 없는 결정론적 평가 순서가 보장된다.

### Gaps
- 사기(morale) → 경기력의 인과 효과를 정량화한 신뢰할 만한 실증 논문은 이번 조사에서 찾지 못했다.
- pi-football 원문 PDF(core.ac.uk)가 403으로 막혀 노드 목록(피로·핵심 선수 결장·동기 등)을 직접 확인하지 못했다.
- 스포츠 지식그래프(KG 임베딩 기반 선수 평가) 논문은 이번 범위에서 조사하지 못했다.

## 4. 게임 데이터 온톨로지 모범 사례 (정적/동적/파생 분리, 튜닝, 버전, 모드)

### Takeaway
공개 표준(CDF·EPTS·FCDF)은 **버전 문자열 + 스키마 검증(JSON Schema/XSD/SHACL)** 을 기본으로 둔다. 게임 쪽 사례(FIFA·rcssserver)는 정적 능력치를 파생 파라미터로 바꾸는 변환 계층을 둔다. 둘을 합치면 "버전 있는 JSON 데이터 + JSON Schema 검증 + 파생 파라미터 테이블"이 모드 친화 구조의 기본형이 된다.

### Cited Findings
- CDF는 v1.0.0을 명시하고 JSON Schema 검증기를 오픈소스로 제공한다 — [arXiv 2505.15820](https://arxiv.org/abs/2505.15820), [ecosyste.ms](https://science.ecosyste.ms/projects/59635)
- FCDF는 revision 1.0.0-alpha-2와 날짜를 명시하고, SHACL 검증을 장점으로 내세운다 — [FCDF 문서](https://semsys.ai.wu.ac.at/football-cdf/core/), [CEUR paper56](https://ceur-ws.org/Vol-4085/paper56.pdf)
- EPTS는 데이터 포맷과 구조 기술(XSD)을 분리하고, 1판을 "기술 발전에 따라 계속 갱신"한다고 명시한다 — [FIFA EPTS R&D](https://inside.fifa.com/innovation/standards/epts/research-development-epts-standard-data-format)
- rcssserver는 모든 물리·스태미나 상수를 server.conf와 player.conf 파라미터로 외부화하고, 이질 타입은 delta factor로 생성한다 — [rcssserver 문서](https://rcsoccersim.readthedocs.io/en/latest/soccerserver.html)
- FIFA: 능력치 35개는 포지션별 공식을 거쳐 OVR(파생값)이 된다 — [GamesRadar FIFA 08](https://www.gamesradar.com/fifa-08-360ps3-developer-blog-5/3)

### Inferences
- 3층 분리 원칙은 다음과 같다. **Static**(세이브에 저장, 시즌 단위 변화) / **Dynamic**(세이브에 저장, 일·경기 단위 변화) / **Derived**(저장하지 않음, 경기 시작 시 Static+Dynamic+Context로 매번 재계산하고 경기 중 불변 스냅샷). 파생값을 저장하지 않아야 공식을 튜닝해도 세이브가 깨지지 않는다.
- 모드 친화 원칙은 세 가지다. (a) 모든 엔티티 ID는 문자열 슬러그로 두고 정수 인덱스는 로드할 때 부여한다. (b) `schemaVersion` 필드와 마이그레이션 함수 체인을 둔다. (c) 수치 공식의 계수는 `tuning/*.json`에 두고 코드에는 형태(공식 구조)만 둔다. (d) 카탈로그(역할 27, 성향 14축, 버릇 15)는 데이터로 정의하고, 엔진은 "효과 키 → 가중치" 맵만 해석한다.
- 결정론을 위해 JSON 실수는 로드할 때 고정소수점(예: ×1000 정수)으로 변환한다. 정렬 순서는 ID 사전순으로 고정한다. 딕셔너리 순회 순서에 의존하지 않는다.

### Gaps
- 상용 스포츠 게임의 세이브·모드 스키마 버전 관리 공개 사례는 찾지 못했다.

---

## Road To PRO에 적용할 점

> 아래는 조사에 근거한 **제안**이다. 수치는 모두 `[미정]`이며, 기획서(docs/index.html)와 대조한 뒤 스펙으로 옮겨야 한다.

### A. 계층 구조 (저장 여부 기준)
| 계층 | 엔티티 | 변화 주기 | 저장 | 근거 사례 |
|---|---|---|---|---|
| Catalog(모드 데이터) | RoleDef(27), TendencyAxisDef(14), HabitDef(15), ScenarioDef(268 + 변주), ActionTypeDef, TuningTables | 버전 단위 | JSON(읽기 전용) | rcssserver conf, EPTS 스키마 분리 |
| Static | Player.Identity, Attributes(0~100), HiddenAttributes, Personality, Tendencies 값, Habits 보유 | 시즌·성장 | SQLite 세이브 | FIFA 35능력+70트레이트 |
| Dynamic | DynamicState(condition, fatigue, sharpness, morale, form, injury, illness), Chemistry | 일·경기 | SQLite 세이브 | GRF tired_factor, rcss stamina |
| Context | MatchContext(home, weather, importance, rest days, derby) | 경기 | 경기 레코드 | BN 보정 노드 |
| Derived(비저장) | EngineParams(선수별), TeamStrength, WinProbability/λ | 경기 시작 시 1회 | 저장 안 함(재계산) | FIFA 포지션별 OVR 공식 |
| InMatch | MatchFatigue(stamina/recovery/capacity 3단), cards, 점수, 시나리오 진행 | 10Hz 틱 | 저장 안 함(경기 중 저장 없음) | rcss 3단 모델 |
| Record | Action/Event 로그(SPADL형), Highlight 클립 | 경기 | 경기 결과·통계 | SPADL, Atomic-SPADL, CDF |

### B. 엔티티와 관계 (온톨로지 개요)
- **Player** —hasAttributes→ Attributes / —hasHidden→ HiddenAttributes(consistency, bigMatch, injuryProneness, professionalism, ambition, temperament… 목록은 `[미정]`) / —hasPersonality→ Personality(히든 조합으로 정한 라벨, FM식 파생) / —playsRole→ Role(27 중 주·부 역할, 숙련도) / —hasTendency→ Tendencies(14축, -100~+100 등 `[미정]`) / —hasHabit→ Habit(15, 강도) / —hasState→ DynamicState / —memberOf→ Team / —managedBy→ Manager(간접)
- **Team** —hasSquad→ Player[] / —uses→ Tactics(포메이션, 라인 높이, 압박 강도, 템포, 폭, 역할 배정 슬롯) / —hasCohesion→ Chemistry(선수쌍 친숙도 + 전술 숙련도) / —managedBy→ Manager(전술 성향, 동기부여 능력, 선호 역할)
- **Match** —home/away→ Team / —in→ MatchContext / —produces→ Event[] / —estimates→ TeamStrength×2 → WinProbability
- **Scenario(플레이 패턴)** —requiresRoles→ Role 슬롯 / —triggeredBy→ (게임 상태 + 팀 Tactics + 선수 Tendencies) / —consistsOf→ ActionStep[] / —breaksInto→ Event(interception/out 등, Atomic-SPADL식)
- **Action**(엔진 내부 의도) → **Event**(기록된 사실: SPADL 12필드 + underPressure + relatedEvents + scenarioId/stepIndex)

### C. 인과 흐름 DAG (엔진이 읽는 순서)
```
Catalog + Static + Dynamic + Context
   │  (경기 시작 1회, 순수 함수, 고정소수점)
   ▼
EngineParams(선수별): 
  speedMax, accel, staminaIncMax, kickNoise, passAccuracy, firstTouch, decisionNoise…
  = f(Attributes) × g(condition, sharpness, form) × h(morale, Personality, importance×bigMatch)
  ± consistency 기반 경기 편차(시드 RNG 1회 샘플)
   ▼
TeamStrength = Σ(역할 적합도 × EngineParams) × Chemistry × Tactics 적합 × 홈보정
   → λ(득점 기대) → WinProbability (BN/pi-rating식, 대량 시뮬 보정 목표: 평균 득점 2.5~3.0)
   ▼
10Hz 틱: 시나리오 선택(Tendencies·Habits·Tactics 가중) → Action 실행(EngineParams + MatchFatigue)
   → 실패/붕괴 시 Utility 폴백 → Event 기록
   ▼
경기 후: Event 집계 → 평점·폼·사기·피로·부상(확률) 갱신 → Dynamic(다음 경기) ← 유일한 되먹임
```
- 계산별로 읽는 대상
  - **팀 형태 레이어**: Tactics(라인·폭) + Role + EngineParams.speed/stamina
  - **시나리오 선택**: Tactics, Role 슬롯 매칭, Tendencies, Habits, 게임 상태(점수·시간·game_mode, GRF식)
  - **액션 성공 판정**: EngineParams(기술), 압박 여부(underPressure), MatchFatigue, decisionNoise
  - **피로(경기 중)**: rcss식 stamina/recovery/capacity. 초기 capacity는 condition으로 정한다
  - **부상 확률**: fatigue, condition, illness, injuryProneness, 휴식일. 실증상 피로 효과는 여기에 크게 건다
  - **승률/하이라이트 선정**: TeamStrength, λ

### D. 결정론·순수 C# 규칙
- Derived/InMatch 계산은 UnityEngine을 참조하지 않는 `MatchEngine`/`Core`에서 순수 함수로 한다. 입력 스냅샷을 불변 구조체로 만들고, 같은 입력과 같은 시드면 같은 Event 로그가 나와야 한다.
- 실수 대신 고정소수점 정수를 쓴다. 컬렉션은 ID 정렬 순서로 순회한다. RNG는 경기 시드에서 하위 스트림(선수별 경기 편차, 시나리오 선택, 판정)으로 분기해 호출 순서 간섭을 막는다.
- 파생값은 저장하지 않는다. 그래야 튜닝 테이블을 바꿔도 세이브 호환이 유지된다(리플레이는 엔진 버전 + 튜닝 해시를 함께 기록).

### E. 모드 친화 JSON 스키마 골격(예시, 수치 미정)
```json
{
  "schemaVersion": "1.0.0",
  "kind": "habit",
  "id": "habit.dribble_addict",
  "name": { "ko": "드리블 중독", "en": "Dribble Addict" },
  "effects": [
    { "target": "scenario.weight", "tag": "dribble", "op": "mul", "value": "[미정]" },
    { "target": "action.pass.weight", "op": "mul", "value": "[미정]" }
  ]
}
```
- 공통 규칙: `schemaVersion`, 문자열 `id`(네임스페이스 `mod.<modId>.*`로 충돌 방지), 다국어 `name`, `effects`는 (target, op ∈ add/mul/set, value)만 허용하는 화이트리스트 DSL로 둔다. 임의 코드 실행은 하지 않는다.
- JSON Schema 파일을 레포에 두고 로드할 때 검증한다(CDF validator 방식). 실패하면 해당 모드만 비활성화하고 로그를 남긴다.
- 이벤트 로그 export는 SPADL/CDF 호환 필드명으로 맞춘다. 그러면 외부 분석 도구(socceraction·kloppy)로 대량 시뮬 결과를 검증할 수 있다. 예: xT/VAEP로 "시나리오가 현실 분포와 닮았는지" 비교.
- 실명 정책(A안): 스키마는 실명과 무관하다. 커뮤니티 로스터 가져오기는 같은 스키마로 받는다.
