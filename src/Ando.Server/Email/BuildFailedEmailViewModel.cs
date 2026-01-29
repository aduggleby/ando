// =============================================================================
// BuildFailedEmailViewModel.cs
//
// Summary: View model for the build failed email template.
// =============================================================================

namespace Ando.Server.Email;

/// <summary>
/// View model for build failed email notifications.
/// </summary>
public class BuildFailedEmailViewModel
{
    /// <summary>
    /// Name of the project that failed.
    /// </summary>
    public string ProjectName { get; set; } = "";

    /// <summary>
    /// Git branch that triggered the failed build.
    /// </summary>
    public string Branch { get; set; } = "";

    /// <summary>
    /// Git commit SHA of the failed build.
    /// </summary>
    public string CommitSha { get; set; } = "";

    /// <summary>
    /// Git commit message.
    /// </summary>
    public string CommitMessage { get; set; } = "";

    /// <summary>
    /// Author of the Git commit.
    /// </summary>
    public string CommitAuthor { get; set; } = "";

    /// <summary>
    /// Error message describing why the build failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// URL to the build details page.
    /// </summary>
    public string BuildUrl { get; set; } = "";

    /// <summary>
    /// When the build failed.
    /// </summary>
    public DateTime FailedAt { get; set; }
}
