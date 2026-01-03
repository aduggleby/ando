// =============================================================================
// OperationsBase.cs
//
// Summary: Base class for operation classes with shared registration logic.
//
// OperationsBase provides common fields and helper methods used by all
// operation classes (DotnetOperations, EfOperations, NpmOperations).
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Base class for operation classes with shared registration logic.
/// </summary>
public abstract class OperationsBase
{
    protected readonly StepRegistry Registry;
    protected readonly IBuildLogger Logger;
    protected readonly Func<ICommandExecutor> ExecutorFactory;

    protected OperationsBase(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    {
        Registry = registry;
        Logger = logger;
        ExecutorFactory = executorFactory;
    }

    /// <summary>
    /// Registers a step that executes a command with the given arguments.
    /// </summary>
    protected void RegisterCommand(
        string stepName,
        string command,
        string[] args,
        string? context = null,
        string? workingDirectory = null)
    {
        Registry.Register(stepName, async () =>
        {
            var options = workingDirectory != null
                ? new CommandOptions { WorkingDirectory = workingDirectory }
                : null;
            var result = await ExecutorFactory().ExecuteAsync(command, args, options);
            return result.Success;
        }, context);
    }

    /// <summary>
    /// Registers a step that executes a command with arguments built by the provided builder function.
    /// </summary>
    protected void RegisterCommand(
        string stepName,
        string command,
        Func<ArgumentBuilder> buildArgs,
        string? context = null,
        string? workingDirectory = null)
    {
        RegisterCommand(stepName, command, buildArgs().Build(), context, workingDirectory);
    }
}
