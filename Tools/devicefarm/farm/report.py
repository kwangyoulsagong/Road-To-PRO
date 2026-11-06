"""스펙별 증거 리포트 evidence/report.html (specs/000 FR-70~74).

구성: 요약 → AC 연결표 → 로직 리포트 스크린샷 → 화면 격자(행=단계, 열=조합) → 성능 → 결정론 → 로그 → 셀프테스트 → 실행 정보.
이미지는 JPEG 썸네일로 줄여 assets/에 두고(아티팩트 용량 ≤ 15 MB), 원본은 .farm/runs/에 남긴다.
"""
import json
import os
import re
import shutil

from PIL import Image

from . import motion
from .htmlkit import e, page

MAX_BYTES = 15 * 1024 * 1024
LEVELS = [(480, 70), (360, 55), (240, 45)]  # (긴 변 px, JPEG 품질) — 용량을 넘으면 다음 단계

STATUS_LABEL = {
    "pass": ("pass", "통과"),
    "fail": ("fail", "실패"),
    "candidate": ("cand", "기준 후보"),
    "skip": ("skip", "건너뜀"),
    "deferred": ("warn", "보류"),
}

EXTRA_CSS = """
.scroll table{width:max-content;min-width:100%}
.scroll td,.scroll th{white-space:nowrap}
.scroll td.wrap{white-space:normal;min-width:180px;max-width:360px}
.grid td,.grid th{text-align:center;min-width:150px}
.grid th[scope=row]{text-align:left;min-width:90px}
.grid img{display:block;width:150px;height:auto;border-radius:8px;border:3px solid var(--line);margin:0 auto 4px}
.grid .pass img{border-color:var(--pass)}.grid .fail img{border-color:var(--fail)}.grid .cand img{border-color:var(--cand)}
details summary{cursor:pointer;list-style:none}details summary::-webkit-details-marker{display:none}
.trio{display:flex;flex-wrap:wrap;gap:8px;margin-top:6px;text-align:left}
.trio figure{margin:0}.trio img{width:220px;border:0;border-radius:6px}
.trio figcaption{font-size:12px;color:var(--muted)}
.summary-row{display:flex;flex-wrap:wrap;gap:8px 20px;align-items:center}
.verdict.deferred{background:var(--warn);color:#000}
"""

LAYOUT_PROBE = """<script>
addEventListener('load',function(){var d=document.documentElement;
d.setAttribute('data-layout',d.scrollWidth<=innerWidth+1?'ok':'overflow:'+d.scrollWidth+'>'+innerWidth);});
</script>"""


EXPECTED_FAIL_TAG = ' <span class="tag info">기대한 실패</span>'


def _tag(status):
    cls, label = STATUS_LABEL.get(status, ("info", status))
    return f'<span class="tag {cls}">{e(label)}</span>'


def _ok_tag(ok, yes="통과", no="실패"):
    return f'<span class="tag {"pass" if ok else "fail"}">{yes if ok else no}</span>'


class _Assets:
    def __init__(self, out_dir, level):
        self.root = os.path.join(out_dir, "assets")
        self.size, self.quality = LEVELS[level]
        self.cache = {}

    def thumb(self, src, rel):
        if not src or not os.path.exists(src):
            return None
        if src in self.cache:
            return self.cache[src]
        dst = os.path.join(self.root, rel + ".jpg")
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with Image.open(src) as im:
            im = im.convert("RGB")
            im.thumbnail((self.size, self.size))
            im.save(dst, "JPEG", quality=self.quality, optimize=True)
        url = os.path.relpath(dst, os.path.dirname(self.root))
        self.cache[src] = url
        return url

    def copy(self, src, rel):
        if not src or not os.path.exists(src):
            return None
        dst = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(src, dst)
        return os.path.relpath(dst, os.path.dirname(self.root))


