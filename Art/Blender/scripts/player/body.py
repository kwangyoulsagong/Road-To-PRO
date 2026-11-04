"""선수 베이스 메시 (specs/043 FR-01·10·11). 순수 파이썬 — bpy 없이 정점·면·UV·가중치를 만든다.

구성: 부위별 닫힌 튜브(단면 링을 이어 붙임) + 머리 타원체 + 헤어 캡. 메시 1개, 머티리얼 2개(0 Body, 1 Kit).
- 같은 LOD면 체형·헤어 인자와 관계없이 **토폴로지가 같다** → 셰이프키는 인자를 바꿔 다시 만든 정점 배열.
- 발(축구화) 단면은 바닥 y = 0에 닿게 만들고 체형 인자의 영향을 받지 않는다 → 극단 체형에서도 접지 0 (AC-02).
- UV 영역은 uv.py, 좌표 규칙은 space.py(선수 좌표 X 오른쪽·Y 위·Z 정면).
"""
import math

from . import skeleton as S
from . import uv as U

BODY, KIT = 0, 1

LODS = {
    0: dict(torso=32, limb=16, head_lat=20, head_lon=36, hair_rows=7, foot=14, hand=10, sub=3),
    1: dict(torso=14, limb=9, head_lat=9, head_lon=16, hair_rows=4, foot=9, hand=6, sub=1),
    2: dict(torso=8, limb=5, head_lat=4, head_lon=8, hair_rows=2, foot=5, hand=4, sub=0),
}

# 체형 셰이프 (FR-01): 그룹별 (rx 배율, rz 배율). 발·손·머리는 영향 없음.
SHAPES = {
    "Thin": {"chest": (0.88, 0.88), "waist": (0.86, 0.84), "pelvis": (0.9, 0.9), "thigh": (0.86, 0.86), "calf": (0.88, 0.88),
             "upperarm": (0.85, 0.85), "forearm": (0.88, 0.88), "neck": (0.9, 0.9)},
    "Muscular": {"chest": (1.10, 1.10), "waist": (1.02, 1.0), "pelvis": (1.03, 1.03), "thigh": (1.07, 1.10), "calf": (1.12, 1.12),
                 "upperarm": (1.22, 1.22), "forearm": (1.12, 1.12), "neck": (1.15, 1.15)},
    "Heavy": {"chest": (1.08, 1.12), "waist": (1.18, 1.30), "pelvis": (1.12, 1.12), "thigh": (1.06, 1.10), "calf": (1.05, 1.05),
              "upperarm": (1.12, 1.12), "forearm": (1.06, 1.06), "neck": (1.18, 1.18)},
}
# 헤어 스타일 셰이프키 (기본 = 버즈컷 캡). 축구 선수에게 흔한 스타일: 삭발·크롭·페이드·곱슬·아프로·장발·번·모히칸·드레드
HAIR_STYLES = ("Hair_Short", "Hair_Long", "Hair_Curly", "Hair_Bun", "Hair_Fade", "Hair_Afro", "Hair_Mohawk", "Hair_Dreads", "Hair_Bald")

HEAD_C = (0.0, 1.70, 0.012)
HEAD_R = (0.085, 0.125, 0.100)
HAIR_GAP = 0.006


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _mul(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _norm(a):
    n = math.sqrt(a[0] ** 2 + a[1] ** 2 + a[2] ** 2)
    return (a[0] / n, a[1] / n, a[2] / n)


def _lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(len(a)))


def _clamp01(x):
    return 0.0 if x < 0 else 1.0 if x > 1 else x


def _blend(x, edge, half):
    """x가 edge-half → edge+half를 지날 때 0 → 1."""
    return _clamp01((x - (edge - half)) / (2 * half))


class Mesh:
    """verts[i] = (x,y,z); faces[j] = (정점 인덱스 튜플, 머티리얼, 코너 UV 튜플, 부위, 기준점)."""

    def __init__(self):
        self.verts, self.weights, self.vpart = [], [], []
        self.faces = []

    def vert(self, p, w, part):
        self.verts.append(tuple(float(x) for x in p))
        tot = sum(w.values())
        self.weights.append({k: v / tot for k, v in sorted(w.items()) if v > 1e-6})
        self.vpart.append(part)
        return len(self.verts) - 1

    def face(self, idx, mat, uvs, part, ref):
        self.faces.append((tuple(idx), mat, tuple(uvs), part, ref))

    @property
    def triangles(self):
        return sum(len(f[0]) - 2 for f in self.faces)


