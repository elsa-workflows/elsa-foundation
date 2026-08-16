namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Well-known CShell feature metadata used to select the deployment schema that matches the
/// features enabled for a shell.
/// </summary>
public static class GroundworkSchemaFeatureMetadata
{
    /// <summary>
    /// Metadata key whose value identifies a schema contribution made by an enabled feature.
    /// </summary>
    public const string Key = "Elsa.Groundwork.SchemaFeature";

    /// <summary>The Foundation Identity storage contribution.</summary>
    public const string Identity = "elsa-identity";

}
