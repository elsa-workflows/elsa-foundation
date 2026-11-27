using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A workflow execution's committed named output values, read without the diagnostics capture policy. Values whose
/// protection policy forbids carrying them, or that are not stored inline, are left out and named in
/// <see cref="WithheldNames"/>; their values never appear here.
/// </summary>
public sealed class WorkflowOutputValues
{
    public WorkflowOutputValues(JsonElement values, IReadOnlyList<string> withheldNames)
    {
        if (values.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Workflow output values must be a JSON object.", nameof(values));
        ArgumentNullException.ThrowIfNull(withheldNames);
        if (withheldNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A withheld workflow output name must be nonblank.", nameof(withheldNames));

        Values = values.Clone();
        WithheldNames = withheldNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Output name to committed value, in ordinal name order.</summary>
    public JsonElement Values { get; }

    /// <summary>Names of the outputs left out, in ordinal order. Names only, never values.</summary>
    public IReadOnlyList<string> WithheldNames { get; }
}
