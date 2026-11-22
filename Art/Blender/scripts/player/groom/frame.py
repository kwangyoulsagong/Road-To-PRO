"""그루밍 좌표 틀 (specs/088 T09). 시안 코드는 Blender 틀(Z 위, −Y 정면, +X = 선수 왼쪽)로 짜여 있다.
선수 좌표(X = 선수 오른쪽, Y 위, Z 정면) ↔ Blender 틀: b = (−X, −Z, Y), 되돌림 X = −bx, Y = bz, Z = −by.
두 변환 모두 반사(행렬식 −1)라 왕복하면 면 감김·법선 방향이 원래대로 돌아온다(MPFB 면은 Blender 틀에서 바깥 법선)."""
import numpy as np


def to_blender(P):
    P = np.asarray(P, dtype=np.float64)
    return np.stack([-P[..., 0], -P[..., 2], P[..., 1]], axis=-1)


def from_blender(B):
    B = np.asarray(B, dtype=np.float64)
    return np.stack([-B[..., 0], B[..., 2], -B[..., 1]], axis=-1)


class Head:
    """기준 선수 몸 표면(Blender 틀): V (n,3), T (m,3) 삼각형(기준 정점 인덱스), TUV (m,3,2), eye (3,), skin_png."""


def head(R=None, verts=None):
    """R = mpfb_src.Reference (없으면 만든다). verts를 주면 그 정점(바탕 머리 델타를 건 기준)으로 표면을 만든다."""
    from player import body as B
    from player import mhassets as MH
    if R is None:
        R = B.reference()
    V = R.verts if verts is None else verts
    body = set(R.groups["body"])
    T, TUV = [], []
    for f, fu in zip(R.faces, R.face_uvs):
        if not all(v in body for v in f):
            continue
        for k in range(1, len(f) - 1):
            T.append((f[0], f[k], f[k + 1]))
            TUV.append((fu[0], fu[k], fu[k + 1]))
    h = Head()
    h.R = R
    h.V = to_blender(V)
    h.T = np.array(T, dtype=np.int64)
    h.TUV = np.array(TUV)
    h.eye = to_blender(MH.Proxy(MH.EYES).fit(V).mean(axis=0))
    h.skin_png = MH.read_mhmat(MH.SKINS["light"])["diffusePath"]
    return h
