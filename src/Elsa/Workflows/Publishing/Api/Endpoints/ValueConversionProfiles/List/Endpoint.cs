using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Services;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ValueConversionProfiles.List;

/// <summary>
/// Projects the active shell's <see cref="IValueConversionProfileRegistry"/> to the picker contract.
/// It advertises the same versioned identities and capabilities publication resolves against, so
/// authoring cannot pin a profile the executable would later reject. No value contents or host
/// configuration are inspected here.
/// </summary>
[Get("/publishing/value-conversion/profiles")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IValueConversionProfileRegistry registry) : ApiEndpoint<ListValueConversionProfiles, ValueConversionProfilesResponse>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "ListValueConversionProfiles";

    public override Task<ValueConversionProfilesResponse> HandleAsync(ListValueConversionProfiles request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = registry.List()
            .Select(definition => new ValueConversionProfileView(
                new ValueConversionProfileReferenceView(definition.Profile.Id, definition.Profile.Version),
                definition.SupportedSourceRepresentations
                    .OrderBy(representation => representation)
                    .ToArray(),
                definition.SupportedTargetAliases
                    .OrderBy(alias => alias, StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(profile => profile.Profile.Id, StringComparer.Ordinal)
            .ThenBy(profile => profile.Profile.Version, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(new ValueConversionProfilesResponse(items));
    }
}
