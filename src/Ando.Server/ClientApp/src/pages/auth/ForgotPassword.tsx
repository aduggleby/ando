// =============================================================================
// pages/auth/ForgotPassword.tsx
//
// Forgot password page component.
// =============================================================================

import { useState } from 'react';
import { Link } from 'react-router-dom';
import { forgotPassword } from '@/api/auth';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Alert } from '@/components/ui/Alert';

export function ForgotPassword() {
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);

    try {
      const result = await forgotPassword(email);
      if (result.success) {
        setSuccess(true);
      } else {
        setError(result.message || 'Failed to send reset email');
      }
    } catch {
      setError('An error occurred. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  if (success) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950 py-12 px-4 sm:px-6 lg:px-8">
        <div className="max-w-md w-full space-y-8">
          <div>
            <h2 className="mt-6 text-center text-3xl font-extrabold text-gray-900 dark:text-slate-100">
              Check your email
            </h2>
            <p className="mt-2 text-center text-sm text-gray-600 dark:text-slate-400">
              If an account exists with that email, we've sent password reset instructions.
            </p>
          </div>
          <div className="text-center">
            <Link to="/auth/login" className="font-medium text-primary-600 hover:text-primary-500 dark:text-primary-400">
              Back to sign in
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full space-y-8">
        <div>
          <h2 className="mt-6 text-center text-3xl font-extrabold text-gray-900 dark:text-slate-100">
            Reset your password
          </h2>
          <p className="mt-2 text-center text-sm text-gray-600 dark:text-slate-400">
            Enter your email and we'll send you a reset link.
          </p>
        </div>

        {error && (
          <Alert variant="error">
            {error}
          </Alert>
        )}

        <form className="mt-8 space-y-6" onSubmit={handleSubmit}>
          <Input
            label="Email address"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />

          <Button
            type="submit"
            className="w-full"
            isLoading={isLoading}
          >
            Send reset link
          </Button>

          <div className="text-center">
            <Link to="/auth/login" className="font-medium text-primary-600 hover:text-primary-500 dark:text-primary-400">
              Back to sign in
            </Link>
          </div>
        </form>
      </div>
    </div>
  );
}
