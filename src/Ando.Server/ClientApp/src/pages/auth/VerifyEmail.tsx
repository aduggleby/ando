// =============================================================================
// pages/auth/VerifyEmail.tsx
//
// Email verification page.
// =============================================================================

import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { verifyEmail } from '@/api/auth';
import { Alert } from '@/components/ui/Alert';
import { Loading } from '@/components/ui/Loading';

export function VerifyEmail() {
  const [searchParams] = useSearchParams();
  const [isLoading, setIsLoading] = useState(true);
  const [result, setResult] = useState<{ success: boolean; message: string } | null>(null);

  useEffect(() => {
    const userId = searchParams.get('userId');
    const token = searchParams.get('token');

    if (!userId || !token) {
      setResult({ success: false, message: 'Invalid verification link.' });
      setIsLoading(false);
      return;
    }

    let mounted = true;
    void (async () => {
      try {
        const response = await verifyEmail(userId, token);
        if (mounted) {
          setResult(response);
        }
      } finally {
        if (mounted) {
          setIsLoading(false);
        }
      }
    })();

    return () => {
      mounted = false;
    };
  }, [searchParams]);

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Verifying your email..." />;
  }

  if (!result) {
    return <Alert variant="error">Verification failed.</Alert>;
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full space-y-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-50 text-center">
          Email Verification
        </h1>

        <Alert variant={result.success ? 'success' : 'error'}>
          {result.message}
        </Alert>

        <div className="text-center">
          <Link
            to={result.success ? '/auth/login' : '/auth/register'}
            className="font-medium text-primary-600 hover:text-primary-500 dark:text-primary-400"
          >
            {result.success ? 'Continue to sign in' : 'Back to registration'}
          </Link>
        </div>
      </div>
    </div>
  );
}
