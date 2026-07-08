#!/usr/bin/env python3
"""Kill the process occupying port 8700 (TCP server port)."""

import socket
import sys


def find_pid(port: int) -> int | None:
    import psutil
    for conn in psutil.net_connections(kind="tcp"):
        if conn.laddr.port == port and conn.status == "LISTEN":
            return conn.pid
    return None


def kill_port(port: int) -> None:
    try:
        import psutil
    except ImportError:
        # Fallback: use netstat + taskkill on Windows
        import subprocess, re
        result = subprocess.run(
            ["netstat", "-ano"],
            capture_output=True, text=True
        )
        pid = None
        for line in result.stdout.splitlines():
            if f":{port}" in line and "LISTENING" in line:
                pid = int(line.split()[-1])
                break
        if pid is None:
            print(f"No process found listening on port {port}.")
            return
        print(f"Killing PID {pid} (via taskkill /F /T)...")
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)], check=False)
        return

    pid = find_pid(port)
    if pid is None:
        print(f"No process found listening on port {port}.")
        return

    proc = psutil.Process(pid)
    print(f"Killing PID {pid} ({proc.name()}) listening on port {port}...")
    # Kill entire process tree so conda-spawned subprocesses are also removed.
    children = proc.children(recursive=True)
    for child in children:
        try:
            child.kill()
        except psutil.NoSuchProcess:
            pass
    proc.kill()
    print("Done.")


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8700
    kill_port(port)
    # kill_port(8766)