def _header(ev, verdict, level):
    m = ev["meta"]
    parts = [
        f'<h1>{e(ev["title"])}</h1><div class="card"><div class="summary-row">'
        f'<span class="verdict {_verdict_class(verdict)}">{e(_verdict_label(ev, verdict))}</span>'
        f'<span class="muted">스펙 {e(ev["spec"]["id"])} · {e(ev["spec"].get("name", ""))}</span></div>'
        f'<p class="meta"><span>커밋 <b>{e(m.get("commit", "")[:10])}</b></span>'
        f'<span>브랜치 <b>{e(m.get("branch"))}</b></span>'
        f'<span>실행 <b>{e(m.get("time"))}</b></span>'
        f'<span>걸린 시간 <b>{m.get("durationSec", 0):.0f}s</b></span>'
        f'<span>Unity <b>{e(m.get("unity"))}</b></span></p>'
        f'<p class="meta"><span>프로필 <b>{e(", ".join(m.get("profiles", [])))}</b></span></p></div>'
    ]
    if m.get("dirty"):
        parts.append('<div class="warnbar">작업 트리에 커밋 안 된 변경이 있다 — 이 증거로는 완료 판정 불가</div>')
    if level > 0:
        parts.append(f'<div class="warnbar">리포트 용량 한도 때문에 이미지를 {LEVELS[level][0]}px로 축소했다. 원본은 로컬 .farm/runs/</div>')
    return "".join(parts)


def _verdict_class(verdict):
    return {"pass": "pass", "deferred": "deferred"}.get(verdict, "fail")


def _verdict_label(ev, verdict):
    if verdict == "pass":
        return "통과"
    if verdict == "candidate":
        return "기준 후보 대기"
    if verdict == "deferred":
        return f"보류 {len(pending_deferred(ev))}건 · 완료 아님"
    return "실패"


def pending_deferred(ev):
    """보류로 지정됐고 아직 증거가 없는 AC id 목록 (specs/004 T19a). 증거가 생기면 보류가 아니라 일반 판정."""
    deferred = ev.get("deferred") or {}
    links = ev.get("acEvidence", {})
    return [a["id"] for a in ev["spec"].get("acs", []) if a["id"] in deferred and not links.get(a["id"])]


def _ac_table(ev):
    rows = []
    links = ev.get("acEvidence", {})
    deferred = ev.get("deferred") or {}
    for ac in ev["spec"].get("acs", []):
        items = links.get(ac["id"], [])
        if items:
            ok = all(i["ok"] for i in items)
            verdict_html = _ok_tag(ok)
            ev_html = "<br>".join(f'{_ok_tag(i["ok"])} {e(i["label"])}' for i in items)
        elif ac["id"] in deferred:
            verdict_html = '<span class="tag warn">보류</span>'
            ev_html = f'<span class="tag warn">보류</span> {e(deferred[ac["id"]])}'
        else:
            verdict_html = _ok_tag(False)
            ev_html = '<span class="tag fail">증거 없음</span>'
        rows.append(
            f'<tr><td><b>{e(ac["id"])}</b></td><td>{e(ac["kind"])}</td><td>{verdict_html}</td>'
            f'<td>{ev_html}</td><td class="muted wrap">{e(ac["text"])}</td></tr>'
        )
    return (
        '<div class="card" id="ac"><h2>수용 기준 ↔ 증거</h2><div class="scroll"><table>'
        "<tr><th>AC</th><th>종류</th><th>판정</th><th>증거</th><th>조건</th></tr>"
        + "".join(rows) + "</table></div></div>"
    )


def _logic(ev, assets):
    lg = ev.get("logic")
    if not lg:
        return ""
    url = assets.copy(lg.get("png"), "logic.png") if lg.get("png") else None
    img = f'<img src="{e(url)}" alt="로직 테스트 리포트" style="width:100%;border-radius:10px">' if url else ""
    return (
        f'<div class="card"><h2>로직 테스트 {_ok_tag(lg["ok"])}</h2>'
        f'<p class="meta"><span>합계 <b>{lg.get("total", 0)}</b></span><span>통과 <b>{lg.get("passed", 0)}</b></span>'
        f'<span>실패 <b>{lg.get("failed", 0) + len(lg.get("compileErrors", []))}</b></span></p>{img}</div>'
    )


def _art(ev, assets):
    a = ev.get("art")
    if not a:
        return ""
    url = assets.copy(a.get("png"), "art.png") if a.get("png") and os.path.exists(a["png"]) else None
    img = f'<img src="{e(url)}" alt="Blender 생성 테스트 리포트" style="width:100%;border-radius:10px">' if url else ""
    return (
        f'<div class="card"><h2>Blender 생성 테스트 {_ok_tag(a["ok"])}</h2>'
        f'<p class="meta"><span>합계 <b>{a.get("total", 0)}</b></span><span>통과 <b>{a.get("passed", 0)}</b></span>'
        f'<span>실패 <b>{a.get("failed", 0)}</b></span><span>{e(a.get("blender") or "")}</span></p>{img}</div>'
    )


