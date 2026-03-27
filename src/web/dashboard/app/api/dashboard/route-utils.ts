import { NextResponse } from "next/server";

const coreBaseUrl = process.env.CORE_API_URL ?? "http://core-service:8080";

interface ProxyCoreJsonOptions<TEmptyPayload> {
  emptyPayload: TEmptyPayload;
  fetchFailureMessage: string;
  upstreamFailureMessage: string;
}

export async function proxyCoreJson<TEmptyPayload>(
  path: string,
  options: ProxyCoreJsonOptions<TEmptyPayload>,
) {
  try {
    const response = await fetch(`${coreBaseUrl}${path}`, {
      cache: "no-store",
    });

    const text = await response.text();
    if (!response.ok) {
      return NextResponse.json(
        { error: text || options.upstreamFailureMessage },
        { status: response.status },
      );
    }

    if (!text) {
      return NextResponse.json(options.emptyPayload, { status: response.status });
    }

    try {
      return NextResponse.json(JSON.parse(text), { status: response.status });
    } catch {
      return new NextResponse(text, {
        status: response.status,
        headers: { "Content-Type": response.headers.get("content-type") ?? "text/plain" },
      });
    }
  } catch {
    return NextResponse.json(
      { error: options.fetchFailureMessage },
      { status: 503 },
    );
  }
}
