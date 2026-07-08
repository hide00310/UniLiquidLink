"""
Generate RpcJsonConverter subclass C# files from a CSV schema.

Usage:
    python tools/generate_converters.py
    python tools/generate_converters.py --dry-run
    python tools/generate_converters.py --row TypeConverter
"""

import os
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)

from generate_common import resolve_paths, run_cli
from schema.converters import generate

CSV_PATH, ROOT_PATH, ADD_CONVERTERS_OUTPUT_PATH = resolve_paths(
    SCRIPT_DIR, "converters.csv", "Server.AddConverters.cs"
)


def main():
    run_cli(
        "Generate RpcJsonConverter C# files from CSV schema",
        generate,
        CSV_PATH, ROOT_PATH, ADD_CONVERTERS_OUTPUT_PATH,
    )


if __name__ == "__main__":
    main()
