using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Api.Constants;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.Api.Endpoints;

internal sealed class ListVariableTypeDescriptors(IRequestSender requestSender)
    : ElsaEndpointWithoutRequest<VariableTypeDescriptorsResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.VariableTypes);
        ConfigurePermissions(PermissionNames.ExpressionsRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = await requestSender.Send(new Requests.ListVariableTypeDescriptors(), cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
