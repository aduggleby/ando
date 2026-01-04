// =============================================================================
// IToolAvailabilityChecker.cs
//
// Summary: Interface for checking tool availability and providing install help.
//
// IToolAvailabilityChecker enables the workflow runner to check for required
// tools without being coupled to specific tool implementations. When a step
// fails, checkers matching that step's name can provide helpful installation
// instructions.
//
// Design Decisions:
// - CanCheck uses step name prefix matching for simplicity
// - IsAvailable performs the actual tool check (e.g., running --version)
// - GetInstallInstructions returns platform-specific help text
// - Multiple checkers can be registered for extensibility
// =============================================================================

namespace Ando.Workflow;

/// <summary>
/// Interface for checking if a tool is available and providing installation help.
/// Implementations check for specific tools (Azure CLI, wrangler, func, etc.).
/// </summary>
public interface IToolAvailabilityChecker
{
    /// <summary>
    /// Determines if this checker applies to the given step name.
    /// Typically matches on step name prefix (e.g., "Azure." or "Bicep.").
    /// </summary>
    /// <param name="stepName">The name of the step that failed.</param>
    /// <returns>True if this checker should be consulted for this step.</returns>
    bool CanCheck(string stepName);

    /// <summary>
    /// Checks if the required tool is available in the current environment.
    /// </summary>
    /// <returns>True if the tool is available, false otherwise.</returns>
    bool IsAvailable();

    /// <summary>
    /// Gets platform-specific installation instructions for the tool.
    /// </summary>
    /// <returns>Human-readable installation instructions.</returns>
    string GetInstallInstructions();

    /// <summary>
    /// Gets a URL for more detailed installation documentation.
    /// </summary>
    string GetDocumentationUrl();
}
