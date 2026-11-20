using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Activities.Scripting.Services;

/// <summary>
/// A <c>RunJavaScript</c> body's variables over the step's variable session (spec 1222 FR-033 to FR-035, task T050):
/// a name the body reads literally reads its pinned target (FR-004); any other name resolves to the nearest visible
/// declaration at run time (FR-008). The session records every read with its version and protection policy, and returns
/// the committed value plus the step's own pending writes. A withheld or transient value is refused: the session has
/// already recorded the fault, so the step faults even when the script catches the error (FR-017a). Writes go back
/// through the same session and commit with the step (ADR 0090, spec 1228 FR-006).
/// </summary>
internal sealed class StepScriptVariables : IScriptVariableSession
{
    private static readonly JsonElement JsonNull = JsonSerializer.SerializeToElement<object?>(null);
    private readonly IStepVariableSession _session;
    private readonly List<string> _missing = [];

    public StepScriptVariables(IStepVariableSession session)
    {
        _session = session;
        // A body with a computed or opaque read may enumerate `variables`: it sees every visible name. Otherwise only the
        // names it reads literally are own properties; any other name is still looked up when read.
        CandidateNames = session.ReadsComputedNames
            ? session.PinnedReads.Keys.Concat(session.VisibleNames).Distinct(StringComparer.Ordinal).ToArray()
            : session.PinnedReads.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<string> CandidateNames { get; }

    /// <summary>The names the body read that no visible variable has, in first-read order (FR-011).</summary>
    public IReadOnlyList<string> MissingNames => _missing;

    public bool HasReadProtectedValue => _session.HasReadProtectedValue;

    public bool IsReadOnly(string name) => TryTarget(name, out var target) && _session.IsReadOnly(target);

    public ExpressionVariableRead Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TryTarget(name, out var target))
        {
            if (!_missing.Contains(name, StringComparer.Ordinal))
                _missing.Add(name);
            return ExpressionVariableRead.Missing;
        }

        JsonNode? value;
        try
        {
            value = _session.ReadJson(target);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The session recorded the fault on the host side; the script sees an error it cannot catch away.
            return ExpressionVariableRead.Refused(exception.Message);
        }

        return ExpressionVariableRead.Present(value is null ? JsonNull : JsonSerializer.SerializeToElement(value));
    }

    public void Write(string name, JsonElement? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TryTarget(name, out var target))
            // The name can be computed from a value the script read; when that value is protected, it is not named.
            throw new InvalidOperationException(_session.HasReadProtectedValue
                ? "The script writes a variable that is not declared in any scope visible to it; a script never creates a variable. " +
                  "The name is withheld because a value it read is protected."
                : $"The script writes variable '{name}', which is not declared in any scope visible to it; a script never creates a variable.");
        _session.WriteJson(target, value is { ValueKind: not JsonValueKind.Null } json ? JsonNode.Parse(json.GetRawText()) : null);
    }

    private bool TryTarget(string name, out StepVariableTarget target)
    {
        if (_session.PinnedReads.TryGetValue(name, out target!))
            return true;
        return !string.IsNullOrWhiteSpace(name) && _session.TryResolve(name, out target!);
    }

    /// <summary>
    /// The warnings for the names the body read that no visible variable has (spec 1222 FR-011): it read
    /// <c>undefined</c>. A computed name can come from a value, so the protected form does not name it.
    /// </summary>
    public IEnumerable<ExecutionWarning> MissingNameWarnings()
    {
        foreach (var name in _missing)
        {
            yield return new ExecutionWarning(
                MissingNameWarning,
                $"{MissingNameWarning}: the script read variable '{name}', and no visible variable has that name; it read undefined.",
                ExecutionWarningSeverity.Warning,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["variable"] = name })
            {
                ProtectedForm = new ExecutionWarning(
                    MissingNameWarning,
                    $"{MissingNameWarning}: the script read a variable by a name no visible variable has; it read undefined. The name is withheld because a value it read is protected.")
            };
        }
    }

    // The code of an absent computed read, as recorded for input expressions (VariableComputedRead.MissingNameWarning).
    private const string MissingNameWarning = "VF-READ-001";
}
