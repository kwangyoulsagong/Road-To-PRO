"""프로필마다 플레이어를 독립 프로세스로 실행 (specs/000 FR-22·24·61).

- 프로필 하나가 크래시·행 걸려도 다음 프로필은 계속 돈다.
- 무응답: 드라이버가 1초마다 쓰는 heartbeat 파일이 hang_sec(기본 30초) 동안 안 바뀌면 프로세스 그룹 종료.
- 단계 타임아웃은 드라이버가 판정하고, 여기서는 전체 상한(timeout_sec)만 둔다.
- 성능 측정이 서로 흔들리지 않게 한 번에 하나씩 순서대로 돈다.
"""
import json
import os
import shutil
import signal
import subprocess
import time
from dataclasses import dataclass, field

HANG_SEC = 30.0


@dataclass
class Job:
    combo: object
    scenario_path: str
    seed: int
    extra_args: list = field(default_factory=list)
    env: dict = field(default_factory=dict)
    label: str = ""


@dataclass
class ComboResult:
    combo: object
    job: Job
    out_dir: str
    status: str  # passed | failed | crashed | hung | timeout | error
    reason: str = ""
    exit_code: int = None
    duration: float = 0.0
    exit_info: dict = None
    args: list = None

    @property
    def ok(self):
        return self.status == "passed"


def player_args(job, out_dir, profile_set, window=(960, 600)):
    c = job.combo
    p = c.profile
    a = [
        "-screen-fullscreen", "0",
        "-screen-width", str(window[0]),
        "-screen-height", str(window[1]),
        "-logFile", os.path.join(out_dir, "player.log"),
        "-farmScenario", job.scenario_path,
        "-farmOut", out_dir,
        "-farmSeed", str(job.seed),
        "-farmProfile", p.id,
        "-farmWidth", str(p.width),
        "-farmHeight", str(p.height),
        "-farmUiScale", repr(float(p.ui_scale)),
        "-farmQuality", p.quality,
        "-farmUnityQuality", profile_set.unity_quality_level(p.quality),
        "-farmRenderScale", repr(float(p.render_scale)),
        "-farmTargetFps", str(p.target_fps),
        "-farmTheme", c.theme,
        "-farmLang", c.lang,
    ]
    if p.battery_saver:
        a.append("-farmBatterySaver")
    return a + list(job.extra_args)


def _kill(proc):
    try:
        os.killpg(proc.pid, signal.SIGKILL)
    except (ProcessLookupError, PermissionError):
        pass
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        pass


def _read_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, json.JSONDecodeError):
        return None


def run_one(exe_cmd, job, out_dir, profile_set, hang_sec=HANG_SEC, timeout_sec=600):
    if os.path.isdir(out_dir):
        shutil.rmtree(out_dir)
    os.makedirs(out_dir)
    args = player_args(job, out_dir, profile_set)
    cmd = list(exe_cmd) + args
    env = dict(os.environ)
    env.update(job.env)
    t0 = time.time()
    proc = subprocess.Popen(
        cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True, env=env
    )
    hb = os.path.join(out_dir, "heartbeat")
    status = None
    while True:
        code = proc.poll()
        if code is not None:
            break
        now = time.time()
        last = os.path.getmtime(hb) if os.path.exists(hb) else t0
        if now - last > hang_sec:
            _kill(proc)
            status, reason = "hung", f"무응답 {hang_sec:.0f}초 (heartbeat 멈춤)"
            break
        if now - t0 > timeout_sec:
            _kill(proc)
            status, reason = "timeout", f"전체 시간 {timeout_sec:.0f}초 초과"
            break
        time.sleep(0.2)
    duration = time.time() - t0
    info = _read_json(os.path.join(out_dir, "exit.json"))
    code = proc.returncode

    if status is None:
        if info is None:
            status, reason = "crashed", f"크래시 또는 비정상 종료 (exit {code}, exit.json 없음)"
        elif code == 0 and info.get("status") == "passed":
            status, reason = "passed", ""
        elif code == 1 and info.get("status") == "failed":
            status, reason = "failed", info.get("reason", "")
        elif info.get("status") == "error" or code == 2:
            status, reason = "error", info.get("reason", f"exit {code}")
        else:
            status, reason = "crashed", f"비정상 종료 코드 {code} (보고 상태 {info.get('status')})"
    return ComboResult(job.combo, job, out_dir, status, reason, code, duration, info, cmd)


def run_matrix(exe_cmd, jobs, run_dir, profile_set, hang_sec=HANG_SEC, timeout_sec=600, log=print):
    results = []
    for i, job in enumerate(jobs, 1):
        key = job.combo.key
        log(f"[farm] ({i}/{len(jobs)}) {key} {job.label}".rstrip())
        out = os.path.join(run_dir, key if not job.label else f"{job.label}/{key}")
        r = run_one(exe_cmd, job, out, profile_set, hang_sec, timeout_sec)
        log(f"[farm]   → {r.status} {r.duration:.1f}s {r.reason}".rstrip())
        results.append(r)
    return results
