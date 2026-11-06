"""E2E 시나리오 데이터 파일 로드·검증 (specs/000 FR-13·20~26·31).

Unity 드라이버(`ScenarioDefinition`)도 같은 형식을 읽는다. 실행 전에 여기서 먼저 걸러서
잘못된 시나리오가 프로필 수만큼 플레이어를 띄우지 않게 한다.
"""
import json
from dataclasses import dataclass, field

from . import paths
from .errors import RunnerError

DEFAULTS = {"timeoutSec": 10, "pixelThreshold": 8, "maxDiffRatio": 0.001}
DEFAULT_3D_RATIO = 0.005
SPEEDS = (0.5, 1, 1.5, 2, 3)
INJECTS = ("warning", "exception", "crash")

# 단계 종류 → 필수 필드
REQUIRED = {
    "goto": ["screen"],
    "tap": ["element"],
    "type": ["element", "text"],
    "waitFor": [],
    "assertVisible": ["element"],
    "assertText": ["element", "text"],
    "assertNumberRange": ["element", "min", "max"],
    "assertNoOverlap": ["scope"],
    "assertNoClip": ["scope"],
    "capture": ["name"],
    "perfBegin": ["name"],
    "perfEnd": ["name"],
    "checkpoint": ["name"],
    "motionMeasure": ["name"],
    "freeze": [],
    "inject": ["what"],
}
WAIT_KEYS = ("element", "simTime", "highlightScene")
FREEZE_KEYS = ("simTime", "scene")


@dataclass
class Scenario:
    id: str
    spec: str
    scene: str
    fixture: dict
    profiles: object
    self_test: bool
    langs: list
    battery_saver: bool
    defaults: dict
    steps: list
    raw: dict = field(repr=False, default=None)
    image_checks: list = field(default_factory=list)
    themes: list = field(default_factory=list)

    def capture_names(self):
        return [s["name"] for s in self.steps if s["type"] == "capture"]

    def capture_step(self, name):
        for s in self.steps:
            if s["type"] == "capture" and s["name"] == name:
                return s
        raise KeyError(name)

    def capture_params(self, name):
        s = self.capture_step(name)
        default_ratio = DEFAULT_3D_RATIO if s.get("kind") == "3d" else self.defaults["maxDiffRatio"]
        return {
            "pixelThreshold": s.get("pixelThreshold", self.defaults["pixelThreshold"]),
            "maxDiffRatio": s.get("maxDiffRatio", default_ratio),
            "masks": list(s.get("masks", [])),
        }

    def step_timeout(self, index):
        return self.steps[index].get("timeoutSec", self.defaults["timeoutSec"])


def _err(sid, i, msg):
    return RunnerError(f"시나리오 {sid} 단계 #{i}: {msg}")


def _check_step(sid, i, st, self_test):
    t = st.get("type")
    if t not in REQUIRED:
        raise _err(sid, i, f"알 수 없는 단계 종류 {t!r}")
    for k in REQUIRED[t]:
        if k not in st:
            raise _err(sid, i, f"'{t}'에 '{k}' 필요")
    if "timeoutSec" in st and not (isinstance(st["timeoutSec"], (int, float)) and st["timeoutSec"] > 0):
        raise _err(sid, i, "timeoutSec은 양수")
    if t == "waitFor" and sum(k in st for k in WAIT_KEYS) != 1:
        raise _err(sid, i, f"waitFor는 {WAIT_KEYS} 중 정확히 하나")
    if t == "freeze":
        if sum(k in st for k in FREEZE_KEYS) != 1:
            raise _err(sid, i, f"freeze는 {FREEZE_KEYS} 중 정확히 하나")
        if "speed" in st and st["speed"] not in SPEEDS:
            raise _err(sid, i, f"재생 속도는 {SPEEDS} 중 하나 ({st['speed']})")
    if t == "assertNumberRange" and st["min"] > st["max"]:
        raise _err(sid, i, "min > max")
    if t == "inject":
        if not self_test:
            raise _err(sid, i, "inject 단계는 셀프테스트 시나리오(selfTest: true)에서만 허용")
        if st["what"] not in INJECTS:
            raise _err(sid, i, f"inject는 {INJECTS} 중 하나")
    if t == "capture":
        if st.get("kind", "ui") not in ("ui", "3d"):
            raise _err(sid, i, "capture.kind는 ui 또는 3d")
        for m in st.get("masks", []):
            if "element" in m:
                continue
            r = m.get("rect")
            if not (isinstance(r, list) and len(r) == 4 and all(isinstance(v, (int, float)) for v in r)):
                raise _err(sid, i, "마스크는 {element} 또는 {rect: [x,y,w,h]}")
            x, y, w, h = r
            if not (0 <= x <= 1 and 0 <= y <= 1 and 0 < w <= 1 and 0 < h <= 1 and x + w <= 1 and y + h <= 1):
                raise _err(sid, i, f"마스크 rect는 0~1 비율 ({r})")