def _image_checks(scs):
    rows = []
    for sc in scs:
        for c in sc["combos"]:
            for r in c.get("imageChecks", []):
                rows.append(f'<tr><td>{e(sc["scenario"])}</td><td>{e(c["key"])}</td><td>{e(r.get("ac") or "")}</td>'
                            f'<td>{e(r["type"])} · {e(r["capture"])}</td><td>{_ok_tag(r["ok"])}</td>'
                            f'<td class="muted wrap">{e(r["detail"])}</td></tr>')
    if not rows:
        return ""
    return ('<div class="card"><h2>이미지 검사</h2><div class="scroll"><table>'
            "<tr><th>시나리오</th><th>조합</th><th>AC</th><th>검사</th><th>판정</th><th>값</th></tr>"
            + "".join(rows) + "</table></div></div>")


def _grid(sc, assets, idx):
    combos = sc["combos"]
    steps = []
    for c in combos:
        for cap in c["captures"]:
            if cap["step"] not in steps:
                steps.append(cap["step"])
    head = "".join(
        f'<th scope="col">{e(c["key"])}<br>{_tag("pass" if c["status"] == "passed" else "fail")}'
        f'{"" if c["status"] == "passed" else "<br><span class=muted>" + e(c["status"] + " " + c["reason"]) + "</span>"}</th>'
        for c in combos
    )
    rows = []
    for st in steps:
        cells = []
        for c in combos:
            cap = next((x for x in c["captures"] if x["step"] == st), None)
            if cap is None:
                cells.append('<td><span class="muted">—</span></td>')
                continue
            base = f'{idx:02d}-{sc["scenario"]}/{c["key"]}/{st}'  # 같은 시나리오를 여러 번 돌려도 썸네일이 겹치지 않게
            cur = assets.thumb(cap["current"], base + "-cur")
            bl = assets.thumb(cap.get("baseline"), base + "-base")
            df = assets.thumb(cap.get("diff"), base + "-diff")
            cls = STATUS_LABEL.get(cap["status"], ("info", ""))[0]
            figs = "".join(
                f'<figure><img src="{e(u)}" alt="{e(t)}"><figcaption>{e(t)}</figcaption></figure>'
                for u, t in ((bl, "기준"), (cur, "현재"), (df, "차이")) if u
            )
            ratio = cap.get("diffRatio") or 0
            cells.append(
                f'<td class="{cls}"><details><summary>'
                f'{f"<img src={chr(34)}{e(cur)}{chr(34)} alt={chr(34)}{e(st)}{chr(34)}>" if cur else ""}'
                f'{_tag(cap["status"])} <span class="muted">{ratio:.3%}</span></summary>'
                f'<div class="trio">{figs}</div><p class="muted">{e(cap.get("reason", ""))}</p></details></td>'
            )
        rows.append(f'<tr><th scope="row">{e(st)}</th>{"".join(cells)}</tr>')
    fails = []
    for c in combos:
        for fc in c.get("failCaptures", []):
            u = assets.thumb(fc, f'{idx:02d}-{sc["scenario"]}/{c["key"]}/{os.path.basename(fc)}')
            if u:
                fails.append(f'<figure><img src="{e(u)}" alt="실패 시점"><figcaption>{e(c["key"])} 실패 시점</figcaption></figure>')
    fail_html = f'<h2>실패 시점 화면</h2><div class="trio">{"".join(fails)}</div>' if fails else ""
    return (
        f'<div class="card"><h2>화면 격자 · {e(sc.get("title") or sc["scenario"])} {_tag(sc["verdict"])}'
        f'{EXPECTED_FAIL_TAG if sc.get("expectedFail") else ""}</h2>'
        f'<p class="muted">{e(sc.get("description", ""))} · 칸을 누르면 기준·현재·차이</p>'
        f'<div class="scroll"><table class="grid"><tr><th></th>{head}</tr>{"".join(rows)}</table></div>{fail_html}</div>'
    )


def _fmt(v, unit=""):
    if v is None:
        return "—"
    if isinstance(v, float):
        return f"{v:.2f}{unit}"
    if isinstance(v, int) and v > 10_000_000:
        return f"{v / 1024 / 1024:.0f} MB"
    return f"{v}{unit}"


