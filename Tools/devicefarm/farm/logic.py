"""로직 증거: EditMode 테스트 실행·결과 해석·HTML 렌더링 (specs/000 FR-01~03)."""
import datetime as _dt
import os
import re
import subprocess
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field

from . import paths
from .errors import RunnerError
from .htmlkit import e, page

_COMPILE_ERR = re.compile(r"^.*\berror CS\d{4}\b.*$", re.M)
_COMPILE_WARN = re.compile(r"^.*\bwarning CS\d{4}\b.*$", re.M)


@dataclass
class LogicResult:
    total: int
    passed: int
    failed: int
    skipped: int
    duration: float
    cases: list
    compile_errors: list = field(default_factory=list)

    @property
    def ok(self):
        return not self.compile_errors and self.failed == 0 and self.total > 0

    def failures(self):
        return [c for c in self.cases if c["result"] == "Failed"]

    def ordered_cases(self):
        order = {"Failed": 0, "Skipped": 1, "Inconclusive": 1, "Passed": 2}
        return sorted(self.cases, key=lambda c: (order.get(c["result"], 1), c["fullname"]))

    def to_dict(self):
        return {
            "ok": self.ok,
            "total": self.total,
            "passed": self.passed,
            "failed": self.failed,
            "skipped": self.skipped,
            "duration": self.duration,
            "compileErrors": self.compile_errors,
            "failures": [{"name": c["name"], "message": c["message"]} for c in self.failures()],
        }


def _short(fullname, classname):
    cls = (classname or "").split(".")[-1]
    meth = fullname.split(".")[-1] if fullname else ""
    if "(" in fullname:  # 파라미터 테스트: Method(1,2)
        meth = fullname[len(classname) + 1 :] if classname and fullname.startswith(classname) else meth
    return f"{cls}.{meth}" if cls else meth


def parse_results(xml_path):
    try:
        root = ET.parse(xml_path).getroot()
    except (OSError, ET.ParseError) as err:
        raise RunnerError(f"테스트 결과 XML을 읽을 수 없다: {xml_path}: {err}") from err
    cases = []
    for tc in root.iter("test-case"):
        res = tc.get("result", "")
        if res not in ("Passed", "Failed"):
            res = "Skipped" if res in ("Skipped", "Ignored") else res
        cases.append(
            {
                "name": _short(tc.get("fullname", ""), tc.get("classname", "")),
                "fullname": tc.get("fullname", ""),
                "result": res,
                "duration": float(tc.get("duration", 0) or 0),
                "message": (tc.findtext("failure/message") or tc.findtext("reason/message") or "").strip(),
                "stack": (tc.findtext("failure/stack-trace") or "").strip(),
            }
        )
    return LogicResult(
        total=int(root.get("total", 0)),
        passed=int(root.get("passed", 0)),
        failed=int(root.get("failed", 0)),
        skipped=int(root.get("skipped", 0)),
        duration=float(root.get("duration", 0) or 0),
        cases=cases,
    )


def extract_compile_errors(log_text):
    return list(dict.fromkeys(m.group(0).strip() for m in _COMPILE_ERR.finditer(log_text)))


def extract_compile_warnings(log_text):
    return list(dict.fromkeys(m.group(0).strip() for m in _COMPILE_WARN.finditer(log_text)))


def compile_failure_result(errors):
    return LogicResult(0, 0, 0, 0, 0.0, [], compile_errors=list(errors))


def _git(*args):
    try:
        return subprocess.run(
            ["git", *args], cwd=paths.REPO, capture_output=True, text=True, timeout=10
        ).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return ""


def unity_version():
    try:
        with open(paths.PROJECT_VERSION_TXT, encoding="utf-8") as f:
            for line in f:
                if line.startswith("m_EditorVersion:"):
                    return line.split(":", 1)[1].strip()
    except OSError:
        pass
    return "?"


def metadata():
    return {
        "commit": _git("rev-parse", "HEAD") or "0000000",
        "branch": _git("rev-parse", "--abbrev-ref", "HEAD") or "?",
        "dirty": bool(_git("status", "--porcelain")),
        "unity": unity_version(),
        "time": _dt.datetime.now().astimezone().strftime("%Y-%m-%d %H:%M:%S %z"),
    }


