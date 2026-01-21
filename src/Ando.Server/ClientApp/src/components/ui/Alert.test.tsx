// =============================================================================
// components/ui/Alert.test.tsx
//
// Tests for the Alert component.
// =============================================================================

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Alert } from './Alert';

describe('Alert', () => {
  it('renders children correctly', () => {
    render(<Alert>Test message</Alert>);
    expect(screen.getByText('Test message')).toBeInTheDocument();
  });

  it('renders title when provided', () => {
    render(<Alert title="Alert Title">Test message</Alert>);
    expect(screen.getByText('Alert Title')).toBeInTheDocument();
    expect(screen.getByText('Test message')).toBeInTheDocument();
  });

  it('applies info variant styles by default', () => {
    const { container } = render(<Alert>Info alert</Alert>);
    expect(container.firstChild).toHaveClass('bg-primary-50');
  });

  it('applies success variant styles', () => {
    const { container } = render(<Alert variant="success">Success alert</Alert>);
    expect(container.firstChild).toHaveClass('bg-success-50');
  });

  it('applies warning variant styles', () => {
    const { container } = render(<Alert variant="warning">Warning alert</Alert>);
    expect(container.firstChild).toHaveClass('bg-warning-50');
  });

  it('applies error variant styles', () => {
    const { container } = render(<Alert variant="error">Error alert</Alert>);
    expect(container.firstChild).toHaveClass('bg-error-50');
  });

  it('shows close button when onClose is provided', () => {
    const handleClose = vi.fn();
    render(<Alert onClose={handleClose}>Closable alert</Alert>);

    const closeButton = screen.getByRole('button');
    expect(closeButton).toBeInTheDocument();
  });

  it('calls onClose when close button is clicked', () => {
    const handleClose = vi.fn();
    render(<Alert onClose={handleClose}>Closable alert</Alert>);

    fireEvent.click(screen.getByRole('button'));
    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it('does not show close button when onClose is not provided', () => {
    render(<Alert>Non-closable alert</Alert>);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('accepts custom className', () => {
    const { container } = render(<Alert className="custom-class">Custom</Alert>);
    expect(container.firstChild).toHaveClass('custom-class');
  });
});