def _perf(scs):
    rows = []
    for sc in scs:
        for c in sc["combos"]:
            for r in c.get("perf", []):
                m = r.get("metrics") or {}
                fm = m.get("frameMs") or {}
                bad = [x for x in r.get("budget", []) + r.get("regression", []) if not x["ok"]]
                note = r.get("note") or ""
                detail = "; ".join(
                    f'{x["metric"]} {_fmt(x["value"])}' + (f' > 예산 {x["budget"]}' if "budget" in x else f' (+{x.get("changePct")}%)')
                    for x in bad
                )
                verdict = _ok_tag(r["ok"]) if r.get("judged") else '<span class="tag skip">기록만</span>'
                rows.append(
                    f'<tr><td>{e(sc["scenario"])}/{e(r["section"])}</td><td>{e(c["key"])}</td><td>{verdict}</td>'
                    f'<td>{_fmt(fm.get("avg"))}</td><td>{_fmt(fm.get("p95"))}</td><td>{_fmt(fm.get("p99"))}</td>'
                    f'<td>{_fmt((m.get("mainThreadMs") or {}).get("p95"))}</td><td>{_fmt((m.get("gpuMs") or {}).get("p95"))}</td><td>{_fmt(m.get("gcAllocTotalBytes"))}</td>'
                    f'<td>{_fmt((m.get("drawCalls") or {}).get("max"))}</td><td>{_fmt((m.get("triangles") or {}).get("max"))}</td>'
                    f'<td>{_fmt(m.get("totalMemoryBytes"))}</td><td class="muted wrap">{e(note)} {e(detail)}</td></tr>'
                )
    if not rows:
        return ""
    return (
        '<div class="card"><h2>성능 (ms)</h2><div class="scroll"><table>'
        "<tr><th>구간</th><th>조합</th><th>판정</th><th>평균</th><th>p95</th><th>p99</th><th>메인 p95</th><th>GPU p95</th>"
        "<th>GC 합</th><th>드로우콜</th><th>삼각형</th><th>메모리</th><th>비고</th></tr>"
        + "".join(rows) + "</table></div></div>"
    )


def _editmode_perf(ev):
    j = ev.get("editmodePerf")
    if not j:
        return ""
    verdict = _ok_tag(j["ok"]) if j["judged"] else '<span class="tag skip">기록만</span>'
    reg = {r["metric"]: r for r in j.get("regression", [])}
    rows = []
    for b in j["budget"]:
        r = reg.get(b["metric"])
        change = f'{r["changePct"]:+.1f}%' if r and r.get("changePct") is not None else ("—" if not r else "∞")
        ok = b["ok"] and (r is None or r["ok"])
        rows.append(f'<tr><td>{e(b.get("ac") or "")}</td><td>{e(b["label"])}</td><td>{_ok_tag(ok)}</td>'
                    f'<td>{_fmt(b["value"])} {e(b.get("unit", ""))}</td><td>≤ {e(b["budget"])} {e(b.get("unit", ""))}</td>'
                    f'<td>{e(change)}</td></tr>')
    return (f'<div class="card"><h2>EditMode 성능 {verdict}</h2>'
            f'<p class="meta"><span>기기 <b>{e(j.get("host", ""))}</b></span><span>기준 프로필 <b>{e(j.get("profile", ""))}</b></span>'
            f'<span>전원 <b>{e(j.get("power", ""))}</b></span></p>'
            + (f'<p class="muted">{e(j["note"])}</p>' if j.get("note") else "")
            + '<div class="scroll"><table><tr><th>AC</th><th>항목</th><th>판정</th><th>측정</th><th>예산</th><th>기준 대비</th></tr>'
            + "".join(rows) + "</table></div></div>")


