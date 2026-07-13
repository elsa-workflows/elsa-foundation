using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Api.Constants;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Expressions.Api.Endpoints;

internal sealed class ListExpressionDescriptors(IRequestSender requestSender, ILogger<ListExpressionDescriptors> logger)
    : ElsaRequestHandlerEndpoint<Requests.ListExpressionDescriptors, ExpressionDescriptorsResponse>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Descriptors);
        ConfigurePermissions(PermissionNames.ExpressionsRead);
    }
}
