"""
Shared code-generation helpers for CSV-schema-driven C# file generation.

Used by schema/converters.py and schema/fallback_converters.py, which each
generate a family of C# files (converters or fallback converters) from a CSV
schema plus a jinja2 template, and share the same control flow.
"""

import csv
import os
import sys
import re

try:
    import jinja2
except ImportError:
    sys.exit("[ERROR] jinja2 not found. Run: conda install -n base jinja2")

SPLIT = ";"
BOOLS = {
    "true" : "true",
    "false" : "false"
}

def load_schema(csv_path):
    with open(csv_path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def base_transform_row(output_dir, row):
    """Common row normalization shared by converters.py and fallback_converters.py.

    Strips whitespace from every field, splits extra_usings on SPLIT, and
    computes the absolute output path. Callers add schema-specific fields.
    """
    ctx = {key: value.strip() for key, value in row.items()}
    for key, value in row.items():
        if isinstance(value, str) and value.lower() in BOOLS.keys():
            ctx[key] = BOOLS[value.lower()]
        if isinstance(value, str) and SPLIT in value:
            ctx[key] = [l.strip() for l in row[key].split(SPLIT) if l.strip()]

    ctx["output_abs"] = os.path.normpath(
        os.path.join(output_dir, *ctx["output_path"].split("/"))
    )

    return ctx


def render(template, ctx):
    content = template.render(**ctx)
    # Ensure single trailing newline
    return content.rstrip("\n") + "\n"


def write_output(path, content, dry_run):
    if dry_run:
        print(f"=== {path} ===")
        print(content)
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(content)


def make_jinja_env(template_dir):
    return jinja2.Environment(
        loader=jinja2.FileSystemLoader(template_dir),
        block_start_string="//{%",
        block_end_string="%}",
        comment_start_string="//{#",
        comment_end_string="#}",
        trim_blocks=True,
        lstrip_blocks=True,
        keep_trailing_newline=True,
        undefined=jinja2.StrictUndefined,
    )


def run_generate(template_dir, template_name, schema_path, out_dir,
                  transform_row, dry_run=False, row_filter=None):
    """Shared control flow for per-row C# file generation.

    transform_row(out_dir, raw_row) is supplied by the caller to handle
    schema-specific fields.
    """
    env = make_jinja_env(template_dir)
    template = env.get_template(template_name)

    rows = load_schema(schema_path)

    for raw in rows:
        if row_filter and raw["class_name"].strip() != row_filter:
            continue
        ctx = transform_row(out_dir, raw)
        hand_written = ctx.get("hand_written")
        if isinstance(hand_written, str) and hand_written.lower() == "true":
            if not dry_run:
                print(f"[SKIP] {ctx['class_name']} is hand-written; not regenerating {ctx['output_abs']}")
            continue
        content = render(template, ctx)
        write_output(ctx["output_abs"], content, dry_run)
        if not dry_run:
            print(f"[OK] {ctx['output_abs']}")