# ---------------------------------------------------------------- 튜브

class Station:
    """fb = (앞, 뒤) 배율: 단면의 u(+Z, 몸 앞) 쪽 반지름과 뒤쪽 반지름을 따로 — 가슴·엉덩이·종아리 같은 앞뒤 비대칭 굴곡."""
    __slots__ = ("c", "rx", "rz", "group", "t", "tag", "fb")

    def __init__(self, c, rx, rz, group, t, tag, fb=(1.0, 1.0)):
        self.c, self.rx, self.rz, self.group, self.t, self.tag, self.fb = c, rx, rz, group, t, tag, fb


def _interp(st, sub):
    if sub <= 0:
        return list(st)
    out = []
    for a, b in zip(st, st[1:]):
        out.append(a)
        if a.tag != b.tag:
            continue  # 영역 경계는 정확히 그 높이에 남긴다
        for k in range(1, sub + 1):
            t = k / (sub + 1)
            out.append(Station(_lerp(a.c, b.c, t), a.rx + (b.rx - a.rx) * t, a.rz + (b.rz - a.rz) * t,
                               a.group, a.t + (b.t - a.t) * t, a.tag, _lerp(a.fb, b.fb, t)))
    out.append(st[-1])
    return out


def tube(m, stations, sides, frame, shape, weight_fn, uv_fn, part, cap0=True, cap1=True):
    """단면 링 튜브. frame = (w, u): 단면 점 = c + rx·cos(a)·w + rz·sin(a)·u, a = 2πk/sides.
    uv_fn(tag, k/sides, station) → (머티리얼, (u, v)). 영역(tag)이 같은 두 링 사이 면만 그 영역."""
    w, u = frame
    rings = []
    for s in stations:
        fx, fz = shape.get(s.group, (1.0, 1.0)) if s.group else (1.0, 1.0)
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            sa = math.sin(a)
            rz = s.rz * fz * (s.fb[0] if sa >= 0 else s.fb[1])
            p = _add(s.c, _add(_mul(w, s.rx * fx * math.cos(a)), _mul(u, rz * sa)))
            ring.append(m.vert(p, weight_fn(s), part))
        rings.append(ring)
    for i in range(len(stations) - 1):
        s0, s1 = stations[i], stations[i + 1]
        tag = s0.tag  # 영역 경계 링에서는 진행 방향 앞쪽 링(s0)의 영역을 따른다
        ref = _lerp(s0.c, s1.c, 0.5)
        for k in range(sides):
            k1 = k + 1
            idx = (rings[i][k], rings[i + 1][k], rings[i + 1][k1 % sides], rings[i][k1 % sides])
            mat, uv00 = uv_fn(tag, k / sides, s0)
            _, uv10 = uv_fn(tag, k / sides, s1)
            _, uv11 = uv_fn(tag, k1 / sides, s1)
            _, uv01 = uv_fn(tag, k1 / sides, s0)
            m.face(idx, mat, (uv00, uv10, uv11, uv01), part, ref)
    for cap, s, ring, flip in ((cap0, stations[0], rings[0], True), (cap1, stations[-1], rings[-1], False)):
        if not cap:
            continue
        c = m.vert(s.c, weight_fn(s), part)
        mat, uvc = uv_fn(s.tag, 0.5, s)
        for k in range(sides):
            a, b = ring[k], ring[(k + 1) % sides]
            _, ua = uv_fn(s.tag, k / sides, s)
            _, ub = uv_fn(s.tag, (k + 1) / sides, s)
            if flip:
                m.face((c, b, a), mat, (uvc, ub, ua), part, s.c)
            else:
                m.face((c, a, b), mat, (uvc, ua, ub), part, s.c)
    return rings


# ---------------------------------------------------------------- 부위

