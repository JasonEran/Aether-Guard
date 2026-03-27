import assert from "node:assert/strict";
import test from "node:test";

import { proxyCoreJson } from "../app/api/dashboard/route-utils";

test("proxyCoreJson returns a 503 JSON error when the upstream fetch throws", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async () => {
    throw new Error("socket hang up");
  }) as typeof fetch;

  try {
    const response = await proxyCoreJson("/api/v1/dashboard/latest", {
      emptyPayload: null,
      fetchFailureMessage: "Core dashboard latest is unavailable.",
      upstreamFailureMessage: "Failed to fetch latest telemetry.",
    });

    assert.equal(response.status, 503);
    assert.deepEqual(await response.json(), {
      error: "Core dashboard latest is unavailable.",
    });
  } finally {
    global.fetch = originalFetch;
  }
});
