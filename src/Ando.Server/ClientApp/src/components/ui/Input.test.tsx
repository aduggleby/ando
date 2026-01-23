// =============================================================================
// components/ui/Input.test.tsx
//
// Tests for the Input component.
// =============================================================================

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Input } from './Input';

describe('Input', () => {
  it('renders label when provided', () => {
    render(<Input label="Email" />);
    expect(screen.getByText('Email')).toBeInTheDocument();
  });

  it('renders input element', () => {
    render(<Input label="Email" />);
    expect(screen.getByRole('textbox')).toBeInTheDocument();
  });

  it('renders helper text when provided', () => {
    render(<Input label="Password" helperText="At least 8 characters" />);
    expect(screen.getByText('At least 8 characters')).toBeInTheDocument();
  });

  it('renders error text when provided', () => {
    render(<Input label="Email" error="Invalid email" />);
    expect(screen.getByText('Invalid email')).toBeInTheDocument();
  });

  it('applies error styles when error is provided', () => {
    render(<Input label="Email" error="Invalid email" />);
    expect(screen.getByRole('textbox')).toHaveClass('border-error-500');
  });

  it('calls onChange when input value changes', () => {
    const handleChange = vi.fn();
    render(<Input label="Email" onChange={handleChange} />);

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'test@example.com' } });
    expect(handleChange).toHaveBeenCalled();
  });

  it('accepts type prop', () => {
    render(<Input label="Password" type="password" />);
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
  });

  it('accepts placeholder prop', () => {
    render(<Input label="Email" placeholder="Enter your email" />);
    expect(screen.getByPlaceholderText('Enter your email')).toBeInTheDocument();
  });

  it('accepts required prop', () => {
    render(<Input label="Email" required />);
    expect(screen.getByRole('textbox')).toBeRequired();
  });

  it('accepts disabled prop', () => {
    render(<Input label="Email" disabled />);
    expect(screen.getByRole('textbox')).toBeDisabled();
  });

  it('accepts custom className', () => {
    render(<Input label="Email" className="custom-class" />);
    expect(screen.getByRole('textbox')).toHaveClass('custom-class');
  });
});
