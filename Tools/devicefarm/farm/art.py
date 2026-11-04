"""Blender 생성 테스트 증거 (specs/044 T06). `farm.py art --spec NNN`.

Art/Blender/tests의 unittest를 이 프로세스에서 돌려 로직 리포트와 같은 형식(LogicResult)으로 만든다.
통합 테스트가 남긴 인자 세트별 미리보기 PNG를 리포트에 격자로 붙이고 evidence/blender/<세트>/로 복사한다.
"""
import os
import re
import shutil
import subprocess
import sys
import time
import traceback
import unittest

from . import chrome, logic, paths
from .htmlkit import e

ART_TESTS = os.path.join(paths.REPO, "Art", "Blender", "tests")
BLENDER_APP = "/Applications/Blender.app/Contents/MacOS/Blender"
PREVIEW_SETS = ("min", "default", "max")
PREVIEW_VIEWS = ("main", "goal", "top")

# 스펙별 테스트·미리보기 설정. 없는 스펙은 044 설정(기존 동작)을 쓴다.
SPEC_ART = {
    "044": {"pattern": ("test_markings.py", "test_params.py", "test_stadium.py"), "sets": PREVIEW_SETS, "views": PREVIEW_VIEWS, "stem": "stadium", "cols": 3,
            "title": "Blender 미리보기 (인자 세트 최소·기본·최대)"},
    "043": {"pattern": "test_player.py", "sets": ("default",), "views": ("lineup", "heads", "poses"), "stem": "player", "cols": 1,
            "title": "Blender 미리보기 (LOD 3단 + 극단 체형 4종 · 헤어 10종 · 대표 클립 접촉 프레임)"},
    "056": {"pattern": ("test_player.py", "test_face.py"), "sets": ("default",), "views": ("lineup", "heads", "poses"), "stem": "player",
            "cols": 1, "title": "Blender 미리보기 (MPFB2 기준 LOD 3단 + 극단 체형 4종 · 헤어 10종 × 얼굴 변주 · 대표 클립)"},
}


def spec_cfg(spec):
    return SPEC_ART.get(str(spec), SPEC_ART["044"])


class _Collector(unittest.TestResult):
    def __init__(self):
        super().__init__()
        self.cases = []
        self._t0 = {}

    def startTest(self, test):
        super().startTest(test)
        self._t0[test.id()] = time.time()

    def _add(self, test, result, err=None):
        msg, stack = "", ""
        if err is not None:
            msg = str(err[1]).strip()
            stack = "".join(traceback.format_exception(*err)).strip()
        tid = test.id()
        parts = tid.split(".")
        self.cases.append({
            "name": ".".join(parts[-2:]),
            "fullname": tid,
            "result": result,
            "duration": time.time() - self._t0.get(tid, time.time()),
            "message": msg,
            "stack": stack,
            "categories": [],
            "acs": sorted(set(re.findall(r"AC-\d+", type(test).__doc__ or ""))),
        })

    def addSuccess(self, test):
        super().addSuccess(test)
        self._add(test, "Passed")

    def addFailure(self, test, err):
        super().addFailure(test, err)
        self._add(test, "Failed", err)

    def addError(self, test, err):
        super().addError(test, err)
        self._add(test, "Failed", err)

    def addSkip(self, test, reason):
        super().addSkip(test, reason)
        self._add(test, "Skipped", None)
        self.cases[-1]["message"] = reason


def blender_version():
    try:
        out = subprocess.run([BLENDER_APP, "--version"], capture_output=True, text=True, timeout=60).stdout
        return out.splitlines()[0].strip() if out else "?"
    except (OSError, subprocess.SubprocessError):
        return "?"


def run_tests(out_root, pattern="test_*.py", start_dir=ART_TESTS):
    """Art/Blender/tests 전체 실행 → LogicResult. 통합 테스트 산출물은 out_root/<세트>/."""
    os.makedirs(out_root, exist_ok=True)
    os.environ["FARM_ART_OUT"] = out_root
    if start_dir not in sys.path:
        sys.path.insert(0, start_dir)
    pats = (pattern,) if isinstance(pattern, str) else tuple(pattern)
    suite = unittest.TestSuite()
    for pat in pats:
        suite.addTests(unittest.TestLoader().discover(start_dir, pattern=pat, top_level_dir=start_dir))
    col = _Collector()
    t0 = time.time()
    suite.run(col)
    n = len(col.cases)
    failed = sum(1 for c in col.cases if c["result"] == "Failed")
    skipped = sum(1 for c in col.cases if c["result"] == "Skipped")
    return logic.LogicResult(total=n, passed=n - failed - skipped, failed=failed, skipped=skipped,
                             duration=time.time() - t0, cases=col.cases)


