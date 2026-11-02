#!/usr/bin/env python3
"""디바이스팜 진입점. 사용: python3 Tools/devicefarm/farm.py <명령> (specs/000)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from farm.cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())
