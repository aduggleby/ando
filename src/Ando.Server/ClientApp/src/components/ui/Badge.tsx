// =============================================================================
// components/ui/Badge.tsx
//
// Badge component for status indicators.
// =============================================================================

import type { ReactNode } from 'react';

interface BadgeProps {
  variant?: 'success' | 'warning' | 'error' | 'info' | 'default' | 'primary';
  size?: 'sm' | 'md' | 'lg';
  children: ReactNode;
  className?: string;
}

const variantStyles = {
  success: 'border border-success-200 bg-success-100 text-success-800 dark:border-success-500/30 dark:bg-success-500/20 dark:text-success-300',
  warning: 'border border-warning-200 bg-warning-100 text-warning-800 dark:border-warning-500/30 dark:bg-warning-500/20 dark:text-warning-300',
  error: 'border border-error-200 bg-error-100 text-error-800 dark:border-error-500/30 dark:bg-error-500/20 dark:text-error-300',
  info: 'border border-primary-200 bg-primary-100 text-primary-800 dark:border-primary-500/30 dark:bg-primary-500/20 dark:text-primary-300',
  primary: 'border border-primary-200 bg-primary-100 text-primary-800 dark:border-primary-500/30 dark:bg-primary-500/20 dark:text-primary-300',
  default: 'border border-gray-200 bg-gray-100 text-gray-800 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-200',
};

const sizeStyles = {
  sm: 'px-2 py-0.5 text-xs',
  md: 'px-2.5 py-0.5 text-xs',
  lg: 'px-3 py-1 text-sm',
};

export function Badge({ variant = 'default', size = 'md', children, className = '' }: BadgeProps) {
  return (
    <span
      className={`
        inline-flex items-center rounded-full font-medium
        ${variantStyles[variant]}
        ${sizeStyles[size]}
        ${className}
      `}
    >
      {children}
    </span>
  );
}

// Helper function to get badge variant from build status
export function getBuildStatusVariant(status: string): BadgeProps['variant'] {
  switch (status) {
    case 'Success':
      return 'success';
    case 'Failed':
    case 'TimedOut':
      return 'error';
    case 'Running':
    case 'Queued':
      return 'info';
    case 'Cancelled':
      return 'warning';
    default:
      return 'default';
  }
}
