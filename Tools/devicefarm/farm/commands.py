"""farm 명령 처리 (specs/000 FR-80): logic / smoke / full / build / approve-baseline / approve-perf."""
import datetime as _dt
import glob
import json
import os
import re
import time

from . import baseline, build, chrome, evaluate, logic, logs, paths, perf, preflight, profiles, report, runner, scenario
from .errors import RunnerError, VerificationFailed

LATEST = os.path.join(paths.RUNS_DIR, "latest.json")


def log(msg):
    print(msg, flush=True)


def new_run_dir(kind):
    rid = _dt.datetime.now().strftime("%Y%m%d-%H%M%S") + f"-{kind}"
    d = os.path.join(paths.RUNS_DIR, rid)
    os.makedirs(d, exist_ok=True)
    return d


def spec_info(spec):
    d = paths.spec_dir(spec) if spec else None
    if not d:
        return {"id": spec or "-", "name": "", "acs": [], "dir": None}
    with open(os.path.join(d, "spec.md"), encoding="utf-8") as f:
        text = f.read()
    title = text.splitlines()[0].lstrip("# ").strip()
    acs = [
        {"id": m.group(1), "kind": m.group(2), "text": m.group(3).strip()}
        for m in re.finditer(r"^- (AC-\d+) \[([^\]]+)\]: (.+)$", text, re.M)
    ]
    return {"id": spec, "name": title, "acs": acs, "dir": d}


def evidence_dir(spec, run_dir):
    info = spec_info(spec) if spec else None
    if info and info["dir"]:
        return os.path.join(info["dir"], "evidence")
    return os.path.join(run_dir, "evidence")


def command_line(argv):
    return "python3 Tools/devicefarm/farm.py " + " ".join(argv)


# ---- logic ----

def logic_section(test_filter="", category="!Slow", out_dir=None, name="logic"):
    """EditMode 실행 → logic.html·PNG. 리포트의 logic 항목 dict를 돌려준다."""
    meta = logic.metadata()
    res, _ = logic.run(test_filter, category)
    out_dir = out_dir or new_run_dir("logic")
    html_path = os.path.join(out_dir, f"{name}.html")
    with open(html_path, "w", encoding="utf-8") as f:
        f.write(logic.render_html(res, meta))
    png = chrome.screenshot(html_path, os.path.join(out_dir, f"{name}.png"))
    d = res.to_dict()
    d["png"] = png
    d["html"] = html_path
    return d, res


def cmd_logic(args, argv):
    preflight.run_all(need_chrome=True)
    t0 = time.time()
    with preflight.RunLock():
        run_dir = new_run_dir("logic")
        log("[farm] 로직 테스트 (EditMode) 실행 중…")
        lg, res = logic_section(args.filter or "", args.category, run_dir)
        meta = logic.metadata()
        meta.update(durationSec=time.time() - t0, command=command_line(argv), profiles=[])
        info = spec_info(args.spec)
        ev = {"title": f"{info['id']} 로직 증거", "spec": info, "meta": meta, "logic": lg, "scenarios": [],
              "acEvidence": {a["id"]: [{"label": "EditMode 테스트", "ok": lg["ok"]}] for a in info["acs"] if a["kind"] == "로직"}}
        path = report.write(ev, evidence_dir(args.spec, run_dir))
    log(f"[farm] 합계 {res.total} 통과 {res.passed} 실패 {res.failed} 컴파일 에러 {len(res.compile_errors)}")
    log(f"[farm] 리포트: {path}")
    if not res.ok:
        raise VerificationFailed("로직 테스트 실패")


# ---- matrix ----

def scenarios_for(args):
    if args.scenario:
        return [scenario.load(args.scenario)]
    out = []
    for p in sorted(glob.glob(os.path.join(paths.DATA_E2E, "*.json"))):
        name = os.path.basename(p)
        if name in ("profiles.json", "log-ignore.json", "perf-baselines.json"):
            continue
        s = scenario.load(p)
        if s.self_test:
            continue
        if args.spec and s.spec != args.spec:
            continue
        out.append(s)
    if not out:
        raise RunnerError("돌릴 E2E 시나리오가 없다 (--scenario 또는 --spec 확인)")
    return out


