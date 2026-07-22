using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// GET publishing/value-conversion/profiles — the registered conversion profiles a visual authoring picker can offer.
/// Advertised under the <c>elsa.api.expressions</c> capability as relation <c>conversion-profiles</c>.
/// </summary>
internal sealed class ListValueConversionProfiles(IRequestSender requestSender)
    : ElsaEndpointWithoutRequest<ValueConversionProfilesResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.ValueConversionProfiles);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = await requestSender.Send(new Requests.ListValueConversionProfiles(), cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
