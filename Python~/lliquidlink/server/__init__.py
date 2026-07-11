import logging
import os

module_dir = os.path.abspath(os.path.dirname(__file__))

logger = logging.getLogger(__name__)
def setup_logger():
    level = logging.DEBUG
    log_path = f"{module_dir}/log.log"
    try:
        os.remove(log_path)
    except OSError:
        pass
    h = logging.FileHandler(log_path, "a")
    h.setFormatter(logging.Formatter("[LLiquidLink.Server] %(message)s"))
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
        _logger.setLevel(level)
setup_logger()

from .server import main
__all__ = ["main"]
