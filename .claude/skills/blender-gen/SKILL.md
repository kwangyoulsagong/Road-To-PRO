---
name: blender-gen
description: Art/Blender/scripts의 bpy 스크립트를 작성하거나 실행해 경기장·소품 glb/fbx를 생성한다. 3D 에셋 생성·수정 요청 시 사용.
---
# Blender 스크립트로 에셋 생성

## 스크립트 작성 규칙
- 위치: `Art/Blender/scripts/<snake_case>.py`. 맨 위 docstring에 무엇을 만드는지, 인자, 폴리곤 예산.
- `sys.argv[sys.argv.index("--") + 1:]`를 argparse로 파싱. 최소 `--out` 지원.
- 시작 시 씬을 비운다 (`bpy.ops.wm.read_factory_settings(use_empty=True)`). 랜덤은 `random.seed(고정값)`.
- 1 unit = 1 m. 내보내기: glb는 `export_yup=True`, 머티리얼은 Principled BSDF만(URP Lit로 매핑됨).
- 텍스처는 CC0만, `Art/ThirdParty/<출처>/`에 두고 `Art/LICENSES.csv`에 기록.

## 실행
`.claude/scripts/blender-gen.sh Art/Blender/scripts/<name>.py [출력경로] [추가 인자]`
- 기본 출력: `Art/Generated/<name>.glb`. Unity로 가져갈 것은 `Assets/_Project/Presentation/Art/` 아래로 복사.
- 결과 보고: 파일 크기, 가능하면 폴리곤 수(스크립트가 마지막에 출력하게 한다).
