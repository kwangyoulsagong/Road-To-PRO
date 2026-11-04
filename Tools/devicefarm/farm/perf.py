"""성능 집계·예산·회귀 판정 (specs/000 FR-40~44).

드라이버(`PerfSampler`)가 남긴 perf.json의 구간 하나:
{name, warmupFrames, frameMs[], mainThreadMs[], gpuMs[] (specs/044, 개발 빌드), gcAllocBytes[], drawCalls[], triangles[],
 textureMemoryBytes, totalMemoryBytes, power: "ac"|"battery"|"unknown"}
"""
import json
import math
import os

REGRESSION_LIMIT = 0.10  # +10% 이상이면 회귀 (FR-43)

# 회귀 판정 대상 (모두 '낮을수록 좋음')
REGRESSION_METRICS = (
    "frameMs.p95",
    "frameMs.avg",
    "mainThreadMs.p95",
    "gpuMs.p95",
    "gcAllocTotalBytes",
    "drawCalls.max",
    "triangles.max",
    "totalMemoryBytes",
)

# Unity Performance Testing 3.x SampleUnit (실측: Time = 2 → ms, GC() 횟수 = 8 → 단위 없음)
UNITS = {0: "ns", 1: "us", 2: "ms", 3: "s", 4: "bytes", 5: "KB", 6: "MB", 7: "GB", 8: ""}


def _pct(sorted_vals, q):
    """최근접 순위 백분위수 (n·q 올림 번째 값)."""
    n = len(sorted_vals)
    k = max(1, math.ceil(q * n))
    return sorted_vals[k - 1]


def _stats(vals):
    s = sorted(vals)
    return {
        "avg": sum(s) / len(s),
        "p50": _pct(s, 0.50),
        "p95": _pct(s, 0.95),
        "p99": _pct(s, 0.99),
        "max": s[-1],
    }


