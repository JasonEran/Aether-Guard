import { NextResponse } from 'next/server';

export async function POST() {
  return NextResponse.json(
    { status: 'rebalance signaled', message: 'Chaos signal accepted.' },
    { status: 202 },
  );
}
