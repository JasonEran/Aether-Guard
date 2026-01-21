import { NextResponse } from 'next/server';

const coreBaseUrl = process.env.CORE_API_URL ?? 'http://core-service:8080';

export async function GET() {
  const response = await fetch(`${coreBaseUrl}/api/v1/dashboard/latest`, {
    cache: 'no-store',
  });

  const text = await response.text();
  if (!response.ok) {
    return NextResponse.json({ error: text || 'Failed to fetch latest telemetry.' }, { status: response.status });
  }

  if (!text) {
    return NextResponse.json(null, { status: response.status });
  }

  try {
    return NextResponse.json(JSON.parse(text), { status: response.status });
  } catch {
    return new NextResponse(text, {
      status: response.status,
      headers: { 'Content-Type': response.headers.get('content-type') ?? 'text/plain' },
    });
  }
}
