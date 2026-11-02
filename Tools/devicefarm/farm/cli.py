"""farm 명령 해석과 실행 (specs/000 FR-80·FR-81).

각 명령의 실제 동작은 이후 태스크에서 `handlers`에 연결한다. 연결되지 않은 명령은 실행기 오류로 끝난다.
"""
import argparse
import sys

from .errors import (
    EXIT_OK,
    EXIT_RUNNER_ERROR,
    EXIT_USAGE,
    EXIT_VERIFY_FAILED,
    RunnerError,
    VerificationFailed,
)

SELF_TEST_CASES = [
    "logic-fail",
    "compile-error",
    "stability",
    "diff",
    "clip",
    "black",
    "perf",
    "determinism",
    "logs",
    "crash",
    "all",
]


def _default_handlers():
    from . import commands, selftest

    return {
        "logic": lambda a: commands.cmd_logic(a, a._argv),
        "smoke": lambda a: commands.cmd_matrix("smoke", a, a._argv),
        "full": lambda a: commands.cmd_matrix("full", a, a._argv),
        "build": lambda a: commands.cmd_build(a, a._argv),
        "approve-baseline": lambda a: commands.cmd_approve_baseline(a, a._argv),
        "approve-perf": lambda a: commands.cmd_approve_perf(a, a._argv),
        "self-test": lambda a: selftest.main(a.case, a._argv),
    }


# 명령 이름 → 처리 함수(args) -> None. 실패는 예외로 알린다. None이면 실제 처리 함수를 쓴다.
HANDLERS = None


def _add_target_options(p):
    p.add_argument("--spec", help="스펙 번호 (예: 012). 해당 스펙의 테스트·시나리오만")
    p.add_argument("--scenario", help="E2E 시나리오 id (예: 000-sample-cards)")
    p.add_argument(
        "--profile",
        action="append",
        help="이 프로필만 실행 (여러 번 지정 가능)",
    )


def build_parser():
    p = argparse.ArgumentParser(prog="farm", description="Road To PRO 디바이스팜")
    sub = p.add_subparsers(dest="command", required=True, metavar="<명령>")

    lg = sub.add_parser("logic", help="로직 테스트만 (EditMode)")
    _add_target_options(lg)
    lg.add_argument("--filter", help="테스트 이름 필터 (Unity -testFilter, 세미콜론 구분)")
    lg.add_argument("--category", default="!Slow", help="테스트 카테고리 (기본 !Slow)")
    for name, help_text in [
        ("smoke", "smoke 등급 프로필 매트릭스"),
        ("full", "full 등급 프로필 매트릭스"),
    ]:
        _add_target_options(sub.add_parser(name, help=help_text))

    b = sub.add_parser("build", help="검증용 플레이어 빌드 (캐시 적중 시 건너뜀)")
    b.add_argument("--force", action="store_true", help="캐시를 무시하고 다시 빌드")

    ab = sub.add_parser("approve-baseline", help="기준 후보 이미지를 기준으로 채택")
    _add_target_options(ab)
    ab.add_argument("--include-failed", action="store_true", help="차이 실패 이미지도 새 기준으로 (의도한 화면 변경)")
    ap = sub.add_parser("approve-perf", help="이번 성능 측정을 기준 측정으로 채택")
    _add_target_options(ap)

    st = sub.add_parser("self-test", help="하네스 셀프테스트")
    st.add_argument("case", choices=SELF_TEST_CASES)
    return p


def main(argv=None, handlers=None):
    raw = list(sys.argv[1:] if argv is None else argv)
    try:
        args = build_parser().parse_args(raw)
    except SystemExit as e:
        return EXIT_OK if e.code == 0 else EXIT_USAGE
    args._argv = raw
    if handlers is None:
        handlers = HANDLERS if HANDLERS is not None else _default_handlers()

    handler = handlers.get(args.command)
    try:
        if handler is None:
            raise RunnerError(f"'{args.command}' 명령은 아직 구현되지 않았다")
        handler(args)
    except RunnerError as e:
        print(f"[farm] 실행기 오류: {e}", file=sys.stderr)
        return EXIT_RUNNER_ERROR
    except VerificationFailed as e:
        print(f"[farm] 검증 실패: {e}", file=sys.stderr)
        return EXIT_VERIFY_FAILED
    print("[farm] 통과")
    return EXIT_OK
