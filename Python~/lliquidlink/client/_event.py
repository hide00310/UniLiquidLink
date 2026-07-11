"""Minimal C#-style multicast event supporting += / -= handler registration."""
from __future__ import annotations
from typing import Callable, List


class Event:
    """A list of callbacks invoked in registration order when called."""

    def __init__(self) -> None:
        self._handlers: List[Callable] = []

    def __iadd__(self, handler: Callable) -> "Event":
        self._handlers.append(handler)
        return self

    def __isub__(self, handler: Callable) -> "Event":
        try:
            self._handlers.remove(handler)
        except ValueError:
            pass
        return self

    def __call__(self, *args, **kwargs) -> None:
        # Snapshot so a handler that mutates registration mid-dispatch is safe.
        for handler in list(self._handlers):
            handler(*args, **kwargs)
