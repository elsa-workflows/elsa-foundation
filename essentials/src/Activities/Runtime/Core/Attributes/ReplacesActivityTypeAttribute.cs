namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares an activity type name from another system or earlier generation that this activity replaces, for example
/// an Elsa 3 custom activity rebuilt as this activity. CLR reconciliation records the declaration in the activity
/// catalog (design facet <c>elsa.replaced-activity-types</c>), where a converter can find it without loading
/// assemblies. The declaration is part of the activity version's content: adding or removing it needs a new version.
/// </summary>
/// <remarks>Read from the class it is declared on only; a derived activity does not inherit it.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ReplacesActivityTypeAttribute(string typeName) : Attribute
{
    /// <summary>The replaced type name, for example <c>Acme.SendInvoice</c>.</summary>
    public string TypeName { get; } = !string.IsNullOrWhiteSpace(typeName)
        ? typeName
        : throw new ArgumentException("A replaced activity type name is required.", nameof(typeName));
}
