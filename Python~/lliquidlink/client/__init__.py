"""lliquidlink.client - Python client to control the Unity Editor over JSON-RPC.

Usage::

    def on_execute(client):
        go = client.Find("Player")
        go.transform.Rotate(0, 90, 0)

    client = Client(TcpJsonRpcTransport("localhost", 8700))
    client.on_execute += on_execute
    client.mainloop()
"""
import logging

logger = logging.getLogger("lliquidlink.client")

def setup_logger():
    level = logging.DEBUG
    h = logging.StreamHandler()
    h.setFormatter(logging.Formatter("[Client] %(message)s"))
    h.setLevel(level)
    logger.addHandler(h)
    logger.setLevel(level)
    from .. import core
    for name in [
        core.__name__,
        # "anyio", "websockets"
    ]:
        _logger = logging.getLogger(name)
        for h in logger.handlers:
            _logger.addHandler(h)
        _logger.setLevel(logger.level)
setup_logger()

from ._client import Client, gc_flush
from ._event import Event
from ._proxy import ObjectProxy, PropertyProxy
from ._transports import StdioJsonRpcTransport, TcpJsonRpcTransport
from ..core import ConnectionClosedError, RpcError
from . import models

__all__ = [
    "Client",
    "gc_flush",
    "Event",
    "ObjectProxy",
    "PropertyProxy",
    "StdioJsonRpcTransport",
    "TcpJsonRpcTransport",
    "ConnectionClosedError",
    "RpcError",
    "models",
]
