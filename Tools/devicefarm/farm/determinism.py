"""결정론 체크포인트 비교 (specs/000 FR-50·51).

체크포인트 파일(checkpoints.json) = [{name, tick, hash}] — SampleSim과 이후 경기 시뮬(001 FR-61)이
같은 형식으로 낸다. 여기서는 '프로세스 간' 파일 비교만 한다.
"""
import json


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def compare_runs(a, b):
    if not a and not b:
        return {"ok": False, "firstDivergence": None, "reason": "체크포인트가 없다"}
    n = max(len(a), len(b))
    for i in range(n):
        ca = a[i] if i < len(a) else None
        cb = b[i] if i < len(b) else None
        if ca is None or cb is None or ca["hash"] != cb["hash"]:
            ref = ca or cb
            return {
                "ok": False,
                "firstDivergence": {
                    "index": i,
                    "name": ref["name"],
                    "tick": ref.get("tick"),
                    "a": ca["hash"] if ca else None,
                    "b": cb["hash"] if cb else None,
                },
                "reason": f"체크포인트 #{i} '{ref['name']}'에서 갈라짐",
            }
    return {"ok": True, "firstDivergence": None, "reason": "", "count": n}


def compare_profiles(runs):
    """{combo_key: checkpoints} → 첫 조합(정렬 순)을 기준으로 나머지 비교."""
    if not runs:
        return {"ok": False, "reference": None, "mismatches": {}, "reason": "체크포인트가 없다"}
    keys = sorted(runs)
    ref = keys[0]
    mism = {}
    for k in keys[1:]:
        r = compare_runs(runs[ref], runs[k])
        if not r["ok"]:
            mism[k] = r["firstDivergence"]
    return {
        "ok": not mism and bool(runs[ref]),
        "reference": ref,
        "mismatches": mism,
        "reason": "" if not mism else f"{len(mism)}개 조합이 기준({ref})과 다름",
    }
