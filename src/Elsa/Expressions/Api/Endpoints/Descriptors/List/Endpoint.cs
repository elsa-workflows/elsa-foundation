using Elsa.Api.AspNetCore;
using Elsa.Expressions.Api.Authorization;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using NativeEndpoints;

namespace Elsa.Expressions.Api.Endpoints.Descriptors.List;

[Get("expressions/descriptors")]
[RequirePermission(ExpressionsPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpointWithoutRequest<ExpressionDescriptorsResponse>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListExpressionDescriptors";

    public override Task<ExpressionDescriptorsResponse> HandleAsync(CancellationToken cancellationToken) =>
        sender.Send(new ListExpressionDescriptors(), cancellationToken);
}
