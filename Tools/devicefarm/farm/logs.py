"""로그 수집·판정과 무시 목록 (specs/000 FR-60).

플레이어 드라이버(`FarmLog`)가 남긴 logs.jsonl 한 줄 = {type, message, stack?}.
type: Log | Warning | Error | Exception | Assert (Unity LogType 이름).
"""
import json
import re
from dataclasses import dataclass, field

from . import paths
from .errors import RunnerError

ERROR_TYPES = ("Error", "Exception", "Assert")
WARNING_TYPES = ("Warning",)


def compile_ignore(entries):
    out = []
    for i, e in enumerate(entries):
        pat = e.get("pattern")
        reason = (e.get("reason") or "").strip()
        if not isinstance(pat, str) or not pat:
            raise RunnerError(f"로그 무시 목록 #{i}: pattern 필요")
        if not reason:
            raise RunnerError(f"로그 무시 목록 #{i} ({pat}): 사유(reason) 필수")
        try:
            rx = re.compile(pat)
        except re.error as err:
            raise RunnerError(f"로그 무시 목록 #{i}: 정규식 오류 {pat!r}: {err}") from err
        out.append({**e, "reason": reason, "regex": rx})
    return out


def load_ignore(path=None):
    path = path or paths.LOG_IGNORE_JSON
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError) as e:
        raise RunnerError(f"로그 무시 목록을 읽을 수 없다: {path}: {e}") from e
    if data.get("schemaVersion") != 1:
        raise RunnerError("로그 무시 목록 schemaVersion 1만 지원")
    return compile_ignore(data.get("ignore") or [])


def read_jsonl(path):
    """마지막 줄이 크래시로 잘렸을 수 있으므로 깨진 줄은 건너뛴다."""
    out = []
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    out.append(json.loads(line))
                except json.JSONDecodeError:
                    continue
    except FileNotFoundError:
        return []
    return out


@dataclass
class LogSummary:
    errors: list = field(default_factory=list)
    warnings: list = field(default_factory=list)
    ignored: list = field(default_factory=list)

    @property
    def ok(self):
        return not self.errors and not self.warnings

    def to_dict(self):
        return {
            "ok": self.ok,
            "errors": self.errors,
            "warnings": self.warnings,
            "ignored": self.ignored,
        }


def judge(entries, ignore):
    s = LogSummary()
    for e in entries:
        t = e.get("type")
        if t not in ERROR_TYPES and t not in WARNING_TYPES:
            continue
        msg = e.get("message", "")
        hit = next((ig for ig in ignore if ig["regex"] and ig["regex"].search(msg)), None)
        item = {"type": t, "message": msg}
        if e.get("stack"):
            item["stack"] = e["stack"]
        if hit:
            # 같은 항목이 수천 번 찍혀도 리포트가 커지지 않게 첫 메시지 + 횟수로 모은다 (specs/041: 시나리오 하나에 1,500건)
            seen = next((x for x in s.ignored if x["pattern"] == hit["pattern"]), None)
            if seen:
                seen["count"] += 1
            else:
                s.ignored.append({**item, "reason": hit["reason"], "pattern": hit["pattern"], "count": 1})
        elif t in ERROR_TYPES:
            s.errors.append(item)
        else:
            s.warnings.append(item)
    return s
