// =============================================================================
// components/ui/Badge.test.tsx
//
// Tests for the Badge component.
// =============================================================================

import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Badge, getBuildStatusVariant } from './Badge';

describe('Badge', () => {
  it('renders children correctly', () => {
    render(<Badge>Test Badge</Badge>);
    expect(screen.getByText('Test Badge')).toBeInTheDocument();
  });

  it('applies default variant styles', () => {
    render(<Badge>Default</Badge>);
    expect(screen.getByText('Default')).toHaveClass('bg-gray-100');
  });

  it('applies success variant styles', () => {
    render(<Badge variant="success">Success</Badge>);
    expect(screen.getByText('Success')).toHaveClass('bg-success-100');
  });

  it('applies warning variant styles', () => {
    render(<Badge variant="warning">Warning</Badge>);
    expect(screen.getByText('Warning')).toHaveClass('bg-warning-100');
  });

  it('applies error variant styles', () => {
    render(<Badge variant="error">Error</Badge>);
    expect(screen.getByText('Error')).toHaveClass('bg-error-100');
  });

  it('applies info variant styles', () => {
    render(<Badge variant="info">Info</Badge>);
    expect(screen.getByText('Info')).toHaveClass('bg-primary-100');
  });

  it('applies primary variant styles', () => {
    render(<Badge variant="primary">Primary</Badge>);
    expect(screen.getByText('Primary')).toHaveClass('bg-primary-100');
  });

  it('applies medium size styles by default', () => {
    render(<Badge>Medium</Badge>);
    expect(screen.getByText('Medium')).toHaveClass('px-2.5');
  });

  it('applies small size styles', () => {
    render(<Badge size="sm">Small</Badge>);
    expect(screen.getByText('Small')).toHaveClass('px-2');
  });

  it('applies large size styles', () => {
    render(<Badge size="lg">Large</Badge>);
    expect(screen.getByText('Large')).toHaveClass('px-3');
  });

  it('accepts custom className', () => {
    render(<Badge className="custom-class">Custom</Badge>);
    expect(screen.getByText('Custom')).toHaveClass('custom-class');
  });
});

describe('getBuildStatusVariant', () => {
  it('returns success for Success status', () => {
    expect(getBuildStatusVariant('Success')).toBe('success');
  });

  it('returns error for Failed status', () => {
    expect(getBuildStatusVariant('Failed')).toBe('error');
  });

  it('returns error for TimedOut status', () => {
    expect(getBuildStatusVariant('TimedOut')).toBe('error');
  });

  it('returns info for Running status', () => {
    expect(getBuildStatusVariant('Running')).toBe('info');
  });

  it('returns info for Queued status', () => {
    expect(getBuildStatusVariant('Queued')).toBe('info');
  });

  it('returns warning for Cancelled status', () => {
    expect(getBuildStatusVariant('Cancelled')).toBe('warning');
  });

  it('returns default for unknown status', () => {
    expect(getBuildStatusVariant('Unknown')).toBe('default');
  });
});
