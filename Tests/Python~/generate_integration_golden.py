"""
Generate golden files for integration tests.
Use this when the Unity server is running and Setup Integration Test has been executed.

Usage:
    cd Assets/Editor/UniLiquidLink
    python Tests/Python/generate_integration_golden.py
"""
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../.."))

from conftest import generate_golden
from integration_client import IntegrationClient


def main():
    client = IntegrationClient()
    print("Connecting to Unity server...")
    client.mainloop()
    for name, value in client.captured.items():
        generate_golden(name, value)
        print(f"Generated: {name}.json = {value}")


if __name__ == "__main__":
    main()
