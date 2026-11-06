// =============================================================================
// StepCancellation.cs
//
// Summary: Cancellation token for in-process steps.
//
// Command steps are interrupted by Ctrl+C through the terminal's signal. Steps
// that run in-process (HTTP health checks) read this token instead. It is the
// run's token during normal steps and CancellationToken.None during cleanup, so
// on-failure and finally steps are never cut short. Cancelled tells those
// cleanup steps (Ando.Cancelled) that the run was cancelled.
// =============================================================================

namespace Ando.Steps;

/// <summary>The cancellation token in-process steps should honor.</summary>
public static class StepCancellation
{
    /// <summary>Token for the step that is running now (None during cleanup).</summary>
    public static CancellationToken Current { get; internal set; }

    // The run's own token, unlike Current it stays set during cleanup. AsyncLocal:
    // it flows into the steps of the run that set it and nowhere else.
    private static readonly AsyncLocal<CancellationToken> RunToken = new();

    internal static void SetRun(CancellationToken token) => RunToken.Value = token;

    /// <summary>True once the user pressed Ctrl+C during the current run.</summary>
    public static bool Cancelled => RunToken.Value.IsCancellationRequested;
}
