// =============================================================================
// ToolAvailabilityCheckers.cs
//
// Summary: Implementations of IToolAvailabilityChecker for various tools.
//
// This file contains concrete checker implementations for common build tools:
// - AzureCliChecker: For Azure CLI (az) and Bicep operations
// - CloudflareChecker: For Cloudflare wrangler operations
// - FunctionsChecker: For Azure Functions Core Tools
//
// Design Decisions:
// - Each checker is a simple, focused class
// - Checkers delegate to existing static methods in operation classes
// - All checkers are collected in ToolAvailabilityRegistry for easy access
// - Step name prefixes are centralized in ToolRequirements for maintainability
// =============================================================================

using Ando.Operations;

namespace Ando.Workflow;

/// <summary>
/// Centralized constants for tool requirements.
/// Step name prefixes that require specific tools to be installed.
/// </summary>
public static class ToolRequirements
{
    /// <summary>
    /// Step prefixes that require Azure CLI.
    /// </summary>
    public static readonly string[] AzureCliPrefixes = ["Azure.", "Bicep."];

    /// <summary>
    /// Step prefixes that require Cloudflare wrangler.
    /// </summary>
    public static readonly string[] CloudflarePrefixes = ["Cloudflare."];

    /// <summary>
    /// Step prefixes that require Azure Functions Core Tools.
    /// </summary>
    public static readonly string[] FunctionsPrefixes = ["Functions."];

    /// <summary>
    /// Checks if a step name matches any of the given prefixes.
    /// </summary>
    public static bool MatchesPrefixes(string stepName, string[] prefixes) =>
        prefixes.Any(prefix => stepName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Checks availability of Azure CLI for Azure and Bicep operations.
/// </summary>
public class AzureCliChecker : IToolAvailabilityChecker
{
    public bool CanCheck(string stepName) =>
        ToolRequirements.MatchesPrefixes(stepName, ToolRequirements.AzureCliPrefixes);

    public bool IsAvailable() => AzureOperations.IsAzureCliAvailable();

    public string GetInstallInstructions() => AzureOperations.GetAzureCliInstallInstructions();

    public string GetDocumentationUrl() => "https://docs.microsoft.com/cli/azure/install-azure-cli";
}

/// <summary>
/// Checks availability of Cloudflare wrangler for Pages operations.
/// </summary>
public class CloudflareChecker : IToolAvailabilityChecker
{
    public bool CanCheck(string stepName) =>
        ToolRequirements.MatchesPrefixes(stepName, ToolRequirements.CloudflarePrefixes);

    public bool IsAvailable() => CloudflareOperations.IsWranglerAvailable();

    public string GetInstallInstructions() => CloudflareOperations.GetWranglerInstallInstructions();

    public string GetDocumentationUrl() => "https://developers.cloudflare.com/workers/wrangler/install-and-update/";
}

/// <summary>
/// Checks availability of Azure Functions Core Tools.
/// </summary>
public class FunctionsChecker : IToolAvailabilityChecker
{
    public bool CanCheck(string stepName) =>
        ToolRequirements.MatchesPrefixes(stepName, ToolRequirements.FunctionsPrefixes);

    public bool IsAvailable() => FunctionsOperations.IsFuncCliAvailable();

    public string GetInstallInstructions() => FunctionsOperations.GetFuncCliInstallInstructions();

    public string GetDocumentationUrl() => "https://docs.microsoft.com/azure/azure-functions/functions-run-local";
}

/// <summary>
/// Registry of all tool availability checkers.
/// Provides a convenient way to get all registered checkers.
/// </summary>
public static class ToolAvailabilityRegistry
{
    private static readonly List<IToolAvailabilityChecker> _checkers =
    [
        new AzureCliChecker(),
        new CloudflareChecker(),
        new FunctionsChecker()
    ];

    /// <summary>
    /// Gets all registered tool availability checkers.
    /// </summary>
    public static IReadOnlyList<IToolAvailabilityChecker> Checkers => _checkers;

    /// <summary>
    /// Registers a custom tool availability checker.
    /// </summary>
    public static void Register(IToolAvailabilityChecker checker)
    {
        _checkers.Add(checker);
    }

    /// <summary>
    /// Finds the checker for a given step name, or null if none applies.
    /// </summary>
    public static IToolAvailabilityChecker? FindChecker(string stepName) =>
        _checkers.FirstOrDefault(c => c.CanCheck(stepName));
}
