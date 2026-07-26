
import argparse
from glob import glob
import json
import os
from pathlib import Path
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


def _rewrite_refs(node):
    # openapi-generator expects "#/components/schemas/X", not the JSON Schema "#/$defs/X".
    if isinstance(node, dict):
        return {
            k: (v.replace("#/$defs/", "#/components/schemas/") if k == "$ref" else _rewrite_refs(v))
            for k, v in node.items()
        }
    if isinstance(node, list):
        return [_rewrite_refs(v) for v in node]
    return node


def _wrap_as_openapi(schema_path, out_path):
    # tmp/schema.json is a bare Pydantic JSON Schema ({"$defs": {...}}), not an
    # OpenAPI document. openapi-generator-cli cannot infer the spec version
    # from that, so wrap the defs into a minimal valid OpenAPI 3.0 document.
    with open(schema_path, encoding="utf-8") as f:
        raw = json.load(f)

    openapi_doc = {
        "openapi": "3.0.0",
        "info": {"title": "Schema", "version": "1.0.0"},
        "paths": {},
        "components": {
            "schemas": _rewrite_refs(raw.get("$defs", {})),
        },
    }
    
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(openapi_doc, f, indent=2)


if __name__ == "__main__":
    openapi_spec_path = f"{SCRIPT_DIR}/tmp/schema.openapi.json"

    # p1 = subprocess.run(
    #     "json-schema-to-openapi-schema "
    #     "convert "
    #     f"{SCRIPT_DIR}/tmp/schema.json ",
    #     shell=True,
    #     capture_output=True,
    #     text=True
    # )
    # Path(openapi_spec_path).write_text(p1.stdout)
    _wrap_as_openapi(f"{SCRIPT_DIR}/tmp/schema.json", openapi_spec_path)

    subprocess.run(
        "openapi-generator-cli generate "
        f"-i {openapi_spec_path} "
        "-g csharp "
        f"-o {SCRIPT_DIR}/tmp/schema.cs "
        "--skip-validate-spec "
        "--additional-properties=conditionalSerialization=false ",
        shell=True
    )
