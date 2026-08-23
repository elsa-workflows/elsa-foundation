using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.List;

[Get("/design/activities/definitions")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListReusableActivityDefinitions, ActivityManagementPageView<ReusableActivityDefinitionManagementView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsList";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ActivityManagementPageView<ReusableActivityDefinitionManagementView>> HandleAsync(ListReusableActivityDefinitions request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
