// =============================================================================
// ContainerExecutor.cs
//
// Summary: Executes commands inside a Docker container via 'docker exec'.
//
// ContainerExecutor implements ICommandExecutor to run commands inside an
// existing Docker container. It translates host paths to container paths and
// provides the same real-time output streaming as ProcessRunner.
//
// Architecture:
// - Wraps commands in 'docker exec' calls to the target container
// - Handles path translation between host and container (/workspace mount)
// - Preserves environment variables and working directory settings
// - Output streaming works the same as local execution for consistency
//
// Design Decisions:
// - Implements ICommandExecutor so build logic is execution-agnostic
// - Container must be pre-created by DockerManager (separation of concerns)
// - Uses -w flag for working directory rather than 'cd' commands
// - Uses 'which' command to check tool availability in container
// =============================================================================

using System.Diagnostics;
using Ando.Logging;
using Ando.Utilities;

namespace Ando.Execution;

/// <summary>
/// Executes commands inside a Docker container via 'docker exec'.
/// Provides real-time output streaming and working directory management.
/// Implements ICommandExecutor for seamless switching between local and container execution.
/// </summary>
public class ContainerExecutor : CommandExecutorBase
{
    private readonly string _containerId;
    private readonly string _containerWorkDir;
    private readonly string _containerRootPath;
    private string? _hostRootPath;
    private bool _azureCliEnsured;

    /// <summary>
    /// Environment variables passed to every command run in the container, for
    /// example the run's private AZURE_CONFIG_DIR. Per-command values win.
    /// </summary>
    public Dictionary<string, string> DefaultEnvironment { get; } = new();

    // Installs the Azure CLI in Debian/Ubuntu images that don't have it.
    internal const string AzureCliInstallScript =
        "set -eu; " +
        "command -v az >/dev/null 2>&1 && exit 0; " +
        "if ! command -v apt-get >/dev/null 2>&1; then " +
        "echo 'The Azure CLI is not in the build image, and ANDO can only install it on Debian or Ubuntu images. Use an image that includes az.' >&2; exit 1; fi; " +
        "export DEBIAN_FRONTEND=noninteractive; " +
        "command -v curl >/dev/null 2>&1 || (apt-get update -qq && apt-get install -y -qq curl ca-certificates); " +
        "curl -fsSL https://aka.ms/InstallAzureCLIDeb -o /tmp/ando-az-install.sh; " +
        "bash /tmp/ando-az-install.sh; rm -f /tmp/ando-az-install.sh; " +
        "command -v az >/dev/null 2>&1 || { echo 'The Azure CLI was not installed.' >&2; exit 1; }";

    /// <summary>
    /// Creates a new ContainerExecutor.
    /// </summary>
    /// <param name="containerId">Docker container ID or name (container must be running)</param>
    /// <param name="logger">Logger for output streaming</param>
    /// <param name="containerWorkDir">Working directory inside the container (default: /workspace)</param>
    public ContainerExecutor(string containerId, IBuildLogger logger, string containerWorkDir = "/workspace")
        : base(logger)
    {
        _containerId = containerId;
        _containerWorkDir = containerWorkDir;
        _containerRootPath = containerWorkDir; // Container root is the workspace mount point
    }

    /// <summary>
    /// Sets the host root path for path translation.
    /// Call this after construction when the host project path is known.
    /// </summary>
    /// <param name="hostRootPath">Absolute path to project root on the host.</param>
    public void SetHostRootPath(string hostRootPath)
    {
        _hostRootPath = Path.GetFullPath(hostRootPath);
    }

    private readonly List<string> _hostRoots = [];

    /// <summary>
    /// Registers another host directory whose absolute paths map to the container workspace
    /// (e.g. the --ref worktree or the directory ando was run from). See <see cref="TranslateArgs"/>.
    /// </summary>
    public void AddHostRoot(string hostRoot)
    {
        var full = Path.GetFullPath(hostRoot).TrimEnd('/', '\\');
        if (full.Length > 0 && !_hostRoots.Contains(full))
        {
            _hostRoots.Add(full);
        }
    }

    /// <summary>
    /// Makes every path argument a container path. Build scripts create absolute HOST paths (for example
    /// Dotnet.Publish Output("out/web") becomes a BuildPath, which is resolved against the host directory
    /// at script load), but commands run inside the container where the project is at /workspace.
    /// An argument that is an absolute path under a known host root (the project root, the --ref
    /// worktree, the invocation directory), alone or after a `name=` prefix such as `-p:Prop=`, is
    /// rewritten to /workspace/&lt;relative&gt;. Relative paths and container-absolute paths pass through
    /// unchanged (the working directory is /workspace).
    /// </summary>
    internal string[] TranslateArgs(string[] args)
    {
        var roots = _hostRoots.Select(r => r).ToList();
        if (_hostRootPath != null)
        {
            roots.Add(_hostRootPath.TrimEnd('/', '\\'));
        }
        if (roots.Count == 0)
        {
            return args;
        }
        roots = [.. roots.Where(r => r.Length > 0).Distinct().OrderByDescending(r => r.Length)];

        string Translate(string value)
        {
            if (value.Length == 0 || !Path.IsPathRooted(value) || value.StartsWith(_containerRootPath, StringComparison.Ordinal))
            {
                return value;
            }
            foreach (var root in roots)
            {
                if (value == root)
                {
                    return _containerRootPath;
                }
                if (value.StartsWith(root + "/", StringComparison.Ordinal))
                {
                    return _containerRootPath + "/" + value[(root.Length + 1)..];
                }
            }
            return value;
        }

        return [.. args.Select(arg =>
        {
            var whole = Translate(arg);
            if (whole != arg)
            {
                return whole;
            }
            var eq = arg.IndexOf('=');
            return eq > 0 && !arg.StartsWith('/') ? arg[..(eq + 1)] + Translate(arg[(eq + 1)..]) : arg;
        })];
    }

