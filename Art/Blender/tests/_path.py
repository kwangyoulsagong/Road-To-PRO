"""테스트가 Art/Blender/scripts 를 import 할 수 있게."""
import os
import sys

SCRIPTS = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "scripts"))
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
REPO = os.path.normpath(os.path.join(SCRIPTS, "..", "..", ".."))
