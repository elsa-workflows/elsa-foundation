namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that the activity's <see cref="ScriptInputKey"/> input is a JavaScript function body that selects outcomes by
/// returning <c>complete(result, outcomes)</c> (spec 1208 FR-007). Draft validation reads the declaration through the
/// <c>elsa.outcomes</c> facet to check literal outcome names against the node's outcome set (<c>VF-AUTH-010</c>), to
/// warn when the script returns a string equal to a connected outcome (<c>VF-AUTH-012</c>), and to refuse a script that
/// declares the reserved name <c>complete</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ActivityScriptOutcomesAttribute(string scriptInputKey) : Attribute
{
    /// <summary>The contract key of the script input.</summary>
    public string ScriptInputKey { get; } = !string.IsNullOrWhiteSpace(scriptInputKey)
        ? scriptInputKey
        : throw new ArgumentException("A script input key is required.", nameof(scriptInputKey));
}
