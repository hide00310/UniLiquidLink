#!/usr/bin/env python3
"""
Generate a single summary-only Markdown from UniLiquidLink C# XML documentation.

Steps:
  1. Build UniLiquidLink.csproj via dotnet msbuild to produce UniLiquidLink.xml.
  2. Parse the XML and extract <summary> text for each type and member.
  3. Write a single combined Markdown to Docs/Server.md.

Usage (from the Calces project root or WebSocketLib):
  python Assets/Editor/WebSocketLib/tools/generate_docs.py
"""

import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import OrderedDict

# ---------------------------------------------------------------------------
# Path resolution
# ---------------------------------------------------------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# tools/ -> UniLiquidLink/ -> WebSocketLib/ -> Editor/ -> Assets/ -> <project root>
PROJECT_ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "..", "..", ".."))
WEBSOCKETLIB = os.path.normpath(os.path.join(SCRIPT_DIR, ".."))

CSPROJ = os.path.join(PROJECT_ROOT, "UniLiquidLink.csproj")
BUILD_DIR = os.path.join(PROJECT_ROOT, "Temp", "bin", "Debug")
XML_PATH = os.path.join(BUILD_DIR, "UniLiquidLink.xml")
OUT_FILE = os.path.join(WEBSOCKETLIB, "Docs", "Server.md")

# ---------------------------------------------------------------------------
# Step 1: Build and generate XML doc
# ---------------------------------------------------------------------------

def build_xml():
    if not os.path.exists(CSPROJ):
        sys.exit(f"[ERROR] Project file not found: {CSPROJ}\n"
                 "       Open the project in Unity once to generate the .csproj.")

    os.makedirs(BUILD_DIR, exist_ok=True)
    # Remove stale XML so we can detect failure to produce it.
    if os.path.exists(XML_PATH):
        os.remove(XML_PATH)

    print("[1/2] Building UniLiquidLink with XML documentation...")
    result = subprocess.run(
        ["dotnet", "msbuild", CSPROJ,
         "-nologo", "-v:quiet",
         "-p:Configuration=Debug",
         f"-p:DocumentationFile={XML_PATH}"],
        capture_output=True, text=True
    )
    if result.returncode != 0:
        if result.stdout:
            sys.stderr.write(result.stdout)
        sys.stderr.write(result.stderr)
        sys.exit("[ERROR] Build failed.")

    if not os.path.exists(XML_PATH):
        sys.exit("[ERROR] XML documentation file was not produced.")

# ---------------------------------------------------------------------------
# Step 2: XML parsing helpers
# ---------------------------------------------------------------------------

# Map common C# type FQNs to short names
_TYPE_SHORTCUTS = {
    "System.String": "string",
    "System.Int32": "int",
    "System.Int64": "long",
    "System.Boolean": "bool",
    "System.Single": "float",
    "System.Double": "double",
    "System.Object": "object",
    "System.Void": "void",
    "System.Byte": "byte",
}

def _shorten_type(fqn):
    """Convert a fully-qualified type name to a short display name."""
    s = _TYPE_SHORTCUTS.get(fqn)
    if s:
        return s
    # Strip namespace, keep only the last segment (and handle generics)
    s = re.sub(r"[A-Za-z_][A-Za-z0-9_.]*\.", lambda m: "", fqn)
    return s


def _shorten_params(raw_params):
    """
    Convert '(System.String,System.Delegate)' to '(string, Delegate)'.
    Returns '' if raw_params is empty / not present.
    """
    if not raw_params or raw_params == "()":
        return "()"
    inner = raw_params.strip("()")
    if not inner:
        return "()"
    parts = []
    depth = 0
    current = []
    for ch in inner:
        if ch in "{([<":
            depth += 1
            current.append(ch)
        elif ch in "})]>":
            depth -= 1
            current.append(ch)
        elif ch == "," and depth == 0:
            parts.append("".join(current).strip())
            current = []
        else:
            current.append(ch)
    if current:
        parts.append("".join(current).strip())
    return "(" + ", ".join(_shorten_type(p) for p in parts) + ")"


def _extract_summary(member_el):
    """Return normalized plain text of the <summary> child, or empty string."""
    summary_el = member_el.find("summary")
    if summary_el is None:
        return ""

    def node_text(el):
        parts = []
        if el.text:
            parts.append(el.text)
        for child in el:
            tag = child.tag
            if tag == "see":
                cref = child.get("cref", "")
                # T:Ns.ClassName -> ClassName
                short = cref.split(".")[-1].lstrip("TMPE:")
                # strip leading kind prefix if present e.g. "T:Foo" -> "Foo"
                short = re.sub(r"^[TMPE]:", "", cref).split(".")[-1]
                parts.append(short)
            elif tag == "paramref":
                parts.append(f"`{child.get('name', '')}`")
            elif tag == "c":
                parts.append(f"`{(child.text or '').strip()}`")
            elif tag in ("typeparamref",):
                parts.append(child.get("name", ""))
            else:
                parts.append((child.text or "").strip())
            if child.tail:
                parts.append(child.tail)
        return "".join(parts)

    raw = node_text(summary_el)
    # Normalize whitespace / newlines
    return re.sub(r"\s+", " ", raw).strip()


