"""달리기·킥 형태 지표 (specs/055 FR-70). FK만 쓴다(mathutils, bpy 없음) — 클립 sample(t) → 포즈 → 관절 위치.

좌표: 선수 좌표(X 오른쪽, Y 위, Z 정면). 진행 방향 d = (sin dir, 0, cos dir). 시상면 = (위, d)가 펼치는 면.
정의 (기준 범위는 Data/anim/055-gait-standards.json):
- cadence 걸음/초 = 2 / 주기, stepLength = 속도 / cadence, duty = 태그 접지 구간 / 주기, contactTime = duty × 주기,
  flightTime = 걸음당 두 발이 모두 떠 있는 시간 = max(0, 주기/2 − 접지 시간)
- pelvisBob = Hips 머리 높이 피크-피크. pelvisMinInStance = 걸음 반주기 안 골반 최저점의 접지 구간 내 비율(0 접지 시작 ~ 1 접지 끝),
  pelvisMaxInFlight = 최고점이 공중 구간 안인지
- 무릎 굴곡 = 180 − (허벅지·정강이 사이 각). 엉덩이 굴곡 = 시상면에서 몸통 아래 방향(−몸통선) 대비 허벅지 앞 회전 각
- landingDist = 접지 시작 순간 발목이 골반(Hips 머리)보다 진행 방향 앞에 있는 거리
- swingFootClear = 스윙 중 발목이 엉덩이 관절 아래를 지날 때 지면에서 발목까지 높이 (FR-13 '발 높이').
  허벅지·정강이 길이가 비슷하면 그 순간 무릎 굴곡 ≈ 2 × 허벅지 각이라 높이는 허벅지 각만으로 정해진다 — 발목 들림량(− 레스트 발목)으로 재면
  jog 0.15 m에 엉덩이 굴곡 52°가 필요해 FR-12 jog 상한 50°와 충돌한다
- kneeLeadsFoot = 스윙 중 무릎의 진행 방향 앞 이동(골반 기준) 피크가 발목 피크보다 먼저
- trunkLean = 골반→목 선의 시상면 전방 경사(주기 평균). neckExtMax = 머리 축(Head 머리→꼬리)이 몸통선 대비 뒤로 젖혀진 각의 최대
  (레스트 자세 차이 뺌). headForwardMax = 머리(Head 머리)가 양 어깨 중점보다 진행 방향 앞에 있는 거리 − 레스트 값, 최대
- pelvisYaw = 엉덩이 관절 선 요 진폭(±, (최대−최소)/2), thoraxPelvisRel = 어깨 선 요 − 골반 요의 절댓값 최대, thoraxAnti = 두 요의 상관 < 0
- pelvisObliquity = 엉덩이 관절 선 정면 기울기 진폭, trunkRoll = 몸통선 정면 기울기 진폭
- 팔꿈치 굴곡 = 180 − (위팔·아래팔 사이 각): mean·min·max·range. shoulderRom = 시상면 위팔 각(몸통선 기준) 최대 − 최소.
  armAbduction = 정면 면 위팔 각(몸통 바깥) 평균. handMidline = 손이 몸 가운데 선(골반·목 중점의 옆 위치)을 넘지 않음
- ankleAccelMax = 월드 발목 **수평** 가속도 최대 (2차 차분, 120 샘플/주기), ankleAccelNorm = 그 값 / (속도 × 걸음 빈도) — 스윙 연속성.
  매끈한 스윙(속도 1−cos 곡선)의 수평 가속 피크는 정규화 약 3이다 (sprint: 보폭 1.9 m, 스윙 0.38 s → 약 83 m/s²)
- hipFlexRom = 엉덩이 굴곡(몸 기준) 가동 범위, 두 다리 중 작은 쪽. 10° 미만(순수 옆걸음)이면 팔-다리 위상은 적용 불가(FR-44)
- armLegPhaseErr = |오른 다리 엉덩이 굴곡 피크 위상 − 왼 어깨 굴곡 피크 위상| (원형 차, 주기 비율)
"""
import math

from mathutils import Vector

from . import pose as PO
from . import skeleton as S