def _torso(m, lod, shape):
    # (y, rx, rz, z 중심, 그룹, 영역)
    # (y, rx, rz, z 중심, 그룹, 영역, (앞, 뒤)) — 엉덩이는 뒤로, 배·가슴은 앞으로, 어깨는 승모근 경사로 목까지
    rows = [
        (0.840, 0.060, 0.050, -0.005, "pelvis", "shorts", (1.0, 1.0)),
        (0.880, 0.150, 0.100, -0.010, "pelvis", "shorts", (0.88, 1.30)),
        (0.930, 0.172, 0.112, -0.012, "pelvis", "shorts", (0.92, 1.22)),
        (0.970, 0.166, 0.106, -0.010, "pelvis", "shirt", (0.98, 1.08)),
        (1.040, 0.150, 0.100, 0.000, "waist", "shirt", (1.04, 0.95)),
        (1.120, 0.148, 0.102, 0.004, "waist", "shirt", (1.06, 0.95)),
        (1.220, 0.160, 0.110, 0.010, "chest", "shirt", (1.10, 0.96)),
        (1.320, 0.178, 0.116, 0.012, "chest", "shirt", (1.18, 0.97)),
        (1.400, 0.196, 0.114, 0.008, "chest", "shirt", (1.10, 1.00)),
        (1.445, 0.200, 0.104, 0.000, "chest", "shirt", (1.0, 1.02)),
        (1.485, 0.168, 0.092, -0.004, "chest", "shirt", (0.95, 1.05)),
        (1.515, 0.112, 0.078, -0.006, "chest", "shirt", (0.95, 1.05)),
        (1.545, 0.070, 0.064, -0.004, "neck", "shirt", (1.0, 1.0)),
    ]
    st = [Station((0.0, y, zc), rx, rz, g, y, tag, fb) for (y, rx, rz, zc, g, tag, fb) in rows]
    st = _interp(st, LODS[lod]["sub"])
    sides = LODS[lod]["torso"]

    def wf(s):
        y = s.t
        a = _blend(y, 1.08, 0.04)
        b = _blend(y, 1.22, 0.04)
        c = _blend(y, 1.36, 0.04)
        w = {"Hips": (1 - a), "Spine": a * (1 - b), "Chest": b * (1 - c), "UpperChest": c}
        return w

    def uvf(tag, f, s):
        if tag == "shirt":
            return KIT, U.torso_shirt(f, s.t)
        return KIT, U.torso_shorts(f, s.t)

    tube(m, st, sides, ((1.0, 0.0, 0.0), (0.0, 0.0, 1.0)), shape, wf, uvf, "torso")


def _neck(m, lod, shape):
    rows = [(1.47, 0.064, 0.056), (1.55, 0.053, 0.052), (1.64, 0.047, 0.050)]
    st = [Station((0.0, y, 0.0), rx, rz, "neck", y, "skin") for (y, rx, rz) in rows]
    sides = max(6, LODS[lod]["limb"])

    def wf(s):
        y = s.t
        a = _blend(y, 1.50, 0.03)
        b = _blend(y, 1.60, 0.02)
        return {"UpperChest": 1 - a, "Neck": a * (1 - b), "Head": b}

    tube(m, st, sides, ((1.0, 0.0, 0.0), (0.0, 0.0, 1.0)), shape, wf,
         lambda tag, f, s: (BODY, U.skin(f, (s.t - 1.47) / 0.16, 0)), "neck")


