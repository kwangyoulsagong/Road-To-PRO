"""farm 명령 처리 (specs/000 FR-80): logic / shot / build / approve-perf / bench.

2026-11-11: 프로필 매트릭스(smoke·full)와 기준 이미지 비교·승인(approve-baseline) 폐지 (2026-11-11 사용자 결정).
"""
import datetime as _dt
import glob
import json
import os
import re
import time

from . import bench, build, chrome, evaluate, logic, logs, paths, perf, preflight, profiles, report, runner, scenario
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


def manual_acs(cfg, info):
    """Data/perf/<spec>.json의 "manual": {"AC-09": "evidence/approval.md"} (specs/056 T19a).
    사용자 육안 승인처럼 기계가 낼 수 없는 증거: 스펙 폴더 기준 파일이 있고 그 안에 AC id가 적혀 있으면 통과 증거로 붙인다.
    스펙에 없는 AC id는 설정 오류. 파일이 없거나 AC id가 없으면 실패 증거(빠뜨림이 통과로 보이지 않게)."""
    m = (cfg or {}).get("manual") or {}
    if not isinstance(m, dict):
        raise RunnerError("manual은 {\"AC-NN\": \"상대 경로\"} 객체여야 한다")
    ids = {a["id"] for a in info.get("acs", [])}
    out = {}
    for ac, rel in m.items():
        if ac not in ids:
            raise RunnerError(f"manual의 {ac}가 스펙 AC 목록에 없다")
        path = os.path.join(info["dir"] or "", rel)
        ok = os.path.exists(path) and ac in open(path, encoding="utf-8").read()
        out[ac] = [{"label": f"수동 승인 기록 {rel}" + ("" if ok else " (없음 또는 AC 미기재)"), "ok": ok}]
    return out


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


def ac_evidence(info, logic_dict, res, perf_judgement, bench_judgement=None, bench_required=False, bench_skip=None):
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
            if rows:
                bad = [r for r in rows if not r["ok"]]
                if not perf_judgement["judged"]:
                    label, ok = f"EditMode 성능 {len(rows)}항목 — {perf_judgement['note']}", False
                else:
                    label = f"EditMode 성능 {len(rows) - len(bad)}/{len(rows)} 통과 ({perf_judgement['profile']})"
                    ok = not bad
                out[a["id"]] = [{"label": label, "ok": ok}]
            # specs/006 D17: 판정 런타임 IL2CPP — farm.py bench 결과가 있어야 통과.
            # EditMode 예산이 없는 AC도 벤치 예산이 있으면 벤치 증거를 붙인다 (specs/043: CPU는 벤치만)
            if bench_required:
                brow = [r for r in (bench_judgement or {}).get("budget", []) + (bench_judgement or {}).get("regression", [])
                        if r.get("ac") in (None, a["id"])]
                if bench_judgement is None:
                    if rows and bench_skip:
                        # 2026-11-12 검증 경량화: 시뮬 틱 경로를 바꾸지 않은 스펙은 벤치를 생략하고 사유를 남긴다
                        out[a["id"]].append({"label": f"IL2CPP 벤치 생략 — {bench_skip}", "ok": True})
                    elif rows:
                        out[a["id"]].append({"label": "IL2CPP 벤치 결과 없음 (farm.py bench --spec, 생략하려면 --skip-bench \"사유\")", "ok": False})
                elif brow:
                    out.setdefault(a["id"], [])
                    bbad = [r for r in brow if not r["ok"]]
                    if not bench_judgement["judged"]:
                        out[a["id"]].append({"label": f"벤치({bench_judgement['backend']}) {len(brow)}항목 — {bench_judgement['note']}", "ok": False})
                    else:
                        out[a["id"]].append({"label": f"IL2CPP 벤치 {len(brow) - len(bbad)}/{len(brow)} 통과", "ok": not bbad})
    return out


LATEST_LOGIC = os.path.join(paths.FARM_DIR, "latest-logic.json")
NO_DET = "결정론 테스트 없음"


def merge_art(ev, ev_dir):
    """같은 스펙 evidence/art.json(farm.py art)이 있으면 리포트에 Blender 섹션과 AC 증거를 합친다 (specs/044).
    EditMode에 결정론 테스트가 없어 생긴 '결정론 테스트 없음'은 Blender 결정론 증거로 대신한다."""
    p = os.path.join(ev_dir, "art.json")
    if not os.path.exists(p):
        return
    with open(p, encoding="utf-8") as f:
        art = json.load(f)
    ev["art"] = {"ok": art["result"]["ok"], "total": art["result"]["total"], "passed": art["result"]["passed"],
                 "failed": art["result"]["failed"], "png": os.path.join(ev_dir, art["png"]) if art.get("png") else None,
                 "blender": art.get("meta", {}).get("blender")}
    acev = ev.setdefault("acEvidence", {})
    for ac, items in art.get("acEvidence", {}).items():
        cur = [i for i in acev.get(ac, []) if not i["label"].startswith(NO_DET)]
        acev[ac] = cur + items


