#!/usr/bin/env python3
"""
Pandoc JSON filter: render plantuml CodeBlock to inline SVG RawBlock.

Called by generate_class_diagram.py via subprocess pipe (pandoc -F protocol).
Reads pandoc AST JSON from stdin, writes transformed AST JSON to stdout.

NOTE: stdout is reserved for the pandoc filter JSON protocol, so all
diagnostic logging must go to a file (never to stdout/stderr of this
process, since pandoc treats stderr output as filter noise too).
"""

import logging
import os
import subprocess

from pandocfilters import toJSONFilter, RawBlock

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
LOG_FILE = os.path.join(SCRIPT_DIR, "plantuml_pandoc_filter.log")

logging.basicConfig(
    filename=LOG_FILE,
    filemode="a",
    level=logging.DEBUG,
    format="%(asctime)s [%(levelname)s] %(message)s",
)
logger = logging.getLogger(__name__)

PLANT_UML_PATH = r"C:\App\plantuml\plantuml-mit.jar"


def _render_svg(code):
    """Run plantuml -pipe -tsvg and return SVG string, or None on failure."""
    logger.debug("Rendering plantuml block (%d chars): %r", len(code), code[:200])
    try:
        # Capture as raw bytes (not text=True) so a non-SVG (e.g. PNG) reply
        # from plantuml can't crash the subprocess reader thread with a
        # UnicodeDecodeError; decode explicitly afterwards instead.
        r = subprocess.run(
            f"java -jar {PLANT_UML_PATH} -pipe -tsvg",
            input=code.encode("utf-8"),
            capture_output=True,
            shell=True,
        )
        stdout_text = r.stdout.decode("utf-8", errors="replace")
        stderr_text = r.stderr.decode("utf-8", errors="replace")
        logger.debug(
            "plantuml finished: returncode=%s stdout_bytes=%d stderr_bytes=%d",
            r.returncode, len(r.stdout), len(r.stderr),
        )
        # plantuml's SVG output may or may not include an XML prolog
        # depending on version, so just check for the root <svg> element.
        if r.returncode == 0 and stdout_text and "<svg" in stdout_text:
            logger.debug("plantuml rendering succeeded (%d chars svg)", len(stdout_text))
            return stdout_text
        logger.error(
            "plantuml rendering failed: returncode=%s stderr=%s stdout_head=%r",
            r.returncode, stderr_text, stdout_text[:200],
        )
    except FileNotFoundError:
        logger.exception("plantuml executable not found (java missing or jar path wrong)")
    return None


def plantuml_filter(key, value, fmt, meta):
    if key == "CodeBlock":
        (ident, classes, kvs), code = value
        if "plantuml" in classes:
            svg = _render_svg(code)
            if svg:
                return RawBlock("html", f'<div class="plantuml">{svg}</div>')
            logger.warning("Skipping plantuml block %r due to render failure", ident)


if __name__ == "__main__":
    logger.info("=== plantuml_pandoc_filter started ===")
    toJSONFilter(plantuml_filter)
    logger.info("=== plantuml_pandoc_filter finished ===")
