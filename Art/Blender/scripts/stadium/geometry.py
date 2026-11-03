"""메시 생성 도우미 (specs/044 plan). bmesh로 직접 만들어 연산자 기본값 변화에 흔들리지 않게 한다.

좌표: 이 모듈의 모든 함수는 **경기장 좌표**(x = 피치 길이, y = 폭, z = 위, 미터, 센터마크 원점 — 001 시뮬과 같은 방향)를 받는다.
메시에 넣을 때 Blender Z축 180° 회전(x, y, z) → (−x, −y, z)을 한다. 실측(T03): Blender (x, y, z) → glTF → glTFast → Unity (−x, z, −y)
이므로 결과는 Unity (X = 길이, Y = 위, Z = 폭). 정확한 값을 위해 회전 행렬 대신 부호 반전을 쓴다.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, rgba, emission=None, roughness=0.8, metallic=0.0):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = emission[:3] + (1.0,)
        bsdf.inputs["Emission Strength"].default_value = emission[3]
    m.diffuse_color = rgba
    return m


def empty(name, parent=None):
    o = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
    return o


TO_BLENDER = Matrix(((-1.0, 0.0, 0.0, 0.0), (0.0, -1.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0)))


def to_blender(p):
    return (-p[0], -p[1], p[2])


def _link(name, bm, mat, parent):
    bmesh.ops.transform(bm, matrix=TO_BLENDER, verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if mat is not None:
        me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
    return o


def from_pydata(name, verts, faces, mat, parent=None, mat_indices=None, mats=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([to_blender(v) for v in verts], [], [tuple(f) for f in faces])
    me.validate(clean_customdata=False)
    if mats is not None:
        for m in mats:
            me.materials.append(m)
        if mat_indices:
            for p, i in zip(me.polygons, mat_indices):
                p.material_index = i
    elif mat is not None:
        me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
    return o


def box(name, center, size, mat, parent=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return _link(name, bm, mat, parent)


def box_minmax(name, lo, hi, mat, parent=None):
    c = tuple((a + b) / 2 for a, b in zip(lo, hi))
    s = tuple(b - a for a, b in zip(lo, hi))
    return box(name, c, s, mat, parent)


def cylinder(name, a, b, r, segments, mat, parent=None, cap=True):
    """a → b 축 원기둥."""
    a, b = Vector(a), Vector(b)
    axis = b - a
    length = axis.length
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=segments, radius1=r, radius2=r, depth=length)
    rot = axis.normalized().to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=bm.verts)
    return _link(name, bm, mat, parent)


def triangle_count(objs=None):
    n = 0
    for o in objs if objs is not None else bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        for p in o.data.polygons:
            n += len(p.vertices) - 2
    return n


def deg(a):
    return math.radians(a)


class MeshBuilder:
    """경기장 좌표로 정점·면을 모아 메시 하나를 만든다. 면마다 원하는 바깥 방향을 주면 감기 순서를 맞춘다."""

    def __init__(self):
        self.verts = []
        self.faces = []
        self.mat_idx = []
        self.mats = []

    def v(self, p):
        self.verts.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.verts) - 1

    def _mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def face(self, idx, mat, want=None):
        idx = list(idx)
        if want is not None:
            n = _newell([self.verts[i] for i in idx])
            if n[0] * want[0] + n[1] * want[1] + n[2] * want[2] < 0:
                idx.reverse()
        self.faces.append(idx)
        self.mat_idx.append(self._mi(mat))

    def quad(self, a, b, c, d, mat, want=None):
        self.face([self.v(a), self.v(b), self.v(c), self.v(d)], mat, want)

    def box(self, lo, hi, mat, skip_bottom=True):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), mat, (0, 0, 1))
        if not skip_bottom:
            self.quad((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), mat, (0, 0, -1))
        self.quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), mat, (0, -1, 0))
        self.quad((x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1), mat, (0, 1, 0))
        self.quad((x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1), mat, (-1, 0, 0))
        self.quad((x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1), mat, (1, 0, 0))

    def cylinder(self, a, b, r, segments, mat, cap_top=True):
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        u = axis.cross(ref).normalized()
        w = axis.cross(u).normalized()
        ra, rb = [], []
        for k in range(segments):
            ang = 2 * math.pi * k / segments
            off = (u * math.cos(ang) + w * math.sin(ang)) * r
            ra.append(self.v(a + off))
            rb.append(self.v(b + off))
        for k in range(segments):
            k2 = (k + 1) % segments
            mid = (Vector(self.verts[ra[k]]) + Vector(self.verts[ra[k2]])) / 2 - a
            self.face([ra[k], ra[k2], rb[k2], rb[k]], mat, tuple(mid))
        if cap_top:
            self.face(list(rb), mat, tuple(axis))
            self.face(list(ra), mat, tuple(-axis))

    def build(self, name, parent=None):
        return from_pydata(name, self.verts, self.faces, None, parent=parent, mat_indices=self.mat_idx, mats=self.mats)


def _newell(pts):
    nx = ny = nz = 0.0
    for i, p in enumerate(pts):
        q = pts[(i + 1) % len(pts)]
        nx += (p[1] - q[1]) * (p[2] + q[2])
        ny += (p[2] - q[2]) * (p[0] + q[0])
        nz += (p[0] - q[0]) * (p[1] + q[1])
    return (nx, ny, nz)
