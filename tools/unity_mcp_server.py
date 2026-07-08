#!/usr/bin/env python3
"""Start Unity integration test server via UnityMCP HTTP API.

Can be used as a module:
    from tools.start_integration_server import start_in_thread
    t = start_in_thread()
    t.join()

Or run directly:
    python tools/start_integration_server.py
"""

import json
import threading
import time
import urllib.error
import urllib.request

_UNITY_MCP_URL = "http://localhost:8080/mcp"
_COMPILE_TIMEOUT = 10
_COMPILE_POLL_INTERVAL = 1


def _parse_body(raw):
    """Parse MCP response body as JSON or SSE (data: {...}) format."""
    raw = raw.strip()
    if not raw:
        return {}
    # SSE: one or more "data: {...}" lines; return last JSON-RPC message
    if raw.startswith("data:") or raw.startswith("event:"):
        result = {}
        for line in raw.splitlines():
            line = line.strip()
            if line.startswith("data:"):
                payload = line[5:].strip()
                if payload and payload != "[DONE]":
                    parsed = json.loads(payload)
                    if "result" in parsed or "error" in parsed:
                        result = parsed
        return result
    return json.loads(raw)


def _post(body, session_id=None):
    """POST a JSON-RPC body to UnityMCP, return (session_id, response_dict)."""
    data = json.dumps(body).encode("utf-8")
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
    }
    if session_id:
        headers["Mcp-Session-Id"] = session_id

    req = urllib.request.Request(
        _UNITY_MCP_URL, data=data, headers=headers, method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            new_sid = resp.headers.get("Mcp-Session-Id") or session_id
            raw = resp.read().decode("utf-8")
            return new_sid, _parse_body(raw)
    except urllib.error.URLError as e:
        raise RuntimeError(f"Cannot reach UnityMCP at {_UNITY_MCP_URL}: {e}") from e


def _initialize():
    """Initialize MCP session and return session_id."""
    sid, resp = _post({
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "start_integration_server", "version": "1.0"},
        },
    })
    if "error" in resp:
        raise RuntimeError(f"MCP initialize failed: {resp['error']}")

    # Notification has no id field (JSON-RPC 2.0 spec)
    _post({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}}, sid)

    return sid


def _call_tool(sid, tool_name, arguments, req_id):
    """Call a UnityMCP tool and return the result dict."""
    _, resp = _post({
        "jsonrpc": "2.0",
        "id": req_id,
        "method": "tools/call",
        "params": {"name": tool_name, "arguments": arguments},
    }, sid)

    if "error" in resp:
        raise RuntimeError(f"Tool '{tool_name}' error: {resp['error']}")

    result = resp.get("result", {})
    if result.get("isError"):
        content = result.get("content", [])
        text = " ".join(c.get("text", "") for c in content if c.get("type") == "text")
        raise RuntimeError(f"Tool '{tool_name}' returned isError: {text}")

    return result


def _extract_text(result):
    """Extract plain text from a MCP tool result's content list."""
    content = result.get("content", [])
    return "".join(c.get("text", "") for c in content if c.get("type") == "text")


def _wait_for_compile(sid):
    """Poll until Unity stops compiling or timeout."""
    deadline = time.time() + _COMPILE_TIMEOUT
    req_id = 100
    while time.time() < deadline:
        result = _call_tool(
            sid,
            "execute_code",
            {"action": "execute", "code": "return UnityEditor.EditorApplication.isCompiling.ToString();"},
            req_id=req_id,
        )
        req_id += 1
        text = _extract_text(result)
        result = json.loads(text)
        if result["success"]:
            print("[OK] Compile complete.")
            return
        remaining = int(deadline - time.time())
        print(f"  Compiling... (isCompiling={text!r}, {remaining}s remaining)")
        time.sleep(_COMPILE_POLL_INTERVAL)

    raise RuntimeError(
        f"Unity did not finish compiling within {_COMPILE_TIMEOUT}s"
    )


def _run():
    """Check compile status then start integration test server."""
    print("[1/3] Initializing MCP session...")
    sid = _initialize()

    print("[2/3] Checking compile status...")
    _wait_for_compile(sid)

    print("[3/3] Starting integration test server...")
    _call_tool(
        sid,
        "execute_menu_item",
        {"menu_path": "UniLiquidLink/Start Integration Test Server"},
        req_id=3,
    )
    print("[DONE] Integration test server started.")

def run_server():
    _run()

def start_in_thread():
    """Start the integration test server in a background daemon thread."""
    t = threading.Thread(target=_run, daemon=True, name="unity-server-start")
    t.start()
    return t


if __name__ == "__main__":
    start_in_thread().join()