def _leg(m, lod, shape, side, name):
    x = side * S.HIP_X
    # 허벅지 앞(대퇴사두), 종아리 뒤(비복근), 무릎·발목은 가늘게
    rows = [
        (0.975, 0.080, 0.085, "thigh", "shorts", (1.0, 1.05)),
        (0.900, 0.082, 0.084, "thigh", "shorts", (1.12, 1.0)),
        (0.800, 0.075, 0.076, "thigh", "shorts", (1.15, 0.98)),
        (0.720, 0.066, 0.066, "thigh", "skin", (1.12, 0.95)),
        (0.600, 0.054, 0.056, "thigh", "skin", (1.05, 0.95)),
        (0.520, 0.047, 0.050, "calf", "skin", (1.0, 1.0)),
        (0.470, 0.049, 0.050, "calf", "sock", (0.95, 1.10)),
        (0.390, 0.053, 0.054, "calf", "sock", (0.88, 1.35)),
        (0.300, 0.046, 0.048, "calf", "sock", (0.90, 1.20)),
        (0.200, 0.036, 0.038, "calf", "sock", (0.95, 1.0)),
        (0.100, 0.033, 0.035, None, "sock", (1.0, 1.0)),
    ]
    st = [Station((x, y, 0.0), rx, rz, g, y, tag, fb) for (y, rx, rz, g, tag, fb) in rows]
    st = _interp(st, LODS[lod]["sub"])
    sides = LODS[lod]["limb"]
    side_name = "Left" if side < 0 else "Right"

    def wf(s):
        y = s.t
        hips = _clamp01((y - 0.88) / 0.09) * 0.6
        knee = _blend(y, 0.50, 0.04)  # 무릎 위 = 1 (UpperLeg), 아래 = 0 (LowerLeg)
        foot = _clamp01((0.14 - y) / 0.05)
        up = knee * (1 - hips)
        lo = (1 - knee) * (1 - foot)
        return {"Hips": hips, side_name + "UpperLeg": up, side_name + "LowerLeg": lo, side_name + "Foot": (1 - knee) * foot}

    def uvf(tag, f, s):
        if tag == "shorts":
            return KIT, U.shorts_leg(side, f, (s.t - 0.72) / (0.975 - 0.72))
        if tag == "sock":
            return KIT, U.sock(side, f, (s.t - 0.10) / (0.47 - 0.10))
        return BODY, U.skin(f, (s.t - 0.47) / 0.25, 1)

    # 면 방향(바깥쪽)은 Blender에 넣을 때 면마다 기준점(축 위 점)으로 맞춘다 (rig.py).
    tube(m, st, sides, ((1.0, 0.0, 0.0), (0.0, 0.0, 1.0)), shape, wf, uvf, name)


def _arm(m, lod, shape, side, name):
    sh0, sh1, el, wr, tip = S._arm(side)
    d = _norm(_sub(el, sh1))
    u = (0.0, 0.0, 1.0)
    w = _norm(_cross(d, u))
    # 어깨(삼각근)는 몸통 안에서 시작해 둥글게, 위팔 앞(이두)·뒤(삼두), 아래팔 위쪽 볼록
    rows = [
        (-0.025, 0.050, 0.052, "upperarm", "sleeve", (1.0, 1.0)),
        (0.030, 0.058, 0.058, "upperarm", "sleeve", (1.05, 1.0)),
        (0.090, 0.054, 0.054, "upperarm", "sleeve", (1.08, 1.05)),
        (0.170, 0.047, 0.047, "upperarm", "skin", (1.15, 1.08)),
        (0.235, 0.043, 0.043, "upperarm", "skin", (1.05, 1.08)),
        (0.290, 0.037, 0.039, "forearm", "skin", (1.0, 1.0)),
        (0.360, 0.042, 0.039, "forearm", "skin", (1.1, 1.0)),
        (0.480, 0.031, 0.027, "forearm", "skin", (1.0, 1.0)),
        (0.550, 0.027, 0.023, None, "skin", (1.0, 1.0)),
    ]
    st = [Station(_add(sh1, _mul(d, s)), rx, rz, g, s, tag, fb) for (s, rx, rz, g, tag, fb) in rows]
    st = _interp(st, LODS[lod]["sub"])
    sides = LODS[lod]["limb"]
    n = "Left" if side < 0 else "Right"

    def wf(s):
        t = s.t
        sh = _clamp01((0.05 - t) / 0.08)
        elb = _blend(t, 0.29, 0.035)
        wri = _blend(t, 0.55, 0.02)
        return {n + "Shoulder": sh * 0.5, n + "UpperArm": (1 - elb) * (1 - sh * 0.5), n + "LowerArm": elb * (1 - wri), n + "Hand": elb * wri}

    def uvf(tag, f, s):
        if tag == "sleeve":
            return KIT, U.sleeve(side, f, (s.t + 0.025) / 0.195)
        return BODY, U.skin(f, s.t / 0.55, 2)

    # 위팔/아래팔을 검사용 부위로 나눈다 (면 중심 기준).
    rings = tube(m, st, sides, (w, u), shape, wf, uvf, name + "_up")
    # 아래팔 부위 이름 붙이기: 팔꿈치 아래 면
    for j in range(len(m.faces) - 1, -1, -1):
        idx, mat, uvs, part, ref = m.faces[j]
        if part != name + "_up":
            break
        s_ref = sum((ref[i] - sh1[i]) * d[i] for i in range(3))
        if s_ref > 0.30:
            m.faces[j] = (idx, mat, uvs, name + "_lo", ref)
    return d, w, u


