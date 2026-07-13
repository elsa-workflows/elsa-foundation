using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Api.Constants;
using Elsa.Expressions.Api.Models;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Expressions.Api.Endpoints;

internal sealed class ListVariableTypeDescriptors(IRequestSender requestSender, ILogger<ListVariableTypeDescriptors> logger)
    : ElsaRequestHandlerEndpoint<Requests.ListVariableTypeDescriptors, VariableTypeDescriptorsResponse>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.VariableTypes);
        ConfigurePermissions(PermissionNames.ExpressionsRead);
    }
}
