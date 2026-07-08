"""
Library for generating RpcJsonConverter subclass C# files from a CSV schema.

Used by tools/generate_converters.py as its CLI entry point.
"""

import os

from .codegen import (
    SPLIT,
    base_transform_row,
    build_registration_context,
    run_generate,
)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TOOLS_DIR = os.path.dirname(SCRIPT_DIR)
TEMPLATE_DIR = os.path.join(TOOLS_DIR, "templates")
TEMPLATE_NAME = "rpc_converter.cs.j2"
ADD_CONVERTERS_TEMPLATE_NAME = "add_converters.cs.j2"


def transform_row(output_dir, row):
    ctx = base_transform_row(output_dir, row)

    write_fields = []
    for field_str in row["write_fields"].split(SPLIT):
        if "=" in field_str:
            key, _, val = field_str.partition("=")
            write_fields.append({"key": key.strip(), "val": val.strip()})
    ctx["write_fields"] = write_fields

    return ctx


def build_add_converters_context(rows, add_converter_path):
    return build_registration_context(rows, add_converter_path)


def generate(schema_path, out_dir, add_converter_path, dry_run=False, row_filter=None):
    """Generate converter C# files and AddConverters.cs from the CSV schema.

    Returns a list of (name, exception) tuples for any rows that failed.
    """
    run_generate(
        TEMPLATE_DIR, TEMPLATE_NAME, ADD_CONVERTERS_TEMPLATE_NAME,
        schema_path, out_dir, add_converter_path,
        transform_row, build_add_converters_context,
        dry_run=dry_run, row_filter=row_filter,
    )
