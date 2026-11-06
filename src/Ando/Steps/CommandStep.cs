// =============================================================================
// CommandStep.cs
//
// Summary: A generic command step whose output later steps can read.
//
// Returned by Shell.Run, Shell.Bash and Azure.Cli. The fluent methods configure
// the step after it was registered; they take effect because the step only
// runs after the whole script was evaluated.
// =============================================================================

using Ando.Execution;

namespace Ando.Steps;

/// <summary>
/// A command step. Configure it with the fluent methods; read its output
/// (<see cref="StepOutput.Value"/>, <see cref="StepOutput.ExitCode"/>,
/// <see cref="StepOutput.Json"/>) at execution time.
/// </summary>
public class CommandStep : StepOutput
{
    internal CommandStep(string stepName, int? defaultTimeoutMs) : base(stepName)
    {
        TimeoutMs = defaultTimeoutMs ?? CommandOptions.DefaultTimeoutMs;
    }

    internal string? WorkingDirectory { get; private set; }
    internal int TimeoutMs { get; private set; }
    internal Dictionary<string, string> Environment { get; } = new();
    internal bool FailureAllowed { get; private set; }
    internal bool IsQuiet { get; private set; }

    /// <summary>
    /// Runs the command in this directory. Relative paths are relative to the
    /// project root (/workspace in the container).
    /// </summary>
    public CommandStep InDirectory(string path)
    {
        WorkingDirectory = path;
        return this;
    }

    /// <summary>Fails the step when the command runs longer than this (default 5 minutes or Ando.CommandTimeout).</summary>
    public CommandStep WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }
        TimeoutMs = (int)timeout.TotalMilliseconds;
        return this;
    }

    /// <summary>Sets an environment variable for this command only.</summary>
    public CommandStep WithEnv(string name, string value)
    {
        Environment[name] = value;
        return this;
    }

    /// <summary>
    /// Lets the build continue when the command exits non-zero. Check
    /// <see cref="StepOutput.ExitCode"/> in a later Ando.When or Ando.Require.
    /// </summary>
    public CommandStep AllowFailure()
    {
        FailureAllowed = true;
        return this;
    }

    /// <summary>Captures the output without printing it (for values such as tokens).</summary>
    public CommandStep Quiet()
    {
        IsQuiet = true;
        return this;
    }
}
