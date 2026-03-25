// =============================================================================
// pages/auth/AccessDenied.tsx
//
// Access denied page.
// =============================================================================

import { Link } from 'react-router-dom';

export function AccessDenied() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full space-y-6 text-center">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">Access Denied</h1>
        <p className="text-gray-600 dark:text-slate-400">
          You do not have permission to access this page.
        </p>
        <Link to="/" className="font-medium text-primary-600 hover:text-primary-500 dark:text-primary-400">
          Return to dashboard
        </Link>
      </div>
    </div>
  );
}
