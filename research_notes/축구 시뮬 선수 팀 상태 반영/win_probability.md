# 팀·선수 강도로 경기 승률·득점 분포 추정하는 방법 (리서치 노트, 2026-11-11)

> 표기: "출처 미검증"은 이번 세션에서 원문을 열지 못하고 일반 지식으로 적은 항목이다. 보고서에 쓸 때는 따로 표시하거나 원문을 확인해야 한다. clubelo.com(403), eloratings.net(JS만 내려옴), fivethirtyeight.com(abcnews로 리다이렉트), web.archive.org(차단)는 원문 접근에 실패했다.

## 1. 레이팅 시스템: Elo(World Football Elo, ClubElo), Pi-ratings, Glicko

### Takeaway
World Football Elo는 `Rn = Ro + K·G·(W−We)`에 홈 +100점, 골 차 배수 G(1 / 1.5 / (11+N)/8)를 쓴다. Pi-rating은 승패가 아니라 골 차 오차로 홈/원정 레이팅을 따로 갱신하며(λ=0.035, γ=0.7), 2017 Soccer Prediction Challenge 1위 모델의 핵심 입력이었다. ClubElo의 공식 파라미터는 원문 접근에 실패해 확인하지 못했다.

### Cited Findings
- World Football Elo 갱신식: `Rn = Ro + P`, `P = K × G × (W − We)`. P는 정수로 반올림한 뒤 반영한다 — [Wikipedia: World Football Elo Ratings](https://en.wikipedia.org/wiki/World_Football_Elo_Ratings)
- K(경기 비중): 월드컵·올림픽(1908–1980) 60 / 대륙 선수권·대륙간 대회 50 / 월드컵·대륙 예선 및 주요 대회 40 / 기타 대회 30 / 친선 20 — [Wikipedia](https://en.wikipedia.org/wiki/World_Football_Elo_Ratings)
- G(골 차 배수, N=골 차): 무승부·1골 차 1, 2골 차 1.5, 3골 차 이상 `(11+N)/8`(N=3이면 1.75) — [Wikipedia](https://en.wikipedia.org/wiki/World_Football_Elo_Ratings)
- 홈 이점: 홈 팀의 레이팅 차 dr에 100점을 더한다. 기대 결과 `We = 1/(10^(−dr/400) + 1)`. 위키 페이지의 수식 렌더링이 깨져 있어 예시값(dr=0이면 0.5, dr=120이면 ≈0.666)으로 복원한 식이다. W는 승 1 / 무 0.5 / 패 0이고, 승부차기로 끝난 경기는 무승부(0.5)로 친다 — [Wikipedia](https://en.wikipedia.org/wiki/World_Football_Elo_Ratings)
- FIFA가 쓰는 Elo 변형에는 홈 이점 항이 없다 — [Wikipedia](https://en.wikipedia.org/wiki/World_Football_Elo_Ratings) (검색 요약 기준)
- ClubElo: K=20, 프리미어리그 홈 이점 약 53점이라는 2차 출처가 있지만 clubelo.com에서 직접 확인하지는 못했다 — [statsultra](https://statsultra.com/?p=2969). 다른 2차 문헌은 관례값인 홈 이점 100점을 쓴다 — [Berkeley write-up (Xiaochen Yang)](https://www.stat.berkeley.edu/~aldous/Research/Ugrad/Xiaochen_Yang_write-up.pdf)
- Pi-ratings(Constantinou & Fenton 2013, JQAS, doi:10.1515/jqas-2012-0036): 팀마다 홈 레이팅과 원정 레이팅을 따로 둔다. λ(최근 경기 학습률, 기본 0.035)로 해당 장소 레이팅을 갱신하고, γ(홈↔원정 전이 학습률, 기본 0.7)를 곱한 같은 오차항으로 반대 장소 레이팅을 갱신한다. 기대 골 차는 `sign(R)·(b^{|R|/c} − 1)`이고 b=10, c=3이다 — [CRAN piratings 문서](https://cran.r-project.org/web/packages/piratings/refman/piratings.html), [소스](https://rdrr.io/cran/piratings/src/R/calculate_pi_ratings.R)
- EPL 데이터로 그리드 서치하면 λ=0.06, γ=0.6이 최적이었다(기본값과 다름) — [piratings vignette](https://cran.r-project.org/web/packages/piratings/vignettes/README.html)
- Opta Power Rankings: 결과와(가능하면) xG를 입력으로 쓰는 계층형 Elo 기반 시스템. 0~100 스케일이고 매일 갱신하며, 190개국 600개 이상 리그를 다룬다 — [The Analyst: Opta Power Rankings](https://theanalyst.com/articles/opta-power-rankings/)

### Inferences
- Elo 차이를 승·무·패 3지선다 확률로 바꾸려면 별도 모델이 필요하다. 기대 점수 We는 "승 + 0.5×무"일 뿐이다. 무승부 확률은 순서형 로짓이나 Poisson 변환으로 따로 정해야 한다.
- Pi-rating은 골 차 기반이라 xG나 시뮬 득점으로 바로 갱신할 수 있다. 게임 내 리그 레이팅(팀 순위표 뒤에서 도는 강도)으로 쓰기 좋다.

### Gaps
- ClubElo 공식 파라미터: K, 홈 이점(리그별 동적 여부), 골 차 처리, 승·무·패 확률 변환 방식. 원문 403으로 확인 실패.
- Glicko/Glicko-2를 축구에 적용한 파라미터 자료는 찾지 못했다(검색 예산 부족).
- Pi-rating의 정확한 오차 가중 함수 ψ(골 차 오차의 로그 압축). 논문 원문 미확인.

## 2. 골 모델: Poisson/Maher, Dixon–Coles, 이변량 Poisson, Skellam

### Takeaway
표준 형태는 `λ_home = exp(공격_h + 수비_a + 홈)`, `μ_away = exp(공격_a + 수비_h)`의 독립 Poisson이다. Dixon–Coles는 0-0, 1-0, 0-1, 1-1에 τ 보정(ρ≈−0.13)을 하고 지수 시간 감쇠 exp(−ξt)를 건다. EPL 적합값은 홈 이점 ≈0.29(로그, ×1.34)였다.

### Cited Findings
- Maher형 독립 Poisson: `λ = exp(α_home + β_away + γ_home)`, `μ = exp(α_away + β_home)`. 식별을 위해 평균 공격력을 고정하는 제약을 둔다 — [dashee87: Dixon-Coles and time weighting](https://dashee87.github.io/football/python/predicting-football-results-with-statistical-modelling-dixon-coles-and-time-weighting/)
- Dixon–Coles τ: `τ(0,0)=1−λμρ`, `τ(0,1)=1+λρ`, `τ(1,0)=1+μρ`, `τ(1,1)=1−ρ`, 그 밖의 스코어는 1. 우도는 `log τ + log Pois(x;λ) + log Pois(y;μ)` — [dashee87](https://dashee87.github.io/football/python/predicting-football-results-with-statistical-modelling-dixon-coles-and-time-weighting/)
- EPL 2017/18 적합값: ρ = −0.1285(opisthokonta 블로그의 ≈−0.13과 일치), 홈 이점 0.2945(로그 척도, ×≈1.34. GLM 적합은 0.2888), 공격 0.47~1.79, 수비 −1.52~−0.64(낮을수록 강함) — [dashee87](https://dashee87.github.io/football/python/predicting-football-results-with-statistical-modelling-dixon-coles-and-time-weighting/)
- 시간 감쇠: 경기마다 우도에 `exp(−ξt)`(t=경과 일수)를 곱한다. ξ는 우도로 추정하면 발산하므로 예측 성능으로 그리드 서치한다. 한 시즌 데이터에서는 ξ=0이 최적, 5시즌 데이터에서는 ξ≈0.00325/일이 최적이었다. Dixon–Coles 원논문은 반주(half-week) 단위로 0.0065를 보고했다. 일 단위 환산값은 출처마다 0.0018~0.00325로 엇갈리며, 블로그 자체의 환산 설명에도 불일치가 있다 — [dashee87](https://dashee87.github.io/football/python/predicting-football-results-with-statistical-modelling-dixon-coles-and-time-weighting/)
- DC 보정은 무승부 확률을 올린다. 예: Arsenal–Southampton 무승부 확률이 Poisson 0.167에서 DC 0.186으로 바뀐다 — [dashee87](https://dashee87.github.io/football/python/predicting-football-results-with-statistical-modelling-dixon-coles-and-time-weighting/)
- FiveThirtyEight SPI의 경기 확률: 리그별 홈 이점으로 조정한 예상 스코어 주위에 독립 Poisson 2개를 놓고 스코어 행렬을 만들어 승·무·패를 합산한다 — [FiveThirtyEight 방법론(검색 스니펫)](https://fivethirtyeight.com/features/how-our-club-soccer-predictions-work). 무승부 보정이 리그별 약 9%였다는 내용은 2차 슬라이드에만 있다 — [Clement Lee slides](https://clement-lee.github.io/slides/sports_20230428.pdf)

### Inferences
- 독립 Poisson은 저득점 무승부를 과소평가한다. 게임 시뮬 결과가 무승부 비율(≈25%)에 못 미치면 DC 식의 저득점 상관이나 무승부 inflation이 처방이 된다.
- 홈 이점 로그 0.29는 xG(득점 기대) 기준으로 약 +34%이다. 리그 평균 2.7골이면 대략 홈 1.5골, 원정 1.2골 정도로 나뉜다(추정).
- Skellam: 두 독립 Poisson의 차 `D = X − Y`는 Skellam(λ, μ) 분포를 따르고 `P(D=k) = e^{−(λ+μ)} (λ/μ)^{k/2} I_{|k|}(2√(λμ))`이다. 승률 = P(D>0), 무 = P(D=0). (표준 정의, 출처 미검증)

### Gaps
- Karlis & Ntzoufras(2003, JRSS D)의 이변량 Poisson(공분산 λ3, 대각 inflation) 적합값은 원문을 확인하지 못했다(출처 미검증 서지).
- Baio & Blangiardo(2010, J. Applied Statistics) 베이지안 계층 모델(공격·수비 랜덤효과, 혼합 사전분포로 과축소 방지)의 상세 수치도 원문 미확인.
- Maher(1982) 원문의 파라미터.

## 3. xG 기반 팀 강도·시뮬, 선수 기반 팀 강도(SPI, Opta, VAEP, EA 팀 OVR)

### Takeaway
SPI는 공격력(중립 경기장에서 평균 팀 상대로 넣을 골)과 수비력을 실제 골, 보정 골, 슈팅 xG, 비슈팅 xG를 섞어서 만든다. 보정 골은 수적 우위 상황의 골과 리드 중 막판 골의 가치를 깎는다. 11명 능력치를 팀 강도로 합치는 공식은 상용 게임 쪽에 공개된 것이 거의 없고, 커뮤니티 추정만 있다(FIFA 17: 18인 평균 + 보정, 선발 가중).

### Cited Findings
- SPI 공격 레이팅 = 골, 보정 골, 슈팅 기반 xG, 비슈팅 xG의 혼합. 의미는 "중립 경기장에서 평균 팀을 상대로 기대되는 득점" — [FiveThirtyEight(검색 스니펫)](https://fivethirtyeight.com/features/how-our-club-soccer-predictions-work)
- 보정 골: 수적 우위일 때 넣은 골과 이미 리드한 상태의 막판 골은 가치를 낮추고, 나머지 골은 가치를 올린다 — 같은 출처
- 비슈팅 xG: 상대 골문 근처의 패스, 인터셉트, 테이크온, 태클로 "넣었어야 할 골"을 추정한다 — 같은 출처
- SPI 방법론 페이지는 현재 원문 접근 불가다(fivethirtyeight.com이 abcnews.com으로 리다이렉트). 검색 결과도 해당 글이 구버전 예측 방법론이라고 적고 있다 — [검색 결과 메모](https://fivethirtyeight.com/features/how-our-club-soccer-projections-work)
- EA FC 팀 OVR: EA의 공식 공식은 찾지 못했다. FIFA 17 커뮤니티 가이드는 `SR = AR(18인 평균) + CF(보정)`이고 교체 선수의 보정은 1/2, 선발 11명이 더 무겁다고 설명한다 — [fifauteam FIFA 17 Squad Rating Guide](https://fifauteam.com/fifa-17-squad-rating-guide/). FC Mobile 비공식 계산기의 추정은 `선발 가중평균×0.85 + 벤치 평균×0.10 + …`이다 — [everycalculators](https://everycalculators.com/fc-mobile-overall-calculator.html). EA 포럼에는 공·미·수 라인별 레이팅 산식에 대한 질문만 있고 공식 답은 없다 — [EA Forums](https://forums.ea.com/discussions/fc-24-general-discussion-en/how-are-the-team-ratings-for-each-part-of-the-squad-attmiddef-being-calculat/7731493/replies/7731494)

### Inferences
- 게임용 팀 강도는 "포지션별 관련 능력치 가중합 → 라인별(공·미·수·GK) 점수 → 공격 지수, 수비 지수"로 만들고, 이를 Maher의 α, β(로그 척도)로 매핑하면 Poisson 승률 계산과 바로 이어진다.
- 핵심 선수 결장 효과는 SPI 계열처럼 선수 단위 기여(분당 xG 기여 등)를 빼서 α, β를 다시 계산하는 방식이 자연스럽다.

### Gaps
- SPI 원문의 혼합 가중치, 선수·라인업 조정(2017-18 이후 추가된 것으로 알려짐), 프리시즌 레이팅(이적 시장가치 반영) 수치는 원문 접근 실패로 미확인.
- 축구 RAPM/plus-minus, VAEP(Decroos et al. 2019, KDD)·xT(Karun Singh)의 팀 강도 집계 방식은 이번 세션에서 검색하지 못했다(예산 소진).
- Football Manager의 팀 강도·배당 표시 방식에 대한 공개 설명은 찾지 못했다.

## 4. 인플레이 승률(남은 시간, 스코어, 레드카드)

### Takeaway
인플레이 모델은 대체로 남은 시간에 비례하는 잔여 득점률(λ·남은 분/90)에 현재 스코어 차를 더해 Poisson으로 계산한다. 레드카드를 받으면 해당 팀 득점 강도가 약 30% 줄고, 시장 내재 승률은 약 12%p 떨어진다는 보고가 있다.

### Cited Findings
- Maia et al.(2026): 3,039경기·레드카드 715장 분석에서 수적 열세 팀의 득점 강도(goal intensity)가 30% 감소했다. AFT 인플레이 모델에서는 레드카드가 단일 공변량 중 개선 폭이 가장 컸다. 반면 Leriou & Ntzoufras(2025)는 380경기·레드 47장의 작은 표본에서 약한 증거만 찾았다 — [arXiv 2605.16066](https://arxiv.org/pdf/2605.16066)
- 분데스리가 베팅시장에서 레드카드는 내재 승률을 약 12%p 낮췄다 — [arXiv 2505.21275](https://arxiv.org/pdf/2505.21275)
- 경기 중 행동 발생률은 경기 단계와 주요 이벤트에 따라 달라진다(대규모 인게임 예측) — [arXiv 2511.18730](https://arxiv.org/pdf/2511.18730v1)
- GAM 기반 카운트 모델의 공변량: 스코어 차, 레드카드 차, 홈/원정, 경기 전 배당에서 뽑은 상대 강도, 경기 분 — [arXiv 2508.04008](https://arxiv.org/html/2508.04008v1)

### Inferences
- 인플레이 승률(추정식): `λ_rem = λ_pre · w(t) · r_card`, 여기서 `w(t) ≈ 남은 분/90`(막판 득점률 상승 반영 시 가중), `r_card ≈ 0.7`(열세 팀). 이후 `P(승) = Σ P(현재차 + X − Y > 0)`.
- 하이라이트의 "결정적 순간"을 재시뮬할 때 이 식으로 전후 승률 변화(WPA)를 계산해 중요도를 매길 수 있다.

### Gaps
- 시간대별 득점률 곡선(전반 대비 후반 증가율, 추가시간)의 정량 수치는 이번 검색에서 확보하지 못했다.

## 5. 리그 기준치(골, 홈 승, 무승부)와 게임의 보정

### Takeaway
유럽 5대 리그 홈 승률은 44~46%, 홈 득점 우위는 경기당 +0.3~0.4골이다. 63개 리그 평균 홈 승률은 44.3%(2015~2020.3)였고, 무관중 시기에는 42.2%로 떨어졌다. 무승부 비율은 잉글랜드에서 1960년대 이후 거의 일정하다.

### Cited Findings
- 63개 리그 홈 승률 44.3%(2015.1~2020.3) → 무관중 이후 42.2%. 평균 골 차 +0.31 → +0.24 — [CIES Football Observatory WP304](https://football-observatory.com/IMG/sites/b5wp/2020/wp304/en/)
- 5대 리그 홈 승률 44~46%, 홈 팀이 0.3~0.4골 더 넣는다 — [Barça Innovation Hub](https://elearning.barcainnovationhub.com/matches-without-an-audience-did-the-advantage-of-being-a-home-team-disappear-in-football/)
- EPL 홈 승률: 2019/20 45.3% → 2020/21(무관중) 37.2% — [Cyprus Mail / 2021](https://cyprus-mail.com/2021/01/12/home-comforts-continue-to-decline-as-stadiums-remain-empty)
- 잉글랜드 홈 승률은 장기적으로 약 60%에서 21세기 45% 미만으로 떨어졌다. 무승부 비율은 1960년대에 뛰어오른 뒤 평탄하다 — [Racing Post](https://racingpost.com/sport/opinion/is-the-decrease-in-home-wins-in-the-premier-league-the-new-normal-azHrZ0w3mly3), [eightyfivepoints](https://eightyfivepoints.blogspot.com/2016/08/home-advantage-home-advantage-is-much.html)

### Inferences
- 보정 목표치: 홈 승 ≈44~46%, 무 ≈25%, 원정 승 ≈29~31%, 경기당 2.5~3.0골, 홈−원정 득점 차 +0.3~0.4. 무승부 25%는 이번 세션에서 직접 출처를 확보하지 못했다(아래 Gaps).

### Gaps
- 최근 시즌(2024/25~2025/26) 리그별 경기당 득점과 무승부 비율의 1차 출처(football-data.co.uk, FBref 집계)를 확보하지 못했다.
- 게임(FM, FC)이 시뮬을 실제 리그 통계에 맞춘다는 공개 개발자 자료는 찾지 못했다.

## 6. 보정·평가 지표(Brier, RPS, log loss)와 벤치마크

### Takeaway
축구 3지선다 예측의 표준 지표는 RPS(순서 반영)다. 2017 Soccer Prediction Challenge에서 상위 모델은 평균 RPS 약 0.205~0.206, 정확도 약 52%였다. 1위는 pi-rating과 PageRank 레이팅을 입력으로 쓴 XGBoost(Hubáček et al.)였다.

### Cited Findings
- 2017 Soccer Prediction Challenge 순위: 1위 Team OH(Hubáček et al.), 2위 Constantinou(pi-rating 변형 + 하이브리드 베이지안 네트워크), 3위 Tsokos et al. 평가 지표는 평균 RPS — [Machine Learning 저널 editorial](https://link.springer.com/article/10.1007/s10994-018-5763-8), [arXiv 2403.07669 서베이](https://arxiv.org/pdf/2403.07669)
- pi-rating + XGBoost는 평균 RPS 0.2063, 정확도 52.43%. Berrar 레이팅 + XGBoost는 0.2054, pi-rating + kNN은 0.2149(서베이 표 기준, 어느 변형이 최상위인지에 대한 진술은 출처마다 엇갈림) — [arXiv 2403.07669](https://arxiv.org/pdf/2403.07669)

### Inferences
- 정의(표준, 출처 미검증): RPS = `1/(r−1) Σ_{i=1}^{r−1} (Σ_{j≤i}(p_j − o_j))²` (r=3, 순서 홈승·무·원정승). Brier = `Σ (p_i − o_i)²`. LogLoss = `−log p_{실제 결과}`.
- 정확도 약 52%, RPS 약 0.20이 실제 축구 예측의 천장에 가깝다. 게임 내 "경기 전 승률"이 현실처럼 느껴지려면 시뮬 결과를 이 승률로 RPS 평가했을 때 0.19~0.22 정도가 나와야 한다. 너무 낮으면(결과가 너무 예측 가능하면) 축구답지 않다(추정).

### Gaps
- 북메이커 배당의 RPS(일반적으로 가장 강한 기준선) 수치는 원문으로 확인하지 못했다.

## Road To PRO에 적용할 점

결정론 엔진, 대량 헤드리스 시뮬 평균 2.5~3.0골, 11명 능력치 → 팀 강도와 경기 전 승률, 결정적 순간 재시뮬을 전제로 정리했다.

1. **11명 → 공격·수비 지수 → Poisson λ, μ.** 포지션·역할별 관련 능력치(0~100) 가중합으로 선수 기여를 구하고, 라인별로 합쳐 팀 공격 지수 A와 수비 지수 D를 만든다. 매핑은 `λ_home = exp(c + a·(A_h − D_a) + h)`, `μ = exp(c + a·(A_a − D_h))` 형태가 좋다. c는 리그 평균 골(2.7이면 팀당 1.35, `c ≈ ln 1.35`)에 맞추고, h ≈ 0.25~0.30을 출발점으로 삼는다(dashee87의 EPL 0.29와 5대 리그 홈 득점 우위 +0.3~0.4골). 계수 a는 헤드리스 대량 시뮬 결과에 회귀해서 정한다.
2. **경기 전 승률은 "엔진 결과"로 보정한다.** 해석식(Poisson + DC τ, ρ≈−0.13)으로 바로 표시하되, 같은 매치업을 헤드리스로 N회 돌린 경험 분포와 RPS·보정 곡선(reliability)으로 비교한다. 목표 기준치는 홈 승 44~46%, 무승부 약 25%, 홈−원정 +0.3~0.4골, 평균 2.5~3.0골이다. 시나리오 엔진은 독립 Poisson이 아니어서 무승부·저득점 비율이 어긋날 수 있다. 어긋나면 표시 승률 쪽을 엔진에 맞춰 보정(로지스틱 재보정 테이블)하는 편이 결정론 엔진을 건드리지 않아 안전하다.
3. **결장·교체 반영.** 선수별 기여를 빼고 A, D를 다시 계산한다. SPI가 라인업을 반영하듯 출전 11명 기준으로 매 경기 계산하고, 주전 결장 시 승률 변화(Δp)를 UI에 "핵심 선수 결장 −x%p"로 보여줄 수 있다.
4. **리그 순위 뒤 레이팅에는 Pi-rating이나 Elo를 쓴다.** 시뮬 득점(또는 시뮬 xG)으로 갱신하는 pi-rating(λ=0.035~0.06, γ=0.6~0.7)이나 Elo(K=20~40, 홈 +50~100, 골 차 배수 G)를 시즌 간 팀 강도 추이와 프리시즌 예측에 쓴다. 모두 정수·고정소수점 연산으로 결정론을 유지할 수 있다. Elo의 `10^(x/400)`과 Poisson의 `exp`는 플랫폼 간 부동소수점 차이가 날 수 있으니 룩업 테이블이나 고정 소수점 구현이 필요하다(추정).
5. **결정적 순간 재시뮬과 인플레이 승률.** `λ_rem = λ_pre × (남은 분/90) × r`로 계산한다(레드카드 열세 팀은 r≈0.7, Maia et al. 2026의 −30%). 현재 스코어에서 출발하는 Skellam/Poisson 승률로 각 이벤트의 WPA(승률 변화)를 구해 하이라이트 선정 점수에 쓸 수 있다. 재시뮬은 같은 시드 분기를 써서 결정론을 지킨다.
6. **성능.** Poisson 스코어 행렬(0~10골 × 0~10골)은 수백 번의 곱셈이면 끝나서 경기 전 승률 표시에 부담이 없다. 대량 시뮬은 지표(평균 골, 홈/무/원정 비율, RPS)를 자동 리포트로 남겨 스펙의 `[성능]`·`[로직]` AC 근거로 쓴다.
7. **지어내지 말아야 할 수치.** EA/FM의 팀 OVR 공식은 공개되지 않았다(커뮤니티 추정뿐). 가중치는 우리 엔진 시뮬로 직접 적합하고, 스펙에서는 `[미정]`/`[제안]`으로 둔다.
