// =============================================================================
// ReleaseStep.cs
//
// Summary: Simple record representing a step in the release workflow.
//
// Each step has an identifier, display label, enabled state, and optional
// reason for being disabled. Steps are displayed in the interactive checklist
// and can be selected/deselected by the user.
// =============================================================================

namespace Ando.Release;

/// <summary>
/// Represents a step in the release workflow.
/// </summary>
/// <param name="Id">Unique identifier for the step (commit, docs, bump, push, publish).</param>
/// <param name="Label">Human-readable label shown in the checklist.</param>
/// <param name="Enabled">Whether the step can be selected (disabled steps are grayed out).</param>
/// <param name="DisabledReason">Reason shown when step is disabled (e.g., "no changes").</param>
public record ReleaseStep(
    string Id,
    string Label,
    bool Enabled,
    string? DisabledReason
);
