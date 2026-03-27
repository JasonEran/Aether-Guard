import assert from "node:assert/strict";
import test from "node:test";

import {
  fetchFleetStatus,
  fetchRiskHistory,
  normalizeRiskScore,
  normalizeTier,
  sendChaosSignal,
  toIsoTimestamp,
} from "../lib/api";

test("normalizeRiskScore clamps and normalizes correctly", () => {
  assert.equal(normalizeRiskScore(undefined), 0);
  assert.equal(normalizeRiskScore("0.4"), 0);
  assert.equal(normalizeRiskScore(-5), 0);
  assert.equal(normalizeRiskScore(0.42), 0.42);
  assert.equal(normalizeRiskScore(80), 0.8);
  assert.equal(normalizeRiskScore(250), 1);
});

test("normalizeTier accepts only supported values", () => {
  assert.equal(normalizeTier("T1"), "T1");
  assert.equal(normalizeTier("T2"), "T2");
  assert.equal(normalizeTier("T3"), "T3");
  assert.equal(normalizeTier("legacy"), "T2");
  assert.equal(normalizeTier(undefined), "T2");
});

test("toIsoTimestamp handles epoch seconds and ISO strings", () => {
  assert.equal(toIsoTimestamp(0), "1970-01-01T00:00:00.000Z");
  assert.equal(toIsoTimestamp("2026-02-27T00:00:00Z"), "2026-02-27T00:00:00.000Z");
});

test("toIsoTimestamp returns a valid fallback ISO string for invalid input", () => {
  const value = toIsoTimestamp("not-a-date");
  assert.match(value, /^\d{4}-\d{2}-\d{2}T/);
  assert.ok(!Number.isNaN(Date.parse(value)));
});

test("fetchFleetStatus maps dashboard payload into a normalized agent record", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async () =>
    new Response(
      JSON.stringify({
        telemetry: {
          agentId: "agent-7",
          workloadTier: "T9",
          rebalanceSignal: true,
          diskAvailable: 512,
          timestamp: 1700000000,
        },
        analysis: {
          status: "READY",
          confidence: 83,
          predictedCpu: 0.67,
          rootCause: "provider_incident",
          alpha: 1.4,
          preemptProbability: 0.91,
          decisionScore: 0.99,
          rationale: "Cloud incident pressure rising.",
          topSignals: [
            {
              key: "status_page",
              label: "Status Page",
              value: 0.9,
              source: "provider",
              detail: "Regional degradation",
            },
          ],
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    )) as typeof fetch;

  try {
    const result = await fetchFleetStatus();
    assert.equal(result.length, 1);
    assert.deepEqual(result[0], {
      agentId: "agent-7",
      status: "MIGRATING",
      tier: "T2",
      riskScore: 0.83,
      lastHeartbeat: "2023-11-14T22:13:20.000Z",
      analysisStatus: "READY",
      analysisConfidence: 83,
      predictedCpu: 0.67,
      rootCause: "provider_incident",
      rebalanceSignal: true,
      diskAvailable: 512,
      alpha: 1.4,
      preemptProbability: 0.91,
      decisionScore: 0.99,
      decisionRationale: "Cloud incident pressure rising.",
      topSignals: [
        {
          key: "status_page",
          label: "Status Page",
          value: 0.9,
          source: "provider",
          detail: "Regional degradation",
        },
      ],
    });
  } finally {
    global.fetch = originalFetch;
  }
});

test("fetchRiskHistory carries forward the last valid score when confidence is missing", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async () =>
    new Response(
      JSON.stringify([
        {
          id: 1,
          agentId: "agent-1",
          workloadTier: "T1",
          rebalanceSignal: false,
          diskAvailable: 100,
          aiStatus: "READY",
          aiConfidence: 80,
          timestamp: "2026-02-27T00:00:00Z",
        },
        {
          id: 2,
          agentId: "agent-1",
          workloadTier: "T1",
          rebalanceSignal: false,
          diskAvailable: 95,
          aiStatus: "READY",
          timestamp: "2026-02-27T00:05:00Z",
        },
        {
          id: 3,
          agentId: "agent-1",
          workloadTier: "T1",
          rebalanceSignal: true,
          diskAvailable: 90,
          aiStatus: "READY",
          timestamp: "2026-02-27T00:10:00Z",
        },
      ]),
      { status: 200, headers: { "Content-Type": "application/json" } },
    )) as typeof fetch;

  try {
    const result = await fetchRiskHistory();
    assert.deepEqual(result, [
      { timestamp: "2026-02-27T00:00:00.000Z", riskScore: 0.8 },
      { timestamp: "2026-02-27T00:05:00.000Z", riskScore: 0.8 },
      { timestamp: "2026-02-27T00:10:00.000Z", riskScore: 0.92 },
    ]);
  } finally {
    global.fetch = originalFetch;
  }
});

test("sendChaosSignal surfaces backend error text", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async () =>
    new Response("chaos disabled by policy", {
      status: 403,
      headers: { "Content-Type": "text/plain" },
    })) as typeof fetch;

  try {
    await assert.rejects(sendChaosSignal(), /chaos disabled by policy/);
  } finally {
    global.fetch = originalFetch;
  }
});
