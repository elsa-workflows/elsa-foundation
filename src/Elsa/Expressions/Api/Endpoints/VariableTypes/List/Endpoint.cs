using Elsa.Api.AspNetCore;
using Elsa.Expressions.Api.Authorization;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.Api.Endpoints.VariableTypes.List;

[Get("expressions/variable-types")]
[RequirePermission(ExpressionsPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpointWithoutRequest<VariableTypeDescriptorsResponse>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListVariableTypeDescriptors";

    public override Task<VariableTypeDescriptorsResponse> HandleAsync(CancellationToken cancellationToken) =>
        sender.Send(new ListVariableTypeDescriptors(), cancellationToken);
}
