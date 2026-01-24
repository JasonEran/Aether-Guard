from dataclasses import dataclass
import math
import statistics
from typing import Sequence


@dataclass(frozen=True)
class RiskAssessment:
    Priority: str
    Reason: str


class RiskScorer:
    def assess_risk(
        self,
        spot_price_history: Sequence[float],
        rebalance_signal: bool,
        capacity_score: float,
    ) -> RiskAssessment:
        if rebalance_signal:
            return RiskAssessment(Priority="CRITICAL", Reason="Cloud Provider Signal")

        clean_prices: list[float] = []
        for value in spot_price_history:
            if value is None:
                continue
            try:
                price = float(value)
            except (TypeError, ValueError):
                continue
            if not math.isfinite(price):
                continue
            clean_prices.append(price)

        if len(clean_prices) < 3:
            return RiskAssessment(Priority="LOW", Reason="Insufficient Data")

        window = clean_prices[-10:]
        moving_average = statistics.fmean(window)
        last_price = window[-1]
        volatility = statistics.pstdev(window)

        if abs(moving_average) < 1e-9:
            trend = 0.0
        else:
            trend = (last_price - moving_average) / moving_average

        if trend > 0.2:
            return RiskAssessment(Priority="CRITICAL", Reason="Price Spike Detected")

        if volatility > 5.0:
            return RiskAssessment(Priority="CRITICAL", Reason="Market Instability")

        return RiskAssessment(Priority="LOW", Reason="Stable")
