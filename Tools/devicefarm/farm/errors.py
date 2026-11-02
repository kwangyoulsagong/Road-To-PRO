"""실행기 오류와 검증 실패를 구분한다 (specs/000 FR-81)."""

EXIT_OK = 0
EXIT_VERIFY_FAILED = 1  # 검증이 돌았고 결과가 실패
EXIT_USAGE = 2  # 명령 사용법 오류 (argparse 기본값과 같음)
EXIT_RUNNER_ERROR = 3  # 도구 없음·빌드 실패·디스크 부족·에디터 열림 등 검증을 못 돌림


class FarmError(Exception):
    """디바이스팜 오류의 공통 부모."""


class RunnerError(FarmError):
    """실행기 자체의 문제. 검증 결과가 아니다."""


class VerificationFailed(FarmError):
    """검증은 끝까지 돌았고, 판정이 실패다."""
