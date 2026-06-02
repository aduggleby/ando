// =============================================================================
// components/layout/useServerUpdateFlow.ts
//
// Client flow for update UX: show countdown, then poll health until server is back.
// =============================================================================

import { useCallback, useEffect, useRef, useState } from 'react';

type UpdatePhase = 'idle' | 'countdown' | 'reconnecting';

const COUNTDOWN_SECONDS = 30;
const RETRY_DELAY_MS = 2000;

function sleep(ms: number) {
  return new Promise((resolve) => {
    window.setTimeout(resolve, ms);
  });
}

export function useServerUpdateFlow() {
  const [phase, setPhase] = useState<UpdatePhase>('idle');
  const [remainingSeconds, setRemainingSeconds] = useState(COUNTDOWN_SECONDS);
  const [attempts, setAttempts] = useState(0);
  const runIdRef = useRef(0);

  const start = useCallback(() => {
    const runId = ++runIdRef.current;
    setPhase('countdown');
    setRemainingSeconds(COUNTDOWN_SECONDS);
    setAttempts(0);

    const run = async () => {
      for (let i = COUNTDOWN_SECONDS; i > 0; i--) {
        if (runIdRef.current != runId) {
          return;
        }

        await sleep(1000);
        if (runIdRef.current != runId) {
          return;
        }

        setRemainingSeconds(i - 1);
      }

      setPhase('reconnecting');

      let localAttempts = 0;
      while (runIdRef.current == runId) {
        localAttempts += 1;
        setAttempts(localAttempts);

        try {
          const response = await fetch('/health', {
            method: 'GET',
            cache: 'no-store',
          });

          if (response.ok) {
            window.location.reload();
            return;
          }
        } catch {
          // Server may be down during restart; keep polling.
        }

        await sleep(RETRY_DELAY_MS);
      }
    };

    void run();
  }, []);

  useEffect(() => {
    return () => {
      // Invalidate in-flight loop on unmount.
      runIdRef.current += 1;
    };
  }, []);

  return {
    isVisible: phase !== 'idle',
    phase,
    remainingSeconds,
    attempts,
    start,
  };
}

