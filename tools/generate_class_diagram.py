#!/usr/bin/env python3
"""
Generate PlantUML class diagrams for lliquidlink.{core,client,server}
using pyreverse, and write them to Docs/ClassDiagram.md.

Usage (from the Calces project root or WebSocketLib):
  python Assets/Editor/WebSocketLib/tools/generate_class_diagram.py
"""

import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import textwrap

import heapq

import networkx as nx

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WEBSOCKETLIB = os.path.normpath(os.path.join(SCRIPT_DIR, ".."))
OUT_FILE = os.path.join(WEBSOCKETLIB, "Docs", "ClassDiagram.md")
HTML_FILE = os.path.join(WEBSOCKETLIB, "Docs", "ClassDiagram.html")

PACKAGES = [
    os.path.join("Python", "lliquidlink", "core"),
    os.path.join("Python", "lliquidlink", "client"),
    os.path.join("Python", "lliquidlink", "server"),
]

_REL_RE = re.compile(
    r'^(\S+)\s+(?:"[^"]*"\s+)?(--\|>|<\|--|--\*|\*--|--o|o--|o->|-->|\.\.>|\+--)\s+(?:"[^"]*"\s+)?(\S+)(?:\s*:\s*.+)?$'
)

_REL_PRIORITY = {"--|>": 0, "<|--": 0, "--*": 1, "*--": 1, "--o": 2, "o--": 2, "o->": 2, "-->": 3, "..>": 4, "+--": 1}

# Matches class/interface/abstract class/enum block opening lines
_BLOCK_START_RE = re.compile(
    r'^(?:abstract\s+)?(?:class|interface|enum)\s+(?:"[^"]*"\s+as\s+)?(\S+)'
)

# Matches puml-gen namespace wrapper lines
_NS_START_RE = re.compile(r'^namespace\s+\S+\s*\{')

CSHARP_SECTIONS = [
    ("UniLiquidLink", os.path.join(WEBSOCKETLIB, "CSharp", "UniLiquidLink")),
    ("LLiquidLink", os.path.join(WEBSOCKETLIB, "CSharp", "LLiquidLink")),
]

DROP_CLASSES = [
    "Action`",
    "List`",
    "Dictionary`",
    "Func`",
    "HashSet`",
    "ConcurrentQueue`",
]

def _parse_plantuml(puml):
    """Parse PlantUML text into (header_lines, class_blocks, relation_lines).

    header_lines: lines before class definitions (@startuml, set ..., etc.)
    class_blocks: list of (fqn, [lines]) for each class definition block
    relation_lines: relationship lines (--|>, --*, --o, -->, ..>)
    """
    lines = puml.splitlines()
    header = []
    class_blocks = []
    relations = []

    in_class = False
    current_block = []
    current_fqn = None

    for line in lines:
        if line.startswith("@enduml"):
            if in_class:
                class_blocks.append((current_fqn, current_block))
            break

        if line.startswith("@startuml") or line.startswith("set ") or line.startswith("hide "):
            header.append(line)
            continue

        if in_class:
            current_block.append(line)
            if line.strip() == "}":
                class_blocks.append((current_fqn, current_block))
                in_class = False
                current_block = []
                current_fqn = None
            continue

        m = _BLOCK_START_RE.match(line)
        if m:
            current_fqn = m.group(1)
            in_class = True
            current_block = [line]
            continue

        if _REL_RE.match(line):
            relations.append(line)

    return header, class_blocks, relations


def _build_dep_graph(relations):
    """Build a directed graph from PlantUML relation lines.

    Edge A→B means "A is more foundational than B" (A should appear first).
    """
    G = nx.DiGraph()
    for line in relations:
        m = _REL_RE.match(line)
        if not m:
            continue
        src, op, dst = m.group(1), m.group(2), m.group(3)
        G.add_node(src)
        G.add_node(dst)
        if op in ("--*", "--o", "+--"):
            # dst contains/aggregates src → dst should appear first
            G.add_edge(dst, src)
        elif op in ("*--", "o--", "o->"):
            # src contains/aggregates dst → src should appear first
            G.add_edge(src, dst)
        elif op == "<|--":
            # src is parent; dst is child → src should appear first
            G.add_edge(src, dst)
        else:
            # --|>, -->, ..> : dst is more foundational (parent/dependency)
            G.add_edge(dst, src)
    return G


def _lexicographic_topo_sort(G):
    """Topological sort with alphabetical tie-breaking (Kahn's algorithm)."""
    in_degree = dict(G.in_degree())
    heap = [n for n, d in in_degree.items() if d == 0]
    heapq.heapify(heap)
    result = []
    while heap:
        node = heapq.heappop(heap)
        result.append(node)
        for successor in G.successors(node):
            in_degree[successor] -= 1
            if in_degree[successor] == 0:
                heapq.heappush(heap, successor)
    if len(result) != len(G):
        raise nx.NetworkXUnfeasible("Graph contains a cycle")
    return result


