// =============================================================================
// StepCancellation.cs
//
// Summary: Cancellation token for in-process steps.
//
// Command steps are interrupted by Ctrl+C through the terminal's signal. Steps
// that run in-process (HTTP health checks) read this token instead. It is the
// run's token during normal steps and CancellationToken.None during cleanup, so
// on-failure and finally steps are never cut short.
// =============================================================================

namespace Ando.Steps;

/// <summary>The cancellation token in-process steps should honor.</summary>
public static class StepCancellation
{
    /// <summary>Token for the step that is running now (None during cleanup).</summary>
    public static CancellationToken Current { get; internal set; }
}