def parse(data):
    sid = data.get("id", "?")
    if data.get("schemaVersion") != 1:
        raise RunnerError(f"시나리오 {sid}: schemaVersion 1만 지원")
    for k in ("id", "spec", "scene", "fixture", "profiles", "steps"):
        if k not in data:
            raise RunnerError(f"시나리오 {sid}: '{k}' 필요")
    fx = data["fixture"]
    if not isinstance(fx, dict) or not isinstance(fx.get("seed"), int):
        raise RunnerError(f"시나리오 {sid}: fixture.seed(정수) 필요 (FR-21 고정 시드)")

    prof = data["profiles"]
    if prof not in ("all", "smoke"):
        if not (isinstance(prof, list) and prof and all(isinstance(p, str) for p in prof)):
            raise RunnerError(f"시나리오 {sid}: profiles는 'all' | 'smoke' | [id, ...]")

    self_test = bool(data.get("selfTest", False))
    defaults = dict(DEFAULTS)
    defaults.update(data.get("defaults") or {})

    steps = data["steps"]
    if not isinstance(steps, list) or not steps:
        raise RunnerError(f"시나리오 {sid}: steps가 비었다")
    open_perf = None
    captures = set()
    for i, st in enumerate(steps):
        _check_step(sid, i, st, self_test)
        t = st["type"]
        if t == "perfBegin":
            if open_perf is not None:
                raise _err(sid, i, f"성능 구간 '{open_perf}'이 닫히기 전에 새 구간 시작")
            open_perf = st["name"]
        elif t == "perfEnd":
            if open_perf != st["name"]:
                raise _err(sid, i, f"perfEnd '{st['name']}'에 맞는 perfBegin 없음")
            open_perf = None
        elif t == "capture":
            if open_perf is not None:
                raise _err(sid, i, f"성능 구간 '{open_perf}' 안에서는 캡처 금지")
            if st["name"] in captures:
                raise _err(sid, i, f"캡처 이름 중복 '{st['name']}'")
            captures.add(st["name"])
    if open_perf is not None:
        raise RunnerError(f"시나리오 {sid}: 성능 구간 '{open_perf}'이 닫히지 않음")

    th = data.get("themes")
    if th is not None and not (isinstance(th, list) and th and all(t in ("light", "dark") for t in th)):
        raise RunnerError(f"시나리오 {sid}: themes는 ['light'|'dark', ...] (UI가 없는 3D 장면은 테마 한 벌로 충분, specs/044)")
    for name, d in (data.get("derived") or {}).items():
        if not (isinstance(d, dict) and d.get("of") and d.get("minus") and isinstance(d.get("budget"), dict)):
            raise RunnerError(f"시나리오 {sid} derived.{name}: {{of, minus, budget}} 필요")

    from . import imagechecks
    checks = data.get("imageChecks") or []
    if not isinstance(checks, list):
        raise RunnerError(f"시나리오 {sid}: imageChecks는 목록")
    for k, c in enumerate(checks):
        if not isinstance(c, dict) or c.get("type") not in imagechecks.TYPES:
            raise RunnerError(f"시나리오 {sid} imageChecks #{k}: type은 {imagechecks.TYPES} 중 하나")
        if c.get("capture") not in captures:
            raise RunnerError(f"시나리오 {sid} imageChecks #{k}: 캡처 '{c.get('capture')}' 없음")
        if c["type"] in imagechecks.PAIRED and c.get("other") not in captures:
            raise RunnerError(f"시나리오 {sid} imageChecks #{k}: {c['type']}는 other(두 번째 캡처) 필요")

    return Scenario(
        id=data["id"],
        spec=data["spec"],
        scene=data["scene"],
        fixture=fx,
        profiles=prof,
        self_test=self_test,
        langs=list(data.get("langs") or []),
        battery_saver=bool(data.get("batterySaver", False)),
        defaults=defaults,
        steps=steps,
        raw=data,
        image_checks=checks,
        themes=list(data.get("themes") or []),
    )


def load(scenario_id_or_path):
    path = scenario_id_or_path
    if not path.endswith(".json"):
        path = paths.scenario_file(scenario_id_or_path)
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError) as e:
        raise RunnerError(f"시나리오 파일을 읽을 수 없다: {path}: {e}") from e
    return parse(data)
