// =============================================================================
// components/ui/Input.tsx
//
// Phosphor input with rounded-lg and subtle borders.
// =============================================================================

import { type InputHTMLAttributes, forwardRef } from 'react';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  helperText?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, helperText, className = '', id, ...props }, ref) => {
    const inputId = id || label?.toLowerCase().replace(/\s+/g, '-');

    return (
      <div className="w-full">
        {label && (
          <label
            htmlFor={inputId}
            className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5"
          >
            {label}
          </label>
        )}
        <input
          ref={ref}
          id={inputId}
          className={`
            block w-full rounded-lg px-3 py-2 text-sm
            bg-white border border-gray-200 text-gray-900
            placeholder-gray-400
            focus:border-primary-500 focus:ring-1 focus:ring-primary-500
            dark:bg-slate-800 dark:border-slate-700 dark:text-slate-100
            dark:placeholder-slate-500 dark:focus:border-primary-400 dark:focus:ring-primary-400
            ${error ? 'border-error-500 focus:border-error-500 focus:ring-error-500 dark:border-error-500' : ''}
            ${className}
          `}
          {...props}
        />
        {error && (
          <p className="mt-1.5 text-xs text-error-600 dark:text-error-400">{error}</p>
        )}
        {helperText && !error && (
          <p className="mt-1.5 text-xs text-gray-400 dark:text-slate-500">{helperText}</p>
        )}
      </div>
    );
  }
);

Input.displayName = 'Input';
