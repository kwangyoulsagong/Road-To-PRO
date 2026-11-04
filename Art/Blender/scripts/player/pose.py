"""포즈 수학 (specs/043 FR-31·32). 선수 좌표에서 FK·2본 IK를 계산하고 Blender 포즈 본 키로 옮긴다.

포즈 정의 `Pose`:
- hips: Hips 머리 위치 이동량 (선수 좌표, m) — 루트(바닥 원점)는 움직이지 않는다(제자리 클립).
- local[bone]: 부모 기준 회전 델타(선수 좌표 축으로 표현한 Quaternion). 부모의 월드 델타가 먼저 곱해진다.
- world[bone]: 월드(선수 좌표) 회전 델타를 직접 지정 (IK 결과·발 평평 등). local보다 우선.
월드 델타 Rw_b = Rw_parent · Rl_b, 포즈 머리 = 부모 포즈 머리 + Rw_parent · (레스트 머리 − 부모 레스트 머리).
"""
import math

from mathutils import Matrix, Quaternion, Vector

from . import skeleton as S
from .space import C as _C

CM = Matrix(_C)
CMI = CM.transposed()

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def q(axis, deg):
    return Quaternion(Vector(axis), math.radians(deg))


def qmul(*qs):
    r = Quaternion()
    for x in qs:
        r = r @ x
    return r


class Pose:
    def __init__(self):
        self.hips = Vector((0, 0, 0))
        self.local = {}
        self.world = {}

    def copy(self):
        p = Pose()
        p.hips = self.hips.copy()
        p.local = {k: v.copy() for k, v in self.local.items()}
        p.world = {k: v.copy() for k, v in self.world.items()}
        return p


def fk(pose):
    """→ (월드 델타 회전 dict, 포즈 머리 위치 dict, 포즈 꼬리 위치 dict) 선수 좌표."""
    rw, head, tail = {}, {}, {}
    for name, parent, h, t in S.BONES:
        h, t = Vector(h), Vector(t)
        if parent is None:
            base = Quaternion()
            head[name] = h + pose.hips
        else:
            base = rw[parent]
            head[name] = head[parent] + base @ (h - Vector(S.HEAD[parent]))
        if name in pose.world:
            rw[name] = pose.world[name].copy()
        else:
            rw[name] = base @ pose.local.get(name, Quaternion())
        tail[name] = head[name] + rw[name] @ (t - h)
    return rw, head, tail


def look(rest_dir, rest_pole, dir_, pole):
    """rest_dir → dir_, rest_pole(수직 성분) → pole 쪽으로 가는 회전."""
    def frame(d, p):
        d = d.normalized()
        p = (p - d * p.dot(d)).normalized()
        return Matrix((d, p, d.cross(p))).transposed()
    return (frame(dir_, pole) @ frame(rest_dir, rest_pole).transposed()).to_quaternion()


def leg_ik(pose_head_hip, ankle, knee_pole, side_name):
    """엉덩이 관절 → 발목 목표. 무릎은 knee_pole 방향. 도달 불가면 최대한 뻗는다. → (UpperLeg 월드, LowerLeg 월드)."""
    a, b = S.L_THIGH, S.L_SHIN
    d = ankle - pose_head_hip
    L = min(d.length, (a + b) * 0.9995)
    dn = d.normalized()
    # 코사인 법칙: 허벅지와 엉덩이→발목 선 사이 각
    cos_t = (a * a + L * L - b * b) / (2 * a * L)
    th = math.acos(max(-1.0, min(1.0, cos_t)))
    side = (knee_pole - dn * knee_pole.dot(dn)).normalized()
    thigh_dir = dn * math.cos(th) + side * math.sin(th)
    knee = pose_head_hip + thigh_dir * a
    shin_dir = (pose_head_hip + dn * L - knee).normalized()
    rest = -Y
    up = look(rest, Z, thigh_dir, knee_pole)
    lo = look(rest, Z, shin_dir, knee_pole)
    return up, lo


def arm_rest_dir(side):
    s = math.sin(math.radians(S.ARM_ANGLE))
    c = math.cos(math.radians(S.ARM_ANGLE))
    return Vector((side * s, -c, 0.0))


class Keyer:
    """Pose → Blender 포즈 본 키 (rotation_quaternion 전부, location은 Hips만)."""

    def __init__(self, rig):
        self.rig = rig
        self.B = {b.name: b.matrix_local.copy() for b in rig.data.bones}
        self.prev = {}

    def _pose_matrix(self, name, rw, head):
        r3 = (CM @ rw.to_matrix() @ CMI) @ self.B[name].to_3x3()
        m = r3.to_4x4()
        m.translation = CM @ head
        return m

    def key(self, pose, frame):
        rw, head, _ = fk(pose)
        P = {n: self._pose_matrix(n, rw[n], head[n]) for n in S.NAMES}
        for name in S.NAMES:
            parent = S.PARENT[name]
            if parent is None:
                basis = self.B[name].inverted() @ P[name]
            else:
                basis = (self.B[parent].inverted() @ self.B[name]).inverted() @ (P[parent].inverted() @ P[name])
            pb = self.rig.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            qt = basis.to_quaternion()
            pr = self.prev.get(name)
            if pr is not None and pr.dot(qt) < 0:
                qt.negate()
            self.prev[name] = qt.copy()
            pb.rotation_quaternion = qt
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            if parent is None:
                pb.location = basis.translation
                pb.keyframe_insert("location", frame=frame)

    def reset(self):
        self.prev = {}