UP = Vector((0.0, 1.0, 0.0))
FWD = Vector((0.0, 0.0, 1.0))  # 몸 정면 — 엉덩이·어깨 굴곡과 팔-다리 위상은 진행 방향이 아니라 몸 기준(옆·뒤 클립도 같은 정의)
SAMPLES = 120


def _dir(deg):
    r = math.radians(deg)
    return Vector((math.sin(r), 0.0, math.cos(r)))


def _sag_angle(v, d, ref_down):
    """시상면 투영 각: ref_down(아래 방향 기준) → v, 진행 방향 d 쪽 회전이 +."""
    def proj(x):
        return Vector((x.dot(d), x.dot(UP)))
    a, b = proj(v), proj(ref_down)
    if a.length < 1e-9 or b.length < 1e-9:
        return 0.0
    ang = math.atan2(a.x, -a.y) - math.atan2(b.x, -b.y)
    return math.degrees((ang + math.pi) % (2 * math.pi) - math.pi)


def _angle(a, b):
    if a.length < 1e-9 or b.length < 1e-9:
        return 0.0
    c = max(-1.0, min(1.0, a.normalized().dot(b.normalized())))
    return math.degrees(math.acos(c))


def flexion(p0, p1, p2):
    """세 점 관절 굴곡 = 180 − 사이각 = 두 분절 방향 차."""
    return _angle(p1 - p0, p2 - p1)


def _yaw(a, b):
    """a → b 선분의 요(위에서 본 각, °). 선분 기준은 +X."""
    v = b - a
    return math.degrees(math.atan2(-v.z, v.x))


def _frontal_tilt(a, b, side):
    """a → b 선분의 옆 기울기(°)."""
    v = b - a
    return math.degrees(math.atan2(v.y, math.hypot(v.x, v.z)))


def trunk_lean(h, d):
    """골반→목 선의 진행 방향 전방 경사(°, 앞 +)."""
    tr = h["Neck"] - h["Hips"]
    return math.degrees(math.atan2(tr.dot(d), tr.dot(UP)))


def _amp(xs):
    return (max(xs) - min(xs)) / 2.0


def _circ(a, b):
    d = (a - b) % 1.0
    return min(d, 1.0 - d)


def frames(clip, n=SAMPLES):
    """주기 0~1을 n등분한 포즈의 (rw, head, tail) 목록."""
    T = clip.frames / 30.0
    return [PO.fk(clip.sample(T * i / n)) for i in range(n)]


def _rest():
    from . import pose as P
    return P.fk(P.Pose())


