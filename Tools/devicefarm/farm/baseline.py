"""기준 이미지 경로 규칙과 승인 (specs/000 FR-30·33).

기준: Data/e2e/baselines/<scenario>/<step>/<profile>-<theme>-<lang>.png (LFS).
자동 갱신은 없다. `farm.py approve-baseline`만 이 모듈의 approve()를 부른다.
"""
import os
import shutil

from . import paths


def baseline_path(scenario, step, combo_key, root=None):
    root = root or paths.BASELINES_DIR
    return os.path.join(root, scenario, step, f"{combo_key}.png")


def approve(items, root=None, include_failed=False):
    """items: 비교 결과 목록 [{scenario, step, combo, status, current}].

    기본은 '기준 후보'만 채택. 차이 실패 항목은 include_failed=True일 때만 (의도한 화면 변경).
    """
    done = []
    for it in items:
        if it["status"] == "candidate" or (include_failed and it["status"] == "fail"):
            dst = baseline_path(it["scenario"], it["step"], it["combo"], root)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(it["current"], dst)
            done.append(dst)
    return done
