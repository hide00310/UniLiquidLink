#!/usr/bin/env python3
"""
Generate a Markdown API summary from lliquidlink.{core,client,server} docstrings.

Steps:
  1. Walk each package directory and parse .py files with the ast module.
  2. Extract module, class, and function docstrings plus signatures.
  3. Write a combined Markdown to Docs/LLiquidLinkPythonAPI.md.

Usage (from the Calces project root or WebSocketLib):
  python Assets/Editor/WebSocketLib/tools/generate_python_docs.py
"""

import ast
import os
import re
import sys
from collections import OrderedDict

# ---------------------------------------------------------------------------
# Path resolution
# ---------------------------------------------------------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WEBSOCKETLIB = os.path.normpath(os.path.join(SCRIPT_DIR, ".."))

PYTHON_DIR = os.path.join(WEBSOCKETLIB, "Python")

PACKAGES = [
    ("lliquidlink.core",   os.path.join(PYTHON_DIR, "lliquidlink", "core")),
    ("lliquidlink.client", os.path.join(PYTHON_DIR, "lliquidlink", "client")),
    ("lliquidlink.server", os.path.join(PYTHON_DIR, "lliquidlink", "server")),
]
OUT_FILE = os.path.join(WEBSOCKETLIB, "Docs", "LLiquidLinkPythonAPI.md")

# File order within each package (controls output order)
_FILE_ORDER = {
    "lliquidlink.core": [
        "__init__.py",
        "_rpc.py",
    ],
    "lliquidlink.client": [
        "__init__.py",
        "_client.py",
        "_proxy.py",
        "_serialization.py",
        "_transports.py",
        "models.py",
    ],
    "lliquidlink.server": [
        "__init__.py",
        "ipc_bridge.py",
        "resolver.py",
        "_transport.py",
        "server.py",
        "__main__.py",
    ],
}

# ---------------------------------------------------------------------------
# AST helpers
# ---------------------------------------------------------------------------

def _first_line(docstring):
    """Return the first non-empty line of a docstring."""
    if not docstring:
        return ""
    for line in docstring.splitlines():
        line = line.strip()
        if line:
            return line
    return ""


def _has_decorator(node, name):
    """Return True if the function node has a decorator with the given name."""
    for dec in node.decorator_list:
        if isinstance(dec, ast.Name) and dec.id == name:
            return True
        if isinstance(dec, ast.Attribute) and dec.attr == name:
            return True
    return False


def _method_kind(node):
    """Return 'constructor', 'property', or 'method' for a function/async-function node."""
    if node.name == "__init__":
        return "constructor"
    if _has_decorator(node, "property"):
        return "property"
    return "method"


def _signature(node, is_property=False):
    """Return a display signature string for a function, omitting 'self' and 'cls'.

    Properties are shown without parentheses. Uses ast.unparse (Python 3.9+).
    """
    if is_property:
        return node.name

    args = node.args
    # Collect positional args excluding self/cls
    all_args = args.posonlyargs + args.args
    skip = {"self", "cls"}
    kept = [a for a in all_args if a.arg not in skip]

    # Rebuild a minimal args object for ast.unparse
    new_args = ast.arguments(
        posonlyargs=[],
        args=kept,
        vararg=args.vararg,
        kwonlyargs=args.kwonlyargs,
        kw_defaults=args.kw_defaults,
        kwarg=args.kwarg,
        defaults=args.defaults[-len(kept):] if args.defaults else [],
    )
    sig = ast.unparse(new_args)
    return f"{node.name}({sig})"


def _is_public(name):
    """Return True for names that should appear in public API docs.

    Includes dunder methods (like __call__, __await__) but excludes
    single-underscore private names.
    """
    if name.startswith("__") and name.endswith("__"):
        return True
    return not name.startswith("_")


# ---------------------------------------------------------------------------
# Per-module parsing
# ---------------------------------------------------------------------------

