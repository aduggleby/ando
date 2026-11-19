// =============================================================================
// BicepWhatIfResult.cs
//
// Summary: Structured result of `az deployment group|sub what-if`.
//
// Bicep.WhatIf returns this handle while the script is evaluated; the step fills
// it when it runs. Scripts read it inside execution-time lambdas (Ando.Require,
// Ando.Confirm(Func), Ando.When), like other step outputs. Reading before the
// step ran throws.
//
// Design Decisions:
// - ChangeType stays the string az reports (Create, Delete, Modify, NoChange,
//   Ignore, Deploy, Unsupported) so new az values need no code change
// - Parsing tolerates unknown fields; property names match case-insensitively
// - A failed what-if (status "Failed" or an error object) fails the step
// =============================================================================

using System.Text.Json;

namespace Ando.Operations;

/// <summary>One resource change reported by what-if.</summary>
/// <param name="ResourceId">Full Azure resource ID.</param>
/// <param name="ChangeType">As az reports it: Create, Delete, Modify, NoChange, Ignore, Deploy, Unsupported.</param>
/// <param name="PropertyChanges">Number of top-level property changes (the length of the change's delta).</param>
public sealed record BicepWhatIfChange(string ResourceId, string ChangeType, int PropertyChanges);

/// <summary>
/// Result of a Bicep what-if step. Filled when the step runs.
/// </summary>
public sealed class BicepWhatIfResult
{
    private static readonly string[] SummaryOrder =
        ["Create", "Modify", "Delete", "Deploy", "NoChange", "Ignore", "Unsupported"];

    private static readonly string[] Quiet = ["NoChange", "Ignore"];

    private readonly string _stepName;
    private IReadOnlyList<BicepWhatIfChange>? _changes;
    private string? _json;

    internal BicepWhatIfResult(string stepName)
    {
        _stepName = stepName;
    }

    /// <summary>Whether the step has run and filled this result.</summary>
    public bool HasValue => _changes != null;

    /// <summary>All reported changes. Throws when read before the step ran.</summary>
    public IReadOnlyList<BicepWhatIfChange> Changes => _changes ?? throw NotRun();

    /// <summary>Raw JSON output of az. Throws when read before the step ran.</summary>
    public string Json => _json ?? throw NotRun();

    /// <summary>
    /// Counts per change type, for example "2 Create, 1 Modify, 30 NoChange". Create, Modify and Delete
    /// come first, unknown types last. "no changes" when az reported nothing.
    /// </summary>
    public string Summary
    {
        get
        {
            var groups = Changes.GroupBy(c => c.ChangeType)
                .OrderBy(g => OrderOf(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Count()} {g.Key}");
            var text = string.Join(", ", groups);
            return text.Length == 0 ? "no changes" : text;
        }
    }

    /// <summary>
    /// Whether any change has one of the given types (case-insensitive). With no types, whether anything
    /// would change at all (any change that is not NoChange or Ignore). Only the <c>changes</c> list counts;
    /// az's <c>potentialChanges</c> (resources it cannot decide on) are not looked at.
    /// </summary>
    public bool HasChanges(params string[] changeTypes) =>
        changeTypes.Length == 0
            ? Changes.Any(c => !IsQuiet(c.ChangeType))
            : Changes.Any(c => changeTypes.Contains(c.ChangeType, StringComparer.OrdinalIgnoreCase));

    /// <summary>The changes of one type (case-insensitive). Like <see cref="HasChanges"/>, only <c>changes</c>, not <c>potentialChanges</c>.</summary>
    public IEnumerable<BicepWhatIfChange> OfType(string changeType) =>
        Changes.Where(c => string.Equals(c.ChangeType, changeType, StringComparison.OrdinalIgnoreCase));

    private static bool IsQuiet(string type) => Quiet.Contains(type, StringComparer.OrdinalIgnoreCase);

    private static int OrderOf(string type)
    {
        var index = Array.FindIndex(SummaryOrder, t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? SummaryOrder.Length : index;
    }

    private InvalidOperationException NotRun() => new(
        $"{_stepName} has not run yet, so there is no what-if result. " +
        "Read it inside an execution-time lambda such as Ando.Require(() => ...) or Ando.Confirm(() => ...).");

    /// <summary>Lines to log for the changes that matter (everything except NoChange and Ignore).</summary>
    internal IEnumerable<string> DescribeChanges() =>
        Changes.Where(c => !IsQuiet(c.ChangeType)).Select(c => $"{c.ChangeType} {c.ResourceId}");

    /// <summary>
    /// Fills the result from az output. Returns an error message when the output cannot be used
    /// or reports a failed what-if, otherwise null.
    /// </summary>
    internal string? Load(string? output)
    {
        var text = output?.Trim();
        // az may print warnings before the JSON on stdout; start at the first object.
        var start = text?.IndexOf('{') ?? -1;
        if (start < 0)
        {
            return "az printed no what-if JSON.";
        }
        text = text![start..];

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "az printed unexpected what-if JSON.";
            }

            var error = Find(root, "error");
            var status = Find(root, "status");
            var failed = status is { ValueKind: JsonValueKind.String } s
                && string.Equals(s.GetString(), "Failed", StringComparison.OrdinalIgnoreCase);
            if (failed || error is { ValueKind: JsonValueKind.Object })
            {
                return ErrorMessage(error);
            }

            var changes = new List<BicepWhatIfChange>();
            if (Find(root, "changes") is { ValueKind: JsonValueKind.Array } array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var delta = Find(item, "delta") ?? Find(item, "propertyChanges");
                    changes.Add(new BicepWhatIfChange(
                        Find(item, "resourceId") is { ValueKind: JsonValueKind.String } id ? id.GetString()! : "",
                        Find(item, "changeType") is { ValueKind: JsonValueKind.String } type ? type.GetString()! : "Unknown",
                        delta is { ValueKind: JsonValueKind.Array } d ? d.GetArrayLength() : 0));
                }
            }

            _json = text;
            _changes = changes;
            return null;
        }
        catch (JsonException ex)
        {
            return $"az printed what-if output that is not valid JSON ({ex.Message}).";
        }
    }

    private static string ErrorMessage(JsonElement? error)
    {
        if (error is { ValueKind: JsonValueKind.Object } e)
        {
            var code = Find(e, "code") is { ValueKind: JsonValueKind.String } c ? c.GetString() : null;
            var message = Find(e, "message") is { ValueKind: JsonValueKind.String } m ? m.GetString() : null;
            if (message != null) return code == null ? message : $"{code}: {message}";
            return e.GetRawText();
        }
        return "the what-if status is Failed.";
    }

    private static JsonElement? Find(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }
        return null;
    }
}
