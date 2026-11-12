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

    def _cats(el):
        return [pr.get("value") for pr in el.findall("properties/property") if pr.get("name") == "Category"]

    # 클래스·네임스페이스 단위 [Category]는 test-suite 속성에 있다 — 조상 스위트 카테고리를 물려받는다 (specs/011 T20a)
    def _walk(el, inherited):
        for child in el:
            if child.tag == "test-suite":
                yield from _walk(child, inherited + _cats(child))
            elif child.tag == "test-case":
                yield child, inherited

    for tc, suite_cats in _walk(root, []):
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
                "categories": list(dict.fromkeys(suite_cats + _cats(tc))),
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


def _git_raw(*args):
    """앞 공백을 지우지 않는다 (porcelain 첫 줄 상태 칸 보존)."""
    try:
        return subprocess.run(
            ["git", *args], cwd=paths.REPO, capture_output=True, text=True, timeout=10
        ).stdout
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


# 증거·문서만 바뀐 작업 트리는 "커밋 안 된 코드 변경"으로 보지 않는다 (2026-11-12 검증 경량화:
# 증거 커밋마다 logic·shot을 다시 돌리던 반복을 없앤다). 코드·데이터·설정이 바뀌었을 때만 dirty.
DOC_ONLY_PREFIXES = ("specs/", "docs/", "reports/", "research_notes/", ".farm/")


def code_dirty(porcelain):
    """git status --porcelain 출력 → 증거·문서 밖의 변경이 있는가."""
    for line in (porcelain or "").splitlines():
        if len(line) < 4:
            continue
        path = line[3:].split(" -> ")[-1].strip().strip('"')
        if not path.startswith(DOC_ONLY_PREFIXES):
            return True
    return False


# Unity -testFilter 정규식 범위 (2026-11-12 검증 경량화). 스펙 끝 로직 검증은 바뀐 모듈 범위만 돌린다.
# match에 Presentation.Highlight가 들어가는 이유: 실경기 하이라이트 픽스처(058)가 경기 엔진을 그대로 돌린다.
SCOPES = {
    "core": ("Core", "Architecture"),
    "match": ("Core", "Architecture", "MatchEngine", r"Presentation\.Highlight"),
    "presentation": ("Architecture", "Presentation"),
    "verification": ("Verification",),
    "all": (),
}


# 바뀐 경로 → 범위 (의존 방향 Presentation·Verification → MatchEngine → Core, 10-csharp.md).
# Core·테스트 asmdef·패키지·프로젝트 설정은 모두가 기대므로 all. 순서대로 첫 일치.
AUTO_RULES = (
    ("Assets/_Project/Core/", "all"),
    ("Assets/_Project/Tests/EditMode/RoadToPro.Tests.EditMode.asmdef", "all"),
    ("Packages/", "all"),
    ("ProjectSettings/", "all"),
    ("Assets/_Project/Tests/EditMode/Core/", "core"),
    ("Assets/_Project/Tests/EditMode/Architecture/", "core"),
    ("Assets/_Project/MatchEngine/", "match"),
    ("Assets/_Project/Tests/EditMode/MatchEngine/", "match"),
    ("Data/tuning/", "match"),
    ("Data/scenarios/", "match"),
    ("Assets/_Project/Tests/EditMode/Presentation/", "presentation"),
    ("Assets/_Project/Presentation/", "presentation"),
    ("Assets/_Project/UI/", "presentation"),
    ("Assets/_Project/Verification/", "verification"),
    ("Assets/_Project/Tests/EditMode/Verification/", "verification"),
    ("Data/e2e/", "verification"),
)
# 위에 없는 Assets/ 아래 코드(Career·WorldGen·Data 등 새 모듈)는 안전하게 all
AUTO_FALLBACK_PREFIX = "Assets/"


def auto_scope(paths_changed):
    """바뀐 파일 목록 → 범위 문자열 (쉼표 구분). 코드 변경이 없으면 ""."""
    found = []
    for path in paths_changed:
        if path.startswith(DOC_ONLY_PREFIXES):
            continue
        name = None
        for prefix, sc in AUTO_RULES:
            if path.startswith(prefix):
                name = sc
                break
        if name is None and path.startswith(AUTO_FALLBACK_PREFIX):
            name = "all"
        if name == "all":
            return "all"
        if name and name not in found:
            found.append(name)
    return ",".join(found)


def changed_since(base="main"):
    """base와 갈라진 지점 이후 커밋된 변경 + 작업 트리 변경 경로."""
    out = _git_raw("diff", "--name-only", f"{base}...HEAD").splitlines()
    for line in _git_raw("status", "--porcelain").splitlines():
        if len(line) > 3:
            out.append(line[3:].split(" -> ")[-1].strip().strip('"'))
    return [p for p in dict.fromkeys(x.strip() for x in out) if p]


def scope_filter(scopes):
    """쉼표로 구분한 범위 이름 → -testFilter 정규식. 'all'이 있거나 비면 "" (전체)."""
    if (scopes or "").strip() == "auto":
        scopes = auto_scope(changed_since())
        if not scopes:
            raise ValueError("main 이후 바뀐 코드가 없다 — 범위를 직접 지정하라 (--scope match 등)")
    names = [n.strip() for n in (scopes or "").split(",") if n.strip()]
    if not names or "all" in names:
        return ""
    unknown = [n for n in names if n not in SCOPES]
    if unknown:
        raise ValueError("알 수 없는 범위: " + ", ".join(unknown) + " (가능: " + ", ".join(SCOPES) + ")")
    parts = []
    for n in names:
        for ns in SCOPES[n]:
            if ns not in parts:
                parts.append(ns)
    return r"^RoadToPro\.Tests\.EditMode\.(" + "|".join(parts) + r")\."


def metadata():
    return {
        "commit": _git("rev-parse", "HEAD") or "0000000",
        "branch": _git("rev-parse", "--abbrev-ref", "HEAD") or "?",
        "dirty": code_dirty(_git_raw("status", "--porcelain")),
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


def run(test_filter="", category="", platform="EditMode", perf_results=None):
    """unity-test.sh로 배치 실행 → (LogicResult, 로그 텍스트). 실행기 오류는 RunnerError.
    perf_results: Performance Testing 결과 JSON 경로 (Unity -perfTestResults)."""
    out_dir = os.path.join(paths.FARM_DIR, "unity-test")
    xml = os.path.join(out_dir, f"results-{platform}.xml")
    log = os.path.join(out_dir, f"log-{platform}.txt")
    args = ["bash", paths.UNITY_TEST_SH, platform, test_filter, category]
    try:
        env = dict(os.environ)
        if perf_results:
            env["UNITY_PERF_RESULTS"] = perf_results
        proc = subprocess.run(args, cwd=paths.REPO, capture_output=True, text=True, timeout=1800, env=env)
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
