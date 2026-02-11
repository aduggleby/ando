// =============================================================================
// pages/NotFound.tsx
//
// 404 Not Found page component.
// =============================================================================

import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/Button';

export function NotFound() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full text-center">
        <h1 className="text-9xl font-bold text-gray-200 dark:text-slate-800">404</h1>
        <h2 className="mt-4 text-2xl font-bold text-gray-900 dark:text-slate-50">Page not found</h2>
        <p className="mt-2 text-gray-600 dark:text-slate-400">
          Sorry, we couldn't find the page you're looking for.
        </p>
        <div className="mt-6">
          <Link to="/">
            <Button>Go back home</Button>
          </Link>
        </div>
      </div>
    </div>
  );
}
