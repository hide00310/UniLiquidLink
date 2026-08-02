"""
Runs the Cube Demo sample script (CubeDemo/create_and_rotate_cube.py) against a
live Cube Demo sample server. Verifies only that the script completes without
raising; skipped if no server is listening on localhost:8700.
"""
import os
import sys

import pytest

_SAMPLE_DIR = os.path.join(
    os.path.dirname(__file__), "..", "..", "Samples~", "UniLiquidLinkSample", "CubeDemo")
sys.path.insert(0, _SAMPLE_DIR)

import create_and_rotate_cube  # noqa: E402


def test_cube_demo_runs_without_error():
    try:
        create_and_rotate_cube.main()
    except OSError:
        pytest.skip("Cube Demo sample server not available at localhost:8700")
