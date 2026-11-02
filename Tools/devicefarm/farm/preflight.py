"""실행 전 점검: 도구·디스크·잠금·에디터 열림·작업 트리 (specs/000 FR-81, 경계 · 예외 상황).

여기서 나는 문제는 모두 RunnerError (검증 실패가 아니라 검증을 못 돌린 것).
"""
import os
import shutil
import subprocess

from . import paths
from .errors import RunnerError

MIN_FREE_GB = 5  # plan [제안]


def _pid_alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


class RunLock:
    """동시에 검증 하나만. 잠금 파일에 PID를 쓰고, 죽은 PID의 잠금은 치운다."""

    def __init__(self, path=None):
        self.path = path or paths.LOCK_FILE

    def __enter__(self):
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        for _ in range(2):
            try:
                fd = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except FileExistsError:
                try:
                    with open(self.path) as f:
                        pid = int((f.read().split() or ["0"])[0])
                except (OSError, ValueError):
                    pid = 0
                if pid and _pid_alive(pid):
                    raise RunnerError(f"다른 검증이 이미 실행 중이다 (PID {pid}, 잠금 {self.path})")
                os.remove(self.path)  # 오래된 잠금
                continue
            with os.fdopen(fd, "w") as f:
                f.write(f"{os.getpid()}\n")
            return self
        raise RunnerError(f"잠금 파일을 만들 수 없다: {self.path}")

    def __exit__(self, *exc):
        try:
            os.remove(self.path)
        except FileNotFoundError:
            pass
        return False


def _unity_lock_held():
    if not os.path.exists(paths.UNITY_LOCKFILE):
        return False
    r = subprocess.run(["lsof", paths.UNITY_LOCKFILE], capture_output=True)
    return r.returncode == 0


def check_editor_closed(is_locked=_unity_lock_held):
    if is_locked():
        raise RunnerError("Unity 에디터가 이 프로젝트를 열고 있다 — 배치모드 실행 불가. 에디터를 닫고 다시 실행 (강제 종료하지 않는다)")


def check_disk(free_bytes=None, min_gb=MIN_FREE_GB):
    free = free_bytes() if free_bytes else shutil.disk_usage(paths.REPO).free
    if free < min_gb * 1024**3:
        raise RunnerError(f"디스크 여유 공간 부족: {free / 1024**3:.1f} GB < {min_gb} GB")


def check_tools(unity=paths.UNITY_APP, chrome=paths.CHROME_APP, need_chrome=True):
    missing = []
    if not (unity and os.access(unity, os.X_OK)):
        missing.append(f"Unity 6000.3.27f2 ({unity})")
    if need_chrome and not (chrome and os.access(chrome, os.X_OK)):
        missing.append(f"Google Chrome ({chrome})")
    try:
        import numpy  # noqa: F401
        import PIL  # noqa: F401
    except ImportError:
        missing.append("Python 패키지 Pillow·numpy (pip3 install pillow numpy)")
    if missing:
        raise RunnerError("필요한 도구가 없다: " + ", ".join(missing))


def _git_status():
    return subprocess.run(
        ["git", "status", "--porcelain"], cwd=paths.REPO, capture_output=True, text=True
    ).stdout


def worktree_dirty(status=_git_status):
    return bool(status().strip())


def run_all(need_chrome=True, need_editor_closed=True):
    check_tools(need_chrome=need_chrome)
    check_disk()
    if need_editor_closed:
        check_editor_closed()
