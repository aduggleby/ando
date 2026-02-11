// =============================================================================
// components/ui/Alert.tsx
//
// Alert component for displaying messages to the user.
// =============================================================================

import { type ReactNode } from 'react';

interface AlertProps {
  variant?: 'success' | 'warning' | 'error' | 'info';
  title?: string;
  children: ReactNode;
  onClose?: () => void;
  className?: string;
}

const variantStyles = {
  success: {
    container: 'bg-success-50 border-success-500 text-success-800 dark:bg-success-500/10 dark:text-success-400',
    icon: 'text-success-500',
  },
  warning: {
    container: 'bg-warning-50 border-warning-500 text-warning-800 dark:bg-warning-500/10 dark:text-warning-400',
    icon: 'text-warning-500',
  },
  error: {
    container: 'bg-error-50 border-error-500 text-error-800 dark:bg-error-500/10 dark:text-error-400',
    icon: 'text-error-500',
  },
  info: {
    container: 'bg-primary-50 border-primary-500 text-primary-800 dark:bg-primary-500/10 dark:text-primary-400',
    icon: 'text-primary-500',
  },
};

export function Alert({ variant = 'info', title, children, onClose, className = '' }: AlertProps) {
  const styles = variantStyles[variant];

  return (
    <div className={`rounded-md border-l-4 p-4 ${styles.container} ${className}`}>
      <div className="flex">
        <div className="flex-1">
          {title && (
            <h3 className="text-sm font-medium mb-1">{title}</h3>
          )}
          <div className="text-sm">{children}</div>
        </div>
        {onClose && (
          <button
            onClick={onClose}
            className="ml-4 text-gray-400 hover:text-gray-500 dark:text-slate-500 dark:hover:text-slate-400"
          >
            <span className="sr-only">Close</span>
            <svg className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
              <path
                fillRule="evenodd"
                d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
                clipRule="evenodd"
              />
            </svg>
          </button>
        )}
      </div>
    </div>
  );
}
