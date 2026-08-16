"""Market endpoint presets: Spot live vs USD-M Futures Demo."""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class MarketKind(str, Enum):
    SPOT = "spot"
    FUTURES_DEMO = "futures-demo"


@dataclass(frozen=True)
class MarketEndpoints:
    kind: MarketKind
    rest_base: str
    depth_path: str
    ws_base: str

    @property
    def depth_url(self) -> str:
        return f"{self.rest_base.rstrip('/')}{self.depth_path}"

    def depth_stream_url(self, symbol: str) -> str:
        return f"{self.ws_base.rstrip('/')}/{symbol.lower()}@depth@100ms"

    def agg_trade_stream_url(self, symbol: str) -> str:
        return f"{self.ws_base.rstrip('/')}/{symbol.lower()}@aggTrade"


def endpoints_for(market: MarketKind | str) -> MarketEndpoints:
    kind = MarketKind(market) if not isinstance(market, MarketKind) else market
    if kind is MarketKind.SPOT:
        return MarketEndpoints(
            kind=kind,
            rest_base="https://api.binance.com",
            depth_path="/api/v3/depth",
            ws_base="wss://stream.binance.com:9443/ws",
        )
    if kind is MarketKind.FUTURES_DEMO:
        # Same Demo hosts as Hermes.BinanceDemoFuturesTerminal
        return MarketEndpoints(
            kind=kind,
            rest_base="https://demo-fapi.binance.com",
            depth_path="/fapi/v1/depth",
            ws_base="wss://demo-fstream.binance.com/ws",
        )
    raise ValueError(f"unsupported market: {market}")
