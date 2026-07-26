"""
Shared CLI helpers for the tools/generate_*.py schema-driven code generators.
"""

import argparse
import os
import sys


def resolve_paths(script_dir, csv_filename):
    csv_path = f'{script_dir}/converters/{csv_filename}'
    root_path = f"{script_dir}/.."
    return csv_path, root_path


def run_cli(description, generate_fn, csv_path, root_path):
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

    errors = generate_fn(csv_path, root_path, dry_run=args.dry_run, row_filter=args.row)

    if errors:
        for name, exc in errors:
            print(f"[ERROR] {name}: {exc}", file=sys.stderr)
        sys.exit(1)