def merge_previous(ev, ev_dir, spec):
    """matrix 리포트에 같은 스펙의 앞선 logic 리포트(run.json) 결과를 합친다 — 한 리포트에 모든 증거 (specs/044)."""
    p = os.path.join(ev_dir, "run.json")
    if not spec or not os.path.exists(p):
        return
    with open(p, encoding="utf-8") as f:
        prev = json.load(f)
    if (prev.get("spec") or {}).get("id") != spec or not prev.get("logic"):
        return
    for k in ("logic", "editmodePerf", "benchPerf", "art", "deferred", "requireAllAcs"):
        if prev.get(k) is not None and ev.get(k) is None:
            ev[k] = prev[k]
    acev = ev.setdefault("acEvidence", {})
    for ac, items in (prev.get("acEvidence") or {}).items():
        # 앞선 매트릭스 실행의 시나리오 판정("<id> (full) fail" 등)은 가져오지 않는다 — 이번 실행이 새로 낸다 (specs/043: 재실행마다 옛 실패가 쌓였다)
        keep = [i for i in items if not _SCENARIO_LABEL.search(i.get("label", ""))]
        if keep or ac not in acev:
            acev[ac] = keep + acev.get(ac, [])
        if not acev[ac]:
            del acev[ac]


_SCENARIO_LABEL = re.compile(r" \((smoke|full|shot)\) (pass|fail|candidate|review|deferred)$")


def cmd_logic(args, argv):
    want_png = bool(getattr(args, "png", False))
    try:
        scope = logic.scope_filter(getattr(args, "scope", None))
    except ValueError as err:
        raise RunnerError(str(err)) from err
    preflight.run_all(need_chrome=want_png)
    t0 = time.time()
    with preflight.RunLock():
        run_dir = new_run_dir("logic")
        cfg = perf.load_spec_config(args.spec) if args.spec else None
        # 보류 AC 설정은 Unity를 띄우기 전에 검사한다 (설정 오류 = 실행기 오류, 종료 3)
        deferred = deferred_acs(cfg, spec_info(args.spec)) if args.spec else {}
        # --filter > --scope(+스펙 성능 테스트) > 스펙 성능 테스트 > 전체
        spec_tests = perf.test_filter(cfg)
        test_filter = args.filter or (";".join(x for x in (scope, spec_tests) if x) if scope else spec_tests)
        perf_json = os.path.join(run_dir, "PerformanceTestResults.json") if cfg else None
        log("[farm] 로직 테스트 (EditMode) 실행 중…" + (f" 필터 {test_filter}" if test_filter else ""))
        meta = logic.metadata()
        res, _ = logic.run(test_filter, args.category, perf_results=perf_json)
        out_dir = run_dir
        html_path = os.path.join(out_dir, "logic.html")
        with open(html_path, "w", encoding="utf-8") as f:
            f.write(logic.render_html(res, meta))
        lg = res.to_dict()
        # 로직 리포트 PNG는 기본으로 만들지 않는다 (2026-11-12 검증 경량화 — report.html 표와 내용이 같다). --png로 켠다
        lg["png"] = chrome.screenshot(html_path, os.path.join(out_dir, "logic.png")) if want_png else None
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
        bench_j = None
        bench_required = bool((cfg or {}).get("il2cpp"))
        bp = os.path.join(ev_dir, "bench-perf.json")
        if bench_required and os.path.exists(bp):
            with open(bp, encoding="utf-8") as f:
                bench_j = json.load(f)
        meta["profiles"] = [judgement["host"]] if judgement else []
        ev = {"title": f"{info['id']} 로직 증거", "spec": info, "meta": meta, "logic": lg, "scenarios": [],
              "editmodePerf": judgement, "benchPerf": bench_j, "requireAllAcs": bool(cfg), "deferred": deferred,
              "acEvidence": {k: v for k, v in ac_evidence(info, lg, res, judgement, bench_j, bench_required, getattr(args, "skip_bench", None)).items()
                             if k not in deferred}}  # 보류 AC에 일반 증거(EditMode 통과)를 자동으로 붙이지 않는다 (specs/006)
        if args.spec:
            ev["acEvidence"].update(manual_acs(cfg, info))
        merge_art(ev, ev_dir)
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
        return "deferred"


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
        # alsoSpecs: 다른 스펙의 회귀 시나리오를 이 스펙 증거로도 돌린다 {"056": ["AC-08"]} (specs/056 T19a)
        if args.spec and s.spec != args.spec and args.spec not in ((s.raw or {}).get("alsoSpecs") or {}):
            continue
        out.append(s)
    if not out:
        raise RunnerError("돌릴 E2E 시나리오가 없다 (--scenario 또는 --spec 확인)")
    return out


