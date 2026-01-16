// =============================================================================
// BicepDeployment.cs
//
// Summary: Strongly-typed result object for Bicep deployments.
//
// BicepDeployment captures deployment outputs and provides typed access to them.
// Outputs are populated when the deployment step executes and can be accessed
// by subsequent steps via OutputRef references.
//
// Design Decisions:
// - Outputs dictionary is populated at step execution time
// - OutputRef provides lazy access that resolves when steps execute
// - This replaces the previous pattern of writing to VarsContext
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Represents the result of a Bicep deployment.
/// Outputs are populated when the deployment step executes.
/// </summary>
public class BicepDeployment
{
    internal readonly Dictionary<string, string> _outputs = new();

    /// <summary>
    /// Gets an output value by name. Returns null if the output doesn't exist.
    /// Note: This should only be called at step execution time, not during script evaluation.
    /// </summary>
    /// <param name="name">The output name from the Bicep template.</param>
    public string? GetOutput(string name) =>
        _outputs.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Creates a lazy reference to an output that can be passed to other operations.
    /// The reference is resolved when the consuming step executes.
    /// </summary>
    /// <param name="name">The output name from the Bicep template.</param>
    public OutputRef Output(string name) => new(this, name);

    /// <summary>
    /// Gets all captured output names.
    /// </summary>
    public IReadOnlyCollection<string> OutputNames => _outputs.Keys;

    /// <summary>
    /// Gets the number of captured outputs.
    /// </summary>
    public int OutputCount => _outputs.Count;
}

/// <summary>
/// A lazy reference to a deployment output that resolves at step execution time.
/// Pass this to operations that need deployment outputs (e.g., connection strings).
/// </summary>
public class OutputRef
{
    private readonly BicepDeployment _deployment;
    private readonly string _name;

    internal OutputRef(BicepDeployment deployment, string name)
    {
        _deployment = deployment;
        _name = name;
    }

    /// <summary>
    /// The name of the output being referenced.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Resolves the output value. Call this at step execution time.
    /// Returns null if the output doesn't exist or the deployment hasn't run.
    /// </summary>
    public string? Resolve() => _deployment.GetOutput(_name);

    /// <summary>
    /// Returns a placeholder string for display/debugging purposes.
    /// </summary>
    public override string ToString() => $"{{deployment.{_name}}}";
}
