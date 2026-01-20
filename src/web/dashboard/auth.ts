import NextAuth from 'next-auth';
import Credentials from 'next-auth/providers/credentials';

const adminUser = process.env.DASHBOARD_ADMIN_USER ?? 'admin';
const adminPassword = process.env.DASHBOARD_ADMIN_PASSWORD ?? 'admin123';

export const { handlers, auth, signIn, signOut } = NextAuth({
  secret: process.env.AUTH_SECRET ?? process.env.NEXTAUTH_SECRET ?? 'dev-secret-change-me',
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
        username: { label: 'Username', type: 'text' },
        password: { label: 'Password', type: 'password' },
      },
      authorize: async (credentials) => {
        const username = credentials?.username;
        const password = credentials?.password;

        if (typeof username !== 'string' || typeof password !== 'string') {
          return null;
        }

        if (username === adminUser && password === adminPassword) {
          return { id: '1', name: 'Admin' };
        }

        return null;
      },
    }),
  ],
  pages: {
    signIn: '/login',
  },
});

export const { GET, POST } = handlers;
