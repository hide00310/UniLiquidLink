"""Private logging helpers shared by lliquidlink.client and lliquidlink.server."""
from __future__ import annotations

import logging


def resolve_configured_level(logger: logging.Logger, default: int) -> int:
    """Return the nearest explicit (non-NOTSET) level already configured on
    `logger` itself or one of its ancestors, ignoring the root logger's
    implicit default level.

    Lets setup_logger() respect a level the caller configured ahead of time
    (e.g. logging.getLogger("lliquidlink").setLevel(logging.DEBUG) before
    importing lliquidlink.client/server) instead of unconditionally
    overwriting it with a hardcoded default. Falls back to `default` when
    nothing has been explicitly configured on the chain.
    """
    node = logger
    while node is not None and node is not logging.root:
        if node.level != logging.NOTSET:
            return node.level
        node = node.parent
    return default