def fold_render_pairs(vals):
    """렌더 카운터(드로우콜·삼각형)의 '0 → 두 배' 쌍을 반씩 나눈다 (specs/043 실측).
    ProfilerRecorder.LastValue가 한 표본 구간에 렌더 두 번(값 두 배), 다음 구간에 0번(값 0)을 기록하는 프레임 페이싱 때문 —
    렌더 1회당 값을 재려면 쌍을 합쳐 반으로 나눈다. 0이 아닌 두 값이 이어지면 그대로."""
    v = list(vals)
    nz = sorted(x for x in v if x)
    if not nz:
        return v
    med = nz[len(nz) // 2]
    used = set()
    for i in range(len(v)):
        if v[i] != 0:
            continue
        # 앞뒤 4프레임 안에서 아직 짝이 없는 '두 배' 값을 찾는다(가까운 것부터)
        for d in (1, -1, 2, -2, 3, -3, 4, -4):
            j = i + d
            if 0 <= j < len(v) and j not in used and v[j] and v[j] > 1.5 * med:
                half = v[j] / 2
                v[i] = v[j] = half
                used.update((i, j))
                break
    return v


def summarize(sec):
    w = int(sec.get("warmupFrames", 0))
    frames = sec["frameMs"][w:]
    if not frames:
        raise ValueError(f"성능 구간 '{sec.get('name')}': 워밍업 뒤 측정 프레임이 없다")

    def cut(key):
        """워밍업을 버린 값 목록. 카운터가 없으면(개발 빌드 아님) None."""
        v = sec.get(key)
        if not v:
            return None
        if len(v) == len(sec["frameMs"]):
            v = v[w:]
        v = [x for x in v if x is not None]
        return v or None

    gc, main, dc, tri = cut("gcAllocBytes"), cut("mainThreadMs"), cut("drawCalls"), cut("triangles")
    dc = fold_render_pairs(dc) if dc else dc
    tri = fold_render_pairs(tri) if tri else tri
    gpu = cut("gpuMs")
    custom = {}
    for k, v in sorted((sec.get("custom") or {}).items()):
        if not v:
            custom[k] = None
            continue
        vv = v[w:] if len(v) == len(sec["frameMs"]) else v
        vv = [x for x in vv if x is not None]
        custom[k] = _stats(vv) if vv else None
    out = {
        "frames": len(frames),
        "frameMs": _stats(frames),
        "mainThreadMs": _stats(main) if main else None,
        "gpuMs": _stats(gpu) if gpu else None,
        "gcAllocPerFrameBytes": {"avg": sum(gc) / len(gc), "max": max(gc)} if gc else None,
        "gcAllocTotalBytes": sum(gc) if gc else None,
        "drawCalls": {"avg": sum(dc) / len(dc), "max": max(dc)} if dc else None,
        "triangles": {"avg": sum(tri) / len(tri), "max": max(tri)} if tri else None,
        "textureMemoryBytes": sec.get("textureMemoryBytes"),
        "totalMemoryBytes": sec.get("totalMemoryBytes"),
    }
    if custom:
        out["custom"] = custom  # specs/043: 화면이 넘긴 카운터 (예: custom.animCpuMs.p95)
    return out


def power_state(unity_battery_status, pmset_text=None):
    """전원 상태: pmset 출력이 있으면 우선 ('AC Power'/'Battery Power'), 없으면 Unity batteryStatus."""
    if pmset_text:
        if "AC Power" in pmset_text:
            return "ac"
        if "Battery Power" in pmset_text:
            return "battery"
    return {"Charging": "ac", "Full": "ac", "NotCharging": "ac", "Discharging": "battery"}.get(
        unity_battery_status, "unknown"
    )


def get_metric(metrics, path):
    cur = metrics
    for part in path.split("."):
        if cur is None:
            return None
        cur = cur.get(part) if isinstance(cur, dict) else None
    return cur


def set_metric(metrics, path, value):
    parts = path.split(".")
    cur = metrics
    for part in parts[:-1]:
        cur = cur.setdefault(part, {})
    cur[parts[-1]] = value


def judge_budget(metrics, budget):
    rows = []
    for path, limit in sorted((budget or {}).items()):
        v = get_metric(metrics, path)
        ok = v is not None and v <= limit
        rows.append({"metric": path, "value": v, "budget": limit, "ok": ok})
    return rows


def judge_regression(metrics, base):
    rows = []
    if not base:
        return rows
    for path in REGRESSION_METRICS:
        try:
            v, b = get_metric(metrics, path), get_metric(base, path)
        except (KeyError, TypeError):
            continue
        if v is None or b is None:  # 개발 빌드가 아니면 렌더 카운터가 없다
            continue
        if b == 0:
            change = 0.0 if v == 0 else math.inf
        else:
            change = (v - b) / b
        ok = change < REGRESSION_LIMIT - 1e-9
        rows.append(
            {
                "metric": path,
                "value": v,
                "previous": b,
                "changePct": round(change * 100, 4) if math.isfinite(change) else None,
                "ok": ok,
            }
        )
    return rows


def evaluate(metrics, budget, base, is_reference, power):
    """FR-44: 기준 기기 + 전원 연결 측정만 통과/실패 판정. 나머지는 기록만."""
    budget_rows = judge_budget(metrics, budget)
    reg_rows = judge_regression(metrics, base)
    judged = bool(is_reference) and power == "ac"
    note = ""
    if not is_reference:
        note = "기준 기기 아님 — 기록·상대 비교만"
    elif power != "ac":
        note = f"전원 상태 {power} — 판정 제외"
    ok = True
    if judged:
        ok = all(r["ok"] for r in budget_rows) and all(r["ok"] for r in reg_rows)
    return {"judged": judged, "ok": ok, "note": note, "budget": budget_rows, "regression": reg_rows}


def _read(path):
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def load_baseline(path, key):
    entry = _read(path).get(key)
    return entry["metrics"] if entry else None


def store_baseline(path, key, metrics, commit, measured_at=""):
    data = _read(path)
    data[key] = {"metrics": metrics, "commit": commit, "measuredAt": measured_at}
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)
        f.write("\n")


