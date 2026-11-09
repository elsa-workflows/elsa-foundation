using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints;

/// <summary>
/// Renders the Design owner's coded failures in the established design error shape with an additive
/// <c>errorCode</c>, ahead of <see cref="WorkflowDesignExceptionTranslator"/>, whose problems carry no code.
/// </summary>
/// <remarks>
/// An owning authorizer that cannot reach a decision raises <see cref="WorkflowDefinitionAuthorizationException"/>.
/// It is answered as unavailable (503 with <see cref="WorkflowDefinitionAuthorizationException.UnavailableCode"/>),
/// fail-closed and distinct from the denied 403, on every Design endpoint whichever authorizer raised it. The body
/// carries only the exception's fixed message; the inner cause is logged and never written.
/// </remarks>
internal sealed class WorkflowDesignFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        if (exception is not WorkflowDefinitionAuthorizationException unavailable)
            return false;

        context.RequestServices.GetService<ILoggerFactory>()?
            .CreateLogger(WorkflowsDesignApi.OwnerId)
            .LogWarning(unavailable, "Workflow definition authorization for the {Operation} operation could not be completed.", unavailable.Operation);
        await WorkflowDesignProblemWriter.WriteAsync(context,
            EndpointProblem.General(StatusCodes.Status503ServiceUnavailable, unavailable.Message),
            WorkflowDefinitionAuthorizationException.UnavailableCode);
        return true;
    }
}
