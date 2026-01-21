import NextAuth from 'next-auth';
import Credentials from 'next-auth/providers/credentials';

const users = [
  { id: 'admin', email: 'admin@aether.com', name: 'Admin', role: 'ADMIN' },
  { id: 'viewer', email: 'viewer@aether.com', name: 'Viewer', role: 'VIEWER' },
];

const resolveRole = (email?: string | null, name?: string | null) => {
  if (email) {
    const match = users.find((entry) => entry.email === email.toLowerCase());
    if (match) {
      return match.role;
    }
  }

  if (name) {
    const match = users.find((entry) => entry.name.toLowerCase() === name.toLowerCase());
    if (match) {
      return match.role;
    }
  }

  return undefined;
};

export const { handlers, auth, signIn, signOut } = NextAuth({
  secret: process.env.AUTH_SECRET ?? process.env.NEXTAUTH_SECRET ?? 'dev-secret-change-me',
  session: {
    strategy: 'jwt',
  },
  logger: {
    error(error, ...message) {
      if (error instanceof Error && error.name === 'CredentialsSignin') {
        return;
      }
      console.error(error, ...message);
    },
  },
  providers: [
    Credentials({
      name: 'Credentials',
      credentials: {
        username: { label: 'Email', type: 'text' },
        password: { label: 'Password', type: 'password' },
      },
      authorize: async (credentials) => {
        const username = credentials?.username;
        const password = credentials?.password;

        if (typeof username !== 'string' || typeof password !== 'string' || password.length === 0) {
          return null;
        }

        const normalized = username.trim().toLowerCase();
        const user =
          users.find((entry) => entry.email === normalized) ??
          users.find((entry) => entry.name.toLowerCase() === normalized) ??
          users.find((entry) => entry.id.toLowerCase() === normalized) ??
          users.find((entry) => entry.email.split('@')[0] === normalized);
        if (!user) {
          return null;
        }

        return {
          id: user.id,
          name: user.name,
          email: user.email,
          role: user.role,
        };
      },
    }),
  ],
  callbacks: {
    jwt({ token, user }) {
      if (user) {
        token.role = (user as { role?: string }).role;
        token.email = user.email;
        token.name = user.name;
      }

      if (!token.role) {
        token.role = resolveRole(
          (token.email as string | null | undefined) ?? undefined,
          (token.name as string | null | undefined) ?? undefined,
        );
      }
      return token;
    },
    session({ session, token }) {
      if (session.user) {
        const email =
          (token.email as string | null | undefined) ?? session.user.email ?? undefined;
        const name =
          (token.name as string | null | undefined) ?? session.user.name ?? undefined;
        session.user.role = (token.role as string | undefined) ?? resolveRole(email, name) ?? 'VIEWER';
        session.user.email = email;
        session.user.name = name;
      }
      return session;
    },
  },
  pages: {
    signIn: '/login',
  },
});

export const { GET, POST } = handlers;
