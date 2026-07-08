#!/usr/bin/env python3
"""
Pandoc JSON filter: render plantuml CodeBlock to inline SVG RawBlock.

Called by generate_class_diagram.py via subprocess pipe (pandoc -F protocol).
Reads pandoc AST JSON from stdin, writes transformed AST JSON to stdout.
"""

import subprocess
from pandocfilters import toJSONFilter, RawBlock

PLANT_UML_PATH = r"C:\App\plantuml\plantuml-mit.jar"
def _render_svg(code):
    """Run plantuml -pipe -tsvg and return SVG string, or None on failure."""
    try:
        r = subprocess.run(
            f"java -jar {PLANT_UML_PATH} -pipe",
            input=code,
            capture_output=True,
            text=True,
            shell=True,
            encoding="utf-8",
        )
        if r.returncode == 0 and r.stdout and "<?xml" in r.stdout:
            return r.stdout
    except FileNotFoundError:
        pass
    return None


def plantuml_filter(key, value, fmt, meta):
    if key == "CodeBlock":
        (ident, classes, kvs), code = value
        if "plantuml" in classes:
            svg = _render_svg(code)
            if svg:
                return RawBlock("html", f'<div class="plantuml">{svg}</div>')


if __name__ == "__main__":
    toJSONFilter(plantuml_filter)
