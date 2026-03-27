import { proxyCoreJson } from "../route-utils";

export const dynamic = 'force-dynamic';
export const revalidate = 0;

export async function GET() {
  return proxyCoreJson("/api/v1/dashboard/latest", {
    emptyPayload: null,
    fetchFailureMessage: "Core dashboard latest is unavailable.",
    upstreamFailureMessage: "Failed to fetch latest telemetry.",
  });
}