def locomotion(clip, n=SAMPLES):
    """로코모션 루프 클립 → 지표 dict."""
    tg = clip.tags
    v, dd = tg["speed"], tg["dir"]
    d = _dir(dd)
    T = clip.frames / 30.0
    fr = frames(clip, n)
    H = [h for _, h, _ in fr]
    m = {"id": clip.id, "gait": tg.get("gait"), "dir": dd, "speed": v, "frames": clip.frames}
    cad = 2.0 / T
    m["cadence"] = cad
    m["stepLength"] = v / cad if cad > 0 else 0.0
    s0, s1 = tg["stance"]["Left"]
    duty = (s1 - s0) % 1.0 or (1.0 if s1 != s0 else 0.0)
    m["duty"] = duty
    m["contactTime"] = duty * T
    m["flightTime"] = max(0.0, T / 2.0 - duty * T)

    hy = [h["Hips"].y for h in H]
    m["pelvisBob"] = max(hy) - min(hy)
    m["pelvisMean"] = sum(hy) / len(hy)
    # 반주기마다(왼발 접지 시작 s0 → s0 + 0.5) 최저점 위치
    mins, maxin = [], True
    for k in range(2):
        st = (s0 + 0.5 * k) % 1.0
        idx = [int(round(((st + 0.5 * j / (n // 2)) % 1.0) * n)) % n for j in range(n // 2)]
        ys = [hy[i] for i in idx]
        jmin = min(range(len(ys)), key=lambda j: ys[j])
        jmax = max(range(len(ys)), key=lambda j: ys[j])
        u = (jmin / (n // 2)) * 0.5  # 위상
        mins.append(u / duty if duty > 0 else 0.0)
        um = (jmax / (n // 2)) * 0.5
        if duty < 0.5 and not (duty - 1e-6 <= um <= 0.5 + 1e-6):
            maxin = False
    m["pelvisMinInStance"] = mins
    m["pelvisMaxInFlight"] = maxin if duty < 0.5 else None

    rest_rw, rest_h, rest_t = _rest()
    legs = {}
    for side, n_ in (("L", "Left"), ("R", "Right")):
        a0, a1 = tg["stance"][n_]
        span = (a1 - a0) % 1.0
        knee = [flexion(h[n_ + "UpperLeg"], h[n_ + "LowerLeg"], h[n_ + "Foot"]) for h in H]
        trunk = [h["Neck"] - h["Hips"] for h in H]
        hipf = [_sag_angle(h[n_ + "LowerLeg"] - h[n_ + "UpperLeg"], FWD, -tr) for h, tr in zip(H, trunk)]
        i_td = int(round(a0 * n)) % n
        i_to = int(round(((a0 + span) % 1.0) * n)) % n
        stance_idx = [int(round(((a0 + span * j / 40) % 1.0) * n)) % n for j in range(41)]
        swing_idx = [int(round(((a0 + span + (1 - span) * j / 60) % 1.0) * n)) % n for j in range(61)]
        land = (H[i_td][n_ + "Foot"] - H[i_td]["Hips"]).dot(d)
        # 스윙: 발목이 엉덩이 아래를 지나는 순간
        clear = None
        rel = [(H[i][n_ + "Foot"] - H[i][n_ + "UpperLeg"]).dot(d) for i in swing_idx]
        for j in range(1, len(rel)):
            if rel[j - 1] < 0 <= rel[j]:
                i = swing_idx[j]
                clear = H[i][n_ + "Foot"].y  # 발 높이 = 지면에서 발목까지 (FR-13)
                break
        kf = [(H[i][n_ + "LowerLeg"] - H[i]["Hips"]).dot(d) for i in swing_idx]
        af = [(H[i][n_ + "Foot"] - H[i]["Hips"]).dot(d) for i in swing_idx]
        jk = max(range(len(kf)), key=lambda j: kf[j])
        ja = max(range(len(af)), key=lambda j: af[j])
        legs[side] = {
            "landingDist": land,
            "kneeTouchdown": knee[i_td],
            "kneeStanceMax": max(knee[i] for i in stance_idx),
            "kneeToeOff": knee[i_to],
            "kneeSwingMax": max(knee[i] for i in swing_idx),
            "hipSwingMax": max(hipf[i] for i in swing_idx),
            "swingFootClear": clear,
            "kneeLeadsFoot": jk < ja,
            "hipFlex": hipf,
        }
    for k in ("landingDist", "kneeTouchdown", "kneeStanceMax", "kneeToeOff", "kneeSwingMax", "hipSwingMax"):
        m[k] = max(legs["L"][k], legs["R"][k]) if k not in ("kneeTouchdown", "kneeToeOff") else min(legs["L"][k], legs["R"][k])
    m["kneeTouchdownMax"] = max(legs["L"]["kneeTouchdown"], legs["R"]["kneeTouchdown"])
    m["kneeToeOffMax"] = max(legs["L"]["kneeToeOff"], legs["R"]["kneeToeOff"])
    cl = [legs[s]["swingFootClear"] for s in ("L", "R") if legs[s]["swingFootClear"] is not None]
    m["swingFootClear"] = min(cl) if cl else None
    m["kneeLeadsFoot"] = legs["L"]["kneeLeadsFoot"] and legs["R"]["kneeLeadsFoot"]
    # 스윙 연속성: 월드 발목 가속도 최대(루트가 속도 v로 움직인다고 보고 2차 차분). 접지↔스윙 경계 꺾임을 잡는다
    dt = T / n
    acc = 0.0
    for n_ in ("Left", "Right"):
        P = [H[i][n_ + "Foot"] + d * v * (T * i / n) for i in range(n)]
        P.append(H[0][n_ + "Foot"] + d * v * T)
        P.append(H[1][n_ + "Foot"] + d * v * (T + dt))
        for i in range(1, n + 1):
            a = (P[i + 1] - 2 * P[i] + P[i - 1]) / (dt * dt)
            acc = max(acc, math.hypot(a.x, a.z))  # 수평만 — 착지 순간 수직 속도 정지(충격)는 물리적으로 정상
    m["ankleAccelMax"] = acc
    m["ankleAccelNorm"] = acc / (max(v, 0.1) * cad)
    m["tdEntrySlide"] = td_entry_slide(clip)

    # 몸통·머리
    leans = [trunk_lean(h, d) for h in H]
    m["trunkLean"] = sum(leans) / len(leans)
    m["trunkLeanRange"] = [min(leans), max(leans)]

    def neck_ext(h, t):
        head_axis = t["Head"] - h["Head"]
        tr = h["Neck"] - h["Hips"]
        return _sag_angle(-head_axis, d, -tr)  # 머리가 몸통보다 뒤로 젖혀지면 + (머리 축 아래 방향이 앞으로 돈다)
    rest_ne = neck_ext(rest_h, rest_t)
    m["neckExtMax"] = max(neck_ext(h, t) - rest_ne for (_, h, t) in fr)

    def head_fwd(h):
        sh = (h["LeftUpperArm"] + h["RightUpperArm"]) * 0.5
        return (h["Head"] - sh).dot(d)
    rest_hf = (rest_h["Head"] - (rest_h["LeftUpperArm"] + rest_h["RightUpperArm"]) * 0.5).z
    m["headForwardMax"] = max(head_fwd(h) for h in H) - rest_hf

    py = [_yaw(h["LeftUpperLeg"], h["RightUpperLeg"]) for h in H]
    ty = [_yaw(h["LeftUpperArm"], h["RightUpperArm"]) for h in H]
    m["pelvisYaw"] = _amp(py)
    rel = [t - p for t, p in zip(ty, py)]
    m["thoraxPelvisRel"] = max(abs(x) for x in rel)
    mp, mt = sum(py) / n, sum(ty) / n
    cov = sum((a - mp) * (b - mt) for a, b in zip(py, ty))
    m["thoraxAnti"] = cov < 0
    m["pelvisObliquity"] = _amp([_frontal_tilt(h["LeftUpperLeg"], h["RightUpperLeg"], 1) for h in H])
    side_v = UP.cross(d)  # 진행 방향 오른쪽... d=+Z → side = +X
    rolls = []
    for h in H:
        tr = h["Neck"] - h["Hips"]
        rolls.append(math.degrees(math.atan2(tr.dot(side_v), tr.dot(UP))))
    m["trunkRoll"] = _amp(rolls)

    # 팔
    arms = {}
    for side, n_, sgn in (("L", "Left", -1), ("R", "Right", 1)):
        el = [flexion(h[n_ + "UpperArm"], h[n_ + "LowerArm"], h[n_ + "Hand"]) for h in H]
        sh = [_sag_angle(h[n_ + "LowerArm"] - h[n_ + "UpperArm"], FWD, -(h["Neck"] - h["Hips"])) for h in H]
        ab = []
        mid_ok = True
        for h in H:
            ua = h[n_ + "LowerArm"] - h[n_ + "UpperArm"]
            body_side = UP.cross(d) * sgn  # 바깥 방향(d 기준)
            # 몸 정면 기준 바깥: 선수 X축 (정면 클립이 아니어도 몸 기준으로 잰다)
            out = Vector((sgn, 0.0, 0.0))
            tr = (h["Neck"] - h["Hips"]).normalized()
            ab.append(math.degrees(math.atan2(ua.dot(out), -ua.dot(tr))))
            centre = (h["Hips"] + h["Neck"]) * 0.5
            if (h[n_ + "Hand"] - centre).x * sgn < 0:
                mid_ok = False
        jmax = max(range(n), key=lambda i: sh[i])
        arms[side] = {"elbow": el, "rom": max(sh) - min(sh), "abd": sum(ab) / n, "mid": mid_ok, "peak": jmax / n,
                      "handFront": H[jmax][n_ + "Hand"].y, "handBack": (H[min(range(n), key=lambda i: sh[i])][n_ + "Hand"] - H[min(range(n), key=lambda i: sh[i])]["Hips"]).dot(d)}
    els = arms["L"]["elbow"] + arms["R"]["elbow"]
    m["elbowMean"] = sum(els) / len(els)
    m["elbowMin"] = min(min(arms[s]["elbow"]) for s in ("L", "R"))
    m["elbowMax"] = max(max(arms[s]["elbow"]) for s in ("L", "R"))
    m["elbowRange"] = min(max(arms[s]["elbow"]) - min(arms[s]["elbow"]) for s in ("L", "R"))
    m["shoulderRom"] = min(arms["L"]["rom"], arms["R"]["rom"])
    m["shoulderRomMax"] = max(arms["L"]["rom"], arms["R"]["rom"])
    m["armAbduction"] = (arms["L"]["abd"] + arms["R"]["abd"]) / 2.0
    m["handMidline"] = arms["L"]["mid"] and arms["R"]["mid"]
    m["handBackBehindHip"] = max(arms["L"]["handBack"], arms["R"]["handBack"])
    hr = legs["R"]["hipFlex"]
    jr = max(range(n), key=lambda i: hr[i])
    hl = legs["L"]["hipFlex"]
    jl = max(range(n), key=lambda i: hl[i])
    m["hipFlexRom"] = min(max(hr) - min(hr), max(hl) - min(hl))
    m["armLegPhaseErr"] = max(_circ(jr / n, arms["L"]["peak"]), _circ(jl / n, arms["R"]["peak"]))
    return m


def judge(m, std, items=None):
    """지표 dict + 기준 dict → {항목: (값, 범위, 통과)}. 기준 null·없음은 건너뜀."""
    out = {}
    for k, rng in std.items():
        if items is not None and k not in items:
            continue
        if k in ("speed",) or rng is None or k not in m:
            continue
        val = m[k]
        if isinstance(rng, bool):
            if rng:
                out[k] = (val, rng, bool(val))
            continue
        lo, hi = rng
        vals = val if isinstance(val, list) else [val]
        if any(x is None for x in vals):
            out[k] = (val, rng, False)
            continue
        out[k] = (val, rng, all(lo - 1e-6 <= x <= hi + 1e-6 for x in vals))
    return out


def failures(j):
    return {k: (v, r) for k, (v, r, ok) in j.items() if not ok}


def football(clip):
    """출발·감속 클립 (FR-40·41): 걸음별 몸통 경사, 첫 걸음 보폭 비율, 어깨 가동, 골반 하강, 뒤꿈치 먼저."""
    from .clips import gait as GT
    tg = clip.tags
    d = FWD
    fr = [PO.fk(clip.sample(i / 30.0)) for i in range(clip.frames + 1)]
    H = [h for _, h, _ in fr]
    starts = list(tg["stepFrames"]) + [clip.frames if tg["football"] == "start" else tg["stepFrames"][-1] + (tg["stepFrames"][-1] - tg["stepFrames"][-2])]
    leans = []
    for a, b in zip(starts, starts[1:]):
        idx = [i for i in range(len(H)) if a <= i < b] or [int(a)]
        leans.append(sum(trunk_lean(H[i], d) for i in idx) / len(idx))
    sp = GT.GAITS["sprint"]
    top_step = sp["v"] / (60.0 / sp["frames"])
    sh = []
    for h in H:
        tr = h["Neck"] - h["Hips"]
        sh.append(_sag_angle(h["LeftLowerArm"] - h["LeftUpperArm"], FWD, -tr))
    hy = [h["Hips"].y for h in H]
    heel = True
    for i, a in enumerate(tg["stepFrames"]):
        n = "Left" if i % 2 == 0 else "Right"
        h = H[int(round(a))]
        if not h[n + "Toes"].y > h[n + "Foot"].y - (S.ANKLE_Y - 0.025) + 0.01:  # 볼(발가락 뿌리)이 평평할 때보다 1 cm 넘게 높다 = 발끝 듦
            heel = False
    return {"id": clip.id, "football": tg["football"], "stepLeans": leans, "firstStepRatio": tg["stepLengths"][0] / top_step,
            "shoulderRom": max(sh) - min(sh), "trunkLeanMin": min(leans), "pelvisDrop": hy[0] - min(hy), "heelFirst": heel,
            "speedCurve": tg["speedCurve"], "frames": clip.frames}


KICK_GROUND_MAX = 0.3   # 공 높이 하한이 이보다 낮으면 땅볼 킥 — 디딤발·무릎 기준(Hay 1993 등 땅볼 실험) 적용 대상
FOOT_HALF_W = 0.045
BALL_R = 0.11


def _grounded(h, n):
    return h[n + "Toes"].y <= 0.035 or h[n + "Foot"].y <= S.ANKLE_Y + 0.012


def kick(clip, hz=120):
    """킥 클립 → FR-60·61 지표. 정의는 Data/anim/055-gait-standards.json kicks 주석."""
    from .clips import base as CB
    tg = clip.tags
    con = tg["contact"]
    c = con["frame"]
    kn = "Right" if con["part"] == "RightFoot" else "Left"
    pn = "Left" if kn == "Right" else "Right"
    T = clip.frames / 30.0
    N = int(round(T * hz))
    fr = [PO.fk(clip.sample(T * i / N)) for i in range(N + 1)]
    H = [h for _, h, _ in fr]
    ic = int(round(c / clip.frames * N))
    hc = H[ic]
    ball = CB.contact_point(con["rule"], hc)
    f = CB.facing(hc)
    side = UP.cross(f)
    pc = (hc[pn + "Foot"] + fr[ic][2][pn + "Toes"]) * 0.5
    rel = ball - pc
    m = {"id": clip.id, "kind": tg.get("kind"), "kickStyle": tg.get("kickStyle"), "backswingClass": tg.get("backswing", "big"),
         "ground": tg.get("ballHeight", [0, 0.3])[0] < KICK_GROUND_MAX}
    m["plantBehind"] = rel.dot(f)
    m["plantSide"] = abs(rel.dot(side)) - BALL_R - FOOT_HALF_W
    m["kneeContactKick"] = flexion(hc[kn + "UpperLeg"], hc[kn + "LowerLeg"], hc[kn + "Foot"])
    m["kneeContactPlant"] = flexion(hc[pn + "UpperLeg"], hc[pn + "LowerLeg"], hc[pn + "Foot"])

    def hipf(h):
        return _sag_angle(h[kn + "LowerLeg"] - h[kn + "UpperLeg"], FWD, -(h["Neck"] - h["Hips"]))
    hf = [hipf(h) for h in H]
    # 차는 발 이지: 접촉 전 마지막으로 '바닥에 붙어 멈춰 있던' 순간 (접촉 순간 차는 발도 땅볼 높이라 높이만으로는 못 가른다)
    ru = tg.get("runup")
    pre_i = int(round(tg.get("runupFrames", 0) / clip.frames * N))
    rv = _dir(ru["dir"]) * ru["speed"] if ru else Vector((0.0, 0.0, 0.0))

    def planted(i, n):
        """바닥에 붙어 (월드에서) 멈춘 발. 도움닫기 구간은 루트가 runup 속도로 움직인다고 보고 보정한다(제자리 클립)."""
        if not _grounded(H[i], n) or i == 0:
            return _grounded(H[i], n) and i == 0
        dv = H[i][n + "Toes"] - H[i - 1][n + "Toes"]
        if i <= pre_i:
            dv = dv + rv * (T / N)
        return dv.length * hz < 0.4
    ito = 0
    for i in range(ic - 1, -1, -1):
        if planted(i, kn):
            ito = i
            break
    m["hipExtMax"] = -min(hf[ito:ic + 1]) if ic > ito else -hf[ic]
    # 엉덩이 굴곡 각속도 피크: 백스윙 최대(이지 → 접촉 중 엉덩이 굴곡 최소) → 접촉 구간의 비율
    ib = min(range(ito, ic + 1), key=lambda i: hf[i]) if ic > ito else ic
    vel = [(hf[i + 1] - hf[i - 1]) for i in range(max(1, ib), ic)]
    if vel:
        j = max(range(len(vel)), key=lambda k: vel[k])
        m["hipFlexVelPeak"] = (max(1, ib) + j - ib) / max(1, ic - ib)
    else:
        m["hipFlexVelPeak"] = 0.0
    m["followThrough"] = max(hf[ic:]) - hf[ic]
    # 참고(FR-60 발 속도, AC 대상 아님): 접촉 직전 차는 발(볼) 속도 m/s — 클립 1배속 기준. Unity는 접촉 시각 정렬로 더 빨리 재생할 수 있다
    m["footSpeedContact"] = (H[ic][kn + "Toes"] - H[max(0, ic - 2)][kn + "Toes"]).length * hz / 2.0
    # 도움닫기 걸음 수: 접촉 전 발이 새로 닿은 횟수(시작 때 닿아 있던 것은 세지 않는다)
    steps, touch = 0, []
    gap = int(hz / 30)  # 1프레임보다 짧게 떴다 다시 닿는 것(착지 직후 발 자리 잡기)은 착지로 세지 않는다
    for n in ("Left", "Right"):
        st = [planted(i, n) for i in range(ic + 1)]
        i = 1
        while i <= ic:
            if st[i] and not st[i - 1]:
                j = i - 1
                while j >= 0 and not st[j]:
                    j -= 1
                if j < 0 and i > 0 and not st[0] or (i - 1 - j) >= gap:
                    steps += 1
                    touch.append((i, n))
            i += 1
    m["runupSteps"] = steps
    m["approachAngle"] = abs(ru["dir"]) if ru else 0.0
    if ru and len(touch) >= 3:
        # 루트가 도움닫기 방향·속도로 움직인다고 보고 발 착지 점(월드)의 진행 방향으로 잰다. 왼·오른발 옆 간격이 섞이지 않게
        # 이웃 착지 두 점의 중점끼리 잇는다(착지 3번 이상일 때만 — 2번이면 태그 각)
        d = _dir(ru["dir"])
        touch.sort()
        pts = [H[i][n + "Foot"] + d * ru["speed"] * (min(i, pre_i) * T / N) for i, n in touch]
        mids = [(a + b) * 0.5 for a, b in zip(pts, pts[1:])]
        v = mids[-1] - mids[0]
        m["approachAngle"] = abs(math.degrees(math.atan2(v.x, v.z)))
    return m


def round_floats(x, nd=5):
    """JSON 출력용: 실수 6자리 미만 반올림(결정론 바이트 비교), 내부 목록(hipFlex)은 뺀다."""
    if isinstance(x, dict):
        return {k: round_floats(v, nd) for k, v in x.items() if k != "hipFlex"}
    if isinstance(x, (list, tuple)):
        return [round_floats(v, nd) for v in x]
    if isinstance(x, float):
        return round(x, nd)
    return x


def td_entry_slide(clip, hz=240):
    """착지 진입 미끄러짐 (043 AC-09 Unity 측정 재현): 30 fps 키 사이를 본 로컬 회전 slerp로 보간한 포즈에서, 착지 직전 볼이
    바닥 3 cm 아래로 들어온 순간부터 착지 점까지 월드 수평 거리의 최대(m). Unity는 '발끝 ≤ 3 cm'부터 미끄러짐을 잰다."""
    from mathutils import Vector
    tg = clip.tags
    v, d = tg["speed"], _dir(tg["dir"])
    keys = []
    for f in range(clip.frames + 1):
        p = clip.sample(f / 30.0)
        rw, _, _ = PO.fk(p)
        keys.append(({n: ((rw[par].inverted() @ rw[n]) if par else rw[n].copy()) for n, par, _, _ in S.BONES}, p.hips.copy()))

    def toe(t, foot):
        fr = (t * 30.0) % clip.frames
        f0 = min(int(fr), clip.frames - 1)
        a = fr - f0
        l0, h0 = keys[f0]
        l1, h1 = keys[f0 + 1]
        p = PO.Pose()
        p.hips = h0.lerp(h1, a)
        rw = {}
        for n, par, _, _ in S.BONES:
            q1 = l1[n].copy()
            if l0[n].dot(q1) < 0:
                q1.negate()
            qq = l0[n].slerp(q1, a)
            rw[n] = (rw[par] @ qq) if par else qq
            p.world[n] = rw[n]
        _, h, _ = PO.fk(p)
        return h[foot] + d * v * t
    T = clip.frames / 30.0
    worst = 0.0
    for foot, st in (("LeftToes", tg["stance"]["Left"][0]), ("RightToes", tg["stance"]["Right"][0])):
        ttd = st * T + T
        ptd = toe(ttd + 1e-4, foot)
        for i in range(1, int(hz * T * 0.5)):
            pp = toe(ttd - i / hz, foot)
            if pp.y > 0.03:
                break
            worst = max(worst, math.hypot(pp.x - ptd.x, pp.z - ptd.z))
    return worst
