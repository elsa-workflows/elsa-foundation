using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Publishing.Api.Constants;

namespace Elsa.Workflows.Publishing.Api.Capabilities;

/// <summary>
/// Advertises the publishing-owned <c>conversion-profiles</c> endpoint under the expressions capability the Studio
/// client already resolves conversion pickers against. The endpoint lives here because it reads
/// <c>IValueConversionProfileRegistry</c>, a publishing contract the expressions module cannot reference without
/// inverting the module layering. Contributing the relation through <see cref="IApiCapabilitySource"/> lets the
/// capability catalog merge it into <c>elsa.api.expressions</c> without a cross-feature project reference — the same
/// merge seam the runtime feature uses to append its own operational relations.
/// </summary>
/// <remarks>
/// The relation is only contributed when the expressions capability is actually present in the active shell. Gating
/// on the registered static declaration keeps a publishing-only host (no expressions API) from manufacturing a phantom
/// <c>elsa.api.expressions</c> capability, and mirrors the client contract: the same authoring surface that consumes
/// expression descriptors consumes conversion-profile pickers.
/// </remarks>
public sealed class ConversionProfilesCapabilitySource(IEnumerable<ApiCapabilityDeclaration> staticDeclarations)
    : IApiCapabilitySource
{
    /// <summary>
    /// The expressions capability id (owned by <c>Elsa.Expressions.Api</c>). It is duplicated as a literal on purpose:
    /// this string is the wire contract the client ships, and taking a project reference on the expressions API just
    /// to read a constant would couple two independent API features.
    /// </summary>
    private const string ExpressionsCapabilityId = "elsa.api.expressions";

    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var expressionsPresent = staticDeclarations.Any(declaration =>
            string.Equals(declaration.CapabilityId, ExpressionsCapabilityId, StringComparison.Ordinal));

        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = expressionsPresent
            ?
            [
                new(
                    ExpressionsCapabilityId,
                    1,
                    [new ApiCapabilityLink("conversion-profiles", RouteConstants.ValueConversionProfiles)],
                    $"{PublishingApiCapabilities.SourceFeatureId}.ConversionProfiles")
            ]
            : [];

        return ValueTask.FromResult(declarations);
    }
}
