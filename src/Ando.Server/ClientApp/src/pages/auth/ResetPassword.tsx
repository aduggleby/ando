// =============================================================================
// pages/auth/ResetPassword.tsx
//
// Reset password page component.
// =============================================================================

import { useState } from 'react';
import { Link, useSearchParams, useNavigate } from 'react-router-dom';
import { resetPassword } from '@/api/auth';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Alert } from '@/components/ui/Alert';

export function ResetPassword() {
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  const email = searchParams.get('email') || '';
  const token = searchParams.get('token') || '';

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (password !== confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    if (password.length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }

    if (!email || !token) {
      setError('Invalid reset link');
      return;
    }

    setIsLoading(true);

    try {
      const result = await resetPassword(email, token, password);
      if (result.success) {
        navigate('/auth/login?reset=success');
      } else {
        setError(result.message || 'Failed to reset password');
      }
    } catch {
      setError('An error occurred. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  if (!email || !token) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50 py-12 px-4 sm:px-6 lg:px-8">
        <div className="max-w-md w-full space-y-8">
          <Alert variant="error">
            Invalid password reset link. Please request a new one.
          </Alert>
          <div className="text-center">
            <Link to="/auth/forgot-password" className="font-medium text-primary-600 hover:text-primary-500">
              Request new reset link
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full space-y-8">
        <div>
          <h2 className="mt-6 text-center text-3xl font-extrabold text-gray-900">
            Set new password
          </h2>
          <p className="mt-2 text-center text-sm text-gray-600">
            Enter your new password below.
          </p>
        </div>

        {error && (
          <Alert variant="error">
            {error}
          </Alert>
        )}

        <form className="mt-8 space-y-6" onSubmit={handleSubmit}>
          <div className="space-y-4">
            <Input
              label="New password"
              type="password"
              autoComplete="new-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              helperText="At least 8 characters"
            />

            <Input
              label="Confirm new password"
              type="password"
              autoComplete="new-password"
              required
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
            />
          </div>

          <Button
            type="submit"
            className="w-full"
            isLoading={isLoading}
          >
            Reset password
          </Button>
        </form>
      </div>
    </div>
  );
}
