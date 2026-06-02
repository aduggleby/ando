// =============================================================================
// pages/auth/Login.test.tsx
//
// Tests for development-login visibility on the login page.
// =============================================================================

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Login } from './Login';

const { getDevLoginAvailabilityMock } = vi.hoisted(() => ({
  getDevLoginAvailabilityMock: vi.fn<() => Promise<boolean>>(),
}));

vi.mock('@/api/auth', () => ({
  getDevLoginAvailability: getDevLoginAvailabilityMock,
}));

vi.mock('@/context/AuthContext', () => ({
  useAuth: () => ({
    login: vi.fn(),
    devLogin: vi.fn(),
  }),
}));

describe('Login', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows Development Login button when API reports availability', async () => {
    getDevLoginAvailabilityMock.mockResolvedValue(true);

    render(
      <MemoryRouter>
        <Login />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Development Login' })).toBeInTheDocument();
    });
  });

  it('hides Development Login button when API reports unavailable', async () => {
    getDevLoginAvailabilityMock.mockResolvedValue(false);

    render(
      <MemoryRouter>
        <Login />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(getDevLoginAvailabilityMock).toHaveBeenCalledTimes(1);
    });
    expect(screen.queryByRole('button', { name: 'Development Login' })).not.toBeInTheDocument();
  });
});