def _parse_member_id(name):
    """
    Parse a member `name` attribute.
    Returns (kind, type_fqn, member_display) where kind is T/M/P/E/F.
    """
    m = re.match(r"^([TMPEF]):(.*)", name)
    if not m:
        return None, None, None
    kind = m.group(1)
    rest = m.group(2)

    if kind == "T":
        return kind, rest, None

    # For M/P/E/F: split off the type path
    # e.g. UniLiquidLink.RpcBus.Register(System.String)
    # Find the last '.' before '(' (or end) that separates type from member name
    paren_idx = rest.find("(")
    if paren_idx == -1:
        dot_idx = rest.rfind(".")
        type_fqn = rest[:dot_idx]
        member_name = rest[dot_idx + 1:]
        params_str = ""
    else:
        before_paren = rest[:paren_idx]
        dot_idx = before_paren.rfind(".")
        type_fqn = before_paren[:dot_idx]
        member_name = before_paren[dot_idx + 1:]
        params_str = rest[paren_idx:]

    # Display: shorten params
    display = member_name + _shorten_params(params_str) if params_str else member_name
    # Constructor alias
    display = display.replace("#ctor", "__init__")
    return kind, type_fqn, display

# ---------------------------------------------------------------------------
# Step 3: Parse XML and build data structure
# ---------------------------------------------------------------------------

def parse_xml(xml_path):
    """
    Returns OrderedDict: type_fqn -> {
        'summary': str,
        'constructors': [(display, summary), ...],
        'properties':   [(display, summary), ...],
        'methods':      [(display, summary), ...],
        'events':       [(display, summary), ...],
    }
    """
    tree = ET.parse(xml_path)
    root = tree.getroot()

    types = OrderedDict()  # type_fqn -> dict

    def ensure_type(fqn):
        if fqn not in types:
            types[fqn] = {
                "summary": "",
                "constructors": [],
                "properties": [],
                "methods": [],
                "events": [],
            }
        return types[fqn]

    for member in root.iter("member"):
        name = member.get("name", "")
        kind, type_fqn, display = _parse_member_id(name)
        if kind is None:
            continue

        summary = _extract_summary(member)

        if kind == "T":
            entry = ensure_type(type_fqn)
            entry["summary"] = summary
        elif kind in ("M", "P", "E", "F"):
            if type_fqn not in types:
                ensure_type(type_fqn)
            entry = types[type_fqn]
            if kind == "M" and ("#ctor" in display or "__init__" in display):
                entry["constructors"].append((display, summary))
            elif kind == "P":
                entry["properties"].append((display, summary))
            elif kind == "E":
                entry["events"].append((display, summary))
            else:
                entry["methods"].append((display, summary))

    return types

# ---------------------------------------------------------------------------
# Step 3: Render Markdown
# ---------------------------------------------------------------------------

def render_markdown(types):
    lines = ["# UniLiquidLink — API Summary", ""]

    for fqn, data in types.items():
        # Derive short class name and namespace
        parts = fqn.rsplit(".", 1)
        class_name = parts[-1]
        namespace = parts[0] if len(parts) > 1 else ""

        lines.append(f"## {class_name}")
        if namespace:
            lines.append(f"*Namespace: {namespace}*")
        lines.append("")
        if data["summary"]:
            lines.append(data["summary"])
            lines.append("")

        def section(title, items):
            if not items:
                return
            lines.append(f"### {title}")
            for display, summary in items:
                bullet = f"- `{display}`"
                if summary:
                    bullet += f" — {summary}"
                lines.append(bullet)
            lines.append("")

        section("Constructors", data["constructors"])
        section("Properties",   data["properties"])
        section("Methods",      data["methods"])
        section("Events",       data["events"])

        lines.append("---")
        lines.append("")

    return "\n".join(lines)

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    build_xml()

    print("[2/2] Parsing XML and generating summary Markdown...")
    types = parse_xml(XML_PATH)
    md = render_markdown(types)

    os.makedirs(os.path.dirname(OUT_FILE), exist_ok=True)
    with open(OUT_FILE, "w", encoding="utf-8") as f:
        f.write(md)

    print(f"\n[DONE] Documentation generated: {OUT_FILE}")


if __name__ == "__main__":
    main()
