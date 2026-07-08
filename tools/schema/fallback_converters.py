"""
Library for generating IChainConverter implementation C# files from a CSV schema.

Used by tools/generate_fallback_converters.py as its CLI entry point.

NOTE: unlike RpcJsonConverter (registered directly into System.Text.Json and
dispatched by its own type matching), IChainConverter instances run through
ConverterChain as a chain of responsibility in registration order --
the ROW ORDER in fallback_converters.csv therefore determines runtime priority
(first matching converter wins).
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
TEMPLATE_NAME = "fallback_converter.cs.j2"
ADD_FALLBACK_CONVERTERS_TEMPLATE_NAME = "add_chain_converters.cs.j2"


def transform_row(output_dir, row):
    ctx = base_transform_row(output_dir, row)
    return ctx

def build_add_fallback_converters_context(rows, add_converter_path):
    return build_registration_context(rows, add_converter_path)


def generate(schema_path, out_dir, add_converter_path, dry_run=False, row_filter=None):
    """Generate IFallbackConverter C# files and AddFallbackConverters.cs from the CSV schema.

    Returns a list of (name, exception) tuples for any rows that failed.
    """
    run_generate(
        TEMPLATE_DIR, TEMPLATE_NAME, ADD_FALLBACK_CONVERTERS_TEMPLATE_NAME,
        schema_path, out_dir, add_converter_path,
        transform_row, build_add_fallback_converters_context,
        dry_run=dry_run, row_filter=row_filter,
    )
