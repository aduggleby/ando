// =============================================================================
// CloudflarePagesDeployOptions.cs
//
// Summary: Fluent configuration options for Cloudflare Pages deployments.
//
// CloudflarePagesDeployOptions provides a fluent builder pattern for configuring
// Cloudflare Pages deployments via wrangler CLI. Options include project name,
// branch, and commit metadata for deployment tracking.
//
// Design Decisions:
// - Fluent builder pattern (methods return `this`) for readable configuration
// - Branch determines preview vs production deployment routing
// - Commit hash/message are optional - wrangler uses git info if available
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Fluent options for configuring Cloudflare Pages deployments.
/// </summary>
public class CloudflarePagesDeployOptions
{
    /// <summary>Cloudflare Pages project name.</summary>
    internal string? ProjectName { get; private set; }

    /// <summary>Git branch name for the deployment.</summary>
    internal string? Branch { get; private set; }

    /// <summary>Git commit hash for the deployment.</summary>
    internal string? CommitHash { get; private set; }

    /// <summary>Git commit message for the deployment.</summary>
    internal string? CommitMessage { get; private set; }

    /// <summary>
    /// Sets the Cloudflare Pages project name.
    /// Required unless CLOUDFLARE_PROJECT_NAME env var is set.
    /// </summary>
    public CloudflarePagesDeployOptions WithProjectName(string name)
    {
        ProjectName = name;
        return this;
    }

    /// <summary>
    /// Sets the git branch name for the deployment.
    /// Used by Cloudflare to determine preview vs production URLs.
    /// </summary>
    public CloudflarePagesDeployOptions WithBranch(string branch)
    {
        Branch = branch;
        return this;
    }

    /// <summary>
    /// Sets the git commit hash for deployment tracking.
    /// </summary>
    public CloudflarePagesDeployOptions WithCommitHash(string hash)
    {
        CommitHash = hash;
        return this;
    }

    /// <summary>
    /// Sets the git commit message for deployment tracking.
    /// </summary>
    public CloudflarePagesDeployOptions WithCommitMessage(string message)
    {
        CommitMessage = message;
        return this;
    }
}