def _bench_perf(ev):
    j = ev.get("benchPerf")
    if not j:
        return ""
    verdict = _ok_tag(j["ok"]) if j["judged"] else '<span class="tag skip">기록만</span>'
    reg = {r["metric"]: r for r in j.get("regression", [])}
    rows = []
    for b in j["budget"]:
        r = reg.get(b["metric"])
        change = f'{r["changePct"]:+.1f}%' if r and r.get("changePct") is not None else ("—" if not r else "∞")
        ok = b["ok"] and (r is None or r["ok"])
        rows.append(f'<tr><td>{e(b.get("ac") or "")}</td><td>{e(b["label"])}</td><td>{_ok_tag(ok)}</td>'
                    f'<td>{_fmt(b["value"])} {e(b.get("unit", ""))}</td><td>≤ {e(b["budget"])} {e(b.get("unit", ""))}</td>'
                    f'<td>{e(change)}</td></tr>')
    stages = "".join(f'<tr><td>{e(k)}</td><td>{_fmt(v)} µs</td></tr>' for k, v in sorted((j.get("stages") or {}).items()))
    m = j.get("metrics") or {}
    extra = "".join(f'<span>{e(k)} <b>{_fmt(m[k])}</b></span>' for k in ("full_match_ms", "full_match_ticks", "tick_avg_us", "stages_sum_us", "alloc_per_tick_b") if k in m)
    return (f'<div class="card"><h2>시뮬 벤치 ({e(j.get("backend", ""))}) {verdict}</h2>'
            f'<p class="meta"><span>기기 <b>{e(j.get("host", ""))}</b></span><span>CPU <b>{e(j.get("cpu", ""))}</b></span>'
            f'<span>전원 <b>{e(j.get("power", ""))}</b></span><span>커밋 <b>{e(j.get("commit", ""))}</b></span></p>'
            f'<p class="meta">{extra}</p>'
            + (f'<p class="muted">{e(j["note"])}</p>' if j.get("note") else "")
            + '<div class="scroll"><table><tr><th>AC</th><th>항목</th><th>판정</th><th>측정</th><th>예산</th><th>기준 대비</th></tr>'
            + "".join(rows) + "</table></div>"
            + ('<h3>단계별 평균 (결정 틱 1회)</h3><div class="scroll"><table><tr><th>단계</th><th>평균</th></tr>' + stages + "</table></div>" if stages else "")
            + "</div>")


def _determinism(scs):
    rows = []
    for sc in scs:
        d = sc.get("determinism")
        if not d:
            continue
        mism = d.get("mismatches") or {}
        detail = "; ".join(f'{k}: #{v["index"]} {v["name"]}' for k, v in mism.items())
        rows.append(f'<tr><td>{e(sc["scenario"])}</td><td>{_ok_tag(d["ok"])}</td><td>{e(d.get("reference"))}</td>'
                    f'<td class="muted wrap">{e(d.get("reason"))} {e(detail)}</td></tr>')
    if not rows:
        return ""
    return ('<div class="card"><h2>결정론 (조합 간 체크포인트 해시)</h2><div class="scroll"><table>'
            "<tr><th>시나리오</th><th>판정</th><th>기준 조합</th><th>내용</th></tr>" + "".join(rows) + "</table></div></div>")


def _logs(scs):
    rows = []
    for sc in scs:
        for c in sc["combos"]:
            lg = c.get("logs") or {}
            for kind, items in (("에러", lg.get("errors", [])), ("경고", lg.get("warnings", []))):
                for it in items:
                    rows.append(f'<tr><td>{e(c["key"])}</td><td><span class="tag fail">{kind}</span></td><td class="wrap">{e(it["message"])}</td></tr>')
            for it in lg.get("ignored", []):
                rows.append(f'<tr><td>{e(c["key"])}</td><td><span class="tag skip">무시</span></td>'
                            f'<td class="wrap">{e(it["message"])}<br><span class="muted">사유: {e(it["reason"])}</span></td></tr>')
    body = ("<div class=\"scroll\"><table><tr><th>조합</th><th>종류</th><th>내용</th></tr>" + "".join(rows) + "</table></div>") if rows else '<p class="muted">에러·경고 0</p>'
    return f'<div class="card"><h2>로그</h2>{body}</div>'


def _selftests(ev, assets):
    st = ev.get("selftests") or []
    if not st:
        return ""
    rows = []
    for i, s in enumerate(st):
        imgs = ""
        for j, src in enumerate(s.get("images", [])):
            safe = re.sub(r"[^A-Za-z0-9_-]+", "-", s["case"])
            u = assets.thumb(src, f"selftest/{i:02d}-{safe}-{j}")
            if u:
                imgs += f'<img src="{e(u)}" alt="{e(s["case"])}" style="width:160px;border-radius:6px;display:block;margin-top:4px">'
        rows.append(
            f'<tr><td><b>{e(s["case"])}</b></td><td>{e(", ".join(s.get("acs", [])))}</td><td>{_ok_tag(s["ok"])}</td>'
            f'<td class="wrap">{e(s.get("expected", ""))}</td><td class="muted wrap">{e(s.get("detail", ""))}{imgs}</td></tr>'
        )
    rows = "".join(rows)
    return ('<div class="card"><h2>셀프테스트 (일부러 깨뜨린 경우가 기대대로 잡히는가)</h2><div class="scroll"><table>'
            "<tr><th>케이스</th><th>AC</th><th>판정</th><th>기대</th><th>결과</th></tr>" + rows + "</table></div></div>")


