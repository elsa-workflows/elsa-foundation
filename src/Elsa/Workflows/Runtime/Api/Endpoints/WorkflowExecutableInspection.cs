using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class ListWorkflowExecutablesEndpoint(IRequestSender requestSender, ILogger<ListWorkflowExecutablesEndpoint> logger)
    : ElsaRequestHandlerEndpoint<ListWorkflowExecutables, WorkflowExecutablesListView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Executables);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }
}

internal sealed class GetWorkflowExecutableEndpoint(IRequestSender requestSender, ILogger<GetWorkflowExecutableEndpoint> logger)
    : ElsaRequestHandlerEndpoint<GetWorkflowExecutable, WorkflowExecutableDetailsView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Executable);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }
}

internal sealed class GetWorkflowExecutableInputSourcesEndpoint(IRequestSender requestSender, ILogger<GetWorkflowExecutableInputSourcesEndpoint> logger)
    : ElsaRequestHandlerEndpoint<GetWorkflowExecutableInputSources, WorkflowExecutableInputSourcesView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.ExecutableInputSources);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }
}

internal sealed class GetWorkflowExecutableProvenanceEndpoint(IRequestSender requestSender, ILogger<GetWorkflowExecutableProvenanceEndpoint> logger)
    : ElsaRequestHandlerEndpoint<GetWorkflowExecutableProvenance, ExecutableProvenanceView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.ExecutableProvenance);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }
}
