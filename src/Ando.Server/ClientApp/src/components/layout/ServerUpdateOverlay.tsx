// =============================================================================
// components/layout/ServerUpdateOverlay.tsx
//
// Full-screen overlay shown while a server update is being applied.
// =============================================================================

type ServerUpdatePhase = 'countdown' | 'reconnecting';

export function ServerUpdateOverlay({
  phase,
  remainingSeconds,
  attempts,
}: {
  phase: ServerUpdatePhase;
  remainingSeconds: number;
  attempts: number;
}) {
  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-950/80 backdrop-blur-sm px-6">
      <div className="w-full max-w-lg rounded-2xl border border-slate-700 bg-slate-900/95 p-8 shadow-2xl shadow-slate-950/70">
        <h2 className="text-xl font-semibold text-slate-100">Updating server</h2>
        {phase === 'countdown' ? (
          <p className="mt-3 text-slate-300">
            Update started. Waiting {remainingSeconds}s for container restart before reconnecting.
          </p>
        ) : (
          <p className="mt-3 text-slate-300">
            Reconnecting to server. Attempt {attempts}.
          </p>
        )}

        <div className="mt-5 h-2 w-full overflow-hidden rounded-full bg-slate-800">
          {phase === 'countdown' ? (
            <div
              className="h-full bg-primary-500 transition-[width] duration-500"
              style={{ width: `${Math.max(0, Math.min(100, ((30 - remainingSeconds) / 30) * 100))}%` }}
            />
          ) : (
            <div className="h-full w-1/2 animate-pulse bg-primary-500" />
          )}
        </div>
      </div>
    </div>
  );
}

