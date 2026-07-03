namespace Elsa.Workflows.Runtime.JavaScript.Options;

/// <summary>
/// Options for the JavaScript workflows runtime feature. Default-constructable (init-only property) so the
/// standard options pipeline can materialize it and hosts can configure it via <c>services.Configure&lt;FeatureOptions&gt;(...)</c>.
/// </summary>
public record FeatureOptions
{
    /// <summary>When true, JavaScript variable write-back (copying script variable mutations back to the workflow scope) is disabled.</summary>
    public bool DisableVariableCopying { get; init; }
}