def _inject_skinparam(puml):
    """Insert `skinparam linetype ortho` after the @startuml/set header lines."""
    lines = puml.splitlines()
    insert_at = 0
    for i, line in enumerate(lines):
        if line.startswith("@startuml") or line.startswith("set "):
            insert_at = i + 1
        else:
            break
    # lines.insert(insert_at, "skinparam linetype ortho")
    return "\n".join(lines)

def drop_classes(class_blocks, relations):
    _relations = []
    _class_blocks = []
    def is_drop(txt):
        for drop_class in DROP_CLASSES:
            if drop_class in txt:
                return True
        return False

    for line in relations:
        m = _REL_RE.match(line)
        if not m:
            _relations.append(line)
            continue
        src, op, dst = m.group(1), m.group(2), m.group(3)
        if not is_drop(src) and not is_drop(dst):
            _relations.append(line)

    for class_block in class_blocks:
        txt = str(class_block)
        if not is_drop(txt):
            _class_blocks.append(class_block)
    return _class_blocks, _relations

def sort_plantuml(puml):
    """Sort class definitions and relation lines by topological dependency order."""
    header, class_blocks, relations = _parse_plantuml(puml)
    G = _build_dep_graph(relations)

    try:
        topo_order = _lexicographic_topo_sort(G)
    except nx.NetworkXUnfeasible:
        print("[WARN] Cycle in class dependency graph; falling back to alphabetical sort", file=sys.stderr)
        topo_order = sorted(G.nodes())

    topo_rank = {name: i for i, name in enumerate(topo_order)}
    n = len(topo_order)

    sorted_blocks = sorted(
        class_blocks,
        key=lambda item: (topo_rank.get(item[0], n), item[0]),
    )

    def rel_key(line):
        m = _REL_RE.match(line)
        if not m:
            return (99, n, n, line)
        src, op, dst = m.group(1), m.group(2), m.group(3)
        return (_REL_PRIORITY.get(op, 99), topo_rank.get(src, n), topo_rank.get(dst, n), line)

    def _flip_arrow(line):
        m = _REL_RE.match(line)
        if not m:
            return line
        src, op, dst = m.group(1), m.group(2), m.group(3)
        if op == "--*":
            op = "*--"
        elif op == "--o":
            op = "o--"
        else:
            return line
        label = line[m.end(3):]
        return f"{dst} {op} {src}{label}"

    sorted_relations = [_flip_arrow(l) for l in sorted(relations, key=rel_key)]

    result = header[:]
    # result.append("skinparam linetype ortho")
    result.append("")
    for _, block_lines in sorted_blocks:
        result.extend(block_lines)
    result.append("")
    result.extend(sorted_relations)
    result.append("@enduml")
    return "\n".join(result)


