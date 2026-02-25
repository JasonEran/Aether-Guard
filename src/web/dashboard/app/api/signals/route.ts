import { NextResponse } from 'next/server';

export const dynamic = 'force-dynamic';
export const revalidate = 0;

const coreBaseUrl = process.env.CORE_API_URL ?? 'http://core-service:8080';

export async function GET(request: Request) {
  const { searchParams } = new URL(request.url);
  const query = searchParams.toString();
  const url = query ? `${coreBaseUrl}/api/v1/signals?${query}` : `${coreBaseUrl}/api/v1/signals`;

  const response = await fetch(url, { cache: 'no-store' });
  const text = await response.text();
  if (!response.ok) {
    return NextResponse.json({ error: text || 'Failed to fetch external signals.' }, { status: response.status });
  }

  if (!text) {
    return NextResponse.json([], { status: response.status });
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