def run_scenario(exe, sc, tier, ps, run_dir, only=None, langs=None, extra_args=None, baseline_root=None,
                 ignore=None, label="", seed=None, selector=None, themes=None):
    combos = ps.expand(tier, selector=selector if selector is not None else sc.profiles, only=only,
                       langs=langs or sc.langs or None, themes=themes or sc.themes or None)
    jobs = [runner.Job(c, paths.scenario_file(sc.id), seed if seed is not None else sc.fixture["seed"],
                       extra_args=list(extra_args or []) + (["-farmBatterySaver"] if sc.battery_saver else []), env=build.hidden_window_env(), label=label) for c in combos]
    results = runner.run_matrix([exe], jobs, run_dir, ps, log=log)
    return evaluate.evaluate_run(results, sc, ps, baseline_root=baseline_root,
                                 ignore=logs.load_ignore() if ignore is None else ignore)


def scenario_build(sc):
    """시나리오의 빌드 종류 (development | release)."""
    v = (sc.raw or {}).get("build", "development")
    if v not in build.VARIANTS:
        raise RunnerError(f"{sc.id}: 알 수 없는 build '{v}' (development|release)")
    return v


SHOT_PROFILE = "mac-m1-1600p"
# 기준 이미지를 두지 않는다 — 모든 캡처는 육안 확인 대상(review)이다 (2026-11-11 사용자 결정)
NO_BASELINES = os.path.join(paths.FARM_DIR, "no-baselines")


def cmd_shot(args, argv):
    tier = "shot"
    preflight.run_all(need_chrome=True)
    t0 = time.time()
    with preflight.RunLock():
        ps = profiles.load()
        scs = scenarios_for(args)
        # 시나리오가 빌드 종류를 정한다 ("build": "release" — specs/048 T11). 기본은 개발 빌드
        variants = sorted({scenario_build(sc) for sc in scs}, key=build.VARIANTS.index)
        exes, cached = {}, True
        for v in variants:
            exes[v], hit, _ = build.ensure_build(log=log, variant=v)
            cached = cached and hit
        run_dir = new_run_dir(tier)
        # 시나리오가 여럿이면 시나리오별 하위 폴더 — 조합 폴더 이름이 같아 캡처가 덮어써지는 것 방지 (specs/044 T18b)
        only = args.profile or [SHOT_PROFILE]
        runs = [run_scenario(exes[scenario_build(sc)], sc, "full", ps, os.path.join(run_dir, sc.id) if len(scs) > 1 else run_dir,
                             only=only, langs=["ko"], themes=["light"], selector="all", baseline_root=NO_BASELINES)
                for sc in scs]
        for sc, r in zip(scs, runs):
            r["build"] = scenario_build(sc)
        meta = logic.metadata()
        keys = sorted({c["key"] for r in runs for c in r["combos"]})
        meta.update(durationSec=time.time() - t0, command=command_line(argv), profiles=keys, buildCached=cached, builds=variants)
        info = spec_info(args.spec or (scs[0].spec if len(scs) == 1 else None))
        ac_ev = {}
        for sc, r in zip(scs, runs):
            acs = (sc.raw or {}).get("acs", []) if not args.spec or sc.spec == args.spec \
                else ((sc.raw or {}).get("alsoSpecs") or {}).get(args.spec, [])
            for ac in acs:
                # 기준 이미지가 없으므로 캡처는 실패가 아니라 육안 확인 대기(review)
                v = "review" if r["verdict"] == "candidate" else r["verdict"]
                ac_ev.setdefault(ac, []).append({"label": f"{sc.id} ({tier}) {v}", "ok": v != "fail"})
        ev = {"title": f"{info['id']} 장면 스크린샷 ({tier})", "spec": info, "meta": meta, "scenarios": runs,
              "acEvidence": ac_ev}
        if args.spec:
            merge_previous(ev, evidence_dir(args.spec, run_dir), args.spec)
            ev["acEvidence"].update(manual_acs(perf.load_spec_config(args.spec), info))
        # 스펙 증거 폴더에는 --spec으로 돌렸을 때만 쓴다 (--scenario 단독 실행이 완료 증거를 덮어쓰지 않게)
        path = report.write(ev, evidence_dir(args.spec, run_dir))
        with open(LATEST, "w", encoding="utf-8") as f:
            json.dump({"runDir": run_dir, "runs": runs}, f, ensure_ascii=False, default=str)
    verdict = report.verdict_of(ev)
    log(f"[farm] 판정: {verdict} — 리포트: {path}")
    if verdict == "candidate":
        caps = [x["current"] for r in runs for c in r["combos"] for x in c["captures"] if x.get("current")]
        log(f"[farm] 스크린샷 {len(caps)}장 — 직접 열어 확인하고 사용자 육안 승인을 받는다 (기준 비교 없음)")
        for p in caps:
            log(f"  {os.path.relpath(p, paths.REPO)}")
        return
    if verdict != "pass":
        raise VerificationFailed("실패 항목이 있다 (리포트 참고)")


