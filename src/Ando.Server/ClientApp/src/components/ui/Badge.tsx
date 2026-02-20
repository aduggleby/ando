// =============================================================================
// components/ui/Badge.tsx
//
// Phosphor badge with dot indicator and pill shape.
// =============================================================================

import type { ReactNode } from 'react';

interface BadgeProps {
  variant?: 'success' | 'warning' | 'error' | 'info' | 'default' | 'primary';
  size?: 'sm' | 'md' | 'lg';
  children: ReactNode;
  className?: string;
}

const variantStyles = {
  success: 'text-success-600 bg-success-50 dark:text-success-400 dark:bg-success-500/10',
  warning: 'text-warning-600 bg-warning-50 dark:text-warning-500 dark:bg-warning-500/10',
  error: 'text-error-600 bg-error-50 dark:text-error-400 dark:bg-error-500/10',
  info: 'text-primary-600 bg-primary-50 dark:text-primary-400 dark:bg-primary-500/10',
  primary: 'text-primary-600 bg-primary-50 dark:text-primary-400 dark:bg-primary-500/10',
  default: 'text-gray-600 bg-gray-100 dark:text-slate-300 dark:bg-slate-700',
};

const dotStyles = {
  success: 'bg-success-500 shadow-[0_0_6px_rgba(16,185,129,0.5)]',
  warning: 'bg-warning-500 shadow-[0_0_6px_rgba(245,158,11,0.5)]',
  error: 'bg-error-500 shadow-[0_0_6px_rgba(244,63,94,0.5)]',
  info: 'bg-primary-500 shadow-[0_0_6px_rgba(6,182,212,0.5)]',
  primary: 'bg-primary-500 shadow-[0_0_6px_rgba(6,182,212,0.5)]',
  default: 'bg-gray-400 dark:bg-slate-400',
};

const sizeStyles = {
  sm: 'px-2 py-0.5 text-[10px]',
  md: 'px-2.5 py-0.5 text-[11px]',
  lg: 'px-3 py-1 text-xs',
};

export function Badge({ variant = 'default', size = 'md', children, className = '' }: BadgeProps) {
  return (
    <span
      className={`
        inline-flex items-center gap-1.5 rounded-full font-semibold
        ${variantStyles[variant]}
        ${sizeStyles[size]}
        ${className}
      `}
    >
      <span className={`w-1.5 h-1.5 rounded-full ${dotStyles[variant]}`} />
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