def _hand(m, lod, side, name, d, w, u):
    _, _, _, wr, tip = S._arm(side)
    rows = [(-0.02, 0.020, 0.030), (0.03, 0.022, 0.044), (0.09, 0.020, 0.046), (0.14, 0.016, 0.040), (0.17, 0.010, 0.026)]
    st = [Station(_add(wr, _mul(d, s)), rx, rz, None, s, "skin") for (s, rx, rz) in rows]
    n = ("Left" if side < 0 else "Right") + "Hand"
    tube(m, st, LODS[lod]["hand"], (w, u), {}, lambda s: {n: 1.0},
         lambda tag, f, s: (BODY, U.skin(f, (s.t + 0.02) / 0.19, 3)), name)


def _foot(m, lod, side, name):
    x = side * S.HIP_X
    rows = [(-0.068, 0.030, 0.035), (-0.050, 0.042, 0.055), (-0.010, 0.050, 0.064), (0.050, 0.051, 0.050),
            (0.120, 0.053, 0.034), (0.180, 0.044, 0.025), (0.212, 0.022, 0.016)]
    st = [Station((x, ry, z), rx, ry, None, z, "boot") for (z, rx, ry) in rows]
    sides = LODS[lod]["foot"]
    n = "Left" if side < 0 else "Right"

    def wf(s):
        t = _clamp01((s.t - 0.10) / 0.06)
        return {n + "Foot": 1 - t, n + "Toes": t}

    # 단면은 XY 평면 타원, 중심 높이 = ry → 바닥 y = 0에 닿는다.
    tube(m, st, sides, ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0)), {}, wf,
         lambda tag, f, s: (KIT, U.boot(f, (s.t + 0.068) / 0.28)), name)


def _g(x, c, w):
    return math.exp(-((x - c) / w) ** 2)


def _sculpt(lat, lon):
    """머리 조형(가상 얼굴, 실존 인물 아님): (반지름 배율, 앞(+Z) 추가 m, 좌우 배율).
    턱선은 좁게, 턱끝·코·눈두덩·광대는 앞으로·옆으로, 눈 자리는 살짝 들어가게, 뒤통수는 볼록하게."""
    lo = ((lon + 180.0) % 360.0) - 180.0           # −180~180, 0 = 정면
    c = math.cos(math.radians(lon))
    front = max(0.0, c)
    radial = 1.0
    side = 1.0
    dz = 0.0
    if lat < 0:
        side *= 1.0 - 0.24 * min(1.0, (-lat) / 70.0) ** 1.4        # 턱으로 갈수록 좁게
    radial += 0.09 * _g(lat, 22.0, 18.0) * max(0.0, -c)           # 뒤통수
    side *= 1.0 + 0.05 * _g(lat, -4.0, 10.0) * _g(abs(lo), 60.0, 18.0)   # 광대
    dz += 0.028 * _g(lo, 0.0, 8.0) * _g(lat, -7.0, 10.0)          # 코
    dz += 0.006 * _g(lo, 0.0, 6.0) * _g(lat, -24.0, 5.0)          # 윗입술
    dz += 0.012 * front ** 6 * _g(lat, -50.0, 10.0)               # 턱끝
    dz += 0.008 * front ** 6 * _g(lat, 17.0, 6.0)                 # 눈두덩
    dz -= 0.006 * _g(lat, 7.0, 6.0) * _g(abs(lo), 24.0, 9.0)      # 눈 자리
    return radial, dz, side


