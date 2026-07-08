"""
Generate IChainConverter implementation C# files from a CSV schema.

NOTE: row order in tools/fallback_converters.csv determines runtime priority --
ConverterChain tries converters in registration order (first match wins).

Usage:
    python tools/generate_fallback_converters.py
    python tools/generate_fallback_converters.py --dry-run
    python tools/generate_fallback_converters.py --row UnityJsonUtilityConverter
"""

import os
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)

from generate_common import resolve_paths, run_cli
from schema.fallback_converters import generate

CSV_PATH, ROOT_PATH, ADD_FALLBACK_CONVERTERS_OUTPUT_PATH = resolve_paths(
    SCRIPT_DIR, "fallback_converters.csv", "Server.AddFallbackConverters.cs"
)


def main():
    run_cli(
        "Generate IChainConverter C# files from CSV schema",
        generate,
        CSV_PATH, ROOT_PATH, ADD_FALLBACK_CONVERTERS_OUTPUT_PATH,
    )


if __name__ == "__main__":
    main()
