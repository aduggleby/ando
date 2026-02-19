// =============================================================================
// pages/auth/RegisterSuccess.tsx
//
// Registration success landing page.
// =============================================================================

import { Link, useSearchParams } from 'react-router-dom';

export function RegisterSuccess() {
  const [searchParams] = useSearchParams();
  const email = searchParams.get('email') ?? '';

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full space-y-6 text-center">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-50">Registration Complete</h1>
        <p className="text-gray-600 dark:text-slate-400">
          {email
            ? `Account created for ${email}. Check your inbox to verify your email.`
            : 'Account created. Check your inbox to verify your email.'}
        </p>
        <Link to="/auth/login" className="font-medium text-primary-600 hover:text-primary-500 dark:text-primary-400">
          Continue to sign in
        </Link>
      </div>
    </div>
  );
}
