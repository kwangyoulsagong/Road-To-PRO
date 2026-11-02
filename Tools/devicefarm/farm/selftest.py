"""하네스 셀프테스트 (specs/000 T36): 일부러 깨뜨린 경우가 기대대로 잡히는지 실제 Unity·플레이어로 확인한다.

`self-test all`은 AC-01~16 증거를 모아 specs/000-verification-harness/evidence/report.html을 만든다.
'기대한 실패'가 실제로 실패로 잡히면 그 케이스는 통과다.
"""
import copy
import os
import shutil
import time

from . import (baseline, build, chrome, commands, determinism, logic, logs, paths, perf, preflight, profiles,
               report, runner, scenario)
from .errors import RunnerError, VerificationFailed

SPEC = "000"
REF = "mac-m1-1600p"
SAMPLE = "000-sample-cards"
TMP_ASSETS = os.path.join(paths.REPO, "Assets", "FarmSelfTest_Tmp")
SMOKE_BUDGET_SEC = 300
LOGIC_BUDGET_SEC = 180


class Ctx:
    def __init__(self):
        self.ps = profiles.load()
        self.exe = None
        self.run_dir = commands.new_run_dir("selftest")
        self.base_root = os.path.join(self.run_dir, "baselines")  # 저장소 기준이 아닌 임시 기준
        self.rows = []
        self.runs = []
        self.logic = None
        self.timings = {}
        self.stable_run = None

    def row(self, case, acs, expected, ok, detail="", images=()):
        self.rows.append({"case": case, "acs": list(acs), "expected": expected, "ok": bool(ok),
                          "detail": detail, "images": [i for i in images if i and os.path.exists(i)]})
        commands.log(f"[farm] self-test {case} {'통과' if ok else '실패'} — {expected} | {detail}")

    def ensure_exe(self):
        if not self.exe:
            self.exe, cached, took = build.ensure_build(log=commands.log)
            self.timings["buildCached"] = cached
        return self.exe

    def run(self, sc_id, tier="smoke", label="", **kw):
        sc = scenario.load(sc_id)
        out = os.path.join(self.run_dir, label or sc_id)
        r = commands.run_scenario(self.ensure_exe(), sc, tier, self.ps, out, label="", **kw)
        r["title"] = f"{sc_id} · {label}" if label else sc_id
        return r


def _ref(run):
    return next(c for c in run["combos"] if c["profile"] == REF)


def _caps(run):
    return [x for c in run["combos"] for x in c["captures"]]


# ---- 케이스 ----

def case_logic_overall(ctx):
    t0 = time.time()
    lg, res = commands.logic_section("", "!Slow", ctx.run_dir, name="logic")
    ctx.timings["logicSec"] = time.time() - t0
    ctx.logic = lg
    ctx.row("logic", [], "EditMode 전체(Slow 제외) 실패 0", res.ok,
            f"합계 {res.total} 통과 {res.passed} 실패 {res.failed}, {ctx.timings['logicSec']:.0f}s")


def case_logic_fail(ctx):
    names = ";".join(f"RoadToPro.Tests.EditMode.Verification.SelfTestFixtures.{n}"
                     for n in ("Pass_One", "Fail_Deliberate", "Pass_Two"))
    lg, res = commands.logic_section(names, "", ctx.run_dir, name="selftest-logic-fail")
    with open(lg["html"], encoding="utf-8") as f:
        html = f.read()
    first = html.find("Fail_Deliberate") < html.find("Pass_One")
    ok = res.total == 3 and res.failed == 1 and res.passed == 2 and not res.ok and first and "의도한 실패" in html
    ctx.row("logic-fail", ["AC-01"], "실패 1·통과 2 → 판정 실패, 리포트 맨 위에 실패 메시지", ok,
            f"합계 {res.total} 실패 {res.failed} 통과 {res.passed}, 실패가 먼저: {first}", [lg["png"]])