def parse_module(pkg_name, rel_path, abs_path):
    """Parse one .py file and return a dict describing its public API.

    Returns:
        {
            'module': 'pkg.submodule',
            'module_doc': str,
            'classes': OrderedDict of class_name -> class_data,
            'functions': list of (signature, first_line_doc),
        }
    """
    with open(abs_path, encoding="utf-8-sig") as fh:
        source = fh.read()

    try:
        tree = ast.parse(source, filename=abs_path)
    except SyntaxError as exc:
        print(f"[WARN] Skipping {abs_path}: {exc}", file=sys.stderr)
        return None

    # Derive dotted module name
    stem = os.path.splitext(rel_path)[0].replace(os.sep, ".")
    module_name = f"{pkg_name}.{stem}" if stem != "__init__" else pkg_name

    module_doc = ast.get_docstring(tree) or ""

    classes = OrderedDict()
    functions = []

    for node in tree.body:
        if isinstance(node, ast.ClassDef) and _is_public(node.name):
            class_doc = ast.get_docstring(node) or ""
            class_data = {
                "summary": _first_line(class_doc),
                "constructors": [],
                "properties": [],
                "methods": [],
            }
            for item in node.body:
                if not isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef)):
                    continue
                if not _is_public(item.name):
                    continue
                doc = _first_line(ast.get_docstring(item) or "")
                kind = _method_kind(item)
                sig = _signature(item, is_property=(kind == "property"))
                if kind == "constructor":
                    class_data["constructors"].append((sig, doc))
                elif kind == "property":
                    class_data["properties"].append((sig, doc))
                else:
                    class_data["methods"].append((sig, doc))
            classes[node.name] = class_data

        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if not _is_public(node.name):
                continue
            doc = _first_line(ast.get_docstring(node) or "")
            if not doc:
                continue
            functions.append((_signature(node), doc))

    return {
        "module": module_name,
        "module_doc": _first_line(module_doc),
        "classes": classes,
        "functions": functions,
    }


# ---------------------------------------------------------------------------
# Collection
# ---------------------------------------------------------------------------

def collect_packages():
    """Walk both packages in defined order and return a list of module dicts."""
    results = []
    for pkg_name, pkg_dir in PACKAGES:
        if not os.path.isdir(pkg_dir):
            print(f"[WARN] Package directory not found: {pkg_dir}", file=sys.stderr)
            continue

        order = _FILE_ORDER.get(pkg_name, [])
        # Files in defined order first, then any remaining files alphabetically
        seen = set()
        filenames = []
        for name in order:
            if os.path.exists(os.path.join(pkg_dir, name)):
                filenames.append(name)
                seen.add(name)
        for name in sorted(os.listdir(pkg_dir)):
            if name.endswith(".py") and name not in seen:
                filenames.append(name)

        for filename in filenames:
            abs_path = os.path.join(pkg_dir, filename)
            data = parse_module(pkg_name, filename, abs_path)
            if data is None:
                continue
            # Skip files with no classes and no functions
            if not data["classes"] and not data["functions"]:
                continue
            results.append(data)

    return results


# ---------------------------------------------------------------------------
# Markdown rendering
# ---------------------------------------------------------------------------

def render_markdown(modules):
    """Render collected module data as a Markdown string."""
    lines = ["# LLiquidLink Python API Summary", ""]

    for mod in modules:
        module_name = mod["module"]

        # --- Classes ---
        for class_name, data in mod["classes"].items():
            lines.append(f"## {class_name}")
            lines.append(f"*Module: {module_name}*")
            lines.append("")
            if data["summary"]:
                lines.append(data["summary"])
                lines.append("")

            def section(title, items):
                if not items:
                    return
                lines.append(f"### {title}")
                for sig, doc in items:
                    bullet = f"- `{sig}`"
                    if doc:
                        bullet += f" — {doc}"
                    lines.append(bullet)
                lines.append("")

            section("Constructors", data["constructors"])
            section("Properties",   data["properties"])
            section("Methods",      data["methods"])

            lines.append("---")
            lines.append("")

        # --- Module-level functions ---
        if mod["functions"]:
            lines.append(f"## Functions ({module_name})")
            lines.append("")
            for sig, doc in mod["functions"]:
                bullet = f"- `{sig}`"
                if doc:
                    bullet += f" — {doc}"
                lines.append(bullet)
            lines.append("")
            lines.append("---")
            lines.append("")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    print("[1/2] Collecting docstrings from Python packages...")
    modules = collect_packages()

    print("[2/2] Rendering Markdown...")
    md = render_markdown(modules)

    os.makedirs(os.path.dirname(OUT_FILE), exist_ok=True)
    with open(OUT_FILE, "w", encoding="utf-8") as fh:
        fh.write(md)

    print(f"\n[DONE] Documentation generated: {OUT_FILE}")


if __name__ == "__main__":
    main()