def _ellipsoid_point(lat, lon, grow=0.0, sculpt=True):
    """lat −90(아래)~90(위), lon 0 = 정면(+Z), 90 = 오른쪽(+X). sculpt면 얼굴·두상 조형을 더한다."""
    cl, sl = math.cos(math.radians(lat)), math.sin(math.radians(lat))
    co, so = math.cos(math.radians(lon)), math.sin(math.radians(lon))
    radial, dz, side = _sculpt(lat, lon) if sculpt else (1.0, 0.0, 1.0)
    return (HEAD_C[0] + (HEAD_R[0] * side * radial + grow) * cl * so,
            HEAD_C[1] + (HEAD_R[1] * radial + grow) * sl,
            HEAD_C[2] + (HEAD_R[2] * radial + grow) * cl * co + dz * (1.0 if grow == 0.0 else 0.0))


def _head(m, lod):
    nl, no = LODS[lod]["head_lat"], LODS[lod]["head_lon"]
    lat_min = -70.0  # 턱 아래는 목 안으로
    rows = []
    for i in range(nl + 1):
        lat = lat_min + (90.0 - lat_min) * i / nl
        if i == nl:
            rows.append([m.vert(_ellipsoid_point(90.0, 0.0), {"Head": 1.0}, "head")])
            break
        rows.append([m.vert(_ellipsoid_point(lat, 360.0 * k / no), {"Head": 1.0}, "head") for k in range(no)])
    bottom = m.vert((HEAD_C[0], HEAD_C[1] + HEAD_R[1] * math.sin(math.radians(lat_min)), HEAD_C[2]), {"Head": 1.0}, "head")

    def region(lon0, lon1):
        mid = (lon0 + lon1) / 2
        return abs(((mid + 180) % 360) - 180) <= 90

    for i in range(nl - 1):
        lat0 = lat_min + (90.0 - lat_min) * i / nl
        lat1 = lat_min + (90.0 - lat_min) * (i + 1) / nl
        for k in range(no):
            lon0, lon1 = 360.0 * k / no, 360.0 * (k + 1) / no
            front = region(lon0, lon1)
            idx = (rows[i][k], rows[i + 1][k], rows[i + 1][(k + 1) % no], rows[i][(k + 1) % no])
            pts = ((lat0, lon0), (lat1, lon0), (lat1, lon1), (lat0, lon1))
            uvs = tuple(_head_uv(front, la, lo, lat_min) for la, lo in pts)
            m.face(idx, BODY, uvs, "head", HEAD_C)
    top = rows[nl][0]
    lat0 = lat_min + (90.0 - lat_min) * (nl - 1) / nl
    for k in range(no):
        lon0, lon1 = 360.0 * k / no, 360.0 * (k + 1) / no
        front = region(lon0, lon1)
        m.face((rows[nl - 1][k], top, rows[nl - 1][(k + 1) % no]), BODY,
               (_head_uv(front, lat0, lon0, lat_min), _head_uv(front, 90.0, (lon0 + lon1) / 2, lat_min),
                _head_uv(front, lat0, lon1, lat_min)), "head", HEAD_C)
    for k in range(no):
        lon0, lon1 = 360.0 * k / no, 360.0 * (k + 1) / no
        front = region(lon0, lon1)
        m.face((bottom, rows[0][k], rows[0][(k + 1) % no]), BODY,
               (_head_uv(front, -90.0, (lon0 + lon1) / 2, lat_min), _head_uv(front, lat_min, lon0, lat_min),
                _head_uv(front, lat_min, lon1, lat_min)), "head", HEAD_C)


def _head_uv(front, lat, lon, lat_min):
    if front:
        p = _ellipsoid_point(max(lat, -89.0), lon)
        return U.face(p[0], p[1])
    lo = lon % 360.0
    return U.skin(lo / 360.0, (lat - lat_min) / (90 - lat_min), 4)


def hairline(lon):
    """헤어 캡 아래 경계 위도: 이마 28°, 옆(귀 위) 14°, 귀 뒤까지 14°를 유지한 뒤 목덜미 −30°."""
    c = math.cos(math.radians(lon))
    if c >= 0:
        return 14.0 + 14.0 * c
    if c >= -0.45:
        return 14.0
    return 14.0 - 44.0 * (-c - 0.45) / 0.55


