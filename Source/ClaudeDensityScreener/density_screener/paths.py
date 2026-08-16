"""Default IPC paths for Hermes Density Screener (separate from Futures bridge)."""
from __future__ import annotations

import os
from pathlib import Path


def default_bridge_dir() -> Path:
    local = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    return Path(local) / "HermesDensity" / "bridge"


def default_snapshot_path() -> Path:
    return default_bridge_dir() / "density_snapshot.json"


def default_heartbeat_path() -> Path:
    return default_bridge_dir() / "heartbeat.txt"


def ensure_bridge_dir() -> Path:
    d = default_bridge_dir()
    d.mkdir(parents=True, exist_ok=True)
    return d


def write_heartbeat(path: Path | None = None) -> None:
    """UTC ISO timestamp — agent treats >15s stale as screener down."""
    from datetime import datetime, timezone

    target = path or default_heartbeat_path()
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(datetime.now(timezone.utc).isoformat(), encoding="utf-8")
