---
paths:
  - "Art/**"
  - "Assets/**/*.fbx"
  - "Assets/**/*.glb"
---
# 3D 아트 파이프라인

- 경기장·소품은 손으로 모델링하지 않고 `Art/Blender/scripts/*.py`(bpy) 스크립트로 생성한다. 스크립트가 원본이고 glb/fbx는 산출물.
- 실행: `/Applications/Blender.app/Contents/MacOS/Blender --background --python <script> -- --out <경로>`. 스크립트는 인자로 출력 경로를 받고, 같은 인자면 같은 결과가 나와야 한다(랜덤은 시드 고정).
- 스케일: 1 unit = 1 m, Y-up으로 내보내기(Unity 기준). 경기장 105×68 m.
- 폴리곤·텍스처 예산은 내장 GPU 60fps 기준. 텍스처 최대 2K, 가능하면 1K + 아틀라스.
- 모션은 bpy 자체 제작(Unity Humanoid 뼈대, 2026-11-04 Mixamo 대체), 텍스처는 Poly Haven·ambientCG·Kenney(CC0). **추가할 때마다 `Art/LICENSES.csv` 갱신** (훅이 상기시킨다).
- 서드파티 원본은 `Art/ThirdParty/`, 바이너리는 LFS.
