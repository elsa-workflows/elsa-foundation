using System.Text.Json;

namespace Elsa.Expressions.JavaScript.Core.Models;

/// <summary>
/// A JSON number that a JavaScript number cannot hold exactly (ADR 0046 V9, spec 1228 FR-011): its value as a double
/// differs from its stored value, or it became <c>Infinity</c>. Recorded when the value enters an evaluation.
/// </summary>
/// <param name="Path">Where it entered, such as <c>variables.order.id</c> or <c>args.ids[2]</c>.</param>
/// <param name="Stored">The stored JSON number text.</param>
/// <param name="Held">The JavaScript number text of the double the script holds.</param>
/// <param name="Value">The double the script holds.</param>
/// <param name="IsInteger">Whether the stored value is an integer (<c>1e30</c> is one; <c>0.1000000000000000000001</c> is not).</param>
public sealed record JavaScriptLossyNumber(string Path, string Stored, string Held, double Value, bool IsInteger);

/// <summary>A lossy entered number found in a value that is about to be written (spec 1228 FR-011a).</summary>
/// <param name="Path">Where the number is in the written value, such as <c>result.id</c>.</param>
/// <param name="Entered">The lossy entered number it matches by double value.</param>
/// <param name="Written">The JSON number text that would be written.</param>
/// <param name="WholeArrayPath">The path of the array written as a whole that contains it, or <see langword="null"/>.</param>
public sealed record JavaScriptPrecisionLoss(string Path, JavaScriptLossyNumber Entered, string Written, string? WholeArrayPath = null);

/// <summary>What a script did to one value it read or assigned (spec 1228 FR-003 to FR-005).</summary>
public enum ScriptValueChangeKind
{
    /// <summary>Read, and equal as a JavaScript value to what was read: nothing is written.</summary>
    Unchanged,

    /// <summary>One or more members changed; the members not changed keep their stored text (a partial update).</summary>
    MemberChange,

    /// <summary>The value was assigned, or it is an array that changed: it is written as a whole (a replace).</summary>
    WholeAssignment
}

/// <summary>The change the bridge reports for one value after an evaluation.</summary>
/// <param name="Key">The value, such as <c>variables.order</c>.</param>
/// <param name="Kind">Unchanged, member change or whole assignment.</param>
/// <param name="Value">The value to write: for a member change the stored value with the changed members patched in;
/// <see langword="null"/> when unchanged, or when the script assigned <c>undefined</c>.</param>
/// <param name="ChangedPaths">The paths that changed, such as <c>variables.order.total</c>.</param>
/// <param name="Losses">The lossy entered numbers the written parts carry.</param>
public sealed record ScriptValueChange(
    string Key,
    ScriptValueChangeKind Kind,
    JsonElement? Value,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<JavaScriptPrecisionLoss> Losses)
{
    public static ScriptValueChange Unchanged(string key) => new(key, ScriptValueChangeKind.Unchanged, null, [], []);
}

/// <summary>A value leaving JavaScript to be written, such as a result: its JSON and the losses it carries.</summary>
/// <param name="Value">The JSON; <see langword="null"/> when the value is <c>undefined</c>. An entered value written
/// unchanged is its original JSON (FR-011a).</param>
/// <param name="Losses">The lossy entered numbers the value carries.</param>
public sealed record ScriptValueWrite(JsonElement? Value, IReadOnlyList<JavaScriptPrecisionLoss> Losses);
