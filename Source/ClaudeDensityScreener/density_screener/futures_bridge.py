"""Enqueue place_order into HermesFutures bridge (same schema as WPF)."""
from __future__ import annotations

import json
import os
import tempfile
import uuid
from datetime import datetime, timezone
from typing import Any, Optional


def futures_bridge_root() -> str:
    local = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    return os.path.join(local, "HermesFutures", "bridge")


def commands_path() -> str:
    return os.path.join(futures_bridge_root(), "commands.json")


def heartbeat_path() -> str:
    return os.path.join(futures_bridge_root(), "heartbeat.txt")


def futures_terminal_alive(max_age_sec: float = 15.0) -> bool:
    path = heartbeat_path()
    if not os.path.isfile(path):
        return False
    try:
        age = datetime.now(timezone.utc).timestamp() - os.path.getmtime(path)
        return age <= max_age_sec
    except OSError:
        return False


def enqueue_place_order(
    *,
    symbol: str,
    side: str,
    quantity_usdt: float,
    order_type: str = "MARKET",
    price: Optional[float] = None,
    requested_by: str = "density_bounce",
    dry_run: bool = True,
) -> dict[str, Any]:
    """
    Append FuturesPlatformCommand to commands.json (Pending list).
    When dry_run=True, only returns the command dict without writing.
    """
    cmd: dict[str, Any] = {
        "Id": str(uuid.uuid4()),
        "CreatedUtc": datetime.now(timezone.utc).isoformat(),
        "Action": "place_order",
        "Symbol": symbol.upper(),
        "Side": side.upper(),
        "OrderType": order_type.upper(),
        "QuantityUsdt": float(quantity_usdt),
        "RequestedBy": requested_by,
    }
    if price is not None and order_type.upper() == "LIMIT":
        cmd["Price"] = float(price)

    if dry_run:
        return {"dry_run": True, "command": cmd, "path": commands_path()}

    if not futures_terminal_alive():
        raise RuntimeError(
            "Futures terminal heartbeat missing/stale — start Binance Demo Futures first"
        )

    root = futures_bridge_root()
    os.makedirs(root, exist_ok=True)
    path = commands_path()
    pending: list[dict] = []
    if os.path.isfile(path):
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            pending = list(data.get("Pending") or [])
        except (json.JSONDecodeError, OSError):
            pending = []

    pending.append(cmd)
    payload = {"Pending": pending}
    fd, tmp = tempfile.mkstemp(dir=root, prefix=".commands_", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)

    return {"dry_run": False, "command": cmd, "path": path}
