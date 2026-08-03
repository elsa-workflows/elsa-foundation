using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class PreflightWorkflowPublicationSnapshotEndpoint(
    IWorkflowExecutableCompiler compiler,
    WorkflowPublicationPreflightReader preflightReader,
    PublicationSnapshotReviewService snapshotReviews,
    TimeProvider timeProvider,
    ILogger<PreflightWorkflowPublicationSnapshotEndpoint> logger)
    : ElsaEndpoint<PreflightWorkflowPublicationSnapshot, PublicationSnapshotPreflightView>
{
    public override void Configure()
    {
        Post(RouteConstants.WorkflowSnapshotPreflight);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(
        PreflightWorkflowPublicationSnapshot request,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.DefinitionId);
            ArgumentNullException.ThrowIfNull(request.State);
            ArgumentNullException.ThrowIfNull(request.Layout);
            var candidateHash = snapshotReviews.ComputeCandidateHash(request.State, request.Layout);
            var snapshotId = $"snapshot:{candidateHash}";
            var now = timeProvider.GetUtcNow();
            var executable = await compiler.CompileAsync(
                new WorkflowExecutableCompileRequest(
                    snapshotId,
                    WorkflowExecutableReferenceScope.Published,
                    now,
                    PublishedAt: null,
                    ExpiresAt: null,
                    "artifact-",
                    new Dictionary<string, string> { ["slice"] = "workflow-publication-snapshot-preflight" })
                {
                    Source = new WorkflowExecutableCompileSource(
                        request.DefinitionId,
                        snapshotId,
                        "snapshot",
                        request.State,
                        "WorkflowDefinitionSnapshot",
                        snapshotId,
                        SourceVersion: null)
                },
                cancellationToken);
            var plan = await preflightReader.EvaluateAsync(
                executable,
                RequestIntent(request.Action, request.SlotName),
                request.ExpectedPublicationId,
                $"preflight:{candidateHash}",
                cancellationToken);
            var issued = await snapshotReviews.IssueAsync(
                candidateHash,
                plan,
                request.Action is { } action ? PublicationIntentContract.ToModel(action) : null,
                request.SlotName,
                request.ExpectedPublicationId,
                PublicationRequestTenant.Resolve(User),
                cancellationToken);
            var resolved = plan.ResolvedAction;
            await Send.OkAsync(
                new PublicationSnapshotPreflightView(
                    issued.PreflightToken,
                    issued.CandidateHash,
                    resolved.WorkflowDefinitionId,
                    VersionId: null,
                    resolved.SlotName,
                    PublicationContract.ToView(resolved.Action),
                    PublicationContract.ToView(resolved.PolicySource),
                    resolved.PolicyRevision,
                    plan.Result.CanActivate,
                    plan.CandidateClaims.Select(PublicationTriggerClaimView.From).ToArray(),
                    plan.Result.Changes.Select(PublicationTriggerChangeView.From).ToArray(),
                    plan.Result.Conflicts.Select(PublicationTriggerConflictView.From).ToArray()),
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
            logger.LogError(exception, "Unexpected publication snapshot preflight failure for definition '{DefinitionId}'.", request.DefinitionId);
            ThrowError("Unexpected error occurred", 500);
        }
    }

    private static PublicationRequestIntent? RequestIntent(PublicationActionView? action, string? slotName) =>
        action is { } requestedAction
            ? new PublicationRequestIntent(PublicationIntentContract.ToModel(requestedAction), slotName)
            : slotName is not null
                ? new PublicationRequestIntent(PublicationAction.Replace, slotName)
                : null;
}
