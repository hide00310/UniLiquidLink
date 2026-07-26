"""
Generate RPC schema files from Pydantic source-of-truth definitions.

Pipeline:
  1. Combine all CS_MODELS in schema_def.py into a single JSON Schema document
     (one file, each model registered under top-level $defs).
  2. Run Quicktype CLI with one --src per model (JSON Pointer fragment into the
     combined file)  ->  LLiquidLink/Models/Schema.cs.
     Post-process: add `partial`, apply CS_FIELD_TYPE_OVERRIDES, add header comment.
     Delete obsolete per-type files (RpcType.cs, RpcChainStep.cs).
  3. For each PY_MODEL: generate Python @dataclass source directly (not via Quicktype,
     because Quicktype mangles class names like RpcType -> RPCType and converts Literal
     to Enum, breaking compatibility with _serialization.py).

Usage:
    python tools/generate_schema.py
    python tools/generate_schema.py --dry-run
    python tools/generate_schema.py --skip-cs
    python tools/generate_schema.py --skip-py
"""

import argparse
from glob import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import textwrap
import typing

# -- Dependency check ---------------------------------------------------------
try:
    import pydantic
    from pydantic import BaseModel
except ImportError:
    sys.exit("[ERROR] pydantic not found. Run: install pydantic")

# -- Path resolution ----------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WEBSOCKETLIB = os.path.normpath(os.path.join(SCRIPT_DIR, ".."))
MODELS_DIR = os.path.join(WEBSOCKETLIB, "CSharp", "LLiquidLink", "Models")

CS_OUT = os.path.join(MODELS_DIR, "Schema.cs")
PY_OUT = os.path.join(WEBSOCKETLIB, "Python~", "lliquidlink", "client", "_schema.py")

# Files replaced by Schema.cs and removed on generation.
OBSOLETE_CS_FILES = [
    os.path.join(MODELS_DIR, "RpcType.cs"),
    os.path.join(MODELS_DIR, "RpcChainStep.cs"),
]

# -- Import schema_def --------------------------------------------------------
sys.path.insert(0, SCRIPT_DIR)
try:
    import schema_def
except ImportError as exc:
    sys.exit("[ERROR] Cannot import tools/schema_def.py: %s" % exc)

_PYDANTIC_UNDEFINED = pydantic.fields.PydanticUndefined


# =============================================================================
# Phase 1 – JSON Schema generation
# =============================================================================

def _strip_const(node):
    """Recursively remove 'const' keys, replacing with plain 'type': 'string'.

    Pydantic emits {"const": "x"} for Literal['x'] fields. Quicktype converts
    const values to single-member Enum types; we want plain strings instead.
    """
    if not isinstance(node, dict):
        return
    if "const" in node:
        node.pop("const")
        node.setdefault("type", "string")
    for val in node.values():
        if isinstance(val, dict):
            _strip_const(val)
        elif isinstance(val, list):
            for item in val:
                if isinstance(item, dict):
                    _strip_const(item)


