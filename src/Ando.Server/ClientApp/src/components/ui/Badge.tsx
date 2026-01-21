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
  success: 'bg-success-100 text-success-800',
  warning: 'bg-warning-100 text-warning-800',
  error: 'bg-error-100 text-error-800',
  info: 'bg-primary-100 text-primary-800',
  primary: 'bg-primary-100 text-primary-800',
  default: 'bg-gray-100 text-gray-800',
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