def hair_offset(style, lat, lon, t):
    """헤어 스타일별 바깥 방향 추가 두께(m)와 아래쪽 늘림(m). t = 경계(0) → 정수리(1)."""
    c = math.cos(math.radians(lon))
    lo = ((lon + 180.0) % 360.0) - 180.0
    back = max(0.0, -c)
    if style == "Hair_Short":                                   # 짧은 크롭
        return 0.012 * (0.3 + 0.7 * t), 0.0
    if style == "Hair_Curly":                                   # 곱슬 윗머리
        bump = 0.006 * math.sin(math.radians(lon) * 7) * math.sin(math.radians(lat) * 5)
        return 0.030 * (0.4 + 0.6 * t) + bump, 0.0
    if style == "Hair_Long":                                    # 어깨 위 장발 (귀 뒤부터 내려옴)
        nape = max(0.0, (-c - 0.5) / 0.5)
        return 0.014 * (0.3 + 0.7 * t) + 0.012 * back, 0.06 * nape * (1 - t) ** 2
    if style == "Hair_Bun":                                     # 뒤로 묶은 번
        d = math.hypot((lat - 45.0) / 25.0, (((lon - 180.0 + 180) % 360) - 180) / 40.0)
        return 0.008 + 0.05 * max(0.0, 1 - d) ** 2, 0.0
    if style == "Hair_Fade":                                    # 옆·뒤 짧고 윗머리 볼륨
        r = min(1.0, max(0.0, (t - 0.35) / 0.35))
        return 0.001 + 0.020 * r * r * (3 - 2 * r), 0.0
    if style == "Hair_Afro":                                    # 둥근 아프로
        bump = 0.004 * math.sin(math.radians(lon) * 11) * math.sin(math.radians(lat) * 9)
        return 0.030 + 0.040 * t + bump, 0.0
    if style == "Hair_Mohawk":                                  # 가운데 줄만 세움
        strip = _g(abs(lo), 0.0, 14.0) + _g(abs(lo), 180.0, 14.0)
        return -0.004 + 0.045 * min(1.0, strip) * t, 0.0
    if style == "Hair_Dreads":                                  # 드레드(덩어리 + 아래로)
        bump = 0.007 * abs(math.sin(math.radians(lon) * 9))
        nape = max(0.0, (-c - 0.45) / 0.55)
        return 0.018 + bump + 0.010 * back, 0.08 * nape * (1 - t) ** 2
    if style == "Hair_Bald":                                    # 삭발: 캡이 두피 안으로 숨는다
        return -HAIR_GAP - 0.004, 0.0
    return 0.0, 0.0


def _hair(m, lod, style):
    rows_n, no = LODS[lod]["hair_rows"], LODS[lod]["head_lon"]
    rows = []
    for i in range(rows_n + 1):
        t = i / rows_n
        row = []
        for k in range(no):
            lon = 360.0 * k / no
            b = hairline(lon)
            lat = b + (90.0 - b) * t if i < rows_n else 90.0
            grow, drop = hair_offset(style, lat, lon, t) if style else (0.0, 0.0)
            # 경계에서 두피에 붙게: 두께·간격을 경계 쪽으로 줄인다(모자 챙처럼 뜨지 않게). 늘어뜨린 뒷머리는 유지
            c = math.cos(math.radians(lon))
            keep = max(0.0, (-c - 0.5) / 0.5) if style in ("Hair_Long", "Hair_Dreads") else 0.0
            ramp = t / 0.3 if t < 0.3 else 1.0
            ramp = ramp * ramp * (3 - 2 * ramp)
            taper = keep + (1.0 - keep) * ramp
            gap = 0.0012 + (HAIR_GAP - 0.0012) * taper
            p = _ellipsoid_point(lat, lon, gap + grow * taper if grow > 0 else gap + grow)
            p = (p[0], p[1] - drop, p[2])
            row.append(p)
        rows.append(row)
    return rows


