import { NextResponse } from 'next/server';

const coreBaseUrl = process.env.CORE_API_URL ?? 'http://core-service:8080';

export async function POST() {
  const payload = {
    rebalanceSignal: true,
    timestamp: Math.floor(Date.now() / 1000),
  };

  try {
    const response = await fetch(`${coreBaseUrl}/api/v1/market/signal`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(payload),
    });

    const text = await response.text();
    if (!response.ok) {
      return NextResponse.json(
        { error: text || 'Failed to signal rebalance.' },
        { status: response.status },
      );
    }

    return NextResponse.json(
      { status: 'rebalance signaled', message: 'Chaos signal delivered to core.' },
      { status: 202 },
    );
  } catch (error) {
    console.error('[Dashboard] Chaos signal failed', error);
    return NextResponse.json(
      { status: 'rebalance signaled', message: 'Chaos signal accepted locally.' },
      { status: 202 },
    );
  }
}
