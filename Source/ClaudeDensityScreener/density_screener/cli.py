"""
Entry point. Wires: OrderBookMaintainer (WS/REST) -> OrderBookDensityDetector
+ VolumeProfileBuilder -> merge -> write_json_snapshot, on a fixed interval.

Usage:
    python -m density_screener.cli --symbol BTCUSDT --market spot
    python -m density_screener.cli --symbol BTCUSDT --market futures-demo
"""
from __future__ import annotations

import argparse
import asyncio
import logging

from .binance_client import OrderBookMaintainer, stream_agg_trades
from .density import OrderBookDensityDetector, VolumeProfileBuilder, merge_with_profile
from .markets import MarketKind, endpoints_for
from .models import ScreenerConfig
from .paths import default_snapshot_path, ensure_bridge_dir, write_heartbeat
from .storage import write_json_snapshot

log = logging.getLogger("density_screener.cli")


def parse_args() -> ScreenerConfig:
    default_out = str(default_snapshot_path())
    p = argparse.ArgumentParser(description="Hermes density screener (bounce levels)")
    p.add_argument("--symbol", default="BTCUSDT")
    p.add_argument(
        "--market",
        default="spot",
        choices=[m.value for m in MarketKind],
        help="spot (live) or futures-demo (same Demo hosts as Futures terminal)",
    )
    p.add_argument("--bucket-pct", type=float, default=0.05)
    p.add_argument("--strength-percentile", type=float, default=0.90)
    p.add_argument("--persistence-sec", type=float, default=3.0)
    p.add_argument("--eaten-threshold", type=float, default=0.6)
    p.add_argument("--profile-window-sec", type=float, default=900.0)
    p.add_argument("--snapshot-interval-sec", type=float, default=1.0)
    p.add_argument("--depth-limit", type=int, default=1000)
    p.add_argument(
        "--output",
        default=default_out,
        help=f"JSON snapshot path (default: {default_out})",
    )
    args = p.parse_args()
    return ScreenerConfig(
        symbol=args.symbol,
        market=args.market,
        bucket_size_pct=args.bucket_pct,
        strength_percentile=args.strength_percentile,
        min_persistence_seconds=args.persistence_sec,
        eaten_threshold=args.eaten_threshold,
        profile_window_seconds=args.profile_window_sec,
        snapshot_interval_seconds=args.snapshot_interval_sec,
        depth_limit=args.depth_limit,
        output_path=args.output,
    )


async def run(config: ScreenerConfig) -> None:
    ensure_bridge_dir()
    out = config.output_path or str(default_snapshot_path())
    endpoints = endpoints_for(config.market)
    detector = OrderBookDensityDetector(config)
    profile_builder = VolumeProfileBuilder(config)
    maintainer = OrderBookMaintainer(config.symbol, config.depth_limit, market=config.market)

    async def trade_consumer():
        async for event in stream_agg_trades(config.symbol, endpoints):
            profile_builder.add_trade(price=float(event["p"]), qty=float(event["q"]))

    async def snapshot_loop():
        while True:
            await asyncio.sleep(config.snapshot_interval_seconds)
            if not maintainer.book.synced:
                continue
            mid = maintainer.book.mid_price()
            bucket_width = (mid or 0) * config.bucket_size_pct / 100.0
            levels = detector.update(maintainer.book, config.symbol)
            if bucket_width > 0:
                levels = merge_with_profile(levels, profile_builder.profile(bucket_width))
            write_json_snapshot(out, config.symbol, mid, levels, market=config.market)
            write_heartbeat()
            log.info(
                "snapshot written market=%s levels=%d mid=%.2f → %s",
                config.market,
                len(levels),
                mid or 0.0,
                out,
            )

    log.info("market=%s rest=%s ws=%s", config.market, endpoints.rest_base, endpoints.ws_base)
    await asyncio.gather(maintainer.run(), trade_consumer(), snapshot_loop())


def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    config = parse_args()
    log.info(
        "starting density screener %s market=%s → %s",
        config.symbol,
        config.market,
        config.output_path,
    )
    asyncio.run(run(config))


if __name__ == "__main__":
    main()
