// =============================================================================
// OperationsBase.cs
//
// Summary: Base class for operation classes with shared registration logic.
//
// OperationsBase provides common fields and helper methods used by all
// operation classes (DotnetOperations, EfOperations, NpmOperations).
//
// Design:
// - All RegisterCommand overloads delegate to a single internal implementation
// - CommandOptions building is centralized in BuildCommandOptions()
// - This reduces code duplication and makes maintenance easier
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
    /// Builds CommandOptions from optional working directory and environment variables.
    /// Centralized to avoid duplication across registration methods.
    /// </summary>
    private static CommandOptions BuildCommandOptions(
        string? workingDirectory,
        Dictionary<string, string>? environment)
    {
        var options = new CommandOptions();

        if (workingDirectory != null)
        {
            options.WorkingDirectory = workingDirectory;
        }

        if (environment != null)
        {
            foreach (var (key, value) in environment)
            {
                options.Environment[key] = value;
            }
        }

        return options;
    }

    /// <summary>
    /// Core registration method that all overloads delegate to.
    /// Takes a function that returns args (evaluated at execution time).
    /// </summary>
    private void RegisterCommandCore(
        string stepName,
        string command,
        Func<string[]> getArgs,
        Func<Task>? ensurer,
        string? context,
        string? workingDirectory,
        Dictionary<string, string>? environment)
    {
        Registry.Register(stepName, async () =>
        {
            // Run ensurer first if provided (auto-install SDK/runtime if needed).
            if (ensurer != null)
            {
                await ensurer();
            }

            // Get arguments (evaluated at execution time, not registration time).
            var args = getArgs();
            var options = BuildCommandOptions(workingDirectory, environment);
            var result = await ExecutorFactory().ExecuteAsync(command, args, options);

            return result.Success;
        }, context);
    }

    /// <summary>
    /// Registers a step that executes a command with the given arguments.
    /// </summary>
    protected void RegisterCommand(
        string stepName,
        string command,
        string[] args,
        string? context = null,
        string? workingDirectory = null,
        Dictionary<string, string>? environment = null)
    {
        RegisterCommandCore(stepName, command, () => args, null, context, workingDirectory, environment);
    }

    /// <summary>
    /// Registers a step that executes a command with arguments built by the provided builder function.
    /// The buildArgs function is called at step execution time, not registration time,
    /// allowing for deferred evaluation of values that may require API calls or prompts.
    /// </summary>
    protected void RegisterCommand(
        string stepName,
        string command,
        Func<ArgumentBuilder> buildArgs,
        string? context = null,
        string? workingDirectory = null,
        Dictionary<string, string>? environment = null)
    {
        RegisterCommandCore(stepName, command, () => buildArgs().Build(), null, context, workingDirectory, environment);
    }

    /// <summary>
    /// Registers a step that runs an ensurer before executing a command with string array args.
    /// The ensurer is called at execution time to auto-install required SDKs/runtimes.
    /// </summary>
    protected void RegisterCommandWithEnsurer(
        string stepName,
        string command,
        string[] args,
        Func<Task>? ensurer,
        string? context = null,
        string? workingDirectory = null,
        Dictionary<string, string>? environment = null)
    {
        RegisterCommandCore(stepName, command, () => args, ensurer, context, workingDirectory, environment);
    }

    /// <summary>
    /// Registers a step that runs an ensurer before executing a command with builder args.
    /// The ensurer is called at execution time to auto-install required SDKs/runtimes.
    /// The buildArgs function is also called at execution time, not registration time.
    /// </summary>
    protected void RegisterCommandWithEnsurer(
        string stepName,
        string command,
        Func<ArgumentBuilder> buildArgs,
        Func<Task>? ensurer,
        string? context = null,
        string? workingDirectory = null,
        Dictionary<string, string>? environment = null)
    {
        RegisterCommandCore(stepName, command, () => buildArgs().Build(), ensurer, context, workingDirectory, environment);
    }
}
