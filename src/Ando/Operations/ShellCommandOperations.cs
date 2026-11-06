// =============================================================================
// ShellCommandOperations.cs
//
// Summary: Generic command steps for build scripts.
//
// Shell.Run registers a step that runs any command in the build container, in
// order with the other steps. It fails the build on a non-zero exit code (unless
// AllowFailure), has a configurable timeout, and its stdout is available to
// later steps through the returned CommandStep.
//
// Design Decisions:
// - Arguments are passed as a list (no shell parsing); use Shell.Bash for
//   pipes, redirection and sourcing scripts
// - Shell.Bash runs with `set -euo pipefail` so a failing command fails the step
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Generic command steps: run any command in the build container.
/// </summary>
public class ShellCommandOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Registers a step that runs <paramref name="command"/> with
    /// <paramref name="args"/> in the build container.
    /// Usage: var zip = Shell.Run("zip", "-qr", "../web.zip", ".").InDirectory("artifacts/web");
    /// </summary>
    public CommandStep Run(string command, params string[] args) =>
        RegisterCommandStep("Shell.Run", command, args, command);

    /// <summary>
    /// Registers a step that runs a bash script (with `set -euo pipefail`) in the
    /// build container. Use it for pipes, redirection and sourcing helper scripts.
    /// Usage: Shell.Bash("source scripts/lib/deploy-gates.sh && require_spa_build artifacts/web");
    /// </summary>
    public CommandStep Bash(string script) =>
        RegisterCommandStep("Shell.Bash", "bash", ["-euo", "pipefail", "-c", script], FirstLine(script));

    private static string FirstLine(string script)
    {
        var line = script.Trim().Split('\n')[0].Trim();
        return line.Length > 60 ? line[..57] + "..." : line;
    }

    /// <summary>
    /// Registers a command step that captures stdout into a <see cref="CommandStep"/>.
    /// Shared by Shell and Azure.Cli.
    /// </summary>
    internal static CommandStep RegisterCommandStep(
        StepRegistry registry,
        IBuildLogger logger,
        Func<ICommandExecutor> executorFactory,
        string stepName,
        string command,
        string[] args,
        string? context,
        int? defaultTimeoutMs = null)
    {
        var step = new CommandStep(stepName, defaultTimeoutMs ?? registry.DefaultCommandTimeoutMs);

        registry.Register(stepName, async () =>
        {
            var options = new CommandOptions
            {
                TimeoutMs = step.TimeoutMs,
                SuppressOutput = step.IsQuiet,
                WorkingDirectory = step.WorkingDirectory == null
                    ? null
                    : Ando.Context.ContainerPaths.Resolve(step.WorkingDirectory)
            };
            foreach (var (key, value) in step.Environment)
            {
                options.Environment[key] = value;
            }

            var result = await executorFactory().ExecuteAsync(command, args, options);
            step.Set(result.Output, result.ExitCode);

            if (result.Success)
            {
                return true;
            }

            if (result.Error?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new TimeoutException(result.Error);
            }

            if (step.FailureAllowed)
            {
                logger.Warning($"    {command} exited with {result.ExitCode} (allowed).");
                return true;
            }

            logger.Error($"    {command} exited with {result.ExitCode}.");
            return false;
        }, context);

        return step;
    }

    private CommandStep RegisterCommandStep(string stepName, string command, string[] args, string? context) =>
        RegisterCommandStep(Registry, Logger, ExecutorFactory, stepName, command, args, context);
}
