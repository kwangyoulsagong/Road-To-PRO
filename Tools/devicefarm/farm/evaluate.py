"""플레이어 실행 결과 → 판정 묶음 (캡처 비교·성능·로그·결정론). 리포트(report.py)의 입력을 만든다."""
import os
import subprocess

from . import baseline, compare, determinism, imagechecks, logs, motion, paths, perf
from .htmlkit import e  # noqa: F401  (리포트와 같은 이스케이프 규칙 공유)


def read_steps(out_dir):
    return logs.read_jsonl(os.path.join(out_dir, "steps.jsonl"))


def pmset_text():
    try:
        return subprocess.run(["pmset", "-g", "batt"], capture_output=True, text=True, timeout=5).stdout
    except (OSError, subprocess.SubprocessError):
        return ""


def _masks(step_capture, w, h):
    out = []
    for m in step_capture.get("masks", []):
        out.append(compare.mask_to_pixels(m, w, h))
    return out


def evaluate_combo(result, scenario, profile_set, baseline_root=None, ignore=None,
                   perf_baselines=None, power_text=None):
    out = result.out_dir
    combo = result.combo
    p = combo.profile
    steps = read_steps(out)
    captures = []
    for st in steps:
        cap = st.get("capture")
        if not cap:
            continue
        name = st["name"]
        cur = os.path.join(out, cap["file"])
        params = scenario.capture_params(name)
        base = baseline.baseline_path(scenario.id, name, combo.key, root=baseline_root)
        diff = os.path.join(out, "diff", f"{name}.png")
        item = {"step": name, "current": cur, "baseline": base if os.path.exists(base) else None,
                "diff": None, "scenario": scenario.id, "combo": combo.key}
        if not os.path.exists(cur):
            item.update(status="fail", reason="캡처 파일 없음")
        else:
            arr = compare.load_rgb(cur)
            ok, why = compare.check_capture_size(arr, (p.width, p.height))
            if not ok:
                item.update(status="fail", reason=why)
            else:
                r = compare.compare_files(cur, base, diff_path=diff, pixel_threshold=params["pixelThreshold"],
                                          max_diff_ratio=params["maxDiffRatio"],
                                          masks=_masks(cap, p.width, p.height))
                item.update(r.to_dict())
                item["diff"] = diff if os.path.exists(diff) and r.diff_pixels else None
        captures.append(item)

    fail_caps = [os.path.join(out, st["failCapture"]) for st in steps if st.get("failCapture")]

    img_rows = []
    checks = getattr(scenario, "image_checks", None) or []
    if checks:
        loaded = {}
        for st in steps:
            cap = st.get("capture")
            path = os.path.join(out, cap["file"]) if cap else None
            if cap and os.path.exists(path):
                loaded[st["name"]] = (compare.load_rgb(path), cap.get("regions") or {})
        img_rows = imagechecks.run(checks, loaded)

    perf_rows = []
    pj = os.path.join(out, "perf.json")
    if os.path.exists(pj):
        import json

        with open(pj, encoding="utf-8") as f:
            sections = json.load(f).get("sections", [])
        budgets = (scenario.raw or {}).get("budgets", {})
        by_name = {}
        for sec in sections:
            try:
                metrics = perf.summarize(sec)
            except ValueError as err:
                perf_rows.append({"section": sec.get("name"), "error": str(err), "ok": False})
                continue
            key = f"{scenario.id}/{sec['name']}/{p.id}"
            base = perf.load_baseline(perf_baselines or paths.PERF_BASELINES_JSON, key)
            power = perf.power_state(sec.get("power"), power_text)
            ev = perf.evaluate(metrics, budgets.get(sec["name"]), base, p.is_reference_device, power)
            perf_rows.append({"section": sec["name"], "key": key, "metrics": metrics, "power": power, **ev})
            by_name[sec["name"]] = (metrics, power)
        # 파생 구간 (specs/044): 두 구간 지표의 차 — 예) 피치만 − 빈 화면 = 피치 셰이더 몫
        for name, d in sorted(((scenario.raw or {}).get("derived") or {}).items()):
            if d["of"] not in by_name or d["minus"] not in by_name:
                perf_rows.append({"section": name, "error": f"파생 구간 입력 없음 ({d['of']} − {d['minus']})", "ok": False})
                continue
            (ma, pw), (mb, _) = by_name[d["of"]], by_name[d["minus"]]
            metrics = {"derived": f"{d['of']} − {d['minus']}"}
            for path in d.get("budget", {}):
                va, vb = perf.get_metric(ma, path), perf.get_metric(mb, path)
                perf.set_metric(metrics, path, None if va is None or vb is None else va - vb)
            ev = perf.evaluate(metrics, d.get("budget"), None, p.is_reference_device, pw)
            perf_rows.append({"section": name, "key": f"{scenario.id}/{name}/{p.id}", "metrics": metrics, "power": pw, **ev})

    log_summary = logs.judge(logs.read_jsonl(os.path.join(out, "logs.jsonl")), ignore or []).to_dict()
    cps_path = os.path.join(out, "checkpoints.json")
    cps = determinism.load(cps_path) if os.path.exists(cps_path) else []

    cap_ok = all(c["status"] == "pass" for c in captures)
    has_candidate = any(c["status"] == "candidate" for c in captures)
    perf_ok = all(r.get("ok", False) for r in perf_rows)
    img_ok = all(r["ok"] for r in img_rows)
    ok = result.ok and log_summary["ok"] and perf_ok and img_ok and all(c["status"] in ("pass", "candidate") for c in captures)
    verdict = "pass" if ok and cap_ok else ("candidate" if ok and has_candidate else "fail")
    return {
        "key": combo.key,
        "profile": p.id,
        "theme": combo.theme,
        "lang": combo.lang,
        "label": result.job.label,
        "status": result.status,
        "reason": result.reason,
        "exitCode": result.exit_code,
        "durationSec": round(result.duration, 1),
        "outDir": out,
        "args": result.args,
        "steps": steps,
        "captures": captures,
        "failCaptures": fail_caps,
        "perf": perf_rows,
        "imageChecks": img_rows,
        "logs": log_summary,
        "checkpoints": cps,
        "motion": motion.load(out),
        "verdict": verdict,
    }


def evaluate_run(results, scenario, profile_set, **kw):
    power = kw.pop("power_text", None)
    if power is None:
        power = pmset_text()
    combos = [evaluate_combo(r, scenario, profile_set, power_text=power, **kw) for r in results]
    with_cps = {c["key"]: c["checkpoints"] for c in combos if c["checkpoints"]}
    det = determinism.compare_profiles(with_cps) if with_cps else None
    verdicts = {c["verdict"] for c in combos}
    if "fail" in verdicts or (det is not None and not det["ok"]):
        verdict = "fail"
    elif "candidate" in verdicts:
        verdict = "candidate"
    else:
        verdict = "pass"
    return {"scenario": scenario.id, "description": (scenario.raw or {}).get("description", ""),
            "combos": combos, "determinism": det, "verdict": verdict}


def baseline_items(run):
    """approve-baseline 입력 목록."""
    return [c for combo in run["combos"] for c in combo["captures"]]
