using System.Text.Json.Serialization;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// A warning an evaluation or an activity step raised: it changes nothing and refuses nothing, and it is recorded on the
/// activity execution so inspection shows it (spec 1228 FR-013, FR-015; the shared run-time warning channel). It never
/// carries a value: <see cref="Message"/> and <see cref="Details"/> name paths, variables and inputs only. A member path
/// can still carry the key of a stored object, which is part of that value, so a warning whose paths go below a
/// variable, an argument or a result has a <see cref="ProtectedForm"/>, and the runtime keeps that form when a value the
/// evaluation or step read is protected (spec 1229 FR-006).
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
    /// <summary>Records a typed JSON conversion diagnostic with a protected form that reveals no nested data keys.</summary>
    public static ExecutionWarning FromJsonConversion(string code, string path, Type targetType, string? converter, string root)
    {
        var severity = code == "json-unmatched-coverage-unavailable" ? ExecutionWarningSeverity.Information : ExecutionWarningSeverity.Warning;
        var target = targetType.FullName ?? targetType.Name;
        var details = new Dictionary<string, string> { ["path"] = root + path.TrimStart('$'), ["targetType"] = target };
        if (converter is not null) details["converter"] = converter;
        var message = code == "json-unmatched-coverage-unavailable" ? $"Unmatched-member coverage is unavailable for {target} using {converter}." : $"JSON member {details["path"]} matches no member of {target}. Conversion continued.";
        return new(code, message, severity, details)
        {
            ProtectedForm = new(code, $"A JSON conversion of {root} to {target} reported {code}.", severity,
                new Dictionary<string, string> { ["path"] = root, ["targetType"] = target })
        };
    }
    /// <summary>
    /// The form kept when a value involved is protected: it names roots only (<c>variables.order</c>, <c>args</c>),
    /// never a member below them. Null when the warning names nothing below a root; it is never stored.
    /// </summary>
    [JsonIgnore]
    public ExecutionWarning? ProtectedForm { get; init; }

    /// <summary>A copy whose details also carry <paramref name="key"/> = <paramref name="value"/>.</summary>
    public ExecutionWarning WithDetail(string key, string value) =>
        this with
        {
            Details = new Dictionary<string, string>(Details ?? new Dictionary<string, string>(), StringComparer.Ordinal) { [key] = value },
            ProtectedForm = ProtectedForm?.WithDetail(key, value)
        };
}

[JsonConverter(typeof(JsonStringEnumConverter<ExecutionWarningSeverity>))]
public enum ExecutionWarningSeverity
{
    Information,
    Warning
}
