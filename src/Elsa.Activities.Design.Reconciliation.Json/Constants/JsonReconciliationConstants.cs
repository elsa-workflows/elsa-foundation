namespace Elsa.Activities.Design.Reconciliation.Json;

/// <summary>
/// Well-known string constants owned by the JSON reconciliation source module. The core
/// library never enumerates these; ownership lives with the module that produces the value.
/// </summary>
public static class JsonReconciliationConstants
{
    /// <summary>
    /// The <c>SourceKind</c> value tagged on every activity definition this source contributes.
    /// </summary>
    public const string SourceKind = "Json";
}
