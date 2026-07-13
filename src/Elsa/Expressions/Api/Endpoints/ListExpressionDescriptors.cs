using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Api.Constants;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.Api.Endpoints;

internal sealed class ListExpressionDescriptors(IRequestSender requestSender)
    : ElsaEndpointWithoutRequest<ExpressionDescriptorsResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.Descriptors);
        ConfigurePermissions(PermissionNames.ExpressionsRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = await requestSender.Send(new Requests.ListExpressionDescriptors(), cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
