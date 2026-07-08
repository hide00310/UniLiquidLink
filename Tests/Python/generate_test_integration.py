"""
Generate test_integration.py from IntegrationClient.run_* methods.
Run after adding or removing a run_* method in integration_client.py.

Usage:
    cd Assets/Editor/WebSocketLib
    python Tests/Python/generate_test_integration.py
"""
import ast
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(SCRIPT_DIR, "integration_client.py")
TARGET = os.path.join(SCRIPT_DIR, "test_integration.py")

HEADER = """\
\"\"\"
Integration tests (auto-generated -- do not edit manually).
Edit integration_client.py and re-run generate_test_integration.py instead.

Prerequisites:
  - Unity Editor must be running
  - Click "UniLiquidLink > Start Integration Test Server" in the Unity menu (port 8700)

All tests are skipped if Unity is not available.
\"\"\"
import pytest
from conftest import assert_golden
from integration_client import IntegrationClient, client  # noqa: F401

"""


def find_run_methods(source):
    tree = ast.parse(source)
    for node in tree.body:
        if isinstance(node, ast.ClassDef) and node.name == "IntegrationClient":
            return sorted(
                m.name for m in node.body
                if isinstance(m, ast.FunctionDef) and m.name.startswith("run_")
            )
    return []


def make_test_block(run_methods):
    parts = []
    for name in run_methods:
        test_name = name[4:]  # strip "run_"
        parts.append(
            f'@pytest.mark.asyncio(loop_scope="session")\n'
            f'async def test_{test_name}(client):\n'
            f'    assert_golden("{test_name}", await client._exec(IntegrationClient.run_{test_name}))\n'
        )
    return "\n".join(parts)


def main():
    with open(SOURCE, encoding="utf-8") as f:
        source = f.read()

    run_methods = find_run_methods(source)
    if not run_methods:
        print("No run_* methods found in IntegrationClient")
        return

    with open(TARGET, "w", encoding="utf-8") as f:
        f.write(HEADER + make_test_block(run_methods))

    print(f"Generated {len(run_methods)} test functions in {TARGET}:")
    for name in run_methods:
        print(f"  test_{name[4:]}")


if __name__ == "__main__":
    main()
