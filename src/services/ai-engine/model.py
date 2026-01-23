from dataclasses import dataclass
from typing import Sequence


@dataclass(frozen=True)
class RiskAssessment:
    Priority: str


class RiskScorer:
    def assess_risk(
        self,
        spot_price_history: Sequence[float],
        rebalance_signal: bool,
        capacity_score: float,
    ) -> RiskAssessment:
        if rebalance_signal:
            return RiskAssessment(Priority="CRITICAL")
        return RiskAssessment(Priority="LOW")
