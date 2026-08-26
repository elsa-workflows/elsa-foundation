using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Diagnostics.Get;

[Get(RouteConstants.RuntimeDiagnosticsSettings)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IRuntimeDiagnosticsSettingsService settings) : ApiEndpoint<GetRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetDiagnostics";

    public override Task<RuntimeDiagnosticsSettingsView> HandleAsync(GetRuntimeDiagnosticsSettings request, CancellationToken cancellationToken) =>
        settings.GetAsync(request, cancellationToken);
}
