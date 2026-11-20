"""062 구운 디테일 = 높이장 굽기 (specs/062 plan 접근 1, FR-90·95).

"고해상도 원형" = 저해상도 메시 + 절차 변위장. 변위장을 UV 텍셀의 3D 위치(기준 몸 래스터)에서 평가해 노멀·캐비티 AO를
바로 계산한다 — Cycles 베이크 없음, 정수 시드·고정 순서라 바이트 단위 결정론.

근육 지도(`body_detail_n.png`·`body_detail_ao.png`, 2048²): 모든 선수가 한 장을 공유하고, 선수별 근육 정도는 셰이더
`_MuscleDef`(0~1)가 노멀 세기로 조절한다(FR-92 공유 원칙).

근육 정의 = (뼈, 축 방향 구간 t0~t1, 둘레 방향 중심 각 θc·폭, 높이). 둘레 각은 뼈 축에 수직인 틀(앞 = 선수 +Z를 축에
수직으로 투영, 옆 = 축 × 앞)에서 잰다. 각 근육은 가장자리가 좁은 평탄 언덕(가장자리 smoothstep) — 근육 사이 경계에
골이 생겨 클로즈업 중주파 음영이 된다. 뼈 돌출(팔꿈치·무릎뼈·손목뼈)·힘줄(아래팔·발목)은 좁은 띠/점.
"""
import math

import numpy as np

from . import mpfb_src as MS
from . import skin as SK

DETAIL = 2048
NORMAL_GAIN = 4.0   # 1 = 물리적 기울기. 클로즈업 판독성을 위해 과장 (T06 측정)


