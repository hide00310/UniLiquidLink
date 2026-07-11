import json
import os
import pytest
from unittest.mock import AsyncMock, MagicMock
from lliquidlink.client import Client, PropertyProxy, ObjectProxy

GOLDEN_DIR = os.path.join(os.path.dirname(__file__), "golden")


def assert_golden(name: str, data):
    path = os.path.join(GOLDEN_DIR, f"{name}.json")
    with open(path, encoding="utf-8") as f:
        expected = json.load(f)
    assert data == expected, f"Regression failure: {name}\n except:{expected}\n data:{data}"

def generate_golden(name: str, data):
    path = os.path.join(GOLDEN_DIR, f"{name}.json")
    if not os.path.exists(path):
        os.makedirs(GOLDEN_DIR, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, default=str)

class FakeEditor(Client):
    def __init__(self):
        transport = MagicMock()
        transport.closed = True
        super().__init__(transport)


@pytest.fixture
def editor():
    return FakeEditor()


@pytest.fixture
def editor_with_mock_transport(editor):
    mock_transport = AsyncMock()
    mock_transport.closed = False
    editor._transport = mock_transport
    return editor, mock_transport
