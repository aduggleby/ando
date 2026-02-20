// =============================================================================
// pages/auth/Login.tsx
//
// Phosphor-styled login page.
// =============================================================================

import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '@/context/AuthContext';
import { getDevLoginAvailability } from '@/api/auth';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Alert } from '@/components/ui/Alert';

export function Login() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(true);
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [showDevLogin, setShowDevLogin] = useState(false);

  const { login, devLogin } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const returnUrl = searchParams.get('returnUrl') || searchParams.get('ReturnUrl') || '/';

  useEffect(() => {
    let isMounted = true;

    const loadDevLoginAvailability = async () => {
      const isAvailable = await getDevLoginAvailability();
      if (isMounted) {
        setShowDevLogin(isAvailable);
      }
    };

    loadDevLoginAvailability();

    return () => {
      isMounted = false;
    };
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);

    try {
      const result = await login(email, password, rememberMe);
      if (result.success) {
        navigate(returnUrl, { replace: true });
      } else {
        setError(result.error || 'Login failed');
      }
    } catch {
      setError('An error occurred. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleDevLogin = async () => {
    setError('');
    setIsLoading(true);
    try {
      const result = await devLogin();
      if (result.success) {
        navigate(returnUrl, { replace: true });
      } else {
        setError(result.error || 'Development login failed');
      }
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-sm w-full space-y-8">
        {/* Logo */}
        <div className="text-center">
          <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-primary-400 to-purple-500 flex items-center justify-center text-white text-base font-bold shadow-lg shadow-primary-500/20 mx-auto">
            A
          </div>
          <h2 className="mt-4 text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight">
            Sign in to Ando
          </h2>
          <p className="mt-1.5 text-sm text-gray-500 dark:text-slate-400">
            Or{' '}
            <Link to="/auth/register" className="font-medium text-primary-600 hover:text-primary-500 dark:text-primary-400">
              create a new account
            </Link>
          </p>
        </div>

        {error && (
          <Alert variant="error">
            {error}
          </Alert>
        )}

        <form className="space-y-5" onSubmit={handleSubmit}>
          <div className="space-y-4">
            <Input
              label="Email address"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />

            <Input
              label="Password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>

          <div className="flex items-center justify-between">
            <div className="flex items-center">
              <input
                id="remember-me"
                name="remember-me"
                type="checkbox"
                className="h-4 w-4 text-primary-600 focus:ring-primary-500 border-gray-300 rounded dark:bg-slate-800 dark:border-slate-700"
                checked={rememberMe}
                onChange={(e) => setRememberMe(e.target.checked)}
              />
              <label htmlFor="remember-me" className="ml-2 block text-sm text-gray-600 dark:text-slate-400">
                Remember me
              </label>
            </div>

            <Link to="/auth/forgot-password" className="text-sm font-medium text-primary-600 hover:text-primary-500 dark:text-primary-400">
              Forgot password?
            </Link>
          </div>

          <Button
            type="submit"
            className="w-full"
            isLoading={isLoading}
          >
            Sign in
          </Button>

          {showDevLogin && (
            <Button
              type="button"
              variant="secondary"
              className="w-full"
              onClick={handleDevLogin}
              isLoading={isLoading}
            >
              Development Login
            </Button>
          )}
        </form>
      </div>
    </div>
  );
}