    /// <summary>
    /// Installs the Azure CLI in the container before the first az command when
    /// the image doesn't include it.
    /// </summary>
    protected override async Task<CommandResult?> PrepareCommandAsync(string command, string[] args, CommandOptions options)
    {
        // Also covers `sh -c "az ..."`, used to keep credentials out of the argument list.
        var runsAz = command == "az"
            || (command == "sh" && args is ["-c", var script, ..] && script.StartsWith("az ", StringComparison.Ordinal));
        if (!runsAz || _azureCliEnsured)
        {
            return null;
        }

        var check = await ExecuteAsync("sh", ["-c", "command -v az"],
            new CommandOptions { SuppressOutput = true, TimeoutMs = 30_000 });
        if (!check.Success)
        {
            Logger.Info("Azure CLI not found in the build container. Installing it...");
            var install = await ExecuteAsync("sh", ["-c", AzureCliInstallScript],
                new CommandOptions { TimeoutMs = 15 * 60 * 1000 });
            if (!install.Success)
            {
                return CommandResult.Failed(install.ExitCode,
                    "Could not install the Azure CLI in the build container. " + (install.Error ?? ""));
            }
        }

        _azureCliEnsured = true;
        return null;
    }

    /// <summary>
    /// Killing `docker exec` leaves the command running in the container. Steps
    /// run one at a time, so stop every process except the container's PID 1
    /// before the build continues (e.g. into rollback steps).
    /// </summary>
    protected override async Task OnCommandTimedOutAsync()
    {
        Logger.Warning("Stopping the timed-out command inside the build container...");
        var runner = new ProcessRunner(Logger);
        await runner.ExecuteAsync("docker",
            ["exec", _containerId, "sh", "-c", StopAllCommandsScript],
            new CommandOptions { SuppressOutput = true, TimeoutMs = 60_000 });
    }

    /// <summary>Stops every process in the container except PID 1 (and itself).</summary>
    internal const string StopAllCommandsScript =
        "kill -TERM -1 2>/dev/null; sleep 2; kill -KILL -1 2>/dev/null; true";

    protected override ProcessStartInfo PrepareProcessStartInfo(string command, string[] args, CommandOptions options)
    {
        // Build the 'docker exec' command with appropriate flags.
        var dockerArgs = new List<string> { "exec" };

        // Set working directory using -w flag.
        // Container paths use /workspace as the project root.
        var workDir = options.WorkingDirectory ?? _containerWorkDir;
        dockerArgs.AddRange(["-w", ConvertToContainerPath(workDir)]);

        // Pass environment variables by name only (-e KEY) and give the values to the
        // docker client through its own environment, so secrets never appear in the
        // host process command line. Precedence: per-command > DefaultEnvironment > tracked.
        var env = new Dictionary<string, string>();
        foreach (var (key, value) in LoadedEnvironmentVariables.GetTrackedVariables())
        {
            env[key] = value;
        }
        foreach (var (key, value) in DefaultEnvironment)
        {
            env[key] = value;
        }
        foreach (var (key, value) in options.Environment)
        {
            env[key] = value;
        }
        foreach (var key in env.Keys)
        {
            dockerArgs.AddRange(["-e", key]);
        }

        // Append container ID and the actual command to execute.
        dockerArgs.Add(_containerId);
        dockerArgs.Add(command);
        dockerArgs.AddRange(TranslateArgs(args));

        var startInfo = new ProcessStartInfo { FileName = "docker" };

        foreach (var arg in dockerArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in env)
        {
            startInfo.Environment[key] = value;
        }

        return startInfo;
    }

    /// <summary>
    /// Checks if a command is available inside the container.
    /// Uses 'which' command to locate the binary.
    /// </summary>
    public override bool IsAvailable(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "exec", _containerId, "which", command },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // Converts a host path to a container path.
    // The project is mounted at /workspace in the container, so paths
    // need to be translated accordingly.
    private string ConvertToContainerPath(string path)
    {
        // Already a container path - no conversion needed.
        if (path.StartsWith(_containerRootPath))
        {
            return path;
        }

        // Relative paths are relative to the container working directory.
        if (!Path.IsPathRooted(path))
        {
            return $"{_containerWorkDir}/{path}";
        }

        // For host absolute paths, translate to container path if possible.
        if (_hostRootPath != null)
        {
            var fullPath = Path.GetFullPath(path);

            // Check if the path is within the host project root.
            // Use Ordinal comparison because Linux paths are case-sensitive.
            if (fullPath.StartsWith(_hostRootPath, StringComparison.Ordinal))
            {
                var relativePath = Path.GetRelativePath(_hostRootPath, fullPath);
                // Convert Windows separators to Unix if needed
                relativePath = relativePath.Replace('\\', '/');
                return $"{_containerRootPath}/{relativePath}";
            }

            // Path is outside the project root - this will fail in the container
            Logger.Warning($"Path '{path}' is outside the project root and may not be accessible in the container.");
        }

        // Pass through unchanged as fallback (may fail if path doesn't exist in container)
        return path;
    }
}
