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


def deferred_acs(cfg, info):
    """Data/perf/<spec>.json의 "deferred": {"AC-15": "사유"} (specs/004 T19a).
    스펙에 없는 AC id나 빈 사유는 설정 오류로 거부한다 (오타로 실패를 숨기지 않게)."""
    d = (cfg or {}).get("deferred") or {}
    if not isinstance(d, dict):
        raise RunnerError("deferred는 {\"AC-NN\": \"사유\"} 객체여야 한다")
    ids = {a["id"] for a in info.get("acs", [])}
    for ac, reason in d.items():
        if ac not in ids:
            raise RunnerError(f"deferred의 {ac}가 스펙 AC 목록에 없다")
        if not isinstance(reason, str) or not reason.strip():
            raise RunnerError(f"deferred {ac}의 사유가 비어 있다")
    return dict(d)


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


def ac_evidence(info, logic_dict, res, perf_judgement):
    """스펙 AC ↔ EditMode 증거 (specs/001 T26).
    로직: EditMode 전체 통과. 결정론: `Determinism` 카테고리 테스트가 1개 이상 있고 전부 통과.
    성능: EditMode 성능 예산 판정 (해당 AC 항목만)."""
    det = [c for c in (res.cases if res else []) if "Determinism" in c.get("categories", [])]
    det_ok = bool(det) and all(c["result"] == "Passed" for c in det)
    det_label = f"결정론 테스트 {sum(c['result'] == 'Passed' for c in det)}/{len(det)} 통과" if det else "결정론 테스트 없음"
    out = {}
    for a in info["acs"]:
        if a["kind"] == "로직":
            out[a["id"]] = [{"label": "EditMode 테스트", "ok": bool(logic_dict.get("ok"))}]
        elif a["kind"] == "결정론":
            out[a["id"]] = [{"label": det_label, "ok": det_ok}]
        elif a["kind"] == "성능" and perf_judgement is not None:
            rows = [r for r in perf_judgement["budget"] + perf_judgement["regression"] if r.get("ac") in (None, a["id"])]
            if not rows:
                continue
            bad = [r for r in rows if not r["ok"]]
            if not perf_judgement["judged"]:
                label, ok = f"EditMode 성능 {len(rows)}항목 — {perf_judgement['note']}", False
            else:
                label = f"EditMode 성능 {len(rows) - len(bad)}/{len(rows)} 통과 ({perf_judgement['profile']})"
                ok = not bad
            out[a["id"]] = [{"label": label, "ok": ok}]
    return out


LATEST_LOGIC = os.path.join(paths.FARM_DIR, "latest-logic.json")


