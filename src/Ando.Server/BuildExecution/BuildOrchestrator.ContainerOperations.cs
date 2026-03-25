// =============================================================================
// BuildOrchestrator.ContainerOperations.cs
//
// Summary: Build container lifecycle, tool provisioning, and artifact collection
// helpers for build orchestration.
//
// Container-specific concerns are isolated here to keep orchestration flow and
// infrastructure commands easier to reason about and maintain.
// =============================================================================

using System.Diagnostics;
using Ando.Server.Data;
using Ando.Server.Models;

namespace Ando.Server.BuildExecution;

public partial class BuildOrchestrator
{
    private async Task<bool> EnsureAndoCliInstalledAsync(
        string containerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        // Use a fixed tool-path to avoid PATH/global tool issues.
        // Prefer update (fast when already installed), fall back to install.
        var shellCmd = "dotnet tool update --tool-path /tmp/ando-tools ando || dotnet tool install --tool-path /tmp/ando-tools ando";
        var exitCode = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", shellCmd],
            logger,
            cancellationToken);
        return exitCode == 0;
    }

    private async Task<bool> EnsureDockerCliInstalledAsync(
        string containerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        // Fast path: already installed.
        var checkExit = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", "command -v docker >/dev/null 2>&1"],
            logger,
            cancellationToken);
        if (checkExit == 0)
        {
            return true;
        }

        // Best-effort install across common base images.
        // - Alpine: `apk add docker-cli` (fallback to `docker`)
        // - Debian/Ubuntu: `apt-get install docker.io`
        var installCmd =
            "set -euo pipefail; " +
            "if command -v docker >/dev/null 2>&1; then exit 0; fi; " +
            "if command -v apk >/dev/null 2>&1; then apk add --no-cache docker-cli || apk add --no-cache docker; exit 0; fi; " +
            "if command -v apt-get >/dev/null 2>&1; then apt-get update && apt-get install -y --no-install-recommends docker.io && rm -rf /var/lib/apt/lists/*; exit 0; fi; " +
            "echo 'Unsupported base image: cannot install docker CLI automatically' >&2; exit 1;";

        var installExit = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", installCmd],
            logger,
            cancellationToken);
        if (installExit != 0)
        {
            return false;
        }

        // Verify.
        var versionExit = await RunDockerExecAsync(
            containerId,
            ["docker", "--version"],
            logger,
            cancellationToken);
        return versionExit == 0;
    }

    private async Task<bool> EnsureGitInstalledAsync(
        string containerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        var checkExit = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", "command -v git >/dev/null 2>&1"],
            logger,
            cancellationToken);
        if (checkExit == 0)
        {
            return true;
        }

        var installCmd =
            "set -euo pipefail; " +
            "if command -v git >/dev/null 2>&1; then exit 0; fi; " +
            "if command -v apk >/dev/null 2>&1; then apk add --no-cache git; exit 0; fi; " +
            "if command -v apt-get >/dev/null 2>&1; then apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*; exit 0; fi; " +
            "echo 'Unsupported base image: cannot install git automatically' >&2; exit 1;";

        var installExit = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", installCmd],
            logger,
            cancellationToken);
        if (installExit != 0)
        {
            return false;
        }

        var versionExit = await RunDockerExecAsync(
            containerId,
            ["git", "--version"],
            logger,
            cancellationToken);
        return versionExit == 0;
    }

    private async Task<bool> EnsureGitHubCliInstalledAsync(
        string containerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        var checkExit = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", "command -v gh >/dev/null 2>&1"],
            logger,
            cancellationToken);
        if (checkExit == 0)
        {
            return true;
        }

        // Best-effort install across common base images.
        // - Alpine: `apk add github-cli`
        // - Debian/Ubuntu: `apt-get install gh` (available in Ubuntu repos)
        var installCmd =
            "set -euo pipefail; " +
            "if command -v gh >/dev/null 2>&1; then exit 0; fi; " +
            "if command -v apk >/dev/null 2>&1; then apk add --no-cache github-cli; exit 0; fi; " +
            "if command -v apt-get >/dev/null 2>&1; then apt-get update && apt-get install -y --no-install-recommends gh && rm -rf /var/lib/apt/lists/*; exit 0; fi; " +
            "echo 'Unsupported base image: cannot install gh automatically' >&2; exit 1;";

        var installExit = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", installCmd],
            logger,
            cancellationToken);
        if (installExit != 0)
        {
            return false;
        }

        var versionExit = await RunDockerExecAsync(
            containerId,
            ["gh", "--version"],
            logger,
            cancellationToken);
        return versionExit == 0;
    }

    private async Task<bool> ConfigureGitCredentialsAsync(
        string containerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        // Configure:
        // 1) Non-interactive auth for https://github.com (for git push/tag pushes)
        // 2) A committer identity for annotated tags (git tag -a requires user.name/user.email)
        //
        // Note: the build container is ephemeral; it's safe to set global config here.
        var cmd =
            "set -euo pipefail; " +
            "if ! command -v git >/dev/null 2>&1; then exit 0; fi; " +
            // Ensure tagger identity exists; allow overrides via env vars.
            // Prefer standard git env vars, then custom ones, then a safe default.
            "name=\"${GIT_COMMITTER_NAME:-${GIT_AUTHOR_NAME:-${GIT_USER_NAME:-Ando Server}}}\"; " +
            "email=\"${GIT_COMMITTER_EMAIL:-${GIT_AUTHOR_EMAIL:-${GIT_USER_EMAIL:-ando-server@localhost}}}\"; " +
            "git config --global user.name >/dev/null 2>&1 || git config --global user.name \"$name\"; " +
            "git config --global user.email >/dev/null 2>&1 || git config --global user.email \"$email\"; " +
            // Configure token-based credentials if available.
            "if [ -z \"${GITHUB_TOKEN:-}\" ]; then exit 0; fi; " +
            "umask 077; " +
            "creds_file=\"${HOME:-/root}/.git-credentials\"; " +
            "printf \"https://x-access-token:%s@github.com\\n\" \"$GITHUB_TOKEN\" > \"$creds_file\"; " +
            // Ensure git uses this specific credentials file and matches by host (not full path),
            // otherwise pushes from non-interactive build containers will fail.
            "git config --global credential.helper \"store --file $creds_file\"; " +
            "git config --global credential.useHttpPath false; " +
            "exit 0;";

        var exit = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", cmd],
            logger,
            cancellationToken);
        return exit == 0;
    }

    private async Task<int> RunDockerExecAsync(
        string containerId,
        IReadOnlyList<string> argsAfterContainerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("exec");
        // Keep exec commands consistent with the repo root inside the build container.
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("/workspace");
        startInfo.ArgumentList.Add(containerId);
        foreach (var a in argsAfterContainerId)
        {
            startInfo.ArgumentList.Add(a);
        }

        // Log the command for debugging
        var cmdArgs = string.Join(" ", startInfo.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        _logger.LogInformation("RunDockerExec: docker {Args}", cmdArgs);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            logger.Output("Failed to start docker exec process");
            return 1;
        }

        var outputTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    logger.Output(line);
                }
            }
        }, cancellationToken);

        var errorTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    logger.Output(line);
                }
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);

        return process.ExitCode;
    }

    /// <summary>
    /// Collects build artifacts.
    /// </summary>
    private async Task CollectArtifactsAsync(
        AndoDbContext db,
        Build build,
        string containerId,
        CancellationToken cancellationToken)
    {
        var artifactDir = Path.Combine(_storageSettings.ArtifactsPath, build.ProjectId.ToString(), build.Id.ToString());
        Directory.CreateDirectory(artifactDir);

        // Copy artifacts from container
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("cp");
        startInfo.ArgumentList.Add($"{containerId}:/workspace/artifacts/.");
        startInfo.ArgumentList.Add(artifactDir);

        using var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        // Record artifacts in database
        if (Directory.Exists(artifactDir))
        {
            var retentionDays = _storageSettings.ArtifactRetentionDays;
            var expiresAt = DateTime.UtcNow.AddDays(retentionDays);

            foreach (var file in Directory.GetFiles(artifactDir, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                var relativePath = Path.GetRelativePath(artifactDir, file);

                db.BuildArtifacts.Add(new BuildArtifact
                {
                    BuildId = build.Id,
                    Name = Path.GetFileName(file),
                    StoragePath = $"{build.ProjectId}/{build.Id}/{relativePath}",
                    SizeBytes = info.Length,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Cleans up the build container.
    /// </summary>
    private async Task CleanupContainerAsync(string containerId)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("rm");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(containerId);

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup container {ContainerId}", containerId);
        }
    }
}
