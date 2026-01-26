'use client';

interface FirstRunGuideProps {
  onDismiss?: () => void;
}

export default function FirstRunGuide({ onDismiss }: FirstRunGuideProps) {
  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-[0_0_30px_rgba(15,23,42,0.5)]">
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="text-xs uppercase tracking-[0.2em] text-slate-400">First Run Guide</div>
          <div className="mt-2 text-lg font-semibold text-slate-100">Bring the stack online</div>
          <p className="mt-2 text-sm text-slate-400">
            Simulation Mode means live telemetry has not arrived yet. Follow these steps to switch to
            live data.
          </p>
        </div>
        {onDismiss && (
          <button
            type="button"
            onClick={onDismiss}
            className="rounded-lg border border-slate-700 px-3 py-2 text-xs uppercase tracking-[0.2em] text-slate-300 transition hover:border-slate-500 hover:text-slate-100"
          >
            Dismiss
          </button>
        )}
      </div>
      <ol className="mt-4 space-y-3 text-sm text-slate-200">
        <li className="flex gap-3">
          <span className="text-slate-500">1.</span>
          <span>
            Validate prerequisites:{' '}
            <span className="rounded bg-slate-950/60 px-2 py-1 font-mono text-xs text-slate-200">
              python scripts/self_check.py --target docker
            </span>
          </span>
        </li>
        <li className="flex gap-3">
          <span className="text-slate-500">2.</span>
          <span>
            Start the platform stack:{' '}
            <span className="rounded bg-slate-950/60 px-2 py-1 font-mono text-xs text-slate-200">
              docker compose up --build -d
            </span>
          </span>
        </li>
        <li className="flex gap-3">
          <span className="text-slate-500">3.</span>
          <span>
            Scale to two agents:{' '}
            <span className="rounded bg-slate-950/60 px-2 py-1 font-mono text-xs text-slate-200">
              docker compose up -d --scale agent-service=2 agent-service
            </span>
          </span>
        </li>
        <li className="flex gap-3">
          <span className="text-slate-500">4.</span>
          <span>
            Run the fire drill:{' '}
            <span className="rounded bg-slate-950/60 px-2 py-1 font-mono text-xs text-slate-200">
              python scripts/fire_drill.py start
            </span>
          </span>
        </li>
        <li className="flex gap-3">
          <span className="text-slate-500">5.</span>
          <span>Confirm the dashboard shows risk changes and migration activity.</span>
        </li>
      </ol>
    </div>
  );
}
