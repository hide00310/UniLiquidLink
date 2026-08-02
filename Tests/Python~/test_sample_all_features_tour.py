"""
Runs the All Features Tour sample script (AllFeaturesTour/all_features_tour.py)
against a live All Features Tour sample server. Verifies only that the script
completes without raising; skipped if no server is listening on
localhost:8700.
"""
import os
import sys

import pytest

_SAMPLE_DIR = os.path.join(
    os.path.dirname(__file__), "..", "..", "Samples~", "UniLiquidLinkSample", "AllFeaturesTour")
sys.path.insert(0, _SAMPLE_DIR)

import all_features_tour  # noqa: E402


def test_all_features_tour_runs_without_error():
    try:
        all_features_tour.main()
    except OSError:
        pytest.skip("All Features Tour sample server not available at localhost:8700")