def case_compile_error(ctx):
    if os.path.isdir(TMP_ASSETS):
        shutil.rmtree(TMP_ASSETS)
    os.makedirs(TMP_ASSETS)
    with open(os.path.join(TMP_ASSETS, "CompileError.cs"), "w", encoding="utf-8") as f:
        f.write("// 셀프테스트 AC-02: 일부러 깨뜨린 코드 (실행 후 지운다)\npublic class FarmSelfTestBroken { void M() { int x = ; } }\n")
    try:
        lg, res = commands.logic_section("NoSuchTest", "", ctx.run_dir, name="selftest-compile-error")
    finally:
        shutil.rmtree(TMP_ASSETS, ignore_errors=True)
        meta = TMP_ASSETS + ".meta"
        if os.path.exists(meta):
            os.remove(meta)
    ok = (not res.ok) and any("error CS" in x for x in res.compile_errors)
    ctx.row("compile-error", ["AC-02"], "컴파일 에러 → 판정 실패, 리포트에 `error CS` 줄", ok,
            (res.compile_errors[0] if res.compile_errors else "컴파일 에러를 못 찾음")[:160], [lg["png"]])


def case_stability(ctx):
    if os.path.isdir(ctx.base_root):
        shutil.rmtree(ctx.base_root)
    ctx.ensure_exe()
    t0 = time.time()
    r1 = ctx.run(SAMPLE, label="1회차", baseline_root=ctx.base_root)
    smoke_sec = time.time() - t0
    ctx.timings["smokeSec"] = smoke_sec

    combos = r1["combos"]
    sizes = []
    for c in combos:
        p = ctx.ps.get(c["profile"])
        for x in c["captures"]:
            from PIL import Image

            with Image.open(x["current"]) as im:
                sizes.append(im.size == (p.width, p.height))
    ctx.row("stability/해상도", ["AC-03"], "smoke 3 프로필 × 캡처, 각 캡처 해상도 = 프로필 해상도",
            len(combos) == 3 and sizes and all(sizes) and all(c["status"] == "passed" for c in combos),
            f"조합 {len(combos)}, 캡처 {len(sizes)}장 해상도 일치 {sum(sizes)}/{len(sizes)}",
            [combos[0]["captures"][0]["current"]] if combos and combos[0]["captures"] else [])
    cands = [x["status"] for x in _caps(r1)]
    ctx.row("stability/기준 후보", ["AC-08"], "기준 이미지가 없으면 '기준 후보'", cands and all(s == "candidate" for s in cands),
            f"{cands.count('candidate')}/{len(cands)} 기준 후보")
    det = r1["determinism"] or {}
    ctx.row("stability/프로필 간 결정론", ["AC-16"], "해상도가 달라도 체크포인트 해시 동일", det.get("ok"),
            f"기준 {det.get('reference')}, 불일치 {len(det.get('mismatches') or {})}")
    fields_ok = True
    for c in combos:
        for row in c["perf"]:
            m = row.get("metrics") or {}
            need = [m.get("frameMs", {}).get("p95"), m.get("gcAllocTotalBytes"), (m.get("drawCalls") or {}).get("max"),
                    (m.get("triangles") or {}).get("max"), m.get("totalMemoryBytes")]
            fields_ok &= all(v is not None for v in need)
    ctx.row("stability/성능 기록", ["AC-09"], "프로필별 p95·GC·드로우콜·삼각형·메모리 기록", fields_ok and all(c["perf"] for c in combos),
            "; ".join(f'{c["profile"]} p95 {c["perf"][0]["metrics"]["frameMs"]["p95"]:.1f}ms' for c in combos if c["perf"] and c["perf"][0].get("metrics")))
    # 빌드가 준비된 뒤(=캐시가 있는 상태)부터 잰 매트릭스 시간. 빌드 시간은 따로 기록한다.
    ctx.row("stability/smoke 시간", ["AC-15"], f"smoke(3 프로필, 빌드 준비된 상태) ≤ {SMOKE_BUDGET_SEC}s",
            smoke_sec <= SMOKE_BUDGET_SEC,
            f"{smoke_sec:.0f}s (이번 실행 빌드: {'캐시 적중' if ctx.timings.get('buildCached') else '새로 빌드 — 시간에서 제외'})")

    done = baseline.approve(_caps(r1), root=ctx.base_root)
    r2 = ctx.run(SAMPLE, label="2회차(임시 기준 승인 후)", baseline_root=ctx.base_root)
    caps2 = _caps(r2)
    zero = all(x["status"] == "pass" and x.get("diffPixels", 1) == 0 for x in caps2)
    ctx.row("stability/2회 동일", ["AC-04"], "같은 커밋 2회 실행 → 모든 캡처 차이 0", zero and len(caps2) == len(cands),
            f"{sum(1 for x in caps2 if x.get('diffPixels') == 0)}/{len(caps2)}장 차이 0")
    ctx.row("stability/승인 후 통과", ["AC-08"], "승인 명령 후 재실행하면 통과", len(done) == len(cands) and r2["verdict"] == "pass",
            f"승인 {len(done)}장 → 판정 {r2['verdict']}")
    ctx.stable_run = r2
    ctx.runs.append(r2)


