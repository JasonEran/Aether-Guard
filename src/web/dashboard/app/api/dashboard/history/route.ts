import { proxyCoreJson } from "../route-utils";

export const dynamic = 'force-dynamic';
export const revalidate = 0;

export async function GET() {
  return proxyCoreJson("/api/v1/dashboard/history", {
    emptyPayload: [],
    fetchFailureMessage: "Core dashboard history is unavailable.",
    upstreamFailureMessage: "Failed to fetch telemetry history.",
  });
}
