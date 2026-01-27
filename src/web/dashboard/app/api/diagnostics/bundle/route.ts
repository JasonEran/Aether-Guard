import { NextResponse } from 'next/server';

import { auth } from '../../../../auth';

export async function GET(req: Request) {
  const session = await auth();
  const userRole = session?.user?.role ?? 'VIEWER';

  if (userRole !== 'ADMIN') {
    return NextResponse.json({ error: 'Forbidden' }, { status: 403 });
  }

  const apiKey = process.env.DIAGNOSTICS_API_KEY ?? process.env.COMMAND_API_KEY;
  if (!apiKey) {
    return NextResponse.json({ error: 'Diagnostics API key not configured.' }, { status: 500 });
  }

  const coreBaseUrl = process.env.CORE_API_URL ?? 'http://core-service:8080';
  const requestUrl = new URL(req.url);
  const coreUrl = new URL(coreBaseUrl);
  coreUrl.pathname = '/api/v1/diagnostics/bundle';
  coreUrl.search = requestUrl.search;

  const response = await fetch(coreUrl, {
    headers: {
      'X-API-Key': apiKey,
    },
    cache: 'no-store',
  });

  if (!response.ok) {
    const text = await response.text();
    return NextResponse.json(
      { error: text || 'Diagnostics bundle export failed.' },
      { status: response.status },
    );
  }

  const contentType = response.headers.get('content-type') ?? 'application/zip';
  const contentDisposition =
    response.headers.get('content-disposition') ??
    'attachment; filename="aetherguard-diagnostics.zip"';

  return new NextResponse(response.body, {
    status: response.status,
    headers: {
      'Content-Type': contentType,
      'Content-Disposition': contentDisposition,
      'Cache-Control': 'no-store',
    },
  });
}
