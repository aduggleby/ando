// =============================================================================
// GitIgnoreCheck.cs
//
// Summary: Asks git whether a file is ignored. Shared by the CLI's .env.ando
// check and by Secrets.EnvFile.
//
// Design Decisions:
// - Returns git's exit code so callers decide what "not a repository" means:
//   0 ignored, 1 not ignored, 128 not a repository (or other git error),
//   null when git could not be run at all
// =============================================================================

namespace Ando.Utilities;

internal static class GitIgnoreCheck
{
    internal static int? CheckIgnore(string workingDirectory, string path)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("git", ["check-ignore", "-q", "--", path])
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(info);
            if (process == null)
            {
                return null;
            }

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { /* already gone */ }
                return null;
            }
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }
}
