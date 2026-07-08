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


def build_registration_context(rows, add_converter_path):
    """Build the context for the aggregate AddConverters-style C# file.

    register_method is the Rpc.* method name to call for each row
    (e.g. "AddRpcConverter" or "AddFallbackConverter").

    The aggregate file's namespace is the host partial class's namespace
    (add_converter_namespace), not any individual row's own namespace --
    those can differ per row now that converters may live in either the
    Unity-independent or Unity-dependent tree.
    """
    namespace = rows[0]["add_converter_namespace"].strip()
    registration_lines = []
    for row in rows:
        add_converter_method = row["add_converter_method"].strip()
        if not add_converter_method:
            continue
        class_name = row["class_name"].strip()
        server_field_name = row["server_field_name"].strip()
        if server_field_name:
            registration_lines.append(
                f"Rpc.{add_converter_method}(new {class_name}({server_field_name}));"
            )
        else:
            registration_lines.append(f"Rpc.{add_converter_method}(new {class_name}());")

    output_abs = os.path.normpath(
        os.path.join(*add_converter_path.split("/"))
    )

    return {
        "namespace": namespace,
        "registration_lines": registration_lines,
        "output_abs": output_abs,
        "add_converter_class": rows[0]["add_converter_class"],
    }


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


def run_generate(template_dir, template_name, add_template_name,
                  schema_path, out_dir, add_converter_path,
                  transform_row, build_add_context,
                  dry_run=False, row_filter=None):
    """Shared control flow for per-row C# file generation plus an aggregate file.

    transform_row(out_dir, raw_row) and build_add_context(rows, add_converter_path)
    are supplied by the caller to handle schema-specific fields.
    """
    env = make_jinja_env(template_dir)
    template = env.get_template(template_name)
    add_converters_template = env.get_template(add_template_name)

    rows = load_schema(schema_path)

    for raw in rows:
        if row_filter and raw["class_name"].strip() != row_filter:
            continue
        ctx = transform_row(out_dir, raw)
        if ctx.get("hand_written") == "true":
            if not dry_run:
                print(f"[SKIP] {ctx['class_name']} is hand-written; not regenerating {ctx['output_abs']}")
            continue
        content = render(template, ctx)
        write_output(ctx["output_abs"], content, dry_run)
        if not dry_run:
            print(f"[OK] {ctx['output_abs']}")

    # The aggregate file reflects every row regardless of row_filter,
    # since it must always represent the full registration list.
    add_converters_ctx = build_add_context(rows, add_converter_path)
    add_converters_content = render(add_converters_template, add_converters_ctx)
    write_output(add_converters_ctx["output_abs"], add_converters_content, dry_run)
    if not dry_run:
        print(f"[OK] {add_converters_ctx['output_abs']}")
