"""시뮬 벤치 (specs/006 T21): 스크립팅 백엔드별 릴리스 플레이어로 시뮬 워크로드를 재고 Data/perf의 il2cpp 예산으로 판정한다.

판정 런타임은 IL2CPP (2026-11-03 사용자 결정, README D17). --backend mono는 파이프라인 점검·비교용 (판정 안 함).
빌드 중 바뀔 수 있는 ProjectSettings.asset은 빌드 전 바이트로 되돌린다 (스크립팅 백엔드 임시 전환, 커밋 변경 없음).
"""
import json
import math
import os
import subprocess
import time

from . import build, paths, perf, preflight
from .errors import RunnerError

PROJECT_SETTINGS = os.path.join(paths.REPO, "ProjectSettings", "ProjectSettings.asset")
REGRESSION_LIMIT = 0.10


def app_path(backend):
    return os.path.join(paths.REPO, "Builds", f"FarmBench-{backend}", "RoadToProBench.app")


def stamp_path(backend):
    return os.path.join(paths.FARM_DIR, "build", f"bench-{backend}.stamp")


def il2cpp_available(unity_app=paths.UNITY_APP):
    """Mac Build Support (IL2CPP) 모듈이 설치됐는가."""
    contents = os.path.dirname(os.path.dirname(unity_app))
    var = os.path.join(contents, "PlaybackEngines", "MacStandaloneSupport", "Variations")
    return os.path.isdir(var) and any("il2cpp" in n for n in os.listdir(var))


def ensure_build(backend, force=False, log=print):
    if backend == "il2cpp" and not il2cpp_available():
        raise RunnerError("IL2CPP 모듈이 없다 — Unity Hub에서 6000.3.27f2에 'Mac Build Support (IL2CPP)'를 설치한다")
    app = app_path(backend)
    key = build.cache_key()
    if not force and build.is_cached(key, app=app, stamp=stamp_path(backend)):
        log(f"[farm] 벤치 빌드({backend}) 캐시 적중")
        return build.executable(app), True
    preflight.check_editor_closed()
    logf = os.path.join(paths.FARM_DIR, "build", f"bench-{backend}.log")
    os.makedirs(os.path.dirname(logf), exist_ok=True)
    with open(PROJECT_SETTINGS, "rb") as f:
        settings_before = f.read()
    log(f"[farm] 벤치 플레이어 빌드 중 ({backend}, 릴리스)…")
    t0 = time.time()
    cmd = [paths.UNITY_APP, "-batchmode", "-quit", "-projectPath", paths.REPO,
           "-executeMethod", "RoadToPro.Verification.Editor.FarmBuild.BuildMacBench",
           "-farmBuildOut", app, "-farmBackend", backend, "-logFile", logf]
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=5400)
    except subprocess.TimeoutExpired as e:
        raise RunnerError("벤치 빌드가 90분 안에 끝나지 않았다") from e
    finally:
        restore_settings(settings_before, log)
    if r.returncode != 0 or not build.executable(app):
        tail = ""
        if os.path.exists(logf):
            with open(logf, encoding="utf-8", errors="replace") as f:
                tail = "\n".join([l.rstrip() for l in f if "error" in l.lower()][-15:])
        raise RunnerError(f"벤치 빌드 실패 (exit {r.returncode}). 로그 {logf}\n{tail}")
    with open(stamp_path(backend), "w", encoding="utf-8") as f:
        f.write(build.cache_key())
    log(f"[farm] 벤치 빌드 완료 {time.time() - t0:.0f}s")
    return build.executable(app), False


def restore_settings(before, log=print, path=PROJECT_SETTINGS):
    """빌드가 ProjectSettings.asset을 바꿨으면 빌드 전 바이트로 되돌린다. 되돌렸으면 True."""
    with open(path, "rb") as f:
        now = f.read()
    if now == before:
        return False
    with open(path, "wb") as f:
        f.write(before)
    log("[farm] ProjectSettings.asset을 빌드 전 상태로 되돌림 (스크립팅 백엔드 임시 전환)")
    return True


def run(exe, spec, out_dir, timeout=3600):
    os.makedirs(out_dir, exist_ok=True)
    cmd = [exe, "-batchmode", "-nographics", "-simBench", spec, "-simBenchOut", out_dir,
           "-simBenchData", os.path.join(paths.REPO, "Data"), "-logFile", os.path.join(out_dir, "player.log")]
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired as e:
        raise RunnerError(f"벤치가 {timeout}s 안에 끝나지 않았다") from e
    p = os.path.join(out_dir, "bench.json")
    if r.returncode != 0 or not os.path.exists(p):
        err = os.path.join(out_dir, "bench-error.txt")
        detail = open(err, encoding="utf-8").read()[-2000:] if os.path.exists(err) else (r.stderr or "")[-2000:]
        raise RunnerError(f"벤치 실행 실패 (exit {r.returncode})\n{detail}")
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def baseline_key(backend, spec, profile):
    return f"{backend}/{spec}/{profile}"


def judge(metrics, cfg, backend, base, is_reference, power):
    """예산은 IL2CPP 결과에만 적용 (D17). 회귀는 같은 백엔드 기준 대비 +10%."""
    budgets = ((cfg or {}).get("il2cpp") or {}).get("budgets", [])
    budget_rows, reg_rows = [], []
    for b in budgets:
        v = metrics.get(b["metric"])
        budget_rows.append({"ac": b.get("ac"), "label": b["label"], "metric": b["metric"], "value": v, "budget": b["max"],
                            "unit": b.get("unit", ""), "ok": v is not None and v <= b["max"]})
        prev = (base or {}).get(b["metric"])
        if v is None or prev is None:
            continue
        change = (0.0 if v == 0 else math.inf) if prev == 0 else (v - prev) / prev
        reg_rows.append({"ac": b.get("ac"), "label": b["label"], "metric": b["metric"], "value": v, "previous": prev,
                         "changePct": round(change * 100, 4) if math.isfinite(change) else None,
                         "ok": change < REGRESSION_LIMIT - 1e-9})
    judged = backend == "il2cpp" and bool(is_reference) and perf.power_judged(power)
    note = ""
    if backend != "il2cpp":
        note = f"백엔드 {backend} — 판정은 IL2CPP만 (D17), 기록만"
    elif not is_reference:
        note = f"기준 기기({(cfg or {}).get('profile')}) 아님 — 기록만"
    elif not perf.power_judged(power):
        note = perf.power_note(power)
    ok = True
    if judged:
        ok = all(r["ok"] for r in budget_rows) and all(r["ok"] for r in reg_rows)
    stages = {k[len("stage_us."):]: v for k, v in metrics.items() if k.startswith("stage_us.")}
    return {"judged": judged, "ok": ok, "note": note, "backend": backend, "profile": (cfg or {}).get("profile"),
            "budget": budget_rows, "regression": reg_rows, "stages": stages, "metrics": metrics}
