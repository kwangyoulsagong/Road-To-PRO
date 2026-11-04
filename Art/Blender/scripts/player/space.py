"""좌표 규칙 (specs/043).

이 패키지의 모든 기하·포즈 계산은 **선수 좌표**(= Unity 좌표, 미터): X = 선수 오른쪽, Y = 위, Z = 선수 정면.
원점은 두 발 사이 바닥. Blender로 넣을 때만 변환한다.
044 실측: Blender (x, y, z) → Unity (−x, z, −y). 그러므로 선수 (X, Y, Z) → Blender (−X, −Z, Y).
이 변환은 행렬식 −1(반사)이지만 회전 R은 C·R·C⁻¹로 옮기면 그대로 회전이다.
"""


def to_blender(p):
    return (-p[0], -p[2], p[1])


def from_blender(b):
    return (-b[0], b[2], -b[1])


C = ((-1.0, 0.0, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0))  # 선수 → Blender 벡터


def _mm(a, b):
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))


def _t(a):
    return tuple(tuple(a[j][i] for j in range(3)) for i in range(3))


def rot_to_blender(r):
    """선수 좌표 3×3 회전 → Blender 좌표 3×3 회전 (C·R·Cᵀ, C는 직교)."""
    return _mm(_mm(C, r), _t(C))