def case_diff(ctx):
    if ctx.stable_run is None:
        case_stability(ctx)
    r = ctx.run(SAMPLE, label="버튼 색 변경", baseline_root=ctx.base_root, extra_args=["-farmSampleButtonColor", "#ff5000"])
    homes = [x for c in r["combos"] for x in c["captures"] if x["step"] == "home"]
    ok = homes and all(x["status"] == "fail" and x.get("diff") for x in homes)
    local = []
    for c in r["combos"]:
        p = ctx.ps.get(c["profile"])
        for x in c["captures"]:
            if x["step"] == "home" and x.get("bbox"):
                bx, by, bw, bh = x["bbox"]
                local.append(bw < p.width * 0.3 and bh < p.height * 0.2)
    r["expectedFail"] = True
    ctx.runs.append(r)
    ctx.row("diff", ["AC-05"], "버튼 색 변경 → 전 프로필 차이 실패, 차이 이미지가 버튼 영역만 강조",
            ok and local and all(local), f"실패 {sum(1 for x in homes if x['status'] == 'fail')}/{len(homes)}, 강조 영역이 버튼 크기 {sum(local)}/{len(local)}",
            [homes[0].get("diff")] if homes else [])


def case_clip(ctx):
    r = ctx.run(SAMPLE, label="en 넘침", only=[REF], langs=["en"], baseline_root=ctx.base_root)
    c = r["combos"][0]
    ok = c["status"] == "failed" and "assertNoClip" in c["reason"] and "card3Caption" in c["reason"]
    ctx.row("clip", ["AC-06"], "en에서 넘치는 라벨 → '잘림 없음' 단언 실패", ok, c["reason"][:200], c.get("failCaptures", [])[:1])


def case_black(ctx):
    empty = os.path.join(ctx.run_dir, "no-baselines")
    r = ctx.run("000-selftest-black", label="검은 화면", baseline_root=empty, extra_args=["-farmSampleBlack"])
    caps = _caps(r)
    ok = caps and all(x["status"] == "fail" and "단색" in x.get("reason", "") for x in caps)
    ctx.row("black", ["AC-07"], "검은 화면 → 기준 없어도 실패", ok, "; ".join(x.get("reason", "") for x in caps)[:200],
            [caps[0]["current"]] if caps else [])


def _scale(metrics, k):
    m = copy.deepcopy(metrics)

    def walk(d):
        for key, v in d.items():
            if isinstance(v, dict):
                walk(v)
            elif isinstance(v, (int, float)) and key != "frames":
                d[key] = v * k

    walk(m)
    return m


def case_perf(ctx):
    if ctx.stable_run is None:
        case_stability(ctx)
    ref = _ref(ctx.stable_run)["perf"][0]
    m = ref["metrics"]
    over = perf.judge_budget(m, {"frameMs.p95": 0.001})
    under = perf.judge_budget(m, {"frameMs.p95": 1000})
    others = [row for c in ctx.stable_run["combos"] if c["profile"] != REF for row in c["perf"]]
    ctx.row("perf/예산", ["AC-09"], "예산을 넘긴 값은 실패로 표시 (실측 p95를 예산 0.001ms와 1000ms로 판정)",
            not over[0]["ok"] and under[0]["ok"] and all(not r["judged"] for r in others),
            f"실측 p95 {m['frameMs']['p95']:.2f}ms → 예산 0.001: {'실패' if not over[0]['ok'] else '통과'}, 1000: {'통과' if under[0]['ok'] else '실패'}; 비기준 프로필은 기록만")
    slow15 = perf.judge_regression(_scale(m, 1.15), m)
    slow5 = perf.judge_regression(_scale(m, 1.05), m)
    p15 = next(r for r in slow15 if r["metric"] == "frameMs.p95")
    ok = (not p15["ok"]) and all(r["ok"] for r in slow5)
    ctx.row("perf/회귀", ["AC-10"], "실측값 대비 +15% → 회귀 실패, +5% → 통과", ok,
            f"+15%: frameMs.p95 {p15['changePct']}% {'실패' if not p15['ok'] else '통과'}; +5%: {'모두 통과' if all(r['ok'] for r in slow5) else '실패 있음'}")


