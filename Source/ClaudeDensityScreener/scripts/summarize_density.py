"""Print a short density snapshot summary (for humans / Hermes CLI)."""
from __future__ import annotations

import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

# Allow running without PYTHONPATH when invoked from repo.
_ROOT = Path(__file__).resolve().parents[1]
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

from density_screener.paths import default_heartbeat_path, default_snapshot_path


def _heartbeat_age_sec(path: Path) -> float | None:
    if not path.is_file():
        return None
    text = path.read_text(encoding="utf-8").strip()
    try:
        ts = datetime.fromisoformat(text.replace("Z", "+00:00"))
        if ts.tzinfo is None:
            ts = ts.replace(tzinfo=timezone.utc)
        return (datetime.now(timezone.utc) - ts).total_seconds()
    except ValueError:
        return time.time() - path.stat().st_mtime


def main() -> int:
    snap = Path(sys.argv[1]) if len(sys.argv) > 1 else default_snapshot_path()
    hb = default_heartbeat_path()
    age = _heartbeat_age_sec(hb)
    if age is None:
        print("STATUS: screener DOWN (no heartbeat)")
    elif age > 15:
        print(f"STATUS: screener STALE (heartbeat age {age:.1f}s)")
    else:
        print(f"STATUS: screener OK (heartbeat age {age:.1f}s)")

    if not snap.is_file():
        print(f"MISSING snapshot: {snap}")
        return 1

    data = json.loads(snap.read_text(encoding="utf-8"))
    levels = sorted(data.get("levels") or [], key=lambda x: -float(x.get("strength") or 0))
    print(f"symbol={data.get('symbol')} price={data.get('current_price')} levels={len(levels)}")
    print(f"file={snap}")
    for lv in levels[:5]:
        print(
            f"  {lv.get('side'):3} @{float(lv.get('price')):.2f} "
            f"str={float(lv.get('strength')):.2f} src={lv.get('source')} "
            f"dist={lv.get('distance_pct')}%"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
