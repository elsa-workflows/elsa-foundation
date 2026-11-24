namespace Elsa3.Mapping;

/// <summary>Portable workflow-policy choices applied by the Elsa 3 converter.</summary>
public sealed class Elsa3MappingOptions
{
    /// <summary>Records default-on-null and undefined-as-null on each converted workflow. Native workflows keep both off.</summary>
    public bool ApplyElsa3NullPolicies { get; set; } = true;
}