def case_determinism(ctx):
    a = ctx.run(SAMPLE, label="결정론 A", only=[REF], baseline_root=ctx.base_root)
    b = ctx.run(SAMPLE, label="결정론 B", only=[REF], baseline_root=ctx.base_root)
    c = ctx.run(SAMPLE, label="결정론 C (RNG +1)", only=[REF], baseline_root=ctx.base_root,
                extra_args=["-farmSampleSimExtraRngAtTick", "450"])
    ca, cb, cc = (x["combos"][0]["checkpoints"] for x in (a, b, c))
    same = determinism.compare_runs(ca, cb)
    diff = determinism.compare_runs(ca, cc)
    fd = diff.get("firstDivergence") or {}
    ok = same["ok"] and not diff["ok"] and fd.get("name") == "start/t500"
    ctx.row("determinism", ["AC-11"], "같은 시드 2회 해시 동일, RNG 호출 1개 추가 → 갈라진 첫 체크포인트 보고", ok,
            f"A=B: {same['ok']} ({len(ca)}개), A≠C 첫 갈림: {fd.get('name')} (#{fd.get('index')})")


def case_logs(ctx):
    empty = os.path.join(ctx.run_dir, "no-baselines")
    r1 = ctx.run("000-selftest-logs", label="경고 주입", baseline_root=empty, ignore=[])
    l1 = r1["combos"][0]["logs"]
    ign = logs.compile_ignore([{"pattern": "셀프테스트: 경고 주입", "reason": "셀프테스트 AC-12: 의도적으로 주입한 경고"}])
    r2 = ctx.run("000-selftest-logs", label="경고 주입 + 무시 목록", baseline_root=empty, ignore=ign)
    l2 = r2["combos"][0]["logs"]
    ok1 = (not l1["ok"]) and any("경고 주입" in w["message"] for w in l1["warnings"])
    ok2 = l2["ok"] and any(i["reason"].startswith("셀프테스트 AC-12") for i in l2["ignored"])
    r1["expectedFail"] = True
    ctx.runs.append(r1)
    ctx.row("logs", ["AC-12"], "경고 1개 → 실패·로그 요약에 표시 / 사유 있는 무시 목록 → 통과·무시 항목 표시", ok1 and ok2,
            f"무시 전: 경고 {len(l1['warnings'])}건 {'실패' if not l1['ok'] else '통과'} / 무시 후: {'통과' if l2['ok'] else '실패'}, 무시 {len(l2['ignored'])}건")


def case_crash(ctx):
    exe = ctx.ensure_exe()
    combos = ctx.ps.expand("smoke")
    crash = scenario.load("000-selftest-crash")
    sample = scenario.load(SAMPLE)
    jobs = [runner.Job(c, paths.scenario_file(crash.id if i == 0 else sample.id), sample.fixture["seed"])
            for i, c in enumerate(combos)]
    res = runner.run_matrix([exe], jobs, os.path.join(ctx.run_dir, "crash"), ctx.ps, log=commands.log)
    st = [r.status for r in res]
    ok = st[0] == "crashed" and all(s == "passed" for s in st[1:])
    ctx.row("crash", ["AC-13"], "한 프로필 크래시 → 그 조합만 실패, 나머지 정상", ok,
            ", ".join(f"{r.combo.profile.id}: {r.status}" for r in res))


CASES = {
    "logic-fail": case_logic_fail,
    "compile-error": case_compile_error,
    "stability": case_stability,
    "diff": case_diff,
    "clip": case_clip,
    "black": case_black,
    "perf": case_perf,
    "determinism": case_determinism,
    "logs": case_logs,
    "crash": case_crash,
}


