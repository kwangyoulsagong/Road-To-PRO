"""UV 영역 규칙 (specs/043 FR-20·21). 우리 메시의 펼침에서 직접 정한다 — 외부 템플릿 없음.

UV는 (u, v), v 위쪽. Blender → glTF → glTFast를 거쳐 Unity에서도 같은 (u, v).

Kit 텍스처 (1K):
  셔츠 앞판  [0.00,0.50]–[0.50,1.00]   정면에서 볼 때 왼쪽 → 오른쪽 = 선수 오른쪽(+X) → 왼쪽
  셔츠 뒤판  [0.50,0.50]–[1.00,1.00]   뒤에서 볼 때 왼쪽 → 오른쪽 = 선수 왼쪽(−X) → 오른쪽 (글자가 바로 읽힘)
  소매 L/R   [0.00,0.30]–[0.25,0.50] / [0.25,0.30]–[0.50,0.50]
  반바지 허리 [0.50,0.375]–[1.00,0.50], 반바지 다리 L/R [0.50,0.25]–[0.75,0.375] / [0.75,0.25]–[1.00,0.375]
  양말 L/R   [0.00,0.05]–[0.25,0.30] / [0.25,0.05]–[0.50,0.30]
  축구화     [0.50,0.00]–[1.00,0.25] (좌우 공유)
  번호·이름 영역(TEXT_AREAS)은 셔츠 판 안에 있다.
Body 텍스처 (1K):
  얼굴 프리셋 8칸 [0,0.5]–[1,1] (4×2, 칸 0.25). 메시는 칸 0([0,0.75]–[0.25,1])에 매핑, 셰이더가 프리셋 오프셋을 더한다.
  피부 [0,0]–[0.5,0.5] (띠 5개: 목·다리·팔·손·뒤통수), 헤어 [0.5,0]–[1,0.5]
"""

SHIRT_Y0, SHIRT_Y1 = 0.97, 1.545
SHORTS_Y0 = 0.84

KIT_REGIONS = {
    "shirtFront": (0.00, 0.50, 0.50, 1.00),
    "shirtBack": (0.50, 0.50, 1.00, 1.00),
    "sleeveL": (0.00, 0.30, 0.25, 0.50),
    "sleeveR": (0.25, 0.30, 0.50, 0.50),
    "shortsWaist": (0.50, 0.375, 1.00, 0.50),
    "shortsL": (0.50, 0.25, 0.75, 0.375),
    "shortsR": (0.75, 0.25, 1.00, 0.375),
    "sockL": (0.00, 0.05, 0.25, 0.30),
    "sockR": (0.25, 0.05, 0.50, 0.30),
    "boots": (0.50, 0.00, 1.00, 0.25),
}

# 번호·이름 영역 (u0, v0, u1, v1). 뒤판 가운데 열이 등 정중앙(a = 270°).
TEXT_AREAS = {
    "backName": (0.60, 0.865, 0.90, 0.925),
    "backNumber": (0.63, 0.60, 0.87, 0.84),
    "frontNumber": (0.29, 0.80, 0.37, 0.88),
}

BODY_REGIONS = {
    "face0": (0.00, 0.75, 0.25, 1.00),
    "faces": (0.00, 0.50, 1.00, 1.00),
    "skin": (0.00, 0.00, 0.50, 0.50),
    "hair": (0.50, 0.00, 1.00, 0.50),
}

FACE_X = 0.10          # 얼굴 칸 가로: x ∈ [−FACE_X, FACE_X]
FACE_Y0, FACE_Y1 = 1.57, 1.835


def _r(region, fu, fv):
    u0, v0, u1, v1 = region
    return (u0 + (u1 - u0) * fu, v0 + (v1 - v0) * fv)


def _c01(x):
    return 0.0 if x < 0 else 1.0 if x > 1 else x


def torso_shirt(f, y):
    """f = 링 각도 비율(0 = +X, 0.25 = 정면, 0.5 = −X, 0.75 = 등). 0~0.5 앞판, 0.5~1 뒤판."""
    v = _c01((y - SHIRT_Y0) / (SHIRT_Y1 - SHIRT_Y0))
    if f <= 0.5:
        return _r(KIT_REGIONS["shirtFront"], f / 0.5, v)
    return _r(KIT_REGIONS["shirtBack"], (f - 0.5) / 0.5, v)


def torso_shorts(f, y):
    return _r(KIT_REGIONS["shortsWaist"], f, _c01((y - SHORTS_Y0) / (SHIRT_Y0 - SHORTS_Y0)))


def shorts_leg(side, f, v):
    return _r(KIT_REGIONS["shortsL" if side < 0 else "shortsR"], f, _c01(v))


def sock(side, f, v):
    return _r(KIT_REGIONS["sockL" if side < 0 else "sockR"], f, _c01(v))


def sleeve(side, f, v):
    return _r(KIT_REGIONS["sleeveL" if side < 0 else "sleeveR"], f, _c01(v))


def boot(f, v):
    return _r(KIT_REGIONS["boots"], f, _c01(v))


def skin(f, v, band):
    """피부 영역을 가로 띠 5개로 나눈다 (band 0~4)."""
    u0, v0, u1, v1 = BODY_REGIONS["skin"]
    h = (v1 - v0) / 5
    return (u0 + (u1 - u0) * f, v0 + h * band + h * _c01(v))


def face(x, y):
    """정면 평면 투영. 정면에서 볼 때 왼쪽(선수 오른쪽 +X) → u 작음."""
    fu = _c01((x + FACE_X) / (2 * FACE_X))
    fv = _c01((y - FACE_Y0) / (FACE_Y1 - FACE_Y0))
    return _r(BODY_REGIONS["face0"], fu, fv)


def hair(f, v):
    return _r(BODY_REGIONS["hair"], f, _c01(v))


def layout():
    return {"kit": KIT_REGIONS, "text": TEXT_AREAS, "body": BODY_REGIONS,
            "face": {"x": FACE_X, "y0": FACE_Y0, "y1": FACE_Y1, "cells": [4, 2]}}