def read_perf_testing(path):
    """Unity Performance Testing 결과 JSON (FR-41) → [{name, groups{name: {median, min, max, avg, unit}}}]."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    rows = []
    for r in data.get("Results", []):
        full = r.get("Name", "")
        parts = full.split(".")
        name = ".".join(parts[-2:]) if len(parts) >= 2 else full
        groups = {}
        for g in r.get("SampleGroups", []):
            groups[g["Name"]] = {
                "median": g.get("Median"),
                "min": g.get("Min"),
                "max": g.get("Max"),
                "avg": g.get("Average"),
                "unit": UNITS.get(g.get("Unit"), str(g.get("Unit"))),
                "samples": len(g.get("Samples", [])),
            }
        rows.append({"name": name, "fullName": full, "groups": groups})
    return rows


# ---- EditMode 성능 테스트 (specs/001 T26) ----
# Unity Performance Testing 결과(PerformanceTestResults.json)를 스펙별 예산 파일 Data/perf/<NNN>-*.json과 비교한다.
# 형식: {"spec", "profile", "tests": [네임스페이스…], "budgets": [{ac, label, test, group, stat, max, unit}]}

PERF_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))), "Data", "perf")


def load_spec_config(spec, perf_dir=None):
    d = perf_dir or PERF_DIR
    if not spec or not os.path.isdir(d):
        return None
    for name in sorted(os.listdir(d)):
        if name.endswith(".json") and (name == f"{spec}.json" or name.startswith(f"{spec}-")):
            with open(os.path.join(d, name), encoding="utf-8") as f:
                return json.load(f)
    return None


def test_filter(cfg):
    return ";".join(cfg.get("tests", [])) if cfg else ""


def host_profile():
    """이 기기가 어떤 기준 프로필인지. Apple Silicon Mac = mac-m1-1600p."""
    import platform
    if platform.system() == "Darwin" and platform.machine() == "arm64":
        return "mac-m1-1600p"
    return f"{platform.system().lower()}-{platform.machine().lower()}"


def editmode_metrics(rows, cfg):
    """예산 항목 키('test/group/stat') → 측정값. 측정이 없으면 None."""
    by = {r["name"]: r["groups"] for r in rows}
    out = {}
    for b in cfg.get("budgets", []):
        g = (by.get(b["test"]) or {}).get(b["group"])
        out[_bkey(b)] = g.get(b["stat"]) if g else None
    return out


def _bkey(b):
    return f'{b["test"]}/{b["group"]}/{b["stat"]}'


def judge_editmode(rows, cfg, base, is_reference, power):
    """예산·회귀 판정. 기준 기기 + 전원 연결일 때만 통과/실패 (FR-44), 나머지는 기록만."""
    metrics = editmode_metrics(rows, cfg)
    budget_rows, reg_rows = [], []
    for b in cfg.get("budgets", []):
        v = metrics[_bkey(b)]
        # max가 null이면 판정 없이 회귀 기준선만 (specs/006: 판정 런타임은 IL2CPP, Mono는 회귀만)
        limit = b.get("max")
        budget_rows.append({"ac": b.get("ac"), "label": b["label"], "metric": _bkey(b), "value": v,
                            "budget": "—" if limit is None else limit, "unit": b.get("unit", ""),
                            "ok": v is not None and (limit is None or v <= limit)})
        prev = (base or {}).get(_bkey(b))
        # "regression": false = 기록만 (짧은 ms 측정이 기기 부하에 흔들리는 항목 — 판정은 다른 런타임에서, specs/006)
        if v is None or prev is None or b.get("regression", True) is False:
            continue
        if prev == 0:
            change = 0.0 if v == 0 else math.inf
        else:
            change = (v - prev) / prev
        reg_rows.append({"ac": b.get("ac"), "label": b["label"], "metric": _bkey(b), "value": v, "previous": prev,
                         "changePct": round(change * 100, 4) if math.isfinite(change) else None,
                         "ok": change < REGRESSION_LIMIT - 1e-9})
    judged = bool(is_reference) and power == "ac"
    note = ""
    if not is_reference:
        note = f"기준 기기({cfg.get('profile')}) 아님 — 기록만"
    elif power != "ac":
        note = f"전원 상태 {power} — 판정 제외"
    ok = True
    if judged:
        ok = all(r["ok"] for r in budget_rows) and all(r["ok"] for r in reg_rows)
    return {"judged": judged, "ok": ok, "note": note, "profile": cfg.get("profile"), "metrics": metrics,
            "budget": budget_rows, "regression": reg_rows}


def write_editmode_perf(out_dir, rows, judgement):
    os.makedirs(out_dir, exist_ok=True)
    p = os.path.join(out_dir, "perf.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump({"results": rows, "judgement": judgement}, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return p


def editmode_baseline_key(spec, profile):
    return f"editmode/{spec}/{profile}"
