namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that an activity owns an authored child-structure contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ActivityStructureAttribute : Attribute
{
    public ActivityStructureAttribute(string kind, string schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("A structure kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new ArgumentException("A structure schema version is required.", nameof(schemaVersion));

        Kind = kind;
        SchemaVersion = schemaVersion;
    }

    public string Kind { get; }

    public string SchemaVersion { get; }

    /// <summary>The designer mode for rendering the primary child collection.</summary>
    public string Mode { get; init; } = "generic";

    /// <summary>Whether this structure introduces a variable scope for its children.</summary>
    public bool SupportsScopedVariables { get; init; }
}
