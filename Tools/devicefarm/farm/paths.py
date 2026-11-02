"""레포 안 경로 한 곳 (specs/000 plan '데이터 · 스키마')."""
import os

DEVICEFARM_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(os.path.dirname(DEVICEFARM_DIR))

DATA_E2E = os.path.join(REPO, "Data", "e2e")
PROFILES_JSON = os.path.join(DATA_E2E, "profiles.json")
LOG_IGNORE_JSON = os.path.join(DATA_E2E, "log-ignore.json")
PERF_BASELINES_JSON = os.path.join(DATA_E2E, "perf-baselines.json")
BASELINES_DIR = os.path.join(DATA_E2E, "baselines")

FARM_DIR = os.path.join(REPO, ".farm")
RUNS_DIR = os.path.join(FARM_DIR, "runs")
LOCK_FILE = os.path.join(FARM_DIR, "farm.lock")
BUILD_DIR = os.path.join(REPO, "Builds", "Farm")

SPECS_DIR = os.path.join(REPO, "specs")
UNITY_TEST_SH = os.path.join(REPO, ".claude", "scripts", "unity-test.sh")
UNITY_LOCKFILE = os.path.join(REPO, "Temp", "UnityLockfile")
PROJECT_VERSION_TXT = os.path.join(REPO, "ProjectSettings", "ProjectVersion.txt")

UNITY_APP = "/Applications/Unity/Hub/Editor/6000.3.27f2/Unity.app/Contents/MacOS/Unity"
CHROME_APP = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"


def scenario_file(scenario_id):
    return os.path.join(DATA_E2E, f"{scenario_id}.json")


def spec_dir(spec):
    """'000' 또는 '000-verification-harness' → specs/000-verification-harness."""
    for name in sorted(os.listdir(SPECS_DIR)):
        if name == spec or name.startswith(f"{spec}-"):
            return os.path.join(SPECS_DIR, name)
    return None