def _latest():
    if not os.path.exists(LATEST):
        raise RunnerError("최근 실행 기록이 없다 — shot을 먼저 돌린다")
    with open(LATEST, encoding="utf-8") as f:
        return json.load(f)


LATEST_BENCH = os.path.join(paths.FARM_DIR, "latest-bench.json")


def cmd_bench(args, argv):
    """시뮬 벤치 (specs/006 T21): 백엔드별 릴리스 플레이어 → bench.json → il2cpp 예산·회귀 판정 → evidence/bench-perf.json."""
    preflight.check_tools(need_chrome=False)
    cfg = perf.load_spec_config(args.spec)
    if not cfg or not cfg.get("il2cpp"):
        raise RunnerError(f"Data/perf에 스펙 {args.spec}의 il2cpp 벤치 예산이 없다")
    with preflight.RunLock():
        exe, cached = bench.ensure_build(args.backend, force=args.force_build, log=log)
        run_dir = new_run_dir(f"bench-{args.backend}")
        log(f"[farm] 벤치 실행 중 ({args.backend})…")
        t0 = time.time()
        doc = bench.run(exe, args.spec, run_dir)
        profile = perf.host_profile()
        from .evaluate import pmset_text
        power = perf.power_state(None, pmset_text())
        key = bench.baseline_key(args.backend, args.spec, profile)
        base = perf.load_baseline(paths.PERF_BASELINES_JSON, key)
        j = bench.judge(doc["metrics"], cfg, args.backend, base, profile == cfg.get("profile"), power)
        j.update(host=profile, power=power, buildCached=cached, durationSec=round(time.time() - t0, 1),
                 unity=doc.get("unity"), cpu=doc.get("cpu"), development=doc.get("development"), commit=logic.metadata()["commit"])
        ev_dir = evidence_dir(args.spec, run_dir)
        os.makedirs(ev_dir, exist_ok=True)
        if args.backend == "il2cpp":
            with open(os.path.join(ev_dir, "bench-perf.json"), "w", encoding="utf-8") as f:
                json.dump(j, f, ensure_ascii=False, indent=2)
                f.write("\n")
        with open(os.path.join(run_dir, "bench-perf.json"), "w", encoding="utf-8") as f:
            json.dump(j, f, ensure_ascii=False, indent=2)
        with open(LATEST_BENCH, "w", encoding="utf-8") as f:
            json.dump({"spec": args.spec, "key": key, "metrics": doc["metrics"]}, f, ensure_ascii=False, indent=2)
    for r in j["budget"]:
        log(f"[farm]   {'✓' if r['ok'] else '✗'} {r['label']}: {r['value']} {r['unit']} (예산 {r['budget']})")
    for name, us in sorted(j["stages"].items()):
        log(f"[farm]   단계 {name}: {us:.3f} µs")
    bad = [r for r in j["budget"] + j["regression"] if not r["ok"]]
    log(f"[farm] 벤치 {'판정' if j['judged'] else '기록만'} ({args.backend}): 초과·회귀 {len(bad)}건 {j['note']}")
    if j["judged"] and not j["ok"]:
        raise VerificationFailed("벤치 예산·회귀 실패")


def cmd_approve_perf(args, argv):
    commit = logic.metadata()["commit"]
    now = _dt.datetime.now().isoformat(timespec="seconds")
    if getattr(args, "bench", False):
        if not os.path.exists(LATEST_BENCH):
            raise RunnerError("최근 벤치 기록이 없다 — farm.py bench를 먼저 돌린다")
        with open(LATEST_BENCH, encoding="utf-8") as f:
            lb = json.load(f)
        if args.spec and lb.get("spec") != args.spec:
            raise RunnerError(f"최근 벤치는 스펙 {lb.get('spec')} — --spec {args.spec}와 다르다")
        perf.store_baseline(paths.PERF_BASELINES_JSON, lb["key"], lb["metrics"], commit, now)
        log(f"[farm] 벤치 성능 기준 갱신: {lb['key']} ({len(lb['metrics'])}항목)")
        return
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
