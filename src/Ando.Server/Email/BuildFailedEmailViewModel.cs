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
    public string ProjectName { get; set; } = "";
    public string Branch { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public string CommitMessage { get; set; } = "";
    public string CommitAuthor { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string BuildUrl { get; set; } = "";
    public DateTime FailedAt { get; set; }
}
