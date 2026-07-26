"""
Shared middleware entry point for the Cube Demo and All Features Tour samples —
starts the lliquidlink.server middleware bound to HOST:PORT.

This process is meant to be launched by Unity via the "UniLiquidLink/Samples/Sample
Window" (see UniLiquidLinkSampleWindow.cs), not run directly from a terminal: it talks
to the C# Unity Editor process over its own stdin/stdout, so running it interactively
will just hang waiting for a Unity parent process.

The window resolves this script's absolute path automatically, so no manual command
needs to be entered; the default Python command is "python".
"""
import argparse
import asyncio
import os
import sys

# Launched by Unity as a bare subprocess (no PYTHONPATH guarantee), so the lliquidlink
# package directory must be added explicitly, same as all_features_tour.py does.
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Python~'))

from lliquidlink.server.server import Server
from lliquidlink.server._transport import TcpServerTransport

# Must match the TcpJsonRpcTransport host/port used by create_and_rotate_cube.py /
# all_features_tour.py. Cube Demo and All Features Tour share this port, so only one
# of them can run at a time.
HOST = "localhost"
PORT = 8700


def main():
    # PythonProcessManager (C# side) always appends "-dataDir <path>", pointing at the
    # directory holding rpc_names.csv / type_names.csv generated for this Unity session.
    parser = argparse.ArgumentParser()
    parser.add_argument("-dataDir", required=True)
    args = parser.parse_args()

    asyncio.run(Server(args.dataDir, TcpServerTransport(HOST, PORT)).serve())


if __name__ == "__main__":
    main()
