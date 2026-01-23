import { NextResponse } from 'next/server';

const coreBaseUrl = process.env.CORE_API_URL ?? 'http://core-service:8080';

export async function GET(request: Request) {
  const url = new URL(request.url);
  const query = url.searchParams.toString();
  const targetUrl = `${coreBaseUrl}/audits${query ? `?${query}` : ''}`;

  const response = await fetch(targetUrl, { cache: 'no-store' });
  const text = await response.text();

  if (!response.ok) {
    return NextResponse.json({ error: text || 'Failed to fetch audit logs.' }, { status: response.status });
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