def _evidence(ctx, meta, extra_rows=()):
    info = commands.spec_info(SPEC)
    rows = ctx.rows + list(extra_rows)
    ac_ev = {}
    for r in rows:
        for ac in r["acs"]:
            ac_ev.setdefault(ac, []).append({"label": f"self-test {r['case']}", "ok": r["ok"]})
    budgets = []
    if "logicSec" in ctx.timings:
        budgets.append({"item": "로직만 검증 (EditMode, Slow 제외)", "budget": f"≤ {LOGIC_BUDGET_SEC}s",
                        "value": f"{ctx.timings['logicSec']:.0f}s", "ok": ctx.timings["logicSec"] <= LOGIC_BUDGET_SEC})
    if "smokeSec" in ctx.timings:
        budgets.append({"item": "smoke 매트릭스 (3 프로필, 빌드 캐시)", "budget": f"≤ {SMOKE_BUDGET_SEC}s",
                        "value": f"{ctx.timings['smokeSec']:.0f}s", "ok": ctx.timings["smokeSec"] <= SMOKE_BUDGET_SEC})
    all_ok = all(r["ok"] for r in rows) and (ctx.logic is None or ctx.logic["ok"])
    keys = sorted({c["key"] for r in ctx.runs for c in r["combos"]})
    meta = dict(meta, profiles=keys)
    return {"title": "000 검증 하네스 · 디바이스팜 증거", "spec": info, "meta": meta, "logic": ctx.logic,
            "scenarios": ctx.runs, "selftests": rows, "acEvidence": ac_ev, "budgets": budgets, "requireAllAcs": True,
            "verdictOverride": "pass" if all_ok and all(ac_ev.get(a["id"]) for a in info["acs"]) else "fail"}


def main(case, argv):
    preflight.run_all(need_chrome=True)
    t0 = time.time()
    with preflight.RunLock():
        ctx = Ctx()
        names = list(CASES) if case == "all" else [case]
        if case == "all":
            case_logic_overall(ctx)
        for n in names:
            commands.log(f"[farm] ── self-test {n}")
            try:
                CASES[n](ctx)
            except (RunnerError, OSError, KeyError, StopIteration, IndexError) as err:
                ctx.row(n, [], "케이스 실행", False, f"{type(err).__name__}: {err}")
        meta = logic.metadata()
        meta.update(durationSec=time.time() - t0, command=commands.command_line(argv))
        out = commands.evidence_dir(SPEC, ctx.run_dir) if case == "all" else os.path.join(ctx.run_dir, "evidence")
        ev = _evidence(ctx, meta)
        path = report.write(ev, out)
        if case == "all":
            # AC-14: 리포트 자체를 400px 폭에서 검사하고, 상단 정보·발행 링크를 확인한다
            layout = chrome.layout_check(path, 400)
            shot = chrome.screenshot(path, os.path.join(ctx.run_dir, "report-400.png"), width=400, height=6000)
            with open(path, encoding="utf-8") as f:
                html = f.read()
            head_ok = meta["commit"][:10] in html and meta["branch"] in html and meta["time"] in html
            url_file = os.path.join(out, "artifact-url.txt")
            url = open(url_file, encoding="utf-8").read().strip() if os.path.exists(url_file) else ""
            row = {"case": "report", "acs": ["AC-14"], "expected": "400px에서 가로 스크롤 없음, 상단에 커밋·브랜치·시각, 아티팩트 링크",
                   "ok": layout == "ok" and head_ok and bool(url),
                   "detail": f"레이아웃 {layout}, 상단 정보 {head_ok}, 링크 {url or '아직 발행 전'}", "images": [shot]}
            ev = _evidence(ctx, meta, [row])
            path = report.write(ev, out)
            size = sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(out) for f in fs if not f.endswith(".json"))
            commands.log(f"[farm] 리포트 크기 {size / 1024 / 1024:.1f} MB")
    verdict = report.verdict_of(ev)
    commands.log(f"[farm] 셀프테스트 판정: {verdict} — {path}")
    failed = [r["case"] for r in ev["selftests"] if not r["ok"]]
    if verdict != "pass":
        raise VerificationFailed(f"실패 케이스: {', '.join(failed) or '(AC 증거 누락)'}")
