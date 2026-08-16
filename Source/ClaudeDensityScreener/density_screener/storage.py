"""
Output layer. Writes a JSON snapshot to disk so both the WPF UI and
the Hermes agent can read the same file (matches Hermes's existing
file-based IPC pattern). Atomic write (tmp file + rename) so readers
never see a half-written file.

A Supabase writer stub is included for when you want history /
backtesting data instead of (or in addition to) the live snapshot.
"""
from __future__ import annotations

import json
import os
import tempfile
from time import time
from typing import List, Optional

from .models import DensityLevel


def write_json_snapshot(
    path: str,
    symbol: str,
    current_price: Optional[float],
    levels: List[DensityLevel],
    market: str = "spot",
) -> None:
    payload = {
        "symbol": symbol,
        "market": market,
        "current_price": current_price,
        "generated_at": time(),
        "levels": [
            {
                **lvl.to_dict(),
                "distance_pct": lvl.distance_pct(current_price) if current_price else None,
            }
            for lvl in sorted(levels, key=lambda l: -l.strength)
        ],
    }
    directory = os.path.dirname(os.path.abspath(path)) or "."
    fd, tmp_path = tempfile.mkstemp(dir=directory, prefix=".density_snapshot_", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
        os.replace(tmp_path, path)  # atomic on POSIX and Windows (NTFS)
    finally:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)


def read_json_snapshot(path: str) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


class SupabaseHistoryWriter:
    """
    Optional: persists confirmed density levels for backtesting / the
    agent's episodic memory. Requires `pip install supabase` and
    SUPABASE_URL / SUPABASE_KEY env vars. Table shape (suggested):

        create table density_levels (
            id bigint generated always as identity primary key,
            symbol text not null,
            price double precision not null,
            side text not null,
            volume double precision not null,
            strength double precision not null,
            source text not null,
            first_seen timestamptz not null,
            last_seen timestamptz not null,
            eaten_ratio double precision not null
        );
    """

    def __init__(self, url: Optional[str] = None, key: Optional[str] = None):
        self.url = url or os.environ.get("SUPABASE_URL")
        self.key = key or os.environ.get("SUPABASE_KEY")
        self._client = None

    def _ensure_client(self):
        if self._client is None:
            from supabase import create_client  # imported lazily, optional dep
            if not self.url or not self.key:
                raise RuntimeError("SUPABASE_URL / SUPABASE_KEY not configured")
            self._client = create_client(self.url, self.key)
        return self._client

    def upsert_levels(self, levels: List[DensityLevel]) -> None:
        client = self._ensure_client()
        rows = []
        for lvl in levels:
            d = lvl.to_dict()
            d["first_seen"] = _iso(d["first_seen"])
            d["last_seen"] = _iso(d["last_seen"])
            rows.append(d)
        if rows:
            client.table("density_levels").insert(rows).execute()


def _iso(ts: float) -> str:
    import datetime
    return datetime.datetime.fromtimestamp(ts, tz=datetime.timezone.utc).isoformat()