def run_pyreverse(package_name):
    """Run pyreverse for one package and return (classes_puml, packages_puml).

    Returns (None, None) if pyreverse fails.
    Generated .puml files are read and deleted after this call.
    """
    result = subprocess.run(
        [
            "pyreverse", "-o", "puml", "-p", package_name, package_name,
            "--filter-mode", "ALL", 
            "-k",
        ],
        cwd=WEBSOCKETLIB,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        print(f"[WARN] pyreverse failed for {package_name}:\n{result.stderr}", file=sys.stderr)
        return None, None

    classes_path = os.path.join(WEBSOCKETLIB, f"classes_{package_name}.puml")
    packages_path = os.path.join(WEBSOCKETLIB, f"packages_{package_name}.puml")

    classes_puml = None
    packages_puml = None

    if os.path.exists(classes_path):
        with open(classes_path, encoding="utf-8") as f:
            classes_puml = f.read()
        os.remove(classes_path)

    if os.path.exists(packages_path):
        with open(packages_path, encoding="utf-8") as f:
            packages_puml = f.read()
        os.remove(packages_path)

    if classes_puml:
        classes_puml = sort_plantuml(classes_puml)

    if packages_puml:
        packages_puml = _inject_skinparam(packages_puml)

    return classes_puml, packages_puml


def _strip_relation_label(line):
    """Remove label from a relation line, returning 'Src arrow Dst'."""
    m = _REL_RE.match(line)
    if not m:
        return line
    return f"{m.group(1)} {m.group(2)} {m.group(3)}"


def _flatten_namespaces(puml):
    """Strip namespace wrapper blocks from puml-gen output.

    Removes 'namespace Foo {' opener and its matching '}' closer without
    touching braces inside class/interface/enum bodies.
    """
    lines = puml.splitlines()
    result = []
    ns_depth = 0
    class_brace_depth = 0
    for line in lines:
        stripped = line.strip()
        if _NS_START_RE.match(stripped):
            ns_depth += 1
            continue
        if class_brace_depth == 0 and ns_depth > 0 and stripped == "}":
            ns_depth -= 1
            continue
        class_brace_depth += stripped.count("{") - stripped.count("}")
        class_brace_depth = max(0, class_brace_depth)
        result.append(line)
    return "\n".join(result)


def run_csharp_plantuml(section_name, src_path):
    """Run puml-gen for a C# directory and return (classes_puml, None).

    Collects all .puml files emitted to a temp dir, merges their class blocks
    and relations into one diagram, deduplicates, then sorts it.
    Returns (None, None) if puml-gen fails or no classes are found.
    """
    out_dir = tempfile.mkdtemp()
    try:
        rel_src = os.path.relpath(src_path, WEBSOCKETLIB)
        env = os.environ.copy()
        env["DOTNET_ROLL_FORWARD"] = "LatestMajor"
        result = subprocess.run(
            [
                "puml-gen", rel_src, out_dir, "-dir", "-excludePaths", "obj,bin",
                # "-ignore", "public,private,protected,internal", 
                "-createAssociation",
            ],
            cwd=WEBSOCKETLIB,
            capture_output=True,
            text=True,
            env=env,
        )
        if result.returncode != 0:
            print(f"[WARN] puml-gen failed for {section_name}:\n{result.stderr}", file=sys.stderr)
            return None, None

        combined_header = [f"@startuml classes_{section_name}", "set namespaceSeparator none", "hide members"]
        combined_blocks = []
        combined_relations = []

        for root, _, files in os.walk(out_dir):
            for fname in sorted(files):
                if not fname.endswith(".puml"):
                    continue
                fpath = os.path.join(root, fname)
                with open(fpath, encoding="utf-8") as f:
                    raw = f.read()
                flat = _flatten_namespaces(raw)
                _, blocks, rels = _parse_plantuml(flat)
                blocks, rels = drop_classes(blocks, rels)
                if blocks:
                    combined_blocks.extend(blocks)
                if rels:
                    combined_relations.extend(rels)

        if not combined_blocks:
            print(f"[WARN] No classes found for {section_name}", file=sys.stderr)
            return None, None

        combined_relations = list(dict.fromkeys(combined_relations))
        combined_relations = list(dict.fromkeys(
            _strip_relation_label(r) for r in combined_relations
        ))

        parts = combined_header + [""]
        for _, block_lines in combined_blocks:
            parts.extend(block_lines)
        parts += [""] + combined_relations + ["@enduml"]

        return sort_plantuml("\n".join(parts)), None
    finally:
        shutil.rmtree(out_dir, ignore_errors=True)


def _append_diagram(lines, title, puml_content):
    if not puml_content:
        return
    lines.append(f"### {title}")
    lines.append("")
    lines.append("```plantuml")
    lines.append(puml_content.strip())
    lines.append("```")
    lines.append("")


def render_markdown(diagrams):
    """Render collected PlantUML diagrams as a Markdown string."""
    lines = []
    # lines.append(textwrap.dedent("""
    #     ---
    #     export_on_save:
    #       html: true
    #     ---                 
    # """).strip())
    lines += ["# Class Diagrams", ""]

    for pkg_name, classes_puml, packages_puml in diagrams:
        lines.append(f"## {pkg_name}")
        lines.append("")
        _append_diagram(lines, "Classes", classes_puml)
        _append_diagram(lines, "Packages", packages_puml)

    return "\n".join(lines)


def render_html(md_content):
    """Generate ClassDiagram.html via pandoc with plantuml_pandoc_filter.py."""
    filter_path = os.path.join(SCRIPT_DIR, "plantuml_pandoc_filter.py")
    try:
        cmd = "powershell -Command pandoc --from markdown --to html5"
        cmd += f" --metadata title=Class Diagrams -o {HTML_FILE}"
        cmd += f" --filter {filter_path}"
        print(f"call: {cmd}")
        p1 = subprocess.run(
            cmd,
            input=md_content,
            text=True,
            shell=True,
        )
        if p1.returncode != 0:
            print("[WARN] pandoc html conversion failed", file=sys.stderr)
            return
    except FileNotFoundError as e:
        print(f"[WARN] render_html skipped: {e}", file=sys.stderr)
        return
    print(f"[DONE] HTML generated: {HTML_FILE}")


def main():
    diagrams = []

    for pkg in PACKAGES:
        break
        pkg_dir = os.path.join(WEBSOCKETLIB, pkg)
        if not os.path.isdir(pkg_dir):
            print(f"[WARN] Package not found: {pkg_dir}", file=sys.stderr)
            continue

        print(f"[{PACKAGES.index(pkg) + 1}/{len(PACKAGES)}] Running pyreverse for {pkg}...")
        classes_puml, packages_puml = run_pyreverse(pkg)
        diagrams.append((pkg, classes_puml, packages_puml))

    for section_name, src_path in CSHARP_SECTIONS:
        if not os.path.isdir(src_path):
            print(f"[WARN] C# source not found: {src_path}", file=sys.stderr)
            continue
        print(f"Running puml-gen for {section_name}...")
        classes_puml, packages_puml = run_csharp_plantuml(section_name, src_path)
        diagrams.append((section_name, classes_puml, packages_puml))

    md = render_markdown(diagrams)

    os.makedirs(os.path.dirname(OUT_FILE), exist_ok=True)
    with open(OUT_FILE, "w", encoding="utf-8") as f:
        f.write(md)

    print(f"\n[DONE] Class diagram generated: {OUT_FILE}")

    render_html(md)


if __name__ == "__main__":
    main()
