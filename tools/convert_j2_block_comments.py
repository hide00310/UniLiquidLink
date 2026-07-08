"""
Rewrite .j2 template files to use the C#-comment-style Jinja2 block delimiters
("//{% ... %}" / "//{# ... #}") that tools/schema/converters.py's jinja2.Environment
now expects, instead of the default "{% ... %}" / "{# ... #}".

Usage:
    python tools/convert_j2_block_comments.py
    python tools/convert_j2_block_comments.py --dry-run
"""

import argparse
import glob
import os
import re
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TEMPLATE_GLOB = os.path.join(SCRIPT_DIR, "templates", "*.j2")

BLOCK_LINE_RE = re.compile(r"^(\s*)\{%(.*)%\}(\s*)$")
COMMENT_LINE_RE = re.compile(r"^(\s*)\{#(.*)#\}(\s*)$")


def convert_line(line):
    ending = ""
    if line.endswith("\n"):
        line = line[:-1]
        ending = "\n"

    match = BLOCK_LINE_RE.match(line)
    if match:
        indent, body, trailing = match.groups()
        return f"{indent}//{{%{body}%}}{trailing}{ending}"

    match = COMMENT_LINE_RE.match(line)
    if match:
        indent, body, trailing = match.groups()
        return f"{indent}//{{#{body}#}}{trailing}{ending}"

    return line + ending


def convert_file(path):
    with open(path, "r", encoding="utf-8", newline="") as f:
        lines = f.readlines()
    return "".join(convert_line(line) for line in lines)


def main():
    parser = argparse.ArgumentParser(
        description="Convert .j2 templates' block/comment tags to C#-comment-style delimiters"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print converted output to stdout without writing files",
    )
    args = parser.parse_args()

    paths = sorted(glob.glob(TEMPLATE_GLOB))
    if not paths:
        sys.exit(f"[ERROR] No .j2 files found under {TEMPLATE_GLOB}")

    for path in paths:
        converted = convert_file(path)
        if args.dry_run:
            print(f"=== {path} ===")
            print(converted)
            continue
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(converted)
        print(f"[OK] {path}")


if __name__ == "__main__":
    main()