def cmd_logic(args, argv):
    preflight.run_all(need_chrome=True)
    t0 = time.time()
    with preflight.RunLock():
        run_dir = new_run_dir("logic")
        cfg = perf.load_spec_config(args.spec) if args.spec else None
        # 보류 AC 설정은 Unity를 띄우기 전에 검사한다 (설정 오류 = 실행기 오류, 종료 3)
        deferred = deferred_acs(cfg, spec_info(args.spec)) if args.spec else {}
        test_filter = args.filter or perf.test_filter(cfg)
        perf_json = os.path.join(run_dir, "PerformanceTestResults.json") if cfg else None
        log("[farm] 로직 테스트 (EditMode) 실행 중…" + (f" 필터 {test_filter}" if test_filter else ""))
        meta = logic.metadata()
        res, _ = logic.run(test_filter, args.category, perf_results=perf_json)
        out_dir = run_dir
        html_path = os.path.join(out_dir, "logic.html")
        with open(html_path, "w", encoding="utf-8") as f:
            f.write(logic.render_html(res, meta))
        lg = res.to_dict()
        lg["png"] = chrome.screenshot(html_path, os.path.join(out_dir, "logic.png"))
        lg["html"] = html_path
        meta.update(durationSec=time.time() - t0, command=command_line(argv), profiles=[])
        info = spec_info(args.spec)
        judgement = None
        ev_dir = evidence_dir(args.spec, run_dir)
        if cfg:
            rows = perf.read_perf_testing(perf_json) if os.path.exists(perf_json) else []
            profile = perf.host_profile()
            from .evaluate import pmset_text
            power = perf.power_state(None, pmset_text())
            base = perf.load_baseline(paths.PERF_BASELINES_JSON, perf.editmode_baseline_key(args.spec, profile))
            judgement = perf.judge_editmode(rows, cfg, base, profile == cfg.get("profile"), power)
            judgement["host"] = profile
            judgement["power"] = power
            perf.write_editmode_perf(ev_dir, rows, judgement)
            with open(LATEST_LOGIC, "w", encoding="utf-8") as f:
                json.dump({"spec": args.spec, "profile": profile, "metrics": judgement["metrics"]}, f, ensure_ascii=False, indent=2)
        meta["profiles"] = [judgement["host"]] if judgement else []
        ev = {"title": f"{info['id']} 로직 증거", "spec": info, "meta": meta, "logic": lg, "scenarios": [],
              "editmodePerf": judgement, "requireAllAcs": bool(cfg), "deferred": deferred,
              "acEvidence": ac_evidence(info, lg, res, judgement)}
        path = report.write(ev, ev_dir)
    log(f"[farm] 합계 {res.total} 통과 {res.passed} 실패 {res.failed} 컴파일 에러 {len(res.compile_errors)}")
    if judgement:
        bad = [r for r in judgement["budget"] + judgement["regression"] if not r["ok"]]
        log(f"[farm] 성능 {'판정' if judgement['judged'] else '기록만'}: 초과·회귀 {len(bad)}건 {judgement['note']}")
        for r in bad:
            log(f"[farm]   ✗ {r['label']}: {r['value']} (예산 {r.get('budget', '—')}, 변화 {r.get('changePct', '—')}%)")
    log(f"[farm] 리포트: {path}")
    if not res.ok:
        raise VerificationFailed("로직 테스트 실패")
    if judgement and not judgement["ok"]:
        raise VerificationFailed("성능 예산·회귀 실패")
    verdict = report.verdict_of(ev)
    if verdict == "fail":
        missing = [k for k, v in ev["acEvidence"].items() if not all(i["ok"] for i in v)]
        if ev.get("requireAllAcs"):
            missing += [a["id"] for a in info["acs"] if a["id"] not in ev["acEvidence"] and a["id"] not in deferred]
        raise VerificationFailed(f"증거가 없거나 실패한 AC: {', '.join(missing)}")
    if verdict == "deferred":
        for ac in report.pending_deferred(ev):
            log(f"[farm] 보류 {ac}: {deferred[ac]}")
        log("[farm] 판정: 보류 — 나머지 AC는 모두 통과. 보류가 풀리기 전에는 완료 판정 불가")


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
    commit = logic.metadata()["commit"]
    now = _dt.datetime.now().isoformat(timespec="seconds")
    if args.spec and os.path.exists(LATEST_LOGIC):
        # EditMode 성능 (farm logic --spec) 기준 채택 (specs/001 T26)
        with open(LATEST_LOGIC, encoding="utf-8") as f:
            lt = json.load(f)
        if lt.get("spec") == args.spec:
            key = perf.editmode_baseline_key(args.spec, lt["profile"])
            perf.store_baseline(paths.PERF_BASELINES_JSON, key, lt["metrics"], commit, now)
            log(f"[farm] 성능 기준 갱신: {key} ({len(lt['metrics'])}항목)")
            return
    data = _latest()
    n = 0
    for r in data["runs"]:
        for combo in r["combos"]:
            for row in combo["perf"]:
                if row.get("metrics"):
                    perf.store_baseline(paths.PERF_BASELINES_JSON, row["key"], row["metrics"], commit, now)
                    n += 1
    log(f"[farm] 성능 기준 {n}개 갱신")


def cmd_build(args, argv):
    preflight.check_tools(need_chrome=False)
    exe, cached, took = build.ensure_build(force=args.force, log=log)
    log(f"[farm] 실행 파일: {exe}")
