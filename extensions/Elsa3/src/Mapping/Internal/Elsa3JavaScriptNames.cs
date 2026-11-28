using System.Collections.Frozen;
using System.Globalization;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa3.Mapping.Internal;

/// <summary>
/// Elsa 3's JavaScript naming rules, needed to tell which <c>get{Name}()</c> functions Elsa 3 generated (spec 247
/// research R-2, R-3). Duplicated from Elsa 3 and Humanizer on purpose: a few lines beat a dependency.
/// </summary>
internal static class Elsa3JavaScriptNames
{
    /// <summary>
    /// Humanizer 2.14.1 <c>Pascalize</c> for the names Elsa 3 generates accessors for (letters and digits only): it
    /// upper-cases the first character. The invariant culture is used where Elsa 3 used the current one.
    /// </summary>
    public static string Pascalize(string name) =>
        name.Length == 0 ? name : string.Concat(char.ToUpper(name[0], CultureInfo.InvariantCulture).ToString(), name.AsSpan(1));

    /// <summary>Elsa 3's <c>IsValidVariableName</c>: only such variables get <c>get{Name}()</c>/<c>set{Name}()</c>.</summary>
    public static bool IsAccessorName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.All(char.IsLetterOrDigit);

    /// <summary>Elsa 3's built-in JavaScript functions; a generated accessor with one of these names is ambiguous.</summary>
    public static FrozenSet<string> BuiltIns { get; } = new[]
    {
        "getWorkflowDefinitionId", "getWorkflowDefinitionVersionId", "getWorkflowDefinitionVersion",
        "getWorkflowInstanceId", "setCorrelationId", "getCorrelationId", "setWorkflowInstanceName",
        "getWorkflowInstanceName", "setVariable", "getVariable", "getInput", "getOutputFrom", "getLastResult",
        "isNullOrWhiteSpace", "isNullOrEmpty", "toJson", "parseGuid", "newGuid", "newGuidString", "newShortGuid",
        "getGuidString", "getShortGuid", "bytesToString", "bytesFromString", "bytesToBase64", "bytesFromBase64",
        "stringToBase64", "stringFromBase64", "streamToBytes", "streamToBase64", "getConfig",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The Elsa 3 functions that are Elsa 4 built-ins returning the same text (spec 1223 FR-006, FR-007, FR-026). An
    /// Elsa 4 built-in that Elsa 3 did not have (<c>structuredClone</c>, #6215) is, in an Elsa 3 script, the host's own
    /// global: it is matched against the host-function map like any other.
    /// </summary>
    public static FrozenSet<string> Elsa4BuiltIns { get; } =
        JavaScriptBuiltInFunctions.Names.Where(BuiltIns.Contains).ToFrozenSet(StringComparer.Ordinal);
}
