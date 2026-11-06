"""specs/054 움직임 지표 (FR-70·71, AC-16): 플레이어가 쓴 motion.json을 읽어 장면별 표·시계열 그래프를 만든다.

판정은 플레이어 쪽 motionMeasure 단계가 기준값 파일(054-motion-standards.json, FR-72)로 한다 — 여기서는 표시만.
위반이 있으면 motion/ 폴더의 위반 프레임 캡처를 함께 붙이고, 위반이 없으면 그래프만 붙인다.
"""
import json
import os

from . import paths
from .htmlkit import e

# 그래프로 그릴 지표: (키, 라벨, 단위, 배율)
CHARTS = (
    ("rootError", "루트 오차", "cm", 100.0),
    ("slide", "미끄러짐", "cm", 100.0),
    ("yawDelta", "프레임당 yaw 변화", "°", 1.0),
    ("torsoOverlap", "몸통 겹침", "cm", 100.0),
    ("ballAfterContact", "접촉 후 화면 공 오차", "cm", 100.0),
)
# 접촉 이벤트에서 그리는 지표 (AC-16): (키, 라벨, 단위, 배율)
CONTACT_CHARTS = (
    ("timeError", "접촉 시각 차", "ms", 1000.0),
    ("distance", "접촉 거리", "cm", 100.0),
)
CHART_W, CHART_H = 260, 70
STANDARDS = os.path.join("Assets", "_Project", "Presentation", "Players", "Data", "054-motion-standards.json")


def standards(repo=None):
    """FR-72: 기준값 파일 → {version, thresholds, previous}. 없으면 None."""
    p = os.path.join(repo or paths.REPO, STANDARDS)
    if not os.path.exists(p):
        return None
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def _bound(t):
    lo, hi = t.get("min"), t.get("max")
    if lo is not None and hi is not None:
        return f"{lo} ~ {hi}"
    return f"≥ {lo}" if lo is not None else f"≤ {hi}"


def standards_html(std):
    """현재 기준 버전과, 바뀐 기준이 있으면 이전 값을 함께 (FR-72)."""
    if not std:
        return ""
    rows = []
    for k, old in sorted((std.get("previous") or {}).items()):
        cur = (std.get("thresholds") or {}).get(k, {})
        rows.append(f"<tr><td>{e(k)}</td><td>{e(_bound(cur))}</td><td>{e(_bound(old))}</td><td>{e(cur.get('unit', ''))}</td></tr>")
    head = f'<p class="muted">기준값 버전 {e(std.get("version"))}, {len(std.get("thresholds") or {})}개. 이전 기준에서 바뀐 값 {len(rows)}개.</p>'
    if not rows:
        return head
    return head + "<table><tr><th>기준</th><th>현재</th><th>이전</th><th>단위</th></tr>" + "".join(rows) + "</table>"


def load(out_dir):
    """결과 폴더의 motion.json → {scenes: [...], captures: [{file, path}]}. 없으면 None."""
    p = os.path.join(out_dir, "motion.json")
    if not os.path.exists(p):
        return None
    with open(p, encoding="utf-8") as f:
        doc = json.load(f)
    caps = []
    cdir = os.path.join(out_dir, "motion")
    if os.path.isdir(cdir):
        for name in sorted(os.listdir(cdir)):
            if name.endswith(".png"):
                caps.append({"file": name, "path": os.path.join(cdir, name)})
    doc["captures"] = caps
    return doc


def violation_count(doc):
    return sum(len(s.get("violations", [])) for s in (doc or {}).get("scenes", []))


def sparkline(points, scale=1.0, width=CHART_W, height=CHART_H):
    """[[t, v], ...] → 인라인 SVG. 축은 첫·마지막 시각과 최대값 라벨. 점이 없으면 빈 문자열."""
    if not points:
        return ""
    ts = [p[0] for p in points]
    vs = [p[1] * scale for p in points]
    t0, t1 = min(ts), max(ts)
    top = max(max(vs), 1e-6)
    span = max(t1 - t0, 1e-6)
    pad = 14
    pts = []
    for t, v in zip(ts, vs):
        x = (t - t0) / span * (width - 4) + 2
        y = height - pad - v / top * (height - pad - 6)
        pts.append(f"{x:.1f},{y:.1f}")
    return (
        f'<svg width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img">'
        f'<line x1="2" y1="{height - pad}" x2="{width - 2}" y2="{height - pad}" stroke="currentColor" stroke-opacity=".25"/>'
        f'<polyline fill="none" stroke="#2f6fed" stroke-width="1.5" points="{" ".join(pts)}"/>'
        f'<text x="2" y="{height - 2}" font-size="10" fill="currentColor">{t0:.1f}s</text>'
        f'<text x="{width - 2}" y="{height - 2}" font-size="10" fill="currentColor" text-anchor="end">{t1:.1f}s</text>'
        f'<text x="{width - 2}" y="10" font-size="10" fill="currentColor" text-anchor="end">최대 {top:.2f}</text>'
        "</svg>"
    )


