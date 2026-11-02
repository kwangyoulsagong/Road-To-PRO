#!/usr/bin/env python3
"""가짜 플레이어 (runner 테스트용). FAKE_MODE 환경변수로 동작을 고른다.

pass: 결과 파일을 쓰고 0 종료 / fail: 단계 실패 1 종료 / crash: 결과 없이 abort
hang: heartbeat 한 번 쓰고 멈춤 / slow: heartbeat는 계속 쓰지만 끝나지 않음
"""
import json
import os
import sys
import time

args = sys.argv[1:]
out = args[args.index("-farmOut") + 1]
mode = os.environ.get("FAKE_MODE", "pass")
os.makedirs(os.path.join(out, "captures"), exist_ok=True)


def beat():
    with open(os.path.join(out, "heartbeat"), "w") as f:
        f.write(str(time.time()))


def write_exit(status, failed=-1, reason=""):
    with open(os.path.join(out, "steps.jsonl"), "w") as f:
        f.write(json.dumps({"index": 0, "type": "goto", "status": "passed" if status == "passed" else "failed"}) + "\n")
    with open(os.path.join(out, "exit.json"), "w") as f:
        json.dump({"status": status, "failedStep": failed, "reason": reason}, f)


beat()
with open(os.path.join(out, "args.json"), "w") as f:
    json.dump(args, f)
if mode == "pass":
    write_exit("passed")
    sys.exit(0)
if mode == "fail":
    write_exit("failed", 0, "#0 goto: 화면 없음")
    sys.exit(1)
if mode == "crash":
    os.abort()
if mode == "hang":
    time.sleep(60)
if mode == "slow":
    for _ in range(600):
        beat()
        time.sleep(0.1)
