// =============================================================================
// components/ui/Loading.test.tsx
//
// Tests for the Loading component.
// =============================================================================

import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Loading } from './Loading';

describe('Loading', () => {
  it('renders loading spinner', () => {
    render(<Loading />);
    // Check for the spinner SVG
    expect(document.querySelector('svg.animate-spin')).toBeInTheDocument();
  });

  it('renders text when provided', () => {
    render(<Loading text="Loading..." />);
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('applies small size styles', () => {
    render(<Loading size="sm" />);
    expect(document.querySelector('svg')).toHaveClass('h-4', 'w-4');
  });

  it('applies medium size styles by default', () => {
    render(<Loading />);
    expect(document.querySelector('svg')).toHaveClass('h-8', 'w-8');
  });

  it('applies large size styles', () => {
    render(<Loading size="lg" />);
    expect(document.querySelector('svg')).toHaveClass('h-12', 'w-12');
  });

  it('accepts custom className', () => {
    render(<Loading className="custom-class" />);
    expect(document.querySelector('.custom-class')).toBeInTheDocument();
  });
});