def _stat(s, key, scale=100.0, stat="p95"):
    v = (s.get("stats") or {}).get(key)
    return "–" if not v else f"{v[stat] * scale:.1f}"


def _scene_row(s):
    n = len(s.get("violations", []))
    kicks = [c for c in s.get("contacts", []) if c.get("isKick")]
    touches = [c for c in s.get("contacts", []) if not c.get("missed")]
    dist = f"{max(c['distance'] for c in touches) * 100:.1f}" if touches else "–"
    plant = "; ".join(f"옆 {c['plantSide'] * 100:.0f} 뒤 {c['plantBack'] * 100:.0f}" for c in kicks) or "–"
    tag = '<span class="tag pass">0</span>' if n == 0 else f'<span class="tag fail">{n}</span>'
    return (f'<tr><td>{e(s["scene"])}</td><td>{s["rate"]}x</td><td>{tag}</td><td>{_stat(s, "rootError")}</td><td>{_stat(s, "rootError", stat="max")}</td>'
            f'<td>{_stat(s, "slide")}</td><td>{_stat(s, "slide", stat="max")}</td><td>{_stat(s, "yawDelta", 1.0, "max")}</td><td>{dist}</td><td>{e(plant)}</td></tr>')


def section(scenarios, assets):
    """리포트 카드. motion 결과가 없으면 빈 문자열."""
    out = []
    for sc in scenarios:
        for combo in sc.get("combos", []):
            doc = combo.get("motion")
            if not doc:
                continue
            scenes = doc.get("scenes", [])
            rows = "".join(_scene_row(s) for s in scenes)
            charts = []
            for s in scenes:
                if s.get("rate") != 1.5:
                    continue
                cells = []
                for key, label, unit, scale in CHARTS:
                    pts = (s.get("series") or {}).get(key)
                    if pts:
                        cells.append(f'<div><div class="muted">{e(label)} ({e(unit)})</div>{sparkline(pts, scale)}</div>')
                touches = [c for c in s.get("contacts", []) if not c.get("missed")]
                for key, label, unit, scale in CONTACT_CHARTS:
                    pts = [[c["time"], c[key]] for c in touches]
                    if pts:
                        cells.append(f'<div><div class="muted">{e(label)} ({e(unit)})</div>{sparkline(pts, scale)}</div>')
                charts.append(f'<h3>{e(s["scene"])}</h3><div style="display:flex;flex-wrap:wrap;gap:12px">{"".join(cells)}</div>')
            caps = []
            for c in doc.get("captures", []):
                rel = assets.copy(c["path"], f'motion/{combo["key"]}/{c["file"]}')
                caps.append(f'<figure><img src="{e(rel)}" loading="lazy" style="max-width:320px"><figcaption>{e(c["file"])}</figcaption></figure>')
            vio = violation_count(doc)
            faults = sorted({f"{s['scene']} e{x}" for s in scenes for x in s.get("simFaults", []) if not s.get("expectSimFault")})
            expected = sorted({s["scene"] for s in scenes if s.get("expectSimFault") and s.get("simFaults")})
            fault_html = ""
            if faults:
                fault_html += f'<p><span class="tag fail">시뮬 실패</span> 005 한계 초과 (054가 아니라 시뮬 쪽): {e(", ".join(faults))}</p>'
            if expected:
                fault_html += f'<p class="muted">시뮬 오류를 일부러 담은 장면 (FR-80, 054 지표에서 제외): {e(", ".join(expected))}</p>'
            cap_html = ""
            if caps:
                cap_html = f'<h3>위반 프레임</h3><div style="display:flex;flex-wrap:wrap;gap:12px">{"".join(caps)}</div>'
            out.append(
                f'<div class="card"><h2>움직임 지표 — {e(combo["key"])}</h2>'
                f'<p class="muted">{len(scenes)}개 측정, 위반 {vio}건. 값은 cm, yaw는 °. p95·최대는 재생 시계 기준. 위반 프레임은 지표별 첫 프레임, 장면·배속당 최대 8장.</p>'
                + standards_html(standards()) + fault_html +
                '<div class="scroll"><table><tr><th>장면</th><th>배속</th><th>위반</th><th>루트 p95</th><th>루트 최대</th><th>미끄러짐 p95</th>'
                '<th>미끄러짐 최대</th><th>yaw 최대</th><th>접촉 거리 최대</th><th>디딤발(cm)</th></tr>'
                + rows + "</table></div>" + "".join(charts) + cap_html + "</div>"
            )
    return "".join(out)
