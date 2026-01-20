'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { signIn } from 'next-auth/react';

const LockIcon = ({ className }: { className?: string }) => (
  <svg
    className={className}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.6"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    <rect x="3" y="11" width="18" height="10" rx="2" />
    <path d="M7 11V8a5 5 0 0 1 10 0v3" />
  </svg>
);

export default function LoginPage() {
  const router = useRouter();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError('');
    setIsSubmitting(true);

    const result = await signIn('credentials', {
      username,
      password,
      redirect: false,
      redirectTo: '/',
    });

    setIsSubmitting(false);

    if (result?.error) {
      setError('Invalid username or password.');
      return;
    }

    router.push(result?.url ?? '/');
  };

  return (
    <main className="min-h-screen bg-gray-950 text-slate-100 flex items-center justify-center p-6">
      <div className="w-full max-w-md rounded-2xl border border-gray-800 bg-gray-900 p-8 shadow-xl">
        <div className="flex items-center gap-3">
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-gray-800 text-emerald-400">
            <LockIcon className="h-6 w-6" />
          </div>
          <div>
            <h1 className="text-2xl font-semibold">Secure Access</h1>
            <p className="text-sm text-slate-400">Sign in to continue to the dashboard.</p>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="mt-8 space-y-4">
          <div>
            <label htmlFor="username" className="text-sm text-slate-300">
              Username
            </label>
            <input
              id="username"
              type="text"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              className="mt-2 w-full rounded-lg border border-gray-800 bg-gray-950 px-3 py-2 text-sm text-slate-100 focus:border-emerald-400 focus:outline-none"
              placeholder="admin"
              autoComplete="username"
              required
            />
          </div>

          <div>
            <label htmlFor="password" className="text-sm text-slate-300">
              Password
            </label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              className="mt-2 w-full rounded-lg border border-gray-800 bg-gray-950 px-3 py-2 text-sm text-slate-100 focus:border-emerald-400 focus:outline-none"
              placeholder="admin123"
              autoComplete="current-password"
              required
            />
          </div>

          {error ? <p className="text-sm text-red-400">{error}</p> : null}

          <button
            type="submit"
            disabled={isSubmitting}
            className="w-full rounded-lg bg-gradient-to-r from-emerald-500 to-sky-500 py-2 text-sm font-semibold text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {isSubmitting ? 'Signing in...' : 'Sign In'}
          </button>
        </form>
      </div>
    </main>
  );
}
