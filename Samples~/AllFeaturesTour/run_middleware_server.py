"""
All Features Tour — start the lliquidlink.server middleware bound to HOST:PORT.

This process is meant to be launched by Unity via the "Python Server Start Command"
field (see UniLiquidLink/Samples/All Features Tour Server Window), not run directly from a
terminal: it talks to the C# Unity Editor process over its own stdin/stdout, so running
it interactively will just hang waiting for a Unity parent process.

Usage (enter as the Python Server Start Command in the Editor Window):
  python Samples/AllFeaturesTour/run_middleware_server.py
"""
import asyncio
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Python'))

from lliquidlink.server.server import Server
from lliquidlink.server._transport import TcpServerTransport

# Must match the TcpJsonRpcTransport host/port used by all_features_tour.py
HOST = "localhost"
PORT = 8700


def main():
    asyncio.run(Server(TcpServerTransport(HOST, PORT)).serve())


if __name__ == "__main__":
    main()
