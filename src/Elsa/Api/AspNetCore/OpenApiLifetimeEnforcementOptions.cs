namespace Elsa.Api.AspNetCore;

/// <summary>
/// Controls whether <c>RequireStableOpenApi()</c> rejects collectible API Explorer-facing metadata.
/// </summary>
/// <remarks>
/// The unload-safe boundary exists because API Explorer and the OpenAPI document service retain an
/// endpoint's request and response <see cref="System.Type"/> for the host service-provider lifetime.
/// A runtime that never builds an OpenAPI document has no such retention: a collectible contract type
/// published as endpoint metadata is released with its endpoint generation. Enforcing the boundary
/// there rejects a candidate for a risk that is not present.
/// <para>
/// Enforcement is on by default, so a host that documents its endpoints is protected without opting
/// in and an unconfigured host never silently loses the guard. A host that does not register an
/// OpenAPI document service may suppress it with
/// <see cref="OpenApiLifetimeServiceCollectionExtensions.SuppressOpenApiLifetimeEnforcement"/>.
/// </para>
/// </remarks>
public sealed class OpenApiLifetimeEnforcementOptions
{
    /// <summary>Whether completed endpoint metadata is validated and marked. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;
}
