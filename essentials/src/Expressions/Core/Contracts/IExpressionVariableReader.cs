using System.Text.Json;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// The host-side lookup behind a script's computed and opaque variable reads (spec 1222 FR-008, FR-010, research R-4).
/// The host makes the candidates readable before evaluation, because a script engine reads synchronously; each call
/// resolves a name nearest scope first, checks the variable actually read, and records the read on the host side, so a
/// refused read faults the step even when the script catches the error it raises (FR-017a).
/// </summary>
public interface IExpressionVariableReader
{
    /// <summary>The names the lookup can resolve, nearest scope first, each once. A script that enumerates <c>variables</c> sees these.</summary>
    IReadOnlyList<string> CandidateNames { get; }

    /// <summary>Reads one variable by name and records the read.</summary>
    ExpressionVariableRead Read(string name);
}

/// <summary>What one computed or opaque variable read gave.</summary>
public readonly record struct ExpressionVariableRead
{
    private ExpressionVariableRead(ExpressionVariableReadOutcome outcome, JsonElement value, string? refusal)
    {
        Outcome = outcome;
        Value = value;
        Refusal = refusal;
    }

    public ExpressionVariableReadOutcome Outcome { get; }

    /// <summary>The value read; JSON <c>null</c> for a declared variable with no value (FR-006). Only for <see cref="ExpressionVariableReadOutcome.Present"/>.</summary>
    public JsonElement Value { get; }

    /// <summary>Why the read was refused. Names the variable, never its value. Only for <see cref="ExpressionVariableReadOutcome.Refused"/>.</summary>
    public string? Refusal { get; }

    public static ExpressionVariableRead Present(JsonElement value) => new(ExpressionVariableReadOutcome.Present, value.Clone(), null);

    /// <summary>The name matches no visible variable: JavaScript reads <c>undefined</c>, Liquid nil (FR-011).</summary>
    public static ExpressionVariableRead Missing { get; } = new(ExpressionVariableReadOutcome.Missing, default, null);

    /// <summary>The read reached a value the script may not have (withheld or transient, FR-017, FR-020).</summary>
    public static ExpressionVariableRead Refused(string refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusal);
        return new(ExpressionVariableReadOutcome.Refused, default, refusal);
    }
}

public enum ExpressionVariableReadOutcome
{
    Present,
    Missing,
    Refused
}