def _hair_mesh(m, lod, rows_pts):
    rows_n, no = len(rows_pts) - 1, len(rows_pts[0])
    idx = [[m.vert(p, {"Head": 1.0}, "hair") for p in row] for row in rows_pts[:-1]]
    top = m.vert(rows_pts[-1][0], {"Head": 1.0}, "hair")
    for i in range(rows_n - 1):
        for k in range(no):
            k1 = (k + 1) % no
            q = (idx[i][k], idx[i + 1][k], idx[i + 1][k1], idx[i][k1])
            uvs = (U.hair(k / no, i / rows_n), U.hair(k / no, (i + 1) / rows_n),
                   U.hair((k + 1) / no, (i + 1) / rows_n), U.hair((k + 1) / no, i / rows_n))
            m.face(q, BODY, uvs, "hair", HEAD_C)
    for k in range(no):
        k1 = (k + 1) % no
        m.face((idx[rows_n - 1][k], top, idx[rows_n - 1][k1]), BODY,
               (U.hair(k / no, (rows_n - 1) / rows_n), U.hair((k + 0.5) / no, 1.0), U.hair((k + 1) / no, (rows_n - 1) / rows_n)),
               "hair", HEAD_C)


def _ear(m, lod, side, name):
    """귀: 머리 옆 납작한 닫힌 튜브(위아래로 긴 단면). LOD2는 생략."""
    if lod >= 2:
        return
    sides = 8 if lod == 0 else 5
    c = _ellipsoid_point(-4.0, side * 90.0)
    x0 = c[0] + side * 0.006
    rows = [(-0.026, 0.005, 0.009), (-0.017, 0.008, 0.014), (0.000, 0.009, 0.018), (0.019, 0.008, 0.016), (0.030, 0.005, 0.009)]
    st = [Station((x0 + side * 0.004 * (dy / 0.03), c[1] + dy, c[2] - 0.010 - 0.12 * dy), rx, rz, None, dy, "skin") for (dy, rx, rz) in rows]
    tube(m, st, sides, ((1.0, 0.0, 0.0), (0.0, 0.0, 1.0)), {}, lambda s_: {"Head": 1.0},
         lambda tag, f, s_: (BODY, U.skin(f, (s_.t + 0.03) / 0.064, 4)), name)


def build(lod, shape_name=None, hair_style=None):
    """LOD 메시 하나. shape_name ∈ SHAPES 또는 None(기본), hair_style ∈ HAIR_STYLES 또는 None(삭발 캡)."""
    shape = SHAPES.get(shape_name, {}) if shape_name else {}
    m = Mesh()
    _torso(m, lod, shape)
    _neck(m, lod, shape)
    _head(m, lod)
    _hair_mesh(m, lod, _hair(m, lod, hair_style))
    _ear(m, lod, -1, "ear_L")
    _ear(m, lod, 1, "ear_R")
    for side, n in ((-1, "L"), (1, "R")):
        _leg(m, lod, shape, side, "leg_" + n)
        _foot(m, lod, side, "foot_" + n)
        d, w, u = _arm(m, lod, shape, side, "arm_" + n)
        _hand(m, lod, side, "hand_" + n, d, w, u)
    return m


def shape_keys(lod):
    """{키 이름: 정점 배열} — 기본 메시와 토폴로지 동일."""
    out = {}
    for name in SHAPES:
        out[name] = build(lod, shape_name=name).verts
    for style in HAIR_STYLES:
        out[style] = build(lod, hair_style=style).verts
    return out


# 자기 관통 검사 대상 (AC-02): 서로 붙어 있지 않은 부위 쌍.
CHECK_PAIRS = [
    ("leg_L", "leg_R"), ("foot_L", "foot_R"), ("foot_L", "leg_R"), ("foot_R", "leg_L"),
    ("arm_L_lo", "leg_L"), ("arm_R_lo", "leg_R"), ("hand_L", "leg_L"), ("hand_R", "leg_R"),
    ("hand_L", "torso"), ("hand_R", "torso"), ("arm_L_lo", "torso"), ("arm_R_lo", "torso"),
    ("arm_L_up", "head"), ("arm_R_up", "head"), ("hair", "torso"),
]
