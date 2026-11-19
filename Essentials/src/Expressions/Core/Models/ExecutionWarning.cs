using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// A warning an evaluation or an activity step raised: it changes nothing and refuses nothing, and it is recorded on the
/// activity execution so inspection shows it (spec 1228 FR-013, FR-015; the shared run-time warning channel). It never
/// carries a value: <see cref="Message"/> and <see cref="Details"/> name paths, variables and inputs only, so a warning
/// is safe to show to anyone who can see the execution (spec 1229 FR-006).
/// </summary>
/// <param name="Code">A stable diagnostic code, such as <c>VF-PREC-002</c>.</param>
/// <param name="Message">What happened and what to do, with no value in it.</param>
/// <param name="Severity">Information or warning.</param>
/// <param name="Details">Structured facts, such as <c>paths</c> or <c>input</c>; never a value.</param>
public sealed record ExecutionWarning(
    string Code,
    string Message,
    ExecutionWarningSeverity Severity = ExecutionWarningSeverity.Warning,
    IReadOnlyDictionary<string, string>? Details = null)
{
    /// <summary>A copy whose details also carry <paramref name="key"/> = <paramref name="value"/>.</summary>
    public ExecutionWarning WithDetail(string key, string value) =>
        this with { Details = new Dictionary<string, string>(Details ?? new Dictionary<string, string>(), StringComparer.Ordinal) { [key] = value } };
}

[JsonConverter(typeof(JsonStringEnumConverter<ExecutionWarningSeverity>))]
public enum ExecutionWarningSeverity
{
    Information,
    Warning
}

/// <summary>Collects the warnings of one evaluation or one step. Thread-safe; the order is the order reported.</summary>
public sealed class ExecutionWarningCollector : IExecutionWarningSink
{
    private readonly List<ExecutionWarning> _warnings = [];
    private readonly object _gate = new();

    public void Report(ExecutionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        lock (_gate)
            _warnings.Add(warning);
    }

    /// <summary>The warnings reported so far.</summary>
    public IReadOnlyList<ExecutionWarning> Warnings
    {
        get
        {
            lock (_gate)
                return _warnings.ToArray();
        }
    }
}
