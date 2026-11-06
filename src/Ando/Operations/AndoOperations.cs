// =============================================================================
// AndoOperations.cs
//
// Summary: Core ANDO operations for build configuration and nested builds.
//
// AndoOperations provides:
// - UseImage: Set the Docker image for the current build
// - CopyArtifactsToHost: Copy files from container to host after build
// - Build: Run child build scripts in subdirectories
//
// Architecture:
// - UseImage sets the image before container creation (not a step)
// - CopyArtifactsToHost registers artifacts for post-build copying
// - Child builds run via `ando run` spawned on the HOST machine
// - Each child gets a fresh container (not shared with parent)
//
// Design Decisions:
// - UseImage is immediate (not a registered step) because it affects container creation
// - CopyArtifactsToHost delegates to ArtifactOperations for actual copying
// - Path translation needed since script sees container paths
// - Child build appears as single grouped step in parent workflow
// =============================================================================

using Ando.Logging;
using Ando.Profiles;
using Ando.References;
using Ando.Steps;
using Ando.Utilities;
using Ando.Workflow;

namespace Ando.Operations;

/// <summary>
/// Core ANDO operations for build configuration, artifacts, and nested builds.
/// </summary>
public class AndoOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<string, string> _containerToHostPath;
    private readonly BuildOptions _buildOptions;
    private readonly ArtifactOperations _artifactOperations;
    private readonly ProfileRegistry _profileRegistry;
    private readonly Func<Execution.ICommandExecutor> _executorFactory;

    /// <summary>
    /// Creates a new AndoOperations instance.
    /// </summary>
    /// <param name="registry">Step registry for registering build steps.</param>
    /// <param name="logger">Logger for output.</param>
    /// <param name="containerToHostPath">Function to translate container paths to host paths.</param>
    /// <param name="buildOptions">Build options for configuring the current build.</param>
    /// <param name="artifactOperations">Artifact operations for copying files to host.</param>
    /// <param name="profileRegistry">Profile registry for passing profiles to sub-builds.</param>
    public AndoOperations(
        StepRegistry registry,
        IBuildLogger logger,
        Func<string, string> containerToHostPath,
        BuildOptions buildOptions,
        ArtifactOperations artifactOperations,
        ProfileRegistry profileRegistry,
        Func<Execution.ICommandExecutor>? executorFactory = null)
    {
        _executorFactory = executorFactory ?? (() => new Execution.ProcessRunner(logger));
        _registry = registry;
        _logger = logger;
        _containerToHostPath = containerToHostPath;
        _buildOptions = buildOptions;
        _artifactOperations = artifactOperations;
        _profileRegistry = profileRegistry;
    }

    /// <summary>
    /// Sets the Docker image for the current build container.
    /// Must be called before any build steps execute.
    /// </summary>
    /// <param name="image">Docker image name (e.g., "ubuntu:24.04", "mcr.microsoft.com/dotnet/sdk:9.0").</param>
    public void UseImage(string image)
    {
        _buildOptions.UseImage(image);
        _logger.Debug($"Set build image: {image}");
    }

    /// <summary>
    /// Registers files or directories to be copied from the container to the host
    /// after the build completes successfully.
    /// </summary>
    /// <param name="containerPath">
    /// Path inside the container. Can be relative to /workspace (e.g., "dist", "./artifacts")
    /// or absolute (e.g., "/workspace/dist").
    /// </param>
    /// <param name="hostPath">
    /// Destination path on the host machine. Can be relative to project root (e.g., "./dist")
    /// or absolute.
    /// </param>
    public void CopyArtifactsToHost(string containerPath, string hostPath)
    {
        _artifactOperations.CopyToHost(containerPath, hostPath);
    }

    /// <summary>
    /// Registers files or directories to be archived and copied from the container
    /// to the host after the build completes successfully. Creates a single archive file
    /// for faster transfer of many small files.
    /// </summary>
    /// <param name="containerPath">
    /// Path inside the container. Can be relative to /workspace (e.g., "dist", "./artifacts")
    /// or absolute (e.g., "/workspace/dist").
    /// </param>
    /// <param name="hostPath">
    /// Destination on the host machine. Can be:
    /// - A directory path (e.g., "./dist") - creates "artifacts.tar.gz" in that directory
    /// - A .tar.gz file path (e.g., "./dist/output.tar.gz") - creates tar.gz archive
    /// - A .zip file path (e.g., "./dist/output.zip") - creates zip archive
    /// </param>
    public void CopyZippedArtifactsToHost(string containerPath, string hostPath)
    {
        _artifactOperations.CopyZippedToHost(containerPath, hostPath);
    }

    /// <summary>
    /// Runs a build script in the specified directory or file.
    /// The child build runs in a new isolated container with its own .env and context.
    /// </summary>
    /// <param name="directory">
    /// Directory containing the build.csando file, or a path to a specific .csando file.
    /// Examples:
    /// - Directory("./website") - runs build.csando in ./website
    /// - Directory("./website") / "deploy.csando" - runs deploy.csando in ./website
    /// </param>
    /// <param name="configure">Optional configuration for the child build.</param>
    public void Build(DirectoryRef directory, Action<AndoBuildOptions>? configure = null)
    {
        var dirPath = directory.Path;
        var isFile = dirPath.EndsWith(".csando", StringComparison.OrdinalIgnoreCase);

        // For display name, use the file name or directory name.
        var displayName = isFile
            ? Path.GetFileName(dirPath)
            : directory.Name;

        _registry.Register($"Ando.Build", async () =>
        {
            // Apply options if provided.
            var options = new AndoBuildOptions();
            configure?.Invoke(options);

            // Translate container path to host path for spawning the child process.
            var hostPath = _containerToHostPath(dirPath);
            _logger.Info($"Child build target (script path): {dirPath}");
            _logger.Info($"Child build translated path: {hostPath}");

            // Determine working directory and build file argument.
            string workingDirectory;
            string? buildFileArg = null;

            if (isFile)
            {
                // Path is a file - run in parent directory with file as argument.
                workingDirectory = Path.GetDirectoryName(hostPath) ?? hostPath;
                buildFileArg = Path.GetFileName(hostPath);
                _logger.Debug($"Running child build: {buildFileArg} in {workingDirectory}");
            }
            else
            {
                // Path is a directory - run in that directory.
                workingDirectory = hostPath;
                _logger.Debug($"Running child build in: {workingDirectory}");
            }

            // Build CLI arguments from options.
            var argsList = options.BuildArgs().ToList();

            // If a specific build file was specified, add it to the arguments.
            if (buildFileArg != null)
            {
                argsList.InsertRange(0, new[] { "-f", buildFileArg });
            }

            // Pass active profiles to child build, but only if not explicitly overridden.
            // If WithProfile() was called, use that instead of inheriting from parent.
            if (string.IsNullOrEmpty(options.Profile))
            {
                var activeProfiles = _profileRegistry.ActiveProfiles;
                if (activeProfiles.Count > 0)
                {
                    argsList.Add("-p");
                    argsList.Add(string.Join(",", activeProfiles));
                }
            }

            var args = argsList.ToArray();

            // Run ando on the host machine - it will create its own container.
            // Using ProcessRunner directly since we need host execution.
            // Interactive mode allows child builds to prompt for user input.
            var runner = new Execution.ProcessRunner(_logger);

            // Calculate the indent level for the child build.
            // Read current level from env var (default 0), increment by 1.
            var currentIndentStr = Environment.GetEnvironmentVariable(Logging.ConsoleLogger.IndentLevelEnvVar);
            var currentIndent = int.TryParse(currentIndentStr, out var level) ? level : 0;
            var childIndent = currentIndent + 1;

            var commandOptions = new Execution.CommandOptions
            {
                WorkingDirectory = workingDirectory,
                Interactive = true,
                TimeoutMs = Execution.CommandOptions.NoTimeout
            };

            if (!Directory.Exists(workingDirectory))
            {
                _logger.Error($"Child build working directory does not exist: {workingDirectory}");
                _logger.Error("This usually indicates an ANDO_HOST_ROOT/container path mapping issue.");
                _logger.Error($"ANDO_HOST_ROOT={Environment.GetEnvironmentVariable("ANDO_HOST_ROOT") ?? "<not set>"}");
            }

            // Set indent level for child build output formatting.
            commandOptions.Environment[Logging.ConsoleLogger.IndentLevelEnvVar] = childIndent.ToString();

            // Pass DIND setting to child builds so they inherit the parent's DIND mode.
            // This ensures child builds don't prompt for DIND when parent already enabled it.
            var parentDind = Environment.GetEnvironmentVariable(DindChecker.DindEnvVar);
            if (!string.IsNullOrEmpty(parentDind))
            {
                commandOptions.Environment[DindChecker.DindEnvVar] = parentDind;
            }

            _logger.Info($"Spawning child build: ando {string.Join(" ", args)}");

            try
            {
                var result = await runner.ExecuteAsync("ando", args, commandOptions);
                if (!result.Success)
                {
                    _logger.Error($"Child build failed (exit code {result.ExitCode}).");
                }
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.Error($"Child build threw exception: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger.Error($"Inner: {ex.InnerException.Message}");
                }
                return false;
            }
        }, displayName);
    }

    // =========================================================================
    // Step model: blocks, finally, deferred steps, gates and reports.
    // =========================================================================

    /// <summary>
    /// Groups steps into a block that can have on-failure and finally steps.
    /// The body runs now (while the script is evaluated) and its steps run in
    /// order with the rest of the build.
    /// Usage:
    ///   Ando.Block("eu", () => { AppService.SwapSlots(app, "release", rg); ... })
    ///       .OnFailure(() => AppService.SwapSlots(app, "release", rg))
    ///       .Finally(() => AppService.Stop(app, rg, "release"));
    /// </summary>
    /// <param name="name">Block name shown in logs.</param>
    /// <param name="body">Registers the block's steps.</param>
    public StepBlock Block(string name, Action body)
    {
        var block = new StepBlock(_registry, name, _registry.Capture(body));
        _registry.Register(new BuildStep("Ando.Block", () => Task.FromResult(false), name)
        {
            ExecuteWithRunner = async runner =>
            {
                _logger.Info($"Block: {name}");
                var success = await runner.RunScopeAsync(block.Body, block.OnFailureScopes);
                if (!success)
                {
                    _logger.Error($"Block failed: {name}");
                }
                return success;
            },
            ChildScopes = block.ChildScopes
        });
        return block;
    }

    /// <summary>
    /// Registers steps that always run at the end of the enclosing block, or at
    /// the end of the run when called at top level: after success, after a
    /// failure and after Ctrl+C. Finally steps run last-registered first, and
    /// every one runs even if an earlier one failed.
    /// Usage: Ando.Finally(() => AppService.Stop(app, rg, "release"));
    /// </summary>
    public void Finally(Action body)
    {
        _registry.RegisterFinally(CreateFinallyStep(_registry, body));
    }

    internal static BuildStep CreateFinallyStep(StepRegistry registry, Action body)
    {
        var scope = registry.Capture(body);
        return new BuildStep("Ando.Finally", () => Task.FromResult(false))
        {
            ExecuteWithRunner = runner => runner.RunCleanupScopeAsync(scope, continueOnFailure: true),
            ChildScopes = [scope]
        };
    }

    /// <summary>
    /// Runs <paramref name="body"/> at execution time, when the steps before it
    /// have run, and then runs the steps it registers. Use it to pass step
    /// outputs to later steps or to use plain C# if/foreach on runtime values.
    /// Finally steps registered inside the body (Ando.Finally,
    /// AppService.TemporaryAccessRule) run when the enclosing block or run ends.
    /// Usage: Ando.Defer(() => Http.WaitForHealthy($"https://{host.Value}/healthz"));
    /// </summary>
    public void Defer(Action body)
    {
        _registry.Register(new BuildStep("Ando.Defer", () => Task.FromResult(false))
        {
            ExecuteWithRunner = runner => runner.RunDeferredAsync(_registry.Capture(body))
        });
    }

    /// <summary>
    /// Runs the steps registered by <paramref name="then"/> only when
    /// <paramref name="condition"/> is true at execution time, otherwise the
    /// steps of <paramref name="otherwise"/> (if given). The condition can read
    /// step outputs and variables set by earlier deferred steps.
    /// Usage: Ando.When(() => show.Json("siteConfig.ipSecurityRestrictionsDefaultAction") == "Deny",
    ///            () => AppService.TemporaryAccessRule(app, rg, "release", "deploy-gate"));
    /// </summary>
    public void When(Func<bool> condition, Action then, Action? otherwise = null)
    {
        Defer(() =>
        {
            if (condition())
            {
                then();
            }
            else
            {
                otherwise?.Invoke();
            }
        });
    }

    /// <summary>
    /// Fails the build with <paramref name="message"/> when
    /// <paramref name="condition"/> is false at execution time.
    /// Usage: Ando.Require(() => pending.Value == "", "Database has pending migrations");
    /// </summary>
    public void Require(Func<bool> condition, string message)
    {
        _registry.Register("Ando.Require", () =>
        {
            if (condition())
            {
                return Task.FromResult(true);
            }

            _logger.Error(message);
            return Task.FromResult(false);
        }, message);
    }

    /// <summary>
    /// Asks the user to confirm before the build continues. Declining fails the
    /// build. `ando run --yes` accepts every confirmation; without --yes a run
    /// with no interactive console fails at this step.
    /// Usage: Ando.Confirm("Swap EU into production?");
    /// </summary>
    public void Confirm(string message)
    {
        _registry.Register("Ando.Confirm", () =>
        {
            if (_buildOptions.Session.AssumeYes)
            {
                _logger.Info($"{message} yes (--yes)");
                return Task.FromResult(true);
            }

            if (!(_buildOptions.Session.InteractiveConsole ?? !Console.IsInputRedirected))
            {
                _logger.Error($"{message} Cannot ask: no interactive console. Run with --yes to accept.");
                return Task.FromResult(false);
            }

            Console.Write($"{message} [y/N] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            var accepted = answer is "y" or "yes";
            if (!accepted)
            {
                _logger.Error("Not confirmed.");
            }
            return Task.FromResult(accepted);
        }, message);
    }

    /// <summary>
    /// Adds a value to this run's record (.ando/runs/*.json) when the step runs.
    /// Secret values are redacted.
    /// Usage: Ando.Report("eu.healthz", health); // a StepOutput
    /// </summary>
    public void Report(string key, string value) => Report(key, () => value);

    /// <summary>Adds a step output's value to this run's record.</summary>
    public void Report(string key, StepOutput output) => Report(key, () => output.Value);

    /// <summary>Adds a value computed at execution time to this run's record.</summary>
    public void Report(string key, Func<string> value)
    {
        _registry.Register("Ando.Report", () =>
        {
            var resolved = value();
            _buildOptions.Session.Reports.Add(new KeyValuePair<string, string>(key, resolved));
            _logger.Info($"{key}: {resolved}");
            return Task.FromResult(true);
        }, key);
    }

    /// <summary>
    /// Sets the timeout for command steps registered after this call (the
    /// built-in default is 5 minutes). Operations with their own longer default,
    /// such as AppService zip deploy, keep it. Use a step's WithTimeout to set
    /// one step only.
    /// Usage: Ando.CommandTimeout(TimeSpan.FromMinutes(15));
    /// </summary>
    public void CommandTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }
        _registry.DefaultCommandTimeoutMs = (int)timeout.TotalMilliseconds;
    }

    /// <summary>
    /// Copies a host file or directory into the build container. Relative host
    /// paths are relative to the directory ANDO was run from (also with --ref, so
    /// gitignored files such as local certificates can be used). Relative
    /// container paths are relative to /workspace. A directory replaces any
    /// existing directory at the container path.
    /// Usage: Ando.CopyToContainer(".run/seal-1.pem", ".run/seal-1.pem");
    /// </summary>
    public void CopyToContainer(string hostPath, string containerPath)
    {
        _registry.Register("Ando.CopyToContainer",
            () => CopyToContainerAsync(_buildOptions.Session, hostPath, containerPath, _logger),
            $"{hostPath} -> {containerPath}");
    }

    internal static async Task<bool> CopyToContainerAsync(
        RunSession session, string hostPath, string containerPath, IBuildLogger logger)
    {
        var source = session.ResolveHostPath(hostPath);
        var target = Ando.Context.ContainerPaths.Resolve(containerPath);

        if (session.ContainerId == null)
        {
            logger.Error("No build container: CopyToContainer only works during `ando run`.");
            return false;
        }

        var isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
        {
            logger.Error($"Host path not found: {source}");
            return false;
        }

        var docker = new Execution.ProcessRunner(logger);
        var quiet = new Execution.CommandOptions { SuppressOutput = true };
        var parent = isDirectory ? target : Path.GetDirectoryName(target)!.Replace('\\', '/');
        // Paths are passed as positional arguments, never spliced into the script.
        var prepare = isDirectory ? "rm -rf \"$1\" && mkdir -p \"$1\"" : "mkdir -p \"$1\"";

        var prepared = await docker.ExecuteAsync("docker",
            ["exec", session.ContainerId, "sh", "-c", prepare, "sh", parent], quiet);
        if (!prepared.Success)
        {
            logger.Error($"Could not prepare {target} in the container: {prepared.Error ?? prepared.Output}");
            return false;
        }

        var copySource = isDirectory ? Path.Combine(source, ".") : source;
        var copied = await docker.ExecuteAsync("docker",
            ["cp", copySource, $"{session.ContainerId}:{target}"], quiet);
        if (!copied.Success)
        {
            logger.Error($"docker cp failed: {copied.Error ?? copied.Output}");
            return false;
        }

        logger.Info($"    Copied {source} -> {target}");
        return true;
    }

    // =========================================================================
    // Build once, deploy the same bytes.
    // =========================================================================

    /// <summary>
    /// The host directory ANDO was run from. Use it to read local (for example
    /// gitignored) files while the script is evaluated, also with --ref.
    /// </summary>
    public string InvocationRoot => _buildOptions.Session.InvocationRoot ?? Environment.CurrentDirectory;

    /// <summary>
    /// Registers a step that writes manifest.json into <paramref name="directory"/>
    /// (in the build container) with the version, commit and SHA-256 and size of
    /// every file in it. Copy the directory to the host afterwards with
    /// Ando.CopyArtifactsToHost, and deploy it in a later run with Ando.ImportArtifacts.
    /// Usage: Ando.WriteManifest(Root / "artifacts", version, Git.Commit);
    /// </summary>
    public void WriteManifest(string directory, string version, string commit)
    {
        var dir = Ando.Context.ContainerPaths.Resolve(directory);

        _registry.Register("Ando.WriteManifest", async () =>
        {
            var executor = _executorFactory();
            var quiet = new Execution.CommandOptions { SuppressOutput = true, TimeoutMs = 30 * 60 * 1000 };
            const string list = "cd \"$1\" && find . -type f ! -path ./manifest.json -print0 | sort -z";

            var hashes = await executor.ExecuteAsync("sh", ["-c", $"{list} | xargs -0 -r sha256sum", "sh", dir], quiet);
            var sizes = await executor.ExecuteAsync("sh", ["-c", $"{list} | xargs -0 -r stat -c '%s %n'", "sh", dir], quiet);
            if (!hashes.Success || !sizes.Success)
            {
                _logger.Error($"    Could not hash the files in {dir}: {hashes.Error ?? sizes.Error}");
                return false;
            }

            var manifest = new ArtifactManifest
            {
                Version = version,
                Commit = commit,
                CreatedAt = DateTimeOffset.UtcNow,
                Files = ArtifactManifest.ParseListings(hashes.Output ?? "", sizes.Output ?? "")
            };

            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(manifest.ToJson()));
            var write = await executor.ExecuteAsync("sh",
                ["-c", "printf %s \"$1\" | base64 -d > \"$2/manifest.json\"", "sh", base64, dir], quiet);
            if (!write.Success)
            {
                _logger.Error($"    Could not write {dir}/{ArtifactManifest.FileName}: {write.Error}");
                return false;
            }

            _logger.Info($"    {manifest.Files.Count} file(s), version {version}, commit {commit}");
            return true;
        }, dir);
    }

    /// <summary>
    /// Imports a build's artifacts for deployment. While the script is evaluated,
    /// reads <paramref name="hostDirectory"/>/manifest.json and verifies every
    /// file's SHA-256 on the host (missing, changed or extra files fail the run
    /// before any step). Then registers a step that copies the directory into
    /// the container (<paramref name="containerDirectory"/>, default: the same
    /// relative path under /workspace) and verifies the hashes there again.
    /// Relative host paths are relative to the directory ANDO was run from.
    /// Usage: var m = Ando.ImportArtifacts("artifacts");
    ///        AppService.DeployFolder(app, m.ContainerPath + "/web", rg, ...);
    ///        Log.Info($"Deploying {m.Version} ({m.Commit})");
    /// </summary>
    public ArtifactManifest ImportArtifacts(string hostDirectory, string? containerDirectory = null)
    {
        var session = _buildOptions.Session;
        var source = session.ResolveHostPath(hostDirectory);
        var manifestPath = Path.Combine(source, ArtifactManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"No {ArtifactManifest.FileName} in {source}. Write one in the build run with Ando.WriteManifest.");
        }

        var manifest = ArtifactManifest.Parse(File.ReadAllText(manifestPath));
        var problems = manifest.VerifyDirectory(source);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The artifacts in {source} do not match {ArtifactManifest.FileName}:\n  " +
                string.Join("\n  ", problems.Take(20)) +
                (problems.Count > 20 ? $"\n  ... and {problems.Count - 20} more" : ""));
        }
        _logger.Info($"Artifacts: {source} verified ({manifest.Files.Count} files, version {manifest.Version}, commit {manifest.Commit})");

        var target = containerDirectory ?? (Path.IsPathRooted(hostDirectory) ? "artifacts" : hostDirectory);
        manifest.ContainerPath = Ando.Context.ContainerPaths.Resolve(target);

        _registry.Register("Ando.ImportArtifacts", async () =>
        {
            if (!await CopyToContainerAsync(session, source, manifest.ContainerPath, _logger))
            {
                return false;
            }

            // Verify again inside the container, against the manifest read at evaluation time.
            var checkList = string.Join("\n", manifest.Files.Select(f => $"{f.Sha256}  ./{f.Path}")) + "\n";
            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(checkList));
            var executor = _executorFactory();
            var check = await executor.ExecuteAsync("sh",
                ["-c", "cd \"$2\" && printf %s \"$1\" | base64 -d | sha256sum --quiet -c -", "sh", base64, manifest.ContainerPath],
                new Execution.CommandOptions { TimeoutMs = 30 * 60 * 1000 });
            if (!check.Success)
            {
                _logger.Error("    The artifacts in the container do not match the manifest.");
                return false;
            }

            // Files added on the host after evaluation must not ship either:
            // the container copy must hold exactly the manifest's files.
            var listing = await executor.ExecuteAsync("sh",
                ["-c", "cd \"$1\" && find . -type f ! -path ./manifest.json", "sh", manifest.ContainerPath],
                new Execution.CommandOptions { SuppressOutput = true, TimeoutMs = 10 * 60 * 1000 });
            var expected = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            var extra = (listing.Output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Select(l => l.StartsWith("./", StringComparison.Ordinal) ? l[2..] : l)
                .Where(p => !expected.Contains(p))
                .ToList();
            if (!listing.Success || extra.Count > 0)
            {
                _logger.Error(listing.Success
                    ? $"    Files not in the manifest: {string.Join(", ", extra.Take(20))}"
                    : "    Could not list the imported artifacts.");
                return false;
            }

            _logger.Info($"    Verified {manifest.Files.Count} file(s) in {manifest.ContainerPath}");
            return true;
        }, $"{hostDirectory} -> {manifest.ContainerPath}");

        return manifest;
    }
}