def ac_evidence(res):
    """테스트 클래스 docstring의 AC-NN → {AC: [{label, ok}]} (specs/044)."""
    by = {}
    for c in res.cases:
        for ac in c.get("acs", []):
            by.setdefault(ac, []).append(c)
    out = {}
    for ac, cs in sorted(by.items()):
        ok = all(c["result"] == "Passed" for c in cs)
        n = sum(c["result"] == "Passed" for c in cs)
        out[ac] = [{"label": f"Blender 생성 테스트 {n}/{len(cs)} 통과", "ok": ok}]
    return out


def previews(out_root, cfg=None):
    """{세트: {시점: 경로}} — 있는 것만."""
    cfg = cfg or SPEC_ART["044"]
    got = {}
    for s in cfg["sets"]:
        for v in cfg["views"]:
            p = os.path.join(out_root, s, f"{cfg['stem']}.preview-{v}.png")
            if os.path.exists(p):
                got.setdefault(s, {})[v] = p
    return got


def copy_previews(found, ev_dir):
    out = {}
    for s, views in found.items():
        d = os.path.join(ev_dir, "blender", s)
        os.makedirs(d, exist_ok=True)
        for v, p in views.items():
            dst = os.path.join(d, f"preview-{v}.png")
            shutil.copyfile(p, dst)
            out.setdefault(s, {})[v] = dst
    return out


def preview_grid_html(found, base_dir, cfg=None):
    cfg = cfg or SPEC_ART["044"]
    if not found:
        return ""
    rows = []
    for s in cfg["sets"]:
        if s not in found:
            continue
        cells = "".join(
            f'<figure><img src="{e(os.path.relpath(found[s][v], base_dir))}" alt="{e(s)} {e(v)}">'
            f"<figcaption>{e(s)} · {e(v)}</figcaption></figure>"
            for v in cfg["views"] if v in found[s])
        rows.append(f'<div class="pgrid">{cells}</div>')
    css = ('<style>.pgrid{display:grid;grid-template-columns:repeat(%d,1fr);gap:8px;margin:8px 0}'
           '.pgrid img{width:100%%;border-radius:6px;display:block}.pgrid figure{margin:0}'
           '.pgrid figcaption{font-size:12px;color:#666;margin-top:2px}</style>') % cfg["cols"]
    return f'{css}<div class="card"><h2>{e(cfg["title"])}</h2>{"".join(rows)}</div>'


def write_report(res, meta, found, out_html, cfg=None):
    html = logic.render_html(res, meta, title="Blender 생성 테스트 리포트")
    grid = preview_grid_html(found, os.path.dirname(out_html), cfg)
    html = html.replace("</main>", grid + "</main>", 1) if "</main>" in html else html + grid
    with open(out_html, "w", encoding="utf-8") as f:
        f.write(html)
    return out_html


def cmd_art(args, argv, log=print):
    from . import commands
    from .errors import VerificationFailed

    t0 = time.time()
    run_dir = commands.new_run_dir("art")
    out_root = os.path.join(run_dir, "blender")
    meta = logic.metadata()
    meta["blender"] = blender_version()
    log(f"[farm] Blender 생성 테스트 실행 중… ({meta['blender']})")
    cfg = spec_cfg(args.spec)
    res = run_tests(out_root, pattern=cfg["pattern"])
    ev_dir = commands.evidence_dir(args.spec, run_dir)
    os.makedirs(ev_dir, exist_ok=True)
    found = copy_previews(previews(out_root, cfg), ev_dir)
    meta.update(durationSec=time.time() - t0, command=commands.command_line(argv))
    html = write_report(res, meta, found, os.path.join(ev_dir, "art.html"), cfg)
    png = chrome.screenshot(html, os.path.join(ev_dir, "art.png"))
    import json
    with open(os.path.join(ev_dir, "art.json"), "w", encoding="utf-8") as f:
        json.dump({"result": res.to_dict(), "meta": meta, "png": os.path.relpath(png, ev_dir) if png else None,
                   "previews": {s: {v: os.path.relpath(p, ev_dir) for v, p in vs.items()} for s, vs in found.items()},
                   "acEvidence": ac_evidence(res),
                   "cases": [{"name": c["name"], "result": c["result"], "message": c["message"], "acs": c.get("acs", [])} for c in res.cases]},
                  f, ensure_ascii=False, indent=2)
    log(f"[farm] 합계 {res.total} 통과 {res.passed} 실패 {res.failed} 건너뜀 {res.skipped}")
    for c in res.failures():
        log(f"[farm]   ✗ {c['name']}: {c['message'][:300]}")
    log(f"[farm] 리포트: {html}")
    if not res.ok:
        raise VerificationFailed("Blender 생성 테스트 실패")
