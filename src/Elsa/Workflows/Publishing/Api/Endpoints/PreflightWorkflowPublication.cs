using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class PreflightWorkflowPublicationEndpoint(
    IWorkflowExecutableCompiler compiler,
    WorkflowPublicationPreflightReader preflightReader,
    TimeProvider timeProvider,
    ILogger<PreflightWorkflowPublicationEndpoint> logger)
    : ElsaEndpoint<PreflightWorkflowPublication, PublicationPreflightView>
{
    public override void Configure()
    {
        Post(RouteConstants.WorkflowPreflight);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(PreflightWorkflowPublication request, CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var executable = await compiler.CompileAsync(
                new WorkflowExecutableCompileRequest(
                    request.VersionId,
                    WorkflowExecutableReferenceScope.Published,
                    now,
                    now,
                    ExpiresAt: null,
                    "artifact-",
                    new Dictionary<string, string> { ["slice"] = "workflow-execution-vertical-slice" }),
                cancellationToken);
            var plan = await preflightReader.EvaluateAsync(
                executable,
                request.Action is { } action
                    ? new PublicationRequestIntent(PublicationIntentContract.ToModel(action), request.SlotName)
                    : request.SlotName is not null
                        ? new PublicationRequestIntent(PublicationAction.Replace, request.SlotName)
                        : null,
                request.ExpectedPublicationId,
                $"preflight:{request.VersionId}",
                cancellationToken);
            var resolved = plan.ResolvedAction;
            await Send.OkAsync(
                new PublicationPreflightView(
                    resolved.WorkflowDefinitionId,
                    resolved.WorkflowDefinitionVersionId,
                    resolved.SlotName,
                    PublicationContract.ToView(resolved.Action),
                    PublicationContract.ToView(resolved.PolicySource),
                    resolved.PolicyRevision,
                    plan.Result.CanActivate,
                    plan.Result.Changes,
                    plan.Result.Conflicts),
                cancellationToken);
        }
        catch (PublicationPolicyResolutionException exception)
        {
            ThrowError(exception.Message, exception.Code == "expected_publication_mismatch" ? 409 : 400);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception.Message, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected workflow publication preflight failure for version '{VersionId}'.", request.VersionId);
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
