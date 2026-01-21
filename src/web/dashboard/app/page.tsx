import { auth } from '../auth';
import DashboardClient from './DashboardClient';

export default async function Page() {
  const session = await auth();
  const user = session?.user;
  const userName = user?.name ?? 'User';
  const userRole = user?.role ?? 'VIEWER';

  return <DashboardClient userName={userName} userRole={userRole} />;
}