def emit_combined_json_schema(models, out_dir):
    """Write a single JSON Schema document holding all models under $defs.

    Each model's own nested $defs (e.g. RpcChainStep referenced by
    RpcResolveChainParam) are merged into the combined top-level $defs dict;
    $ref pointers are document-root-relative so they keep resolving unchanged.
    Returns the written file's path.
    """
    combined_defs = {}
    for model in models:
        schema = model.model_json_schema(by_alias=True)
        schema["title"] = model.__name__
        _strip_const(schema)
        nested_defs = schema.pop("$defs", {})
        for name, def_schema in nested_defs.items():
            combined_defs.setdefault(name, def_schema)
        combined_defs[model.__name__] = schema

    path = os.path.join(out_dir, "schema.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"$defs": combined_defs}, f, indent=2)
        f.write("\n")
    return path


# =============================================================================
# Phase 2 – Quicktype -> Schema.cs
# =============================================================================

def run_quicktype_cs(schema_path, type_names):
    """Invoke Quicktype and return the generated C# source as a string.

    Each entry in type_names becomes a --src pointing at that model's
    JSON Pointer fragment within the single combined schema_path document.

    Quicktype's $ref resolver misparses a Windows drive-letter absolute path
    combined with a JSON Pointer fragment (e.g. "C:\\...#/$defs/X" gets its
    drive letter mistaken for a URI scheme, breaking in-document ref lookup).
    Working around this by running with cwd set to the schema's directory
    and passing only its relative filename in --src.
    """
    if not shutil.which("quicktype"):
        sys.exit(
            "[ERROR] quicktype not found.\n"
            "Install with: npm install -g quicktype"
        )
    cmd = [
        "quicktype",
        "--lang", "cs",
        "--src-lang", "schema",
        "--namespace", "LLiquidLink",
        "--csharp-version", "5",
        "--features", "just-types",
        "--keep-property-name",
        "--array-type", "array",
        "--no-enums",
        "--quiet",
    ]
    schema_dir, schema_name = os.path.split(schema_path)
    for name in type_names:
        cmd += ["--src", "%s#/$defs/%s" % (schema_name, name)]

    # On Windows, quicktype is installed as a .cmd batch file and requires
    # shell=True for subprocess to locate and execute it.
    result = subprocess.run(
        cmd, capture_output=True, text=True, encoding="utf-8",
        shell=(sys.platform == "win32"), cwd=schema_dir,
    )
    if result.returncode != 0:
        sys.exit("[ERROR] Quicktype failed:\n" + result.stderr)
    return result.stdout


def _postprocess_cs(raw_cs):
    """Post-process Quicktype C# output for project compatibility."""
    content = raw_cs

    # 1. Make every class declaration partial.
    content = re.sub(r"\bpublic class (\w+)", r"public partial class \1", content)

    # 2. Apply integer type overrides (Quicktype emits 'long' for JSON integer).
    for org_type, cs_type in schema_def.CS_FIELD_TYPE_OVERRIDES.items():
        pattern = r"(public\s+)" + org_type +r"(.+\{)"
        content = re.sub(pattern, r"\g<1>" + cs_type + r"\2", content)

    # 3. Prepend auto-generated header.
    header = (
        "// <auto-generated>\n"
        "// This file was generated by tools/generate_schema.py.\n"
        "// Do not edit manually. Edit tools/schema_def.py instead,\n"
        "// then re-run: python tools/generate_schema.py\n"
        "// </auto-generated>\n\n"
    )
    content = header + content

    # 4. Normalize line endings to LF.
    content = content.replace("\r\n", "\n")
    return content


# =============================================================================
# Phase 3 – Direct Python @dataclass generation
# =============================================================================

def _annotation_to_str(annotation) -> str:
    """Convert a Python type annotation to its source-code string form."""
    origin = typing.get_origin(annotation)
    args = typing.get_args(annotation)

    if origin is typing.Literal:
        val = args[0]
        return "Literal[%r]" % (val,)

    if origin is list:
        if args:
            return "list[%s]" % _annotation_to_str(args[0])
        return "list"

    if annotation is dict:
        return "dict"

    if hasattr(annotation, "__name__"):
        return annotation.__name__

    return str(annotation)


def _model_field_lines(model):
    """Return (required_lines, optional_lines) for a Pydantic model's fields.

    Dataclass fields with defaults must follow fields without defaults.
    """
    required = []
    optional = []
    for name, info in model.model_fields.items():
        ann_str = _annotation_to_str(info.annotation)
        if info.is_required():
            required.append("    %s: %s" % (name, ann_str))
        else:
            required_val = repr(info.default)
            optional.append("    %s: %s = %s" % (name, ann_str, required_val))
    return required, optional


def _generate_py_schema(models) -> str:
    """Build the complete _schema.py source from PY_MODELS."""
    lines = [
        '"""RPC wire-protocol type definitions.',
        "",
        "Auto-generated by tools/generate_schema.py.",
        "Do not edit manually. Edit tools/schema_def.py and re-run the generator.",
        '"""',
        "from __future__ import annotations",
        "",
        "from dataclasses import dataclass",
        "from typing import Any, Literal",
        "",
        "",
    ]

    for model in models:
        doc = (model.__doc__ or "").strip()
        lines.append("@dataclass")
        lines.append("class %s:" % model.__name__)
        if doc:
            lines.append('    """%s"""' % doc)
            lines.append("")
        req, opt = _model_field_lines(model)
        if not req and not opt:
            lines.append("    pass")
        else:
            lines.extend(req)
            lines.extend(opt)
        lines.append("")
        lines.append("")

    content = "\n".join(lines)
    # Collapse trailing blank lines to a single newline.
    content = content.rstrip("\n") + "\n"
    return content


# =============================================================================
# Utilities
# =============================================================================

def _write_file(path, content, dry_run, label):
    if dry_run:
        print("=== %s ===" % path)
        print(content)
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(content)
    print("[OK] %s" % label)


def _delete_obsolete(paths, dry_run):
    for path in paths:
        if not os.path.exists(path):
            continue
        if dry_run:
            print("[DRY-RUN] Would delete: %s" % path)
        else:
            os.remove(path)
            print("[DEL] %s" % os.path.basename(path))


# =============================================================================
# Entry point
# =============================================================================

def main():
    parser = argparse.ArgumentParser(
        description="Generate _schema.py and Schema.cs from tools/schema_def.py"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print output to stdout without writing or deleting files",
    )
    parser.add_argument(
        "--skip-cs",
        action="store_true",
        help="Skip C# Schema.cs generation",
    )
    parser.add_argument(
        "--skip-py",
        action="store_true",
        help="Skip Python _schema.py generation",
    )
    args = parser.parse_args()

    tmp_dir = tempfile.mkdtemp(prefix="rpc_schemas_")
    # tmp_dir = f"{SCRIPT_DIR}/tmp"
    try:
        # -- Phase 1 & 2: C# generation via Quicktype --------------------------
        if not args.skip_cs:
            if not args.dry_run:
                for file in glob(f'{MODELS_DIR}/*.cs') + glob(f'{MODELS_DIR}/*.meta'):
                    os.remove(file)
                os.makedirs(tmp_dir, exist_ok=True)
                for file in glob(f'{tmp_dir}/*.json') + glob(f'{tmp_dir}/*.json.meta'):
                    os.remove(file)

            schema_path = emit_combined_json_schema(schema_def.CS_MODELS, tmp_dir)
            type_names = [m.__name__ for m in schema_def.CS_MODELS]
            print("[1] Schema: %s (%s)" % (os.path.basename(schema_path), ", ".join(type_names)))

            print("[2] Running Quicktype for C#...")
            raw_cs = run_quicktype_cs(schema_path, type_names)
            cs_content = _postprocess_cs(raw_cs)
            _write_file(CS_OUT, cs_content, args.dry_run, "Schema.cs")

            _delete_obsolete(OBSOLETE_CS_FILES, args.dry_run)

        # -- Phase 3: Python generation ----------------------------------------
        if not args.skip_py:
            py_content = _generate_py_schema(schema_def.PY_MODELS)
            _write_file(PY_OUT, py_content, args.dry_run, "_schema.py")

    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)
        pass

if __name__ == "__main__":
    main()
