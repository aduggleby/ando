// =============================================================================
// components/ui/Button.test.tsx
//
// Tests for the Button component.
// =============================================================================

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Button } from './Button';

describe('Button', () => {
  it('renders children correctly', () => {
    render(<Button>Click me</Button>);
    expect(screen.getByRole('button')).toHaveTextContent('Click me');
  });

  it('calls onClick when clicked', () => {
    const handleClick = vi.fn();
    render(<Button onClick={handleClick}>Click me</Button>);

    fireEvent.click(screen.getByRole('button'));
    expect(handleClick).toHaveBeenCalledTimes(1);
  });

  it('is disabled when disabled prop is true', () => {
    render(<Button disabled>Disabled</Button>);
    expect(screen.getByRole('button')).toBeDisabled();
  });

  it('is disabled when isLoading prop is true', () => {
    render(<Button isLoading>Loading</Button>);
    expect(screen.getByRole('button')).toBeDisabled();
  });

  it('shows loading spinner when isLoading', () => {
    render(<Button isLoading>Loading</Button>);
    // Check for SVG spinner element
    expect(screen.getByRole('button').querySelector('svg')).toBeInTheDocument();
  });

  it('applies primary variant styles by default', () => {
    render(<Button>Primary</Button>);
    expect(screen.getByRole('button')).toHaveClass('bg-primary-600');
  });

  it('applies secondary variant styles', () => {
    render(<Button variant="secondary">Secondary</Button>);
    expect(screen.getByRole('button')).toHaveClass('bg-white');
    expect(screen.getByRole('button')).toHaveClass('border-gray-300');
  });

  it('applies danger variant styles', () => {
    render(<Button variant="danger">Danger</Button>);
    expect(screen.getByRole('button')).toHaveClass('bg-error-600');
  });

  it('applies ghost variant styles', () => {
    render(<Button variant="ghost">Ghost</Button>);
    expect(screen.getByRole('button')).toHaveClass('hover:bg-gray-100');
  });

  it('applies small size styles', () => {
    render(<Button size="sm">Small</Button>);
    expect(screen.getByRole('button')).toHaveClass('px-3');
    expect(screen.getByRole('button')).toHaveClass('py-1.5');
  });

  it('applies medium size styles by default', () => {
    render(<Button>Medium</Button>);
    expect(screen.getByRole('button')).toHaveClass('px-4');
    expect(screen.getByRole('button')).toHaveClass('py-2');
  });

  it('applies large size styles', () => {
    render(<Button size="lg">Large</Button>);
    expect(screen.getByRole('button')).toHaveClass('px-6');
    expect(screen.getByRole('button')).toHaveClass('py-3');
  });

  it('accepts custom className', () => {
    render(<Button className="custom-class">Custom</Button>);
    expect(screen.getByRole('button')).toHaveClass('custom-class');
  });
});
