import unittest
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from model import RiskScorer  # noqa: E402


class RiskScorerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.scorer = RiskScorer()

    def test_rebalance_signal_forces_critical(self) -> None:
        result = self.scorer.assess_risk([1.0, 1.1, 1.2], True, 0.8)
        self.assertEqual(result.Priority, "CRITICAL")
        self.assertEqual(result.Reason, "Cloud Provider Signal")

    def test_insufficient_history_returns_low(self) -> None:
        result = self.scorer.assess_risk([1.0, 1.2], False, 0.8)
        self.assertEqual(result.Priority, "LOW")
        self.assertEqual(result.Reason, "Insufficient Data")

    def test_price_spike_detected(self) -> None:
        # Last price is >20% above moving average in the final window.
        history = [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.6]
        result = self.scorer.assess_risk(history, False, 0.8)
        self.assertEqual(result.Priority, "CRITICAL")
        self.assertEqual(result.Reason, "Price Spike Detected")

    def test_volatility_detected(self) -> None:
        # Keep last value low so trend rule does not pre-empt volatility rule.
        history = [1.0, 20.0, 1.0, 20.0, 1.0, 20.0, 1.0, 20.0, 1.0, 1.0]
        result = self.scorer.assess_risk(history, False, 0.8)
        self.assertEqual(result.Priority, "CRITICAL")
        self.assertEqual(result.Reason, "Market Instability")

    def test_stable_market_returns_low(self) -> None:
        history = [1.0, 1.02, 1.01, 0.99, 1.0, 1.01]
        result = self.scorer.assess_risk(history, False, 0.8)
        self.assertEqual(result.Priority, "LOW")
        self.assertEqual(result.Reason, "Stable")


if __name__ == "__main__":
    unittest.main()
