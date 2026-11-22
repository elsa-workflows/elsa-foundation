using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Requests;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.PersistenceMode.Get;

[Get("runtime/workflows/persistence-mode")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("reading runtime persistence mode")]
public sealed class Endpoint(RuntimePersistenceModeInspector inspector) : ApiEndpoint<GetRuntimePersistenceMode, RuntimePersistenceModeProjection>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetRuntimePersistenceMode";

    public override Task<RuntimePersistenceModeProjection> HandleAsync(GetRuntimePersistenceMode request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(inspector.Resolve());
    }
}
