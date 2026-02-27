import assert from "node:assert/strict";
import test from "node:test";

import { normalizeRiskScore, normalizeTier, toIsoTimestamp } from "../lib/api";

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
