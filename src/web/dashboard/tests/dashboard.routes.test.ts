import assert from "node:assert/strict";
import test from "node:test";

import { GET as getHistory } from "../app/api/dashboard/history/route";
import { GET as getLatest } from "../app/api/dashboard/latest/route";

test("latest route returns JSON payload from core service", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async (input) => {
    assert.equal(input, "http://core-service:8080/api/v1/dashboard/latest");
    return new Response(JSON.stringify({ telemetry: { agentId: "agent-9" } }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  }) as typeof fetch;

  try {
    const response = await getLatest();
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { telemetry: { agentId: "agent-9" } });
  } finally {
    global.fetch = originalFetch;
  }
});

test("latest route preserves plain text responses when core returns non-JSON", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async () =>
    new Response("temporarily degraded", {
      status: 200,
      headers: { "Content-Type": "text/plain" },
    })) as typeof fetch;

  try {
    const response = await getLatest();
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("content-type"), "text/plain");
    assert.equal(await response.text(), "temporarily degraded");
  } finally {
    global.fetch = originalFetch;
  }
});

test("history route returns empty array when core returns an empty body", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async (input) => {
    assert.equal(input, "http://core-service:8080/api/v1/dashboard/history");
    return new Response("", { status: 200 });
  }) as typeof fetch;

  try {
    const response = await getHistory();
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), []);
  } finally {
    global.fetch = originalFetch;
  }
});

test("history route wraps upstream failures as JSON errors", async () => {
  const originalFetch = global.fetch;
  global.fetch = (async () =>
    new Response("core unavailable", { status: 503 })) as typeof fetch;

  try {
    const response = await getHistory();
    assert.equal(response.status, 503);
    assert.deepEqual(await response.json(), { error: "core unavailable" });
  } finally {
    global.fetch = originalFetch;
  }
});