def run_scenario(exe, sc, tier, ps, run_dir, only=None, langs=None, extra_args=None, baseline_root=None,
                 ignore=None, label="", seed=None):
    combos = ps.expand(tier, selector=sc.profiles, only=only, langs=langs or sc.langs or None)
    jobs = [runner.Job(c, paths.scenario_file(sc.id), seed if seed is not None else sc.fixture["seed"],
                       extra_args=list(extra_args or []), label=label) for c in combos]
    results = runner.run_matrix([exe], jobs, run_dir, ps, log=log)
    return evaluate.evaluate_run(results, sc, ps, baseline_root=baseline_root,
                                 ignore=logs.load_ignore() if ignore is None else ignore)


def cmd_matrix(tier, args, argv):
    preflight.run_all(need_chrome=True)
    t0 = time.time()
    with preflight.RunLock():
        ps = profiles.load()
        scs = scenarios_for(args)
        exe, cached, _ = build.ensure_build(log=log)
        run_dir = new_run_dir(tier)
        runs = [run_scenario(exe, sc, tier, ps, run_dir, only=args.profile) for sc in scs]
        meta = logic.metadata()
        keys = sorted({c["key"] for r in runs for c in r["combos"]})
        meta.update(durationSec=time.time() - t0, command=command_line(argv), profiles=keys, buildCached=cached)
        info = spec_info(args.spec or (scs[0].spec if len(scs) == 1 else None))
        ac_ev = {}
        for sc, r in zip(scs, runs):
            for ac in (sc.raw or {}).get("acs", []):
                # 기준 후보는 실패가 아니라 승인 대기 — 전체 판정이 'candidate'로 남는다
                ac_ev.setdefault(ac, []).append({"label": f"{sc.id} ({tier}) {r['verdict']}", "ok": r["verdict"] != "fail"})
        ev = {"title": f"{info['id']} 디바이스팜 증거 ({tier})", "spec": info, "meta": meta, "scenarios": runs,
              "acEvidence": ac_ev}
        # 스펙 증거 폴더에는 --spec으로 돌렸을 때만 쓴다 (--scenario 단독 실행이 완료 증거를 덮어쓰지 않게)
        path = report.write(ev, evidence_dir(args.spec, run_dir))
        with open(LATEST, "w", encoding="utf-8") as f:
            json.dump({"runDir": run_dir, "runs": runs}, f, ensure_ascii=False, default=str)
    verdict = report.verdict_of(ev)
    log(f"[farm] 판정: {verdict} — 리포트: {path}")
    if verdict == "candidate":
        n = sum(1 for r in runs for c in r["combos"] for x in c["captures"] if x["status"] == "candidate")
        raise VerificationFailed(f"기준 후보 {n}장 — 확인 후 `farm.py approve-baseline`으로 채택")
    if verdict != "pass":
        raise VerificationFailed("실패 항목이 있다 (리포트 참고)")


def _latest():
    if not os.path.exists(LATEST):
        raise RunnerError("최근 실행 기록이 없다 — smoke/full을 먼저 돌린다")
    with open(LATEST, encoding="utf-8") as f:
        return json.load(f)


def cmd_approve_baseline(args, argv):
    data = _latest()
    items = [c for r in data["runs"] for combo in r["combos"] for c in combo["captures"]
             if (not args.scenario or c["scenario"] == args.scenario)]
    done = baseline.approve(items, include_failed=args.include_failed)
    for d in done:
        log(f"[farm] 기준 채택: {os.path.relpath(d, paths.REPO)}")
    log(f"[farm] {len(done)}장 채택 (기준 후보{' + 차이 실패' if args.include_failed else ''})")


def cmd_approve_perf(args, argv):
    data = _latest()
    commit = logic.metadata()["commit"]
    n = 0
    for r in data["runs"]:
        for combo in r["combos"]:
            for row in combo["perf"]:
                if row.get("metrics"):
                    perf.store_baseline(paths.PERF_BASELINES_JSON, row["key"], row["metrics"], commit,
                                        _dt.datetime.now().isoformat(timespec="seconds"))
                    n += 1
    log(f"[farm] 성능 기준 {n}개 갱신")


def cmd_build(args, argv):
    preflight.check_tools(need_chrome=False)
    exe, cached, took = build.ensure_build(force=args.force, log=log)
    log(f"[farm] 실행 파일: {exe}")