def _budgets(ev):
    b = ev.get("budgets") or []
    if not b:
        return ""
    rows = "".join(f'<tr><td>{e(x["item"])}</td><td>{e(x["budget"])}</td><td>{e(x["value"])}</td><td>{_ok_tag(x["ok"])}</td></tr>' for x in b)
    return ('<div class="card"><h2>하네스 예산</h2><div class="scroll"><table>'
            "<tr><th>항목</th><th>예산</th><th>측정</th><th>판정</th></tr>" + rows + "</table></div></div>")


def _run_info(ev):
    m = ev["meta"]
    return (f'<div class="card"><h2>다시 실행하려면</h2><pre>{e(m.get("command", ""))}</pre>'
            f'<p class="muted">플레이어 인자는 run.json의 combos[].args</p></div>')


def verdict_of(ev):
    acs = ev["spec"].get("acs", [])
    links = ev.get("acEvidence", {})
    # 완료 판정(셀프테스트·/verify)일 때만 모든 AC에 증거가 있어야 한다. 일반 smoke/full은 연결된 AC만 본다.
    pending = set(pending_deferred(ev))
    if ev.get("requireAllAcs"):
        # 보류 AC(증거 없음)는 실패로 세지 않지만 통과도 아니다 → 아래에서 'deferred'
        ac_ok = all(links.get(a["id"]) and all(i["ok"] for i in links[a["id"]]) for a in acs if a["id"] not in pending) if acs else True
    else:
        ac_ok = all(i["ok"] for items in links.values() for i in items)
    sc_v = [s["verdict"] for s in ev.get("scenarios", [])]
    if ev.get("verdictOverride"):
        return ev["verdictOverride"]
    em = ev.get("editmodePerf")
    if "fail" in sc_v or not ac_ok or (ev.get("logic") and not ev["logic"]["ok"]) or (em and not em["ok"]):
        return "fail"
    if "candidate" in sc_v:
        return "candidate"
    if pending and ev.get("requireAllAcs"):
        return "deferred"
    return "pass"


def _dir_size(d):
    return sum(os.path.getsize(os.path.join(p, f)) for p, _, fs in os.walk(d) for f in fs) if os.path.isdir(d) else 0


def write(ev, out_dir, max_bytes=MAX_BYTES):
    os.makedirs(out_dir, exist_ok=True)
    verdict = verdict_of(ev)
    for level in range(len(LEVELS)):
        assets_dir = os.path.join(out_dir, "assets")
        if os.path.isdir(assets_dir):
            shutil.rmtree(assets_dir)
        assets = _Assets(out_dir, level)
        body = "".join([
            _header(ev, verdict, level),
            _ac_table(ev),
            _art(ev, assets),
            _logic(ev, assets),
            _editmode_perf(ev),
            _bench_perf(ev),
            "".join(_grid(sc, assets, i) for i, sc in enumerate(ev.get("scenarios", []))),
            motion.section(ev.get("scenarios", []), assets),
            _image_checks(ev.get("scenarios", [])),
            _perf(ev.get("scenarios", [])),
            _determinism(ev.get("scenarios", [])),
            _logs(ev.get("scenarios", [])),
            _selftests(ev, assets),
            _budgets(ev),
            _run_info(ev),
        ])
        html = page(ev["title"], body, EXTRA_CSS, LAYOUT_PROBE)
        size = _dir_size(assets_dir) + len(html.encode())
        if size <= max_bytes or level == len(LEVELS) - 1:
            break
    path = os.path.join(out_dir, "report.html")
    with open(path, "w", encoding="utf-8") as f:
        f.write(html)
    slim = json.loads(json.dumps(ev, default=str))
    slim["verdict"] = verdict
    with open(os.path.join(out_dir, "run.json"), "w", encoding="utf-8") as f:
        json.dump(slim, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return path
