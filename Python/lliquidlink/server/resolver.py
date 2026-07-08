"""Resolves abbreviated RPC method names and short .NET type names using CSV indexes."""
import pandas as pd
import numpy as np
import logging

logger = logging.getLogger(__name__)


class RpcNameResolver:
    """Maps (class_name, method_name) pairs to full RPC names loaded from a CSV file.

    Supports a list of abbreviated classes; resolve() searches them in registration order.
    """

    def __init__(self, csv_path: str):
        # {(class_name, method_name): full_name}
        self._lookup: pd.DataFrame = None
        self._abbreviated_classes = np.array([])
        self._load(csv_path)

    def _load(self, path: str) -> None:
        try:
            self._lookup = pd.read_csv(path, encoding="utf-8").set_index(["class_name", "method_name"])
            logger.info("Loaded %d RPC name entries from %s", len(self._lookup), path)
        except FileNotFoundError:
            logger.warning("RPC names CSV not found at %s; abbreviated class resolution disabled", path)

    def add_abbreviated_classes(self, class_names) -> None:
        """Register a class whose unqualified method names are resolved to full RPC names."""
        if isinstance(class_names, str):
            class_names = [class_names]
        for class_name in class_names:
            if class_name not in self._abbreviated_classes:
                self._abbreviated_classes = np.append(self._abbreviated_classes, class_name)
                logger.info("Abbreviated class registered: %s", class_name)

    def try_resolve(self, name: str):
        """Return the full RPC name for name under any registered abbreviated class.

        Returns the full RPC name when a (class_name, name) pair is registered,
        or None when no abbreviated class provides it.
        """
        if self._lookup is None:
            return None
        for cls_ in self._abbreviated_classes:
            try:
                full = self._lookup.loc[(cls_, name), "full_name"]
            except KeyError:
                continue
            if not isinstance(full, str):
                full = str(full.iloc[0])
            logger.debug("try_resolve: %s->%s", name, full)
            return full
        logger.debug("try_resolve not found: %s", name)
        return None

    def resolve(self, method: str) -> str:
        """Return the full RPC name if method matches an abbreviated class, else return method unchanged."""
        full = self.try_resolve(method)
        return full if full is not None else method

    def resolve_chain_params(self, params: list) -> list:
        """Resolve the first chain step (params[1][0]['name']) of a ResolveChain* call.

        For a root chain (params[0] is null), the first step name may be an
        abbreviated-class member: registering 'A' lets the client write `B.C.D`
        instead of `A.B.C.D`. Returns a new params list with the first step name
        replaced by its full RPC name when (abbreviated_class, name) is registered;
        otherwise returns params unchanged.
        """
        if len(params) < 2:
            return params
        # Only root chains (obj is null) carry an abbreviated-class first step.
        if params[0] is not None:
            return params
        steps = params[1]
        if not isinstance(steps, list) or len(steps) == 0:
            return params
        first = steps[0]
        if not isinstance(first, dict) or "name" not in first:
            return params
        full = self.try_resolve(first["name"])
        if full is None:
            return params
        # Build a new params list without mutating the input.
        new_first = dict(first)
        new_first["name"] = full
        new_steps = list(steps)
        new_steps[0] = new_first
        new_params = list(params)
        new_params[1] = new_steps
        return new_params

    def try_resolve_class_method(self, class_name: str, method: str):
        """Return the full RPC name registered for (class_name, method), or None.

        Unlike try_resolve this keys on an explicit written class step rather than
        the abbreviated-class set, so it works regardless of abbreviation registration.
        """
        if self._lookup is None:
            return None
        try:
            full = self._lookup.loc[(class_name, method), "full_name"]
        except KeyError:
            return None
        if not isinstance(full, str):
            full = str(full.iloc[0])
        return full

    def try_collapse_static_chain(self, method: str, params: list):
        """Collapse a root JsonRpc_ResolveChain whose last step is a static class.

        params layout: [obj, steps, terminal, args].  Only root chains (obj is None)
        whose last step name resolves to a non-underscore registered method are
        collapsed.  Returns (full_name, args) or None.
        """
        if method != "JsonRpc_ResolveChain" or len(params) < 4:
            return None
        if params[0] is not None:
            return None
        steps = params[1]
        if not isinstance(steps, list) or len(steps) == 0:
            return None
        last = steps[-1]
        if not isinstance(last, dict) or "name" not in last:
            return None
        terminal = params[2]
        if not isinstance(terminal, str):
            return None
        full = self.try_resolve_class_method(last["name"], terminal)
        if full is None or full.startswith("_"):
            return None
        return full, params[3]


class TypeNameResolver:
    """Resolves short .NET type names to their FullName using a CSV of allowed types.

    The CSV is written by TypeResolver.SaveAllowedTypesCsv() at server startup.
    Lookup order: exact full-name match (case-insensitive), then simple-name match
    (last dot-separated segment). If multiple types share a simple name, the caller
    should use the full name to disambiguate.
    """

    def __init__(self, csv_path: str):
        # lower(full_name) -> original full_name
        self._by_full_name_lower: dict = {}
        # lower(simple_name) -> [full_name, ...]
        self._by_simple_name: dict = {}
        self._abbreviated_namespaces: list = []
        self._loaded = False
        self._load(csv_path)

    def _load(self, path: str) -> None:
        try:
            df = pd.read_csv(path, encoding="utf-8")
            for full_name in df["full_name"].dropna():
                full_name = str(full_name)
                self._by_full_name_lower[full_name.lower()] = full_name
                simple = full_name.rsplit(".", 1)[-1].lower()
                self._by_simple_name.setdefault(simple, []).append(full_name)
            self._loaded = True
            logger.info("Loaded %d type names from %s", len(self._by_full_name_lower), path)
        except FileNotFoundError:
            logger.warning("Type names CSV not found at %s; type name resolution disabled", path)

    def add_abbreviated_namespaces(self, namespaces) -> None:
        """Register namespaces whose types can be referred to by simple name."""
        if isinstance(namespaces, str):
            namespaces = [namespaces]
        for ns in namespaces:
            if ns not in self._abbreviated_namespaces:
                self._abbreviated_namespaces.append(ns)
                logger.info("Abbreviated namespace registered: %s", ns)

    def resolve(self, name: str) -> str:
        """Resolve a .NET type name to its FullName.

        Returns the name unchanged if the CSV was not loaded or no match is found.
        """
        if not self._loaded:
            return name
        # Exact match (case-insensitive)
        found = self._by_full_name_lower.get(name.lower())
        if found is not None:
            return found
        # Abbreviated namespace match: try ns + "." + name in registration order
        for ns in self._abbreviated_namespaces:
            found = self._by_full_name_lower.get((ns + "." + name).lower())
            if found is not None:
                logger.debug("resolve type via ns %s: %s -> %s", ns, name, found)
                return found
        # Simple name match (last segment after last '.')
        simple = name.rsplit(".", 1)[-1].lower()
        candidates = self._by_simple_name.get(simple, [])
        if len(candidates) == 1:
            logger.debug("resolve type: %s -> %s", name, candidates[0])
            return candidates[0]
        if len(candidates) > 1:
            logger.warning(
                "Ambiguous type name '%s': %s — use the full name to disambiguate",
                name, candidates,
            )
        return name
