"""절차 클립 (specs/043 FR-31·32). all_clips()가 전체 목록을 고정 순서로 돌려준다."""


def all_clips():
    from . import actions, football, gait, reactions, styles
    return gait.clips() + football.clips() + styles.clips() + actions.clips() + reactions.clips()
