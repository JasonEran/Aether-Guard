'use client';

import { useState } from 'react';

import { sendCommand } from '../lib/api';

interface ControlPanelProps {
  agentId: string;
  userRole: string;
}

export default function ControlPanel({ agentId, userRole }: ControlPanelProps) {
  const [status, setStatus] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [message, setMessage] = useState<string>('');
  const isAdmin = userRole === 'ADMIN';

  const handleRestart = async () => {
    if (!agentId) {
      setStatus('error');
      setMessage('No agent selected.');
      return;
    }

    setStatus('loading');
    setMessage('Sending command...');

    try {
      await sendCommand(agentId, 'RESTART');
      setStatus('success');
      setMessage('Command queued.');
    } catch (error) {
      setStatus('error');
      setMessage(error instanceof Error ? error.message : 'Command failed.');
    }

    setTimeout(() => {
      setStatus('idle');
      setMessage('');
    }, 4000);
  };

  const buttonLabel = status === 'loading' ? 'Queueing...' : 'Restart Agent';
  const buttonDisabled = status === 'loading';

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900 p-6 shadow-lg">
      <div className="text-sm uppercase tracking-wide text-slate-400">Control Panel</div>
      <div className="mt-4 flex items-center gap-3 text-lg font-semibold text-slate-100">
        Remote Actions
        {!isAdmin && (
          <span className="rounded bg-slate-800 px-2 py-1 text-xs font-medium text-slate-400">
            Read-Only Access
          </span>
        )}
      </div>
      {isAdmin ? (
        <>
          <button
            type="button"
            onClick={handleRestart}
            disabled={buttonDisabled}
            className="mt-4 w-full rounded-lg border border-red-500 bg-red-600 px-4 py-2 text-sm font-semibold text-white transition hover:border-red-400 hover:bg-red-500 disabled:cursor-not-allowed disabled:border-slate-700 disabled:bg-slate-800"
          >
            {buttonLabel}
          </button>
          {message && (
            <div
              className={`mt-3 text-sm ${
                status === 'success'
                  ? 'text-emerald-400'
                  : status === 'error'
                  ? 'text-red-400'
                  : 'text-slate-400'
              }`}
            >
              {message}
            </div>
          )}
        </>
      ) : (
        <p className="mt-3 text-sm text-slate-400">
          Commands are disabled for your role. Contact an administrator for access.
        </p>
      )}
    </div>
  );
}