def _smooth(x, e0, e1):
    t = np.clip((x - e0) / max(e1 - e0, 1e-9), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# (뼈 접미사, t0, t1, θc(도, 0 = 앞, +90 = 바깥쪽), 반폭(도), 높이 m, 가장자리 비율)
# 바깥쪽 = 왼쪽 팔다리는 −X 쪽, 오른쪽은 +X 쪽 — 좌우 거울은 아래 _frame이 처리.
MUSCLES = [
    # 위팔
    ("UpperArm", -0.05, 0.36, 30, 45, 0.0060, 0.30),    # 삼각근 앞 갈래
    ("UpperArm", -0.05, 0.40, 95, 45, 0.0072, 0.30),    # 삼각근 가쪽 갈래
    ("UpperArm", -0.05, 0.36, 160, 45, 0.0060, 0.30),   # 삼각근 뒤 갈래
    ("UpperArm", 0.28, 0.88, 0, 70, 0.0064, 0.28),      # 이두
    ("UpperArm", 0.18, 0.80, 180, 65, 0.0056, 0.28),    # 삼두 긴갈래
    ("UpperArm", 0.20, 0.70, 130, 45, 0.0048, 0.30),    # 삼두 가쪽갈래
    ("UpperArm", 0.55, 0.95, 70, 25, 0.0029, 0.35),     # 위팔근 (이두 바깥 아래)
    # 아래팔
    ("LowerArm", -0.02, 0.55, 45, 60, 0.0056, 0.30),    # 위팔노근 (바깥 앞, 팔꿈치 아래 볼록)
    ("LowerArm", 0.00, 0.60, -40, 65, 0.0048, 0.30),    # 굽힘근 무리 (안쪽 앞)
    ("LowerArm", 0.02, 0.62, 150, 60, 0.0045, 0.30),    # 폄근 무리 (뒤)
    ("LowerArm", 0.00, 0.45, 100, 30, 0.0035, 0.30),    # 긴노쪽손목폄근 (바깥)
    ("LowerArm", 0.05, 0.50, -90, 30, 0.0030, 0.30),    # 자쪽손목굽힘근 (안쪽)
    # 허벅지
    ("UpperLeg", 0.10, 0.85, 0, 55, 0.0064, 0.25),      # 넙다리곧은근
    ("UpperLeg", 0.15, 0.92, 70, 60, 0.0072, 0.25),     # 가쪽넓은근
    ("UpperLeg", 0.55, 0.95, -55, 50, 0.0080, 0.30),    # 안쪽넓은근 (무릎 위 눈물방울)
    ("UpperLeg", 0.10, 0.85, 180, 80, 0.0056, 0.25),    # 햄스트링
    # 종아리
    ("LowerLeg", 0.02, 0.50, 165, 60, 0.0080, 0.25),    # 장딴지근 안·바깥 갈래
    ("LowerLeg", 0.05, 0.70, 45, 25, 0.0032, 0.30),     # 앞정강근
    # 목·어깨(셔츠 밖으로 보이는 부분)
    ("Neck", 0.00, 0.95, 35, 18, 0.0048, 0.35),         # 목빗근 (좌우 따로: 아래 _frame에서 θ 거울)
    ("Neck", 0.00, 0.95, -35, 18, 0.0048, 0.35),
]
# 뼈 돌출 (뼈, t, θc, 각 반폭, 축 반폭(t), 높이)
BONY = [
    ("LowerArm", 0.00, 180, 35, 0.06, 0.0030),   # 팔꿈치머리
    ("LowerArm", 0.97, 90, 30, 0.04, 0.0015),    # 손목 노뼈 붓돌기
    ("LowerArm", 0.97, -90, 30, 0.04, 0.0015),   # 자뼈 붓돌기
    ("LowerLeg", 0.02, 0, 35, 0.05, 0.0040),     # 무릎뼈 (허벅지 끝 = 종아리 시작 앞)
    ("UpperLeg", 0.98, 0, 40, 0.04, 0.0030),
]
# 힘줄 띠 (뼈, t0, t1, θc, 각 반폭, 높이)
TENDONS = [
    ("UpperLeg", 0.15, 0.95, 120, 6, -0.0025),   # 엉덩정강띠 고랑 (가쪽넓은근 뒤 홈, 음수 = 골)
    ("UpperLeg", 0.10, 0.80, -20, 5, -0.0020),   # 넙다리빗근 고랑 (안쪽 앞)
    ("UpperArm", 0.40, 0.95, 90, 5, -0.0020),    # 이두·삼두 사이 바깥 고랑
    ("UpperArm", 0.40, 0.95, -90, 5, -0.0020),   # 안쪽 고랑
    ("LowerArm", 0.60, 0.98, -20, 6, 0.0012),    # 손바닥쪽 손목 힘줄
    ("LowerArm", 0.60, 0.98, -45, 5, 0.0010),
    ("LowerLeg", 0.80, 1.00, 180, 10, 0.0020),   # 아킬레스
]


# 정맥 (뼈, t0, t1, 기준 각 θ0, 사인 진폭(도), 굽이 수, 위상, 폭 m, 높이 m) — 운동선수 아래팔·위팔의 표면 정맥.
# 가상 패턴(정맥 지도 실측 아님). 좌우는 바깥쪽 각 규칙으로 거울.
VEINS = [
    ("LowerArm", 0.10, 0.95, -15, 10, 1.3, 0.0, 0.0030, 0.0016),   # 손바닥쪽 정중 정맥
    ("LowerArm", 0.05, 0.90, 40, 14, 1.0, 1.1, 0.0032, 0.0018),    # 노쪽 피부 정맥
    ("LowerArm", 0.15, 0.85, -60, 12, 1.6, 2.3, 0.0028, 0.0014),   # 자쪽 피부 정맥
    ("LowerArm", 0.35, 0.70, 10, 25, 0.8, 0.6, 0.0024, 0.0012),    # 사이 연결 가지
    ("UpperArm", 0.35, 1.00, 60, 8, 0.9, 0.4, 0.0034, 0.0016),     # 머리 정맥 (이두 바깥 고랑)
    ("UpperArm", 0.55, 1.00, -30, 10, 1.1, 1.7, 0.0028, 0.0012),   # 자쪽 정맥 아래
]


def _hash3(ix, iy, iz, seed):
    h = (ix * 374761393 + iy * 668265263 + iz * 1440662683 + seed * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def smooth_noise3(P, cell, seed):
    """3D 위치(m) 격자 cell 간격 값 노이즈를 3선형 + smoothstep 보간 (UV 이음매 없음, 정수 해시 — 결정론). 0~1."""
    q = P / cell
    i0 = np.floor(q).astype(np.int64)
    f = q - i0
    f = f * f * (3 - 2 * f)
    out = np.zeros(P.shape[:-1])
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[..., 0] if dx else 1 - f[..., 0]) * (f[..., 1] if dy else 1 - f[..., 1]) * (f[..., 2] if dz else 1 - f[..., 2])
                out += w * _hash3(i0[..., 0] + dx, i0[..., 1] + dy, i0[..., 2] + dz, seed)
    return out


# 피하 형태 굴곡: 팔다리 피부의 잔잔한 높낮이(근섬유 묶음·피하 지방 고르지 않음). (격자 m, 높이 m) 두 옥타브.
FORM_OCTAVES = ((0.035, 0.0008), (0.018, 0.0004))
FORM_BONES = ("UpperArm", "LowerArm", "UpperLeg", "LowerLeg", "Shoulder", "Neck")


def _frame(R, name):
    h, t = (np.array(x, dtype=float) for x in R.joints[name])
    ax = t - h
    ln = float(np.linalg.norm(ax))
    ax /= ln
    front = np.array([0.0, 0.0, 1.0]) - ax * ax[2]
    if np.linalg.norm(front) < 1e-6:
        front = np.array([0.0, 1.0, 0.0]) - ax * ax[1]
    front /= np.linalg.norm(front)
    out = np.cross(ax, front)
    out /= np.linalg.norm(out)
    # "바깥쪽"(+θ)이 왼쪽 팔다리는 −X(선수 왼쪽), 오른쪽은 +X가 되게 부호를 맞춘다. 가운데 뼈(목)는 +X.
    want = -1.0 if name.startswith("Left") else 1.0
    if out[0] * want < 0:
        out = -out
    return h, ax, ln, front, out


def _bone_coords(R, pos, name):
    h, ax, ln, front, out = _frame(R, name)
    P = pos - h
    t = P @ ax / ln
    r = P - np.outer(P.reshape(-1, 3) @ ax, ax).reshape(P.shape)
    th = np.degrees(np.arctan2(r @ out, r @ front))
    return t, th


def _angdist(a, b):
    return np.abs((a - b + 180.0) % 360.0 - 180.0)


def muscle_height(R, pos, valid, dom):
    """→ 높이 (S,S) m. 텍셀 주 뼈(dom)로 근육을 가둔다 — 몸통 텍셀에 팔 근육이 새지 않게."""
    S = pos.shape[0]
    hgt = np.zeros((S, S))
    sides = ("Left", "Right")
    for suffix, t0, t1, thc, hw, amp, edge in MUSCLES:
        for side in (sides if suffix != "Neck" else ("",)):
            name = side + suffix
            bi = MS.OUR_NAMES.index(name)
            near = valid & ((dom == bi) | _neighbor_bone(dom, R, name))
            if not near.any():
                continue
            t, th = _bone_coords(R, pos, name)
            # 타원 근육 배: d² = ((t − 중심)/반길이)² + (각 거리/반폭)², 높이 = amp × (1 − d²)^p (p < 1 = 윗면이 둥글고
            # 가장자리가 가팔라 이웃 근육과 만나는 곳에 골이 생긴다). edge가 작을수록 가장자리가 가파르다.
            tc, ht = 0.5 * (t0 + t1), 0.5 * (t1 - t0)
            d2 = ((t - tc) / ht) ** 2 + (_angdist(th, thc) / hw) ** 2
            hgt += amp * np.clip(1.0 - d2, 0.0, 1.0) ** (0.6 + edge) * near
    for suffix, t0, t1, th0, ta, nw, ph, width, amp in VEINS:
        for side in sides:
            name = side + suffix
            bi = MS.OUR_NAMES.index(name)
            near = valid & ((dom == bi) | _neighbor_bone(dom, R, name))
            h, ax, ln, front, outv = _frame(R, name)
            P = pos - h
            t = P @ ax / ln
            r = P - np.outer(P.reshape(-1, 3) @ ax, ax).reshape(P.shape)
            rad = np.maximum(np.linalg.norm(r, axis=-1), 1e-4)
            th = np.degrees(np.arctan2(r @ outv, r @ front))
            centre = th0 + ta * np.sin(2 * math.pi * nw * (t - t0) / max(t1 - t0, 1e-6) + ph)
            arc = np.radians(_angdist(th, centre)) * rad          # 정맥 중심선까지 표면 거리 (m)
            fade = _smooth(t, t0, t0 + 0.08) * (1 - _smooth(t, t1 - 0.08, t1))
            hgt += amp * np.exp(-(arc / (0.5 * width)) ** 2) * fade * near
    limb = np.zeros(dom.shape, dtype=bool)
    for n in MS.OUR_NAMES:
        if n.endswith(FORM_BONES):
            limb |= dom == MS.OUR_NAMES.index(n)
    for k, (cell, amp) in enumerate(FORM_OCTAVES):
        hgt += amp * (smooth_noise3(pos, cell, 620 + k) - 0.5) * 2 * (limb & valid)
    for suffix, tc, thc, hw, ht, amp in BONY:
        for side in sides:
            name = side + suffix
            bi = MS.OUR_NAMES.index(name)
            near = valid & ((dom == bi) | _neighbor_bone(dom, R, name))
            t, th = _bone_coords(R, pos, name)
            hgt += amp * np.exp(-((t - tc) / ht) ** 2) * np.exp(-(_angdist(th, thc) / hw) ** 2) * near
    for suffix, t0, t1, thc, hw, amp in TENDONS:
        for side in sides:
            name = side + suffix
            bi = MS.OUR_NAMES.index(name)
            t, th = _bone_coords(R, pos, name)
            band = _smooth(t, t0, t0 + 0.08) * (1 - _smooth(t, t1 - 0.05, t1))
            hgt += amp * band * np.exp(-(_angdist(th, thc) / hw) ** 2) * valid * (dom == bi)
    return hgt


ADJ = {"UpperArm": ("Shoulder", "LowerArm"), "LowerArm": ("UpperArm", "Hand"), "UpperLeg": ("Hips", "LowerLeg"),
       "LowerLeg": ("UpperLeg", "Foot"), "Neck": ("UpperChest", "Head")}


def _neighbor_bone(dom, R, name):
    """관절 이음 텍셀(주 뼈가 이웃 뼈)도 근육 끝 감쇠 구간에 포함 — 관절에서 음영이 잘리지 않게."""
    side = "Left" if name.startswith("Left") else ("Right" if name.startswith("Right") else "")
    suffix = name[len(side):]
    out = np.zeros(dom.shape, dtype=bool)
    for n in ADJ.get(suffix, ()):
        full = n if n in ("Hips", "UpperChest", "Head") else side + n
        if full in MS.OUR_NAMES:
            out |= dom == MS.OUR_NAMES.index(full)
    return out


def raster_bones(R, size):
    """skin._raster와 같은 UV 래스터 + 텍셀별 주 뼈(면 세 정점 가중치 합의 최대) + UV 1 단위당 표면 길이(m)."""
    body = np.zeros(R.n, dtype=bool)
    body[R.groups["body"]] = True
    pos = np.zeros((size, size, 3))
    valid = np.zeros((size, size), dtype=bool)
    dom = np.full((size, size), -1, dtype=np.int64)
    a3 = auv = 0.0
    for f, fu in zip(R.faces, R.face_uvs):
        if not body[list(f)].all():
            continue
        bone = int(np.argmax(R.weights[list(f)].sum(axis=0)))
        for k in range(1, len(f) - 1):
            idx = (0, k, k + 1)
            uv1 = np.array([fu[i] for i in idx])
            P = R.verts[[f[i] for i in idx]]
            a3 += 0.5 * float(np.linalg.norm(np.cross(P[1] - P[0], P[2] - P[0])))
            auv += 0.5 * abs(float(np.cross(uv1[1] - uv1[0], uv1[2] - uv1[0])))
            uv = uv1 * size
            x0, x1 = max(int(math.floor(uv[:, 0].min())), 0), min(int(math.ceil(uv[:, 0].max())), size)
            y0, y1 = max(int(math.floor(uv[:, 1].min())), 0), min(int(math.ceil(uv[:, 1].max())), size)
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            a, b, c = uv
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-12:
                continue
            l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
            l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
            l2 = 1 - l0 - l1
            m = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
            if not m.any():
                continue
            p = l0[..., None] * P[0] + l1[..., None] * P[1] + l2[..., None] * P[2]
            rows = size - 1 - ys[m].astype(int)
            cols = xs[m].astype(int)
            pos[rows, cols] = p[m]
            valid[rows, cols] = True
            dom[rows, cols] = bone
    return pos, valid, dom, math.sqrt(a3 / max(auv, 1e-12))


def cavity_ao(hgt, valid, radius_px, gain):
    """캐비티 AO = 1 − gain × max(0, 주변 평균 높이 − 높이) (오목한 곳 어둡게)."""
    k = max(int(radius_px), 1)
    blur = SK._box(hgt, k)
    ao = 1.0 - gain * np.clip(blur - hgt, 0, None)
    return np.clip(ao, 0.0, 1.0) * valid + (~valid) * 1.0


def hand_height(R, pos, valid, dom):
    """062 FR-12·91 손: 손가락 마디 주름(손바닥 쪽 골 + 손등 잔주름 3줄), 손톱(끝 마디 손등 쪽 판 + 경계 골), 손바닥 주름 3줄.
    각도 0 = 손바닥 쪽(hands.palm_frame 법선), 180 = 손등."""
    from . import hands as HD
    S_ = pos.shape[0]
    hgt = np.zeros((S_, S_))
    for side in ("Left", "Right"):
        d, n, a = (np.array(v) for v in HD.palm_frame(side))
        for F, _ in MS.FINGERS:
            for k, (g, _) in enumerate(MS.FINGER_SEGS):
                name = side + F + g
                bi = MS.OUR_NAMES.index(name)
                near = valid & ((dom == bi) | (dom == MS.OUR_NAMES.index(MS.OUR_PARENT[name])))
                if not near.any():
                    continue
                h, t_ = (np.array(x, dtype=float) for x in R.joints[name])
                ax = t_ - h
                ln = float(np.linalg.norm(ax))
                ax /= ln
                fr = n - ax * (n @ ax)
                fr /= np.linalg.norm(fr)
                side_v = np.cross(ax, fr)
                P = pos - h
                along = P @ ax                        # m (마디 시작에서)
                r = P - along[..., None] * ax
                th = np.abs(np.degrees(np.arctan2(r @ side_v, r @ fr)))  # 0 = 손바닥, 180 = 손등
                palm = 1 - _smooth(th, 60, 90)
                back = _smooth(th, 110, 140)
                hgt += -0.0006 * np.exp(-(along / 0.0012) ** 2) * palm * near            # 손바닥 쪽 마디 골
                for j in range(3):                                                    # 손등 마디 잔주름
                    hgt += -0.00025 * np.exp(-((along - (j - 1) * 0.0016) / 0.0004) ** 2) * back * near
                if g == "Distal":
                    tt = along / ln
                    plate = _smooth(tt, 0.35, 0.45) * (1 - _smooth(tt, 0.92, 1.0)) * _smooth(th, 130, 145)
                    hgt += 0.0003 * plate * near
                    edge = np.exp(-((tt - 0.40) / 0.025) ** 2) * _smooth(th, 125, 140)
                    hgt += -0.0004 * edge * near                                      # 손톱 뿌리 골
        # 손바닥 주름 (손바닥 쪽, 손 뼈 주 텍셀): 손 방향 길이 비율 t, 가로 비율 u (엄지 쪽 0 → 새끼 쪽 1)
        name = side + "Hand"
        bi = MS.OUR_NAMES.index(name)
        near = valid & (dom == bi)
        h, t_ = (np.array(x, dtype=float) for x in R.joints[name])
        ln = float(np.linalg.norm(t_ - h))
        P = pos - h
        tt = (P @ d) / ln
        u = (P @ a) / 0.09 + 0.5
        palm_side = (P @ n) > 0
        for c0, c1, amp in ((0.42, 0.30, -0.0005), (0.36, 0.26, -0.0005)):   # 감정선·두뇌선 (가상 곡선)
            line = c0 + (c1 - c0) * u + 0.03 * np.sin(u * 3.0)
            hgt += amp * np.exp(-((tt - line) / 0.012) ** 2) * near * palm_side
        life = 0.18 + 0.25 * np.clip(u, 0, 1) ** 0.5                            # 생명선 (엄지 뿌리 둘레)
        hgt += -0.0005 * np.exp(-((tt - life) / 0.012) ** 2) * (u < 0.45) * near * palm_side
    return hgt


def body_detail(R, size=DETAIL):
    """→ (노멀 uint8 (S,S,3), AO uint8 (S,S,3), 높이 (S,S)). 같은 R → 같은 바이트."""
    pos, valid, dom, m_per_uv = raster_bones(R, size)
    hgt = muscle_height(R, pos, valid, dom) + hand_height(R, pos, valid, dom)
    texel_m = m_per_uv / size  # 텍셀 한 칸의 평균 몸 표면 길이(m) — 높이 기울기를 실제 단위로
    nrm = SK._normal_from_height(hgt / texel_m, NORMAL_GAIN)
    nrm = SK._dilate(nrm, valid)
    ao = cavity_ao(hgt, valid, size // 128, 120.0)
    ao = SK._dilate(np.repeat(ao[..., None], 3, axis=-1), valid)
    return SK._to8(nrm), SK._to8(ao), hgt


def write_all(out_dir, R):
    import os
    from .textures import write_png
    n, ao, _ = body_detail(R)
    write_png(os.path.join(out_dir, "body_detail_n.png"), n)
    write_png(os.path.join(out_dir, "body_detail_ao.png"), ao)
    return {"body_detail_n.png": (DETAIL, DETAIL), "body_detail_ao.png": (DETAIL, DETAIL)}


# ---------------------------------------------------------------- 062 FR-20·23·24·31·91 키트 구운 지도

KIT_DETAIL = 2048
KIT_GAIN = 3.5     # 천 주름 노멀 과장 (T14 측정)
PART_CODE = {"shirt": 1, "sleeve": 2, "shorts": 3, "sock": 4, "boot": 5}


def raster_kit(m, size):
    """LOD0 키트 면(턱 포함)을 키트 UV에 래스터 → (위치, 법선, 부위 코드, 유효, UV당 표면 길이)."""
    from . import body as B
    vn = B._normals(m.verts, [f[0] for f in m.faces])
    pos = np.zeros((size, size, 3))
    nrm = np.zeros((size, size, 3))
    code = np.zeros((size, size), dtype=np.int64)
    valid = np.zeros((size, size), dtype=bool)
    a3 = auv = 0.0
    for f, mat, uvs, part, _ in m.faces:
        if mat != B.KIT:
            continue
        pc = PART_CODE.get(part.replace("_rim", ""), 0)
        for k in range(1, len(f) - 1):
            idx = (0, k, k + 1)
            uv1 = np.array([uvs[i] for i in idx], dtype=float)
            if uv1.min() < -1e-6 or uv1.max() > 1 + 1e-6:
                continue
            P = m.verts[[f[i] for i in idx]]
            N = vn[[f[i] for i in idx]]
            a3 += 0.5 * float(np.linalg.norm(np.cross(P[1] - P[0], P[2] - P[0])))
            auv += 0.5 * abs(float(np.cross(uv1[1] - uv1[0], uv1[2] - uv1[0])))
            uv = uv1 * size
            x0, x1 = max(int(math.floor(uv[:, 0].min())), 0), min(int(math.ceil(uv[:, 0].max())), size)
            y0, y1 = max(int(math.floor(uv[:, 1].min())), 0), min(int(math.ceil(uv[:, 1].max())), size)
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            a, b, c = uv
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-12:
                continue
            l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
            l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
            l2 = 1 - l0 - l1
            ins = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
            if not ins.any():
                continue
            rows, cols = size - 1 - ys[ins].astype(int), xs[ins].astype(int)
            pos[rows, cols] = l0[ins, None] * P[0] + l1[ins, None] * P[1] + l2[ins, None] * P[2]
            nn = l0[ins, None] * N[0] + l1[ins, None] * N[1] + l2[ins, None] * N[2]
            nrm[rows, cols] = nn / np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-9)
            code[rows, cols] = pc
            valid[rows, cols] = True
    return pos, nrm, code, valid, math.sqrt(a3 / max(auv, 1e-12))


def kit_height(pos, nrm, code, valid):
    """천 주름·봉제선·칼라 리브·양말 골지·발목 주름·축구화 패널 (m). 위치·법선·부위만의 함수 (결정론)."""
    from . import body as B
    from . import skeleton as S
    planes = {pl[0]: pl for pl in B.cut_planes()}
    h = np.zeros(code.shape)
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    nx, nz = nrm[..., 0], nrm[..., 2]
    shirt = valid & ((code == 1) | (code == 2))
    torso = valid & (code == 1)
    # 셔츠 허리 주름: 밑단 위 2~18 cm 가로 물결, 위상은 둘레 방향 노이즈 — 등 쪽이 더 깊다
    hem_y = planes["hem"][1][1]
    dh = y - hem_y
    env = np.clip(dh / 0.02, 0, 1) * np.clip((0.18 - dh) / 0.08, 0, 1)
    phase = smooth_noise3(pos * np.array([1.0, 0.0, 1.0]), 0.06, 631) * 6.0
    amp_n = smooth_noise3(pos, 0.05, 633)                       # 주름마다 깊이가 다르게
    lam = 0.024 + 0.012 * smooth_noise3(pos, 0.09, 634)          # 간격도 고르지 않게
    h += 0.0011 * np.sin(2 * math.pi * dh / lam + phase) * env * (0.3 + 0.9 * amp_n) * (0.6 + 0.4 * np.clip(-nz, 0, 1)) * torso
    # 늘어짐 주름: 길쭉한 매끈 노이즈(세로로 길게 + 겨드랑이에서 허리 쪽 대각) — 몸판 전체의 부드러운 천 굴곡
    vert = smooth_noise3(pos * np.array([1.0, 0.25, 1.0]), 0.035, 641) - 0.5
    diag_pos = np.stack([np.abs(pos[..., 0]) + 0.6 * pos[..., 1], pos[..., 1] * 0.3, pos[..., 2]], axis=-1)  # 좌우 대칭, 가운데 이음 없음
    diag = smooth_noise3(diag_pos, 0.04, 642) - 0.5
    h += (0.0020 * vert + 0.0014 * diag) * torso
    h += 0.0012 * (smooth_noise3(pos * np.array([1.0, 0.4, 1.0]), 0.03, 643) - 0.5) * valid * (code == 2)   # 소매
    # 겨드랑이 방사 주름: 겨드랑이 점에서 12 cm 안, 각 방향 사인
    for s_ in ("Left", "Right"):
        hd = np.array(S.HEAD[s_ + "UpperArm"])
        c = hd + np.array([0.0, -0.07, 0.0]) - np.sign(hd[0]) * np.array([0.04, 0.0, 0.0])
        d = pos - c
        r = np.linalg.norm(d, axis=-1)
        ang = np.arctan2(d[..., 2], d[..., 1])
        h += 0.0015 * np.sin(ang * 7.0) * np.clip(1 - r / 0.12, 0, 1) ** 1.5 * shirt
    # 어깨 봉제선: 몸통 위쪽에서 앞뒤 경계(z ≈ 0) 홈 + 땀 점선
    top = np.clip((y - (S.HEAD["RightUpperArm"][1] - 0.04)) / 0.04, 0, 1)
    seam = np.exp(-(z / 0.0018) ** 2)
    stitch = 0.5 + 0.5 * np.sign(np.sin(np.hypot(x, y) / 0.003 * math.pi))
    h += -0.0006 * seam * top * shirt + -0.0002 * np.exp(-((np.abs(z) - 0.004) / 0.0007) ** 2) * stitch * top * shirt
    # 옆구리 봉제선: 몸통 옆면(법선 z ≈ 0, |x| 큼)
    side = np.exp(-(nz / 0.06) ** 2) * (np.abs(nx) > 0.7)
    h += -0.0005 * side * torso
    # 소매 끝 박음질 두 줄 · 반바지·셔츠 밑단 접힘 턱
    for s_ in ("L", "R"):
        d = np.abs(np.einsum("...k,k->...", pos - np.array(planes["sleeve" + s_][1]), np.array(planes["sleeve" + s_][2])))
        h += -0.0003 * (np.exp(-((d - 0.006) / 0.0008) ** 2) + np.exp(-((d - 0.010) / 0.0008) ** 2)) * valid * (code == 2)
        ds = np.abs(y - planes["shorts" + s_][1][1])
        h += 0.0006 * np.clip(1 - ds / 0.015, 0, 1) * valid * (code == 3) * (np.sign(x) == (-1 if s_ == "L" else 1))
    # 칼라 리브: 칼라 평면 위 2 cm 띠의 세로 골
    cpl = planes["collar"]
    dc = np.einsum("...k,k->...", pos - np.array(cpl[1]), np.array(cpl[2]))
    neck_ang = np.arctan2(x, z)
    h += 0.0003 * np.sin(neck_ang * 90.0) * (np.abs(dc) < 0.02) * shirt
    # 반바지: 사타구니 방사 주름 + 옆선
    shorts = valid & (code == 3)
    crotch = np.array([0.0, S.HEAD["LeftUpperLeg"][1] - 0.06, 0.0])
    d = pos - crotch
    r = np.linalg.norm(d, axis=-1)
    ang = np.arctan2(d[..., 0], d[..., 2])
    h += 0.0014 * np.sin(ang * 6.0) * np.clip(1 - r / 0.14, 0, 1) ** 1.5 * shorts
    h += 0.0016 * (smooth_noise3(pos * np.array([1.0, 0.35, 1.0]), 0.035, 644) - 0.5) * shorts   # 반바지 늘어짐
    h += -0.0005 * np.exp(-(nz / 0.06) ** 2) * (np.abs(nx) > 0.7) * shorts
    # 양말: 세로 골지 + 발목 가로 주름
    sock = valid & (code == 4)
    for s_ in ("Left", "Right"):
        hd, tl = np.array(S.HEAD[s_ + "LowerLeg"]), np.array(S.TAIL[s_ + "LowerLeg"])
        ax = (tl - hd) / np.linalg.norm(tl - hd)
        rr = pos - hd - np.einsum("...k,k->...", pos - hd, ax)[..., None] * ax
        ang = np.arctan2(rr[..., 0], rr[..., 2])
        mine = sock & (np.sign(x) == np.sign(hd[0]))
        h += 0.0004 * np.sin(ang * 48.0) * mine
        ank = S.HEAD[s_ + "Foot"][1]
        h += 0.0006 * np.sin((y - ank) / 0.006 * math.pi) * np.clip(1 - (y - ank - 0.03) / 0.04, 0, 1) * (y > ank) * mine
    # 축구화: 끈 없는 니트 갑피 결(대각) + 앞코 덮개·뒤꿈치 보강 이음선 + 밑창 경계
    boot = valid & (code == 5)
    knit = np.sin((x + z) / 0.0025 * math.pi) * np.sin((x - z) / 0.0025 * math.pi)
    h += 0.00015 * knit * boot * (y > 0.03)
    for s_ in ("Left", "Right"):
        toe = np.array(S.TAIL[s_ + "Toes"])
        dt = np.linalg.norm((pos - toe) * np.array([1.0, 0.6, 1.0]), axis=-1)
        h += -0.0005 * np.exp(-((dt - 0.055) / 0.0012) ** 2) * boot * (np.sign(x) == np.sign(toe[0]))
        heel = np.array(S.HEAD[s_ + "Foot"]) + np.array([0.0, -0.04, -0.05])
        dhl = np.linalg.norm((pos - heel) * np.array([1.0, 0.8, 1.0]), axis=-1)
        h += -0.0005 * np.exp(-((dhl - 0.06) / 0.0012) ** 2) * boot * (np.sign(x) == np.sign(heel[0]))
    h += -0.0006 * np.exp(-((y - 0.022) / 0.0015) ** 2) * boot
    return h


def kit_detail(m, size=KIT_DETAIL):
    """→ (노멀 uint8, AO uint8, 높이). 키트 UV 공용 — 팀 색·번호는 이 위에 셰이더가 합친다(043 키트 합성 유지)."""
    pos, nrm, code, valid, m_per_uv = raster_kit(m, size)
    hgt = kit_height(pos, nrm, code, valid)
    texel_m = m_per_uv / size
    n = SK._normal_from_height(hgt / texel_m, KIT_GAIN)
    n = SK._dilate(n, valid)
    ao = cavity_ao(hgt, valid, size // 160, 150.0)
    ao = SK._dilate(np.repeat(ao[..., None], 3, axis=-1), valid)
    return SK._to8(n), SK._to8(ao), hgt


WEAVE = 256


def weave_tiles():
    """천 미세 결 높이 타일 (반복 텍스처, RGB = 셔츠 메시 · 반바지 평직 · 양말 골지). 셰이더가 영역 맵 B로 고른다."""
    u, v = np.meshgrid((np.arange(WEAVE) + 0.5) / WEAVE, (np.arange(WEAVE) + 0.5) / WEAVE)
    k = 2 * math.pi
    mesh = 0.5 + 0.5 * np.cos(k * 8 * u) * np.cos(k * 8 * v)                      # 구멍 송송 메시(점 격자)
    mesh = np.clip(mesh, 0.15, 1.0)
    plain = 0.5 + 0.25 * np.sign(np.sin(k * 16 * u)) * np.sin(k * 16 * v) + 0.25 * np.sin(k * 16 * u) * np.sign(np.sin(k * 16 * v))
    rib = 0.5 + 0.5 * np.sin(k * 12 * u) ** 2
    return SK._to8(np.stack([mesh, plain, rib], axis=-1))


def kit_region(m, size=1024):
    """영역 맵: R 거칠기(0~1), G 인쇄 광택 배율(셰이더가 인쇄 픽셀에만), B 결 선택(0 메시·0.5 평직·1 골지), A 젖음 대상(1).
    재질 값 표는 specs/062 plan D단계(T19)에서 확정 — 여기서는 부위별 기본값."""
    pos, nrm, code, valid, _ = raster_kit(m, size)
    rough = np.select([code == 1, code == 2, code == 3, code == 4, code == 5], [0.62, 0.62, 0.70, 0.80, 0.35], 0.6)
    gloss = np.full(code.shape, 0.75)
    weave = np.select([code == 3, code == 4], [0.5, 1.0], 0.0)
    wet = (code > 0).astype(float)
    img = np.stack([rough, gloss, weave, wet], axis=-1)
    img = SK._dilate(img, valid)
    return SK._to8(img)


def write_kit(out_dir, m):
    import os
    from .textures import write_png
    n, ao, _ = kit_detail(m)
    write_png(os.path.join(out_dir, "kit_detail_n.png"), n)
    write_png(os.path.join(out_dir, "kit_detail_ao.png"), ao)
    write_png(os.path.join(out_dir, "kit_weave.png"), weave_tiles())
    write_png(os.path.join(out_dir, "kit_region.png"), kit_region(m))
    return {"kit_detail_n.png": (KIT_DETAIL, KIT_DETAIL), "kit_detail_ao.png": (KIT_DETAIL, KIT_DETAIL),
            "kit_weave.png": (WEAVE, WEAVE), "kit_region.png": (1024, 1024)}
