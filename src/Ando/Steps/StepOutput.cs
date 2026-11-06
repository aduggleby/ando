// =============================================================================
// StepOutput.cs
//
// Summary: The result of a step, readable by later steps at execution time.
//
// Steps register while the script is evaluated and run afterwards, so a step's
// output does not exist when the script code after it runs. StepOutput is the
// handle a script keeps: reading it inside an execution-time lambda (Ando.Defer,
// Ando.When, Ando.Require, Func<> overloads) returns the value once the step ran.
//
// Design Decisions:
// - Reading before the step ran throws, so a mistaken eval-time read fails
//   loudly instead of passing an empty string
// - Value is stdout trimmed, which is what command output checks usually want
// - Json() reads a dotted path so scripts don't need a JSON parser for az output
// =============================================================================

using System.Text.Json;

namespace Ando.Steps;

/// <summary>
/// Output of a step that later steps can read at execution time.
/// </summary>
public class StepOutput
{
    private string? _value;
    private int? _statusCode;

    /// <summary>Creates an output handle for the named step.</summary>
    public StepOutput(string stepName)
    {
        StepName = stepName;
    }

    /// <summary>The step this output belongs to (for error messages).</summary>
    public string StepName { get; }

    /// <summary>Whether the step has run and set its output.</summary>
    public bool HasValue { get; private set; }

    /// <summary>
    /// The step's output: trimmed stdout for commands, the body for HTTP requests.
    /// Throws when read before the step ran.
    /// </summary>
    public string Value
    {
        get
        {
            EnsureHasValue();
            return _value!;
        }
    }

    /// <summary>Exit code of a command step (0 on success). Throws when read before the step ran.</summary>
    public int ExitCode
    {
        get
        {
            EnsureHasValue();
            return _exitCode;
        }
    }

    private int _exitCode;

    /// <summary>HTTP status code for HTTP steps. Throws for other steps.</summary>
    public int StatusCode
    {
        get
        {
            EnsureHasValue();
            return _statusCode ?? throw new InvalidOperationException(
                $"{StepName} is not an HTTP step, so it has no status code.");
        }
    }

    /// <summary>
    /// Reads a value from the output parsed as JSON. The path is dotted property
    /// names with optional array indexes, for example "siteConfig.ipSecurityRestrictions[0].name".
    /// Returns the string value, the raw JSON text for objects and arrays, or null
    /// when the path does not exist or the value is JSON null.
    /// </summary>
    public string? Json(string path)
    {
        using var document = JsonDocument.Parse(Value);
        var element = document.RootElement;

        foreach (var segment in ParsePath(path))
        {
            if (segment is int index)
            {
                if (element.ValueKind != JsonValueKind.Array || index >= element.GetArrayLength())
                {
                    return null;
                }
                element = element[index];
            }
            else if (element.ValueKind != JsonValueKind.Object ||
                     !TryGetPropertyIgnoreCase(element, (string)segment, out element))
            {
                return null;
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }

    /// <summary>Returns the value, so a StepOutput can be used in string interpolation inside lambdas.</summary>
    public override string ToString() => Value;

    /// <summary>Sets the output. Called by the step when it completes.</summary>
    public void Set(string? value, int exitCode = 0, int? statusCode = null)
    {
        _value = value?.Trim() ?? "";
        _exitCode = exitCode;
        _statusCode = statusCode;
        HasValue = true;
    }

    private void EnsureHasValue()
    {
        if (!HasValue)
        {
            throw new InvalidOperationException(
                $"The output of {StepName} is not available yet. Steps run after the script is evaluated, " +
                "so read step outputs inside an execution-time lambda (Ando.Defer, Ando.When, Ando.Require) " +
                "and only after the step has run.");
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // Splits "a.b[2].c" into "a", "b", 2, "c".
    internal static IEnumerable<object> ParsePath(string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = part.IndexOf('[');
            var name = bracket < 0 ? part : part[..bracket];
            if (name.Length > 0)
            {
                yield return name;
            }

            while (bracket >= 0)
            {
                var close = part.IndexOf(']', bracket);
                if (close < 0 || !int.TryParse(part[(bracket + 1)..close], out var index))
                {
                    throw new ArgumentException($"Invalid JSON path segment '{part}' in '{path}'.", nameof(path));
                }
                yield return index;
                bracket = part.IndexOf('[', close);
            }
        }
    }
}
