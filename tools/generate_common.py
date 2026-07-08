"""
Shared CLI helpers for the tools/generate_*.py schema-driven code generators.
"""

import argparse
import os
import sys


def resolve_paths(script_dir, csv_filename, output_filename):
    """Resolve CSV path, per-row output root, and the aggregate file's output path.

    root_path is the WebSocketLib folder itself: each CSV row's own output_path
    column is relative to it (e.g. "CSharp/LLiquidLink/Converters/..." or
    "CSharp/UniLiquidLink/Converters/..."), since individual converters may live
    in either the Unity-independent or Unity-dependent tree. The aggregate file
    (AddConverters.cs / AddFallbackConverters.cs) always extends the Unity-only
    Server partial class, so its own output path is fixed.
    """
    csv_path = os.path.join(script_dir, csv_filename)
    root_path = f"{script_dir}/.."
    output_path = f"{root_path}/CSharp/UniLiquidLink/Unity/{output_filename}"
    return csv_path, root_path, output_path


def run_cli(description, generate_fn, csv_path, root_path, output_path):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print rendered output to stdout without writing files",
    )
    parser.add_argument(
        "--row",
        metavar="CLASS_NAME",
        help="Only generate the specified class",
    )
    args = parser.parse_args()

    errors = generate_fn(csv_path, root_path, output_path, dry_run=args.dry_run, row_filter=args.row)

    if errors:
        for name, exc in errors:
            print(f"[ERROR] {name}: {exc}", file=sys.stderr)
        sys.exit(1)