def render_html(r, meta, title="로직 테스트 리포트"):
    verdict = '<span class="verdict pass">통과</span>' if r.ok else '<span class="verdict fail">실패</span>'
    head = (
        f'<h1>{e(title)}</h1><div class="card">{verdict}'
        f'<div class="stats"><div><span>합계</span><b class="big">{r.total}</b></div>'
        f'<div><span>통과</span><b class="big">{r.passed}</b></div>'
        f'<div><span>실패</span><b class="big">{r.failed + len(r.compile_errors)}</b></div>'
        f'<div><span>건너뜀</span><b class="big">{r.skipped}</b></div>'
        f'<div><span>시간</span><b class="big">{r.duration:.1f}s</b></div></div>'
        f'<p class="meta"><span>커밋 <b>{e(meta.get("commit", "")[:10])}</b></span>'
        f'<span>브랜치 <b>{e(meta.get("branch"))}</b></span>'
        f'<span>Unity <b>{e(meta.get("unity"))}</b></span>'
        f'<span>실행 <b>{e(meta.get("time"))}</b></span></p></div>'
    )
    parts = [head]
    if meta.get("dirty"):
        parts.append('<div class="warnbar">작업 트리에 커밋 안 된 변경이 있다 — 완료 판정 불가</div>')
    if r.compile_errors:
        lines = "\n".join(e(x) for x in r.compile_errors)
        parts.append(
            f'<div class="card"><h2>컴파일 에러 {len(r.compile_errors)}건 — 테스트가 돌지 않았다</h2>'
            f"<pre>{lines}</pre></div>"
        )
    fails = r.failures()
    if fails:
        items = "".join(
            f'<div class="case"><span class="tag fail">실패</span> <span class="name">{e(c["name"])}</span>'
            f'<pre>{e(c["message"])}\n{e(c["stack"])}</pre></div>'
            for c in fails
        )
        parts.append(f'<div class="card"><h2>실패 {len(fails)}건</h2>{items}</div>')
    rest = [c for c in r.ordered_cases() if c["result"] != "Failed"]
    if rest:
        rows = "".join(
            f'<tr><td><span class="tag {"pass" if c["result"] == "Passed" else "skip"}">'
            f'{"통과" if c["result"] == "Passed" else e(c["result"])}</span></td>'
            f'<td>{e(c["name"])}</td><td class="muted">{c["duration"] * 1000:.0f} ms</td></tr>'
            for c in rest
        )
        parts.append(
            f'<div class="card"><h2>전체 테스트</h2><div class="scroll"><table>'
            f"<tr><th>결과</th><th>테스트</th><th>시간</th></tr>{rows}</table></div></div>"
        )
    return page(title, "".join(parts))


def run(test_filter="", category="", platform="EditMode"):
    """unity-test.sh로 배치 실행 → (LogicResult, 로그 텍스트). 실행기 오류는 RunnerError."""
    out_dir = os.path.join(paths.FARM_DIR, "unity-test")
    xml = os.path.join(out_dir, f"results-{platform}.xml")
    log = os.path.join(out_dir, f"log-{platform}.txt")
    args = ["bash", paths.UNITY_TEST_SH, platform, test_filter, category]
    try:
        proc = subprocess.run(args, cwd=paths.REPO, capture_output=True, text=True, timeout=1800)
    except subprocess.TimeoutExpired as err:
        raise RunnerError("Unity 테스트 실행이 30분 안에 끝나지 않았다") from err
    if proc.returncode == 3:
        raise RunnerError("Unity 에디터가 이 프로젝트를 열고 있다 — 에디터를 닫고 다시 실행")
    log_text = ""
    if os.path.exists(log):
        with open(log, encoding="utf-8", errors="replace") as f:
            log_text = f.read()
    errs = extract_compile_errors(log_text)
    if errs:
        return compile_failure_result(errs), log_text
    if not os.path.exists(xml):
        raise RunnerError(f"테스트 결과가 없다 (exit {proc.returncode}). 로그: {log}")
    return parse_results(xml), log_text
