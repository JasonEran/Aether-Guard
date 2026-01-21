import { NextResponse } from 'next/server';

import { auth } from '../../../auth';

interface CommandRequest {
  agentId?: string;
  type?: string;
}

export async function POST(req: Request) {
  const session = await auth();
  const userRole = session?.user?.role ?? 'VIEWER';

  if (userRole !== 'ADMIN') {
    return NextResponse.json({ error: 'Forbidden' }, { status: 403 });
  }

  let payload: CommandRequest;
  try {
    payload = (await req.json()) as CommandRequest;
  } catch {
    return NextResponse.json({ error: 'Invalid JSON body.' }, { status: 400 });
  }

  if (!payload.agentId || !payload.type) {
    return NextResponse.json({ error: 'agentId and type are required.' }, { status: 400 });
  }

  const apiKey = process.env.COMMAND_API_KEY;
  if (!apiKey) {
    return NextResponse.json({ error: 'Command API key not configured.' }, { status: 500 });
  }

  const coreBaseUrl = process.env.CORE_API_URL ?? 'http://core-service:8080';
  const response = await fetch(`${coreBaseUrl}/api/v1/agents/${payload.agentId}/commands`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-API-Key': apiKey,
    },
    body: JSON.stringify({ type: payload.type }),
  });

  const text = await response.text();
  if (!response.ok) {
    return NextResponse.json({ error: text || 'Command request failed.' }, { status: response.status });
  }

  if (!text) {
    return NextResponse.json({ status: 'queued' }, { status: response.status });
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
