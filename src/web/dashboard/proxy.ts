import { auth } from './auth';

export default auth((req) => {
  const isLoggedIn = !!req.auth;
  const isLoginPage = req.nextUrl.pathname === '/login';

  if (!isLoggedIn && !isLoginPage) {
    return Response.redirect(new URL('/login', req.nextUrl));
  }

  if (isLoggedIn && isLoginPage) {
    return Response.redirect(new URL('/', req.nextUrl));
  }

  return undefined;
});

export const config = {
  matcher: ['/((?!api|_next/static|_next/image|favicon.ico).*)'],
};
