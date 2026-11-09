"""검증용 플레이어 빌드와 캐시 (specs/000 T27).

캐시 키 = Assets·Packages·ProjectSettings 파일의 (경로, 크기, 수정 시각) 해시.
키가 같고 앱이 있으면 빌드를 건너뛴다 (smoke 5분 예산은 캐시 적중 기준).
"""
import hashlib
import os
import plistlib
import subprocess
import time

from . import paths, preflight
from .errors import RunnerError

APP = os.path.join(paths.BUILD_DIR, "RoadToProFarm.app")
STAMP = os.path.join(paths.FARM_DIR, "build", "stamp.txt")
LOG = os.path.join(paths.FARM_DIR, "build", "build.log")
HIDE_WINDOW_SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hide_window.m")
HIDE_WINDOW_LIB = os.path.join(paths.FARM_DIR, "build", "hide_window.dylib")
SOURCE_DIRS = ("Assets", "Packages", "ProjectSettings")


def cache_key(root=None, dirs=SOURCE_DIRS):
    root = root or paths.REPO
    h = hashlib.sha256()
    for d in dirs:
        base = os.path.join(root, d)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames.sort()
            for fn in sorted(filenames):
                if fn == ".DS_Store":
                    continue
                p = os.path.join(dirpath, fn)
                try:
                    st = os.stat(p)
                except OSError:
                    continue
                rel = os.path.relpath(p, root)
                h.update(f"{rel}\0{st.st_size}\0{st.st_mtime_ns}\n".encode())
    return h.hexdigest()


def executable(app=APP):
    macos = os.path.join(app, "Contents", "MacOS")
    if not os.path.isdir(macos):
        return None
    names = sorted(os.listdir(macos))
    return os.path.join(macos, names[0]) if names else None


def is_cached(key, app=APP, stamp=STAMP):
    if not executable(app) or not os.path.exists(stamp):
        return False
    with open(stamp, encoding="utf-8") as f:
        return f.read().strip() == key


def hide_from_dock(app=APP):
    """Dock 아이콘 없이 뜨도록 LSUIElement를 켠다. Info.plist가 바뀌면 서명이 깨지므로 ad-hoc으로 다시 서명한다."""
    plist = os.path.join(app, "Contents", "Info.plist")
    with open(plist, "rb") as f:
        info = plistlib.load(f)
    if info.get("LSUIElement") is True:
        return
    info["LSUIElement"] = True
    with open(plist, "wb") as f:
        plistlib.dump(info, f)
    r = subprocess.run(
        ["codesign", "--force", "--deep", "--sign", "-", app], capture_output=True, text=True, timeout=300
    )
    if r.returncode != 0:
        raise RunnerError(f"재서명 실패 (exit {r.returncode}): {r.stderr.strip()}")


def hidden_window_env(source=HIDE_WINDOW_SRC, lib=HIDE_WINDOW_LIB):
    """플레이어 창을 숨기는 라이브러리를 (필요하면 컴파일해) 주입하는 환경 변수. 창을 보려면 FARM_SHOW_WINDOW=1."""
    if os.environ.get("FARM_SHOW_WINDOW"):
        return {}
    if not os.path.exists(lib) or os.path.getmtime(lib) < os.path.getmtime(source):
        os.makedirs(os.path.dirname(lib), exist_ok=True)
        r = subprocess.run(
            ["clang", "-dynamiclib", "-framework", "AppKit", "-o", lib, source],
            capture_output=True, text=True, timeout=120,
        )
        if r.returncode != 0:
            raise RunnerError(f"창 숨김 라이브러리 컴파일 실패: {r.stderr.strip()}")
    return {"DYLD_INSERT_LIBRARIES": lib}


def ensure_build(force=False, log=print):
    """빌드가 최신이면 건너뛰고 (실행 파일, 캐시 적중 여부, 걸린 초)를 돌려준다."""
    key = cache_key()
    if not force and is_cached(key):
        log("[farm] 빌드 캐시 적중 — 건너뜀")
        return executable(), True, 0.0
    preflight.check_editor_closed()
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    log("[farm] 플레이어 빌드 중 (macOS 개발 빌드)…")
    t0 = time.time()
    cmd = [
        paths.UNITY_APP, "-batchmode", "-quit", "-projectPath", paths.REPO,
        "-executeMethod", "RoadToPro.Verification.Editor.FarmBuild.BuildMac",
        "-farmBuildOut", APP, "-logFile", LOG,
    ]
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=3600)
    except subprocess.TimeoutExpired as e:
        raise RunnerError("빌드가 60분 안에 끝나지 않았다") from e
    took = time.time() - t0
    if r.returncode != 0 or not executable():
        tail = ""
        if os.path.exists(LOG):
            with open(LOG, encoding="utf-8", errors="replace") as f:
                lines = [l.rstrip() for l in f if "error" in l.lower()]
            tail = "\n".join(lines[-15:])
        raise RunnerError(f"빌드 실패 (exit {r.returncode}). 로그 {LOG}\n{tail}")
    hide_from_dock()
    # 빌드 중 Unity가 고친 파일(.meta 등)이 있을 수 있으니 빌드 뒤의 키를 기록
    with open(STAMP, "w", encoding="utf-8") as f:
        f.write(cache_key())
    log(f"[farm] 빌드 완료 {took:.0f}s")
    return executable(), False, took
