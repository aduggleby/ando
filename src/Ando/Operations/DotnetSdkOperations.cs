// =============================================================================
// DotnetSdkOperations.cs
//
// Summary: Backward compatibility wrapper for .NET SDK installation.
//
// This class provides the legacy DotnetSdk.Install() API that delegates to
// Dotnet.SdkInstall(). This ensures older build.csando scripts continue to work.
//
// Design Decisions:
// - Simple delegation to DotnetOperations.SdkInstall()
// - Maintains backward compatibility without code duplication
// - Marked as deprecated in favor of Dotnet.SdkInstall()
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Legacy wrapper for .NET SDK installation operations.
/// Use Dotnet.SdkInstall() instead for new scripts.
/// </summary>
public class DotnetSdkOperations
{
    private readonly DotnetOperations _dotnet;

    public DotnetSdkOperations(DotnetOperations dotnet)
    {
        _dotnet = dotnet;
    }

    /// <summary>
    /// Installs the .NET SDK. Delegates to Dotnet.SdkInstall().
    /// </summary>
    /// <param name="version">The SDK version to install (default: 9.0).</param>
    [Obsolete("Use Dotnet.SdkInstall() instead.")]
    public void Install(string version = "9.0")
    {
        _dotnet.SdkInstall(version);
    }
}
