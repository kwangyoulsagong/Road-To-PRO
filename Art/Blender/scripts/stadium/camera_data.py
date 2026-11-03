"""카메라 거치 범위·리플레이 금지 영역 (specs/044 FR-26, 042 입력)."""
from . import layout as L

MOUNT_HALF_X = 6.0
MOUNT_ABOVE = 3.0


def mount_center(pl):
    """메인 스탠드(S) 하프라인, 맨 위 줄 2 m 앞 위 3 m."""
    u = pl["depth"] - 2.0
    return (0.0, -(L.STAND_Y + u), pl["top"] + MOUNT_ABOVE)


def build(pl, structure_boxes):
    c = mount_center(pl)
    lo = (c[0] - MOUNT_HALF_X, c[1] - 1.0, c[2] - 1.0)
    hi = (c[0] + MOUNT_HALF_X, c[1] + 1.0, c[2] + 1.0)
    return {"mainMount": L.unity_box(lo, hi), "mainMountCenter": L.unity(c),
            "noGo": [dict(L.unity_box(lo_, hi_), name=n) for n, lo_, hi_ in structure_boxes]}
