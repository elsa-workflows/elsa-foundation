using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Versions.Retire;

[Post("/design/activities/versions/{versionId}/retire")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(ICommandSender sender) : ApiEndpoint<RetireReusableActivityVersion, ReusableActivityVersionLifecycleView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsRetire";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ReusableActivityVersionLifecycleView> HandleAsync(RetireReusableActivityVersion command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
