using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public class ActivityExecutionInspectionProjectionTests
{
    [Fact]
    public void Detail_Projects_Attempt_Boundary_Aggregate_And_Explicit_Value_Redaction()
    {
        var baseRoot = Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true);
        var executionMetadata = baseRoot.Metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        executionMetadata["secret"] = "protected-execution-metadata";
        var root = baseRoot with
        {
            Attempt = new(2, "outer-first", "outer-first"),
            ValueSnapshots =
            [
                new("secret", ActivityExecutionInspectionValueSubject.ActivityOutput, RuntimePayloadCaptureMode.Payload,
                    null, DateTimeOffset.UnixEpoch, JsonSerializer.SerializeToElement("protected"), "captured", true,
                    new Dictionary<string, string> { ["secret"] = "protected-value-metadata" })
            ],
            Bookmarks =
            [
                new(
                    new("bookmark", "wf", "outer", "resume"),
                    "test",
                    "hash",
                    DateTimeOffset.UnixEpoch,
                    null)
            ],
            Incidents =
            [
                new("incident", IncidentSeverity.Error, IncidentStatus.Blocking, null,
                    "TestFailure", "protected-incident", DateTimeOffset.UnixEpoch, null, true,
                    new Dictionary<string, string> { ["secret"] = "protected-incident-metadata" },
                    new("causal-incident", "child", "node-child", "graph-descendant-fault"))
            ],
            Metadata = executionMetadata,
            Provenance = baseRoot.Provenance with
            {
                Metadata = new Dictionary<string, string> { ["secret"] = "protected-provenance-metadata" }
            }
        };
        var child = Projection("child", "outer", "outer", 2, ActivityExecutionStatus.Completed);
        var records = new[]
        {
            ActivityExecutionHierarchyProjector.FromInspection(root),
            ActivityExecutionHierarchyProjector.FromInspection(child)
        };

        var boundary = ActivityExecutionHierarchyProjector.FindBoundary(records, "outer");
        var view = ActivityExecutionInspectionView.From(root, boundary, canInspectSensitiveValues: false);

        Assert.Equal(2, view.Attempt!.AttemptNumber);
        Assert.Equal("activity-ver", view.Boundary!.DefinitionVersionId);
        Assert.Equal("Completed", view.Boundary.Aggregate.Status);
        Assert.Equal(1, view.Boundary.Aggregate.Total);
        var value = Assert.Single(view.ValueSnapshots);
        Assert.Equal("payloadCaptured", value.CaptureState);
        Assert.Equal("redacted", value.AccessState);
        Assert.Null(value.Payload);
        Assert.Null(value.Snapshot);
        Assert.Empty(value.Metadata);
        var bookmark = Assert.Single(view.Bookmarks);
        Assert.Equal("bookmark", bookmark.Identity.BookmarkId);
        Assert.Equal("resume", bookmark.Identity.ResumeTargetId);
        var incident = Assert.Single(view.Incidents);
        Assert.Equal("Incident details are redacted.", incident.Message);
        Assert.Equal("causal-incident", incident.Causation!.IncidentId);
        Assert.Equal("child", incident.Causation.ActivityExecutionId);
        Assert.Empty(incident.Metadata);
        Assert.Empty(view.Metadata);
        Assert.Empty(view.Provenance.Metadata);
    }

    [Fact]
    public void Detail_keeps_capture_truth_separate_from_payload_resolution_authorization()
    {
        var projection = Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true) with
        {
            ValueSnapshots =
            [
                new("result", ActivityExecutionInspectionValueSubject.ActivityOutput, RuntimePayloadCaptureMode.Payload,
                    null, DateTimeOffset.UnixEpoch, JsonSerializer.SerializeToElement(42), "captured", false,
                    new Dictionary<string, string>(), EvidenceId: "value-1")
            ]
        };

        var view = ActivityExecutionInspectionView.From(
            projection,
            canInspectSensitiveValues: true,
            canResolveValuePayloads: true);

        var evidence = Assert.Single(view.ValueSnapshots);
        Assert.Equal("payloadCaptured", evidence.CaptureState);
        Assert.Equal("resolutionAvailable", evidence.AccessState);
        Assert.Null(evidence.Payload);
        Assert.Null(evidence.Snapshot);
        Assert.Equal("value-1", evidence.EvidenceId);
    }

    [Fact]
    public void Failed_capture_takes_precedence_over_the_policy_capture_mode()
    {
        var snapshot = new ActivityExecutionInspectionValueSnapshot(
            "secret",
            ActivityExecutionInspectionValueSubject.ActivityInput,
            RuntimePayloadCaptureMode.None,
            null,
            DateTimeOffset.UnixEpoch,
            null,
            "sensitive",
            true,
            new Dictionary<string, string>(),
            Failure: new("InputEvaluationFailed", "Safe failure"));

        var view = ActivityExecutionInspectionValueSnapshotView.From(snapshot);

        Assert.Equal("captureFailed", view.CaptureState);
        Assert.Equal("unavailable", view.AccessState);
    }

    [Fact]
    public async Task Detail_Applies_Structure_Authorization_Before_Existence_Disclosure()
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        var inspections = new InMemoryActivityExecutionInspectionStore();
        await workflows.SaveAsync(new(
            "wf", new("artifact", "def", "ver", "1", "hash"), WorkflowExecutionStatus.Running, null,
            DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant-a", new Dictionary<string, string>()));
        await inspections.SaveAsync(Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true));
        var handler = new GetActivityExecutionRequestHandler(workflows, inspections, authorization: new DenyStructureAuthorization());

        var response = await handler.Handle(new GetActivityExecution("wf", "outer"), default);

        Assert.Null(response.ActivityExecution);
    }

    [Fact]
    public async Task Http_Inspection_Authorization_Is_Tenant_Scoped_And_Separates_Structure_From_Values()
    {
        var context = new DefaultHttpContext
        {
            User = Principal("tenant-a", HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission)
        };
        using var provider = IdentityProvider();
        var authorization = new HttpContextActivityExecutionInspectionAuthorizationContext(new HttpContextAccessor { HttpContext = context }, provider.GetRequiredService<IPermissionAuthorizationService>());
        var sameTenant = new WorkflowExecutionState(
            "wf", new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running, null,
            DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant-a", new Dictionary<string, string>());
        var otherTenant = sameTenant with { TenantId = "tenant-b" };

        Assert.True(await authorization.CanInspectStructureAsync(sameTenant));
        Assert.False(await authorization.CanInspectSensitiveValuesAsync(sameTenant));
        Assert.False(await authorization.CanInspectStructureAsync(otherTenant));

        var structureOnlyProfile = await authorization.GetAuthorizationProfileAsync();
        context.User = Principal("tenant-a",
            HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission,
            HttpContextActivityExecutionInspectionAuthorizationContext.SensitiveValuesPermission);
        Assert.False(await authorization.CanInspectSensitiveValuesAsync(sameTenant));
        Assert.Equal(structureOnlyProfile, await authorization.GetAuthorizationProfileAsync());

        var replacement = new HttpContextActivityExecutionInspectionAuthorizationContext(new HttpContextAccessor { HttpContext = context }, provider.GetRequiredService<IPermissionAuthorizationService>());
        Assert.True(await replacement.CanInspectSensitiveValuesAsync(sameTenant));
        Assert.NotEqual(structureOnlyProfile, await replacement.GetAuthorizationProfileAsync());
    }

    [Fact]
    public void Http_payload_audit_subject_requires_a_stable_subject_identifier()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "mutable-display-name")
            ], "test"))
        };
        using var provider = IdentityProvider();
        var authorization = new HttpContextActivityExecutionInspectionAuthorizationContext(
            new HttpContextAccessor { HttpContext = context }, provider.GetRequiredService<IPermissionAuthorizationService>());

        Assert.Empty(authorization.AuditSubject);

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "stable-subject-1"),
            new Claim(ClaimTypes.Name, "mutable-display-name")
        ], "test"));
        authorization = new HttpContextActivityExecutionInspectionAuthorizationContext(
            new HttpContextAccessor { HttpContext = context }, provider.GetRequiredService<IPermissionAuthorizationService>());
        Assert.Equal("stable-subject-1", authorization.AuditSubject);

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, " "),
            new Claim("sub", "oidc-subject-1"),
            new Claim(ClaimTypes.Name, "mutable-display-name")
        ], "test"));
        authorization = new HttpContextActivityExecutionInspectionAuthorizationContext(
            new HttpContextAccessor { HttpContext = context }, provider.GetRequiredService<IPermissionAuthorizationService>());
        Assert.Equal("oidc-subject-1", authorization.AuditSubject);
    }

    private static ClaimsPrincipal Principal(string tenantId, params string[] permissions) =>
        new(new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, tenantId),
            ..permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
        ], "test"));

    private static ServiceProvider IdentityProvider()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        return services.BuildServiceProvider();
    }

    internal static ActivityExecutionInspectionProjection Projection(
        string id,
        string scopeId,
        string? parentId,
        long sequence,
        ActivityExecutionStatus status,
        bool boundary = false)
    {
        var metadata = new Dictionary<string, string>();
        if (boundary)
        {
            metadata["activity.definitionId"] = "activity-def";
            metadata["activity.definitionVersionId"] = "activity-ver";
            metadata["activity.version"] = "1.0.0";
            metadata["activity.templateHash"] = "sha256-template";
            metadata["activity.sourceReferenceId"] = "source-ref";
            metadata["activity.invocationOrigin"] = JsonSerializer.Serialize(new[]
            {
                new ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow-ver"),
                new ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind.AuthoredNode, "node-boundary")
            });
        }

        return new(
            id,
            "wf",
            $"node-{id}",
            $"authored-{id}",
            boundary ? "elsa.graph-activity" : "elsa.test",
            "1",
            status,
            null,
            sequence,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            status is ActivityExecutionStatus.Completed ? DateTimeOffset.UnixEpoch.AddSeconds(sequence + 1) : null,
            "checkpoint-first",
            "checkpoint-last",
            DateTimeOffset.UnixEpoch.AddSeconds(sequence + 1),
            ActivitySchedulingProvenance.From("wf", parentId, parentId, null, null, null, scopeId, "test"),
            ["Done"],
            [],
            [],
            [],
            metadata,
            scopeId,
            null);
    }

    private sealed class DenyStructureAuthorization : IActivityExecutionInspectionAuthorizationContextAsync
    {
        public string TenantScope => "tenant:b";
        public string AuditSubject => "test";
        public string RequestCorrelationId => "test-request";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("denied");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
}
