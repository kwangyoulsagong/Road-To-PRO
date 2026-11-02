"""성능 집계·예산·회귀 판정 (specs/000 FR-40~44).

드라이버(`PerfSampler`)가 남긴 perf.json의 구간 하나:
{name, warmupFrames, frameMs[], mainThreadMs[], gcAllocBytes[], drawCalls[], triangles[],
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
    return {
        "frames": len(frames),
        "frameMs": _stats(frames),
        "mainThreadMs": _stats(main) if main else None,
        "gcAllocPerFrameBytes": {"avg": sum(gc) / len(gc), "max": max(gc)} if gc else None,
        "gcAllocTotalBytes": sum(gc) if gc else None,
        "drawCalls": {"avg": sum(dc) / len(dc), "max": max(dc)} if dc else None,
        "triangles": {"avg": sum(tri) / len(tri), "max": max(tri)} if tri else None,
        "textureMemoryBytes": sec.get("textureMemoryBytes"),
        "totalMemoryBytes": sec.get("totalMemoryBytes"),
    }


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
        cur = cur[part]
    return cur


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
