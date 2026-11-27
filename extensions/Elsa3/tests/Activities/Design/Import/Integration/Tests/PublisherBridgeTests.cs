using System.Net;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Publishing.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 246 T030: how the <c>Elsa3ImportActivitiesPublishing</c> bridge maps the activity publisher's answers onto the
/// import's publish port, and what an apply does in a host that does not compose it.
/// </summary>
public sealed class PublisherBridgeTests
{
    private static readonly ReusableActivityImportPublishRequest Request = new(
        "draft-1", 3, "head-1", "1.0.0", "elsa3-activity-version-1", "elsa3-import:receipt-1:elsa3-activity-version-1", "tenant-a");

    [Fact]
    public async Task A_publisher_that_resolves_another_tenant_refuses_without_publishing()
    {
        var publisher = new FakeActivityPublisher();

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-b")).PublishAsync(Request);

        Assert.False(outcome.Published);
        Assert.Equal("activity.tenant.reference-denied", outcome.ErrorCode);
        Assert.Equal(0, publisher.PreflightCalls);
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public async Task A_preflight_that_is_not_publishable_refuses_with_its_diagnostics()
    {
        var diagnostic = Diagnostic("activity.graph.invalid", ActivityDiagnosticSeverity.Error);
        var publisher = new FakeActivityPublisher { Preflight = Preflight(isPublishable: false, Diagnostic("activity.graph.note", ActivityDiagnosticSeverity.Warning), diagnostic) };

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-a")).PublishAsync(Request);

        Assert.False(outcome.Published);
        Assert.Equal("activity.graph.invalid", outcome.ErrorCode);
        Assert.Contains(diagnostic, outcome.Diagnostics);
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public async Task A_rejected_publish_refuses_with_its_code_and_diagnostics()
    {
        var diagnostic = Diagnostic("activity.publication.version-exists", ActivityDiagnosticSeverity.Error);
        var publisher = new FakeActivityPublisher
        {
            Preflight = Preflight(isPublishable: true),
            Rejection = new ActivityPublicationRejectedException("activity.publication.version-exists", "Rejected.", [diagnostic])
        };

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-a")).PublishAsync(Request);

        Assert.False(outcome.Published);
        Assert.Equal("activity.publication.version-exists", outcome.ErrorCode);
        Assert.Equal([diagnostic], outcome.Diagnostics);
    }

    [Fact]
    public async Task A_publish_rejected_as_invalid_refuses_with_the_code_of_its_first_error()
    {
        // The publisher rejects a version below the head as "publication invalid" and says why in a diagnostic.
        var bump = Diagnostic("activity.version.bump-insufficient", ActivityDiagnosticSeverity.Error);
        var publisher = new FakeActivityPublisher
        {
            Preflight = Preflight(isPublishable: true),
            Rejection = new ActivityPublicationRejectedException("activity.publication.invalid", "Below the minimum.", [bump])
        };

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-a")).PublishAsync(Request);

        Assert.Equal("activity.version.bump-insufficient", outcome.ErrorCode);
        Assert.Equal([bump], outcome.Diagnostics);
    }

    [Fact]
    public async Task A_publish_whose_data_selection_is_rejected_refuses_with_each_finding()
    {
        // #5850: the graph provider rejects a malformed manifest with located findings; the import reports them instead
        // of "failed unexpectedly", however deep the publisher wrapped the rejection.
        var rejection = new ActivityDataSelectionRejectedException(
        [
            new("/variables/0/initialValue", "ActivityDataSelection/activity.graph.variable.initial-value-invalid", "'initialValue' must be an expression object with a syntax and value."),
            new("write/inputs/text", "Expressions/UnresolvedVariable", "Variable 'missing' is not in scope.")
        ]);
        var publisher = new FakeActivityPublisher { PreflightFailure = new InvalidOperationException("Compiling failed.", rejection) };

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-a")).PublishAsync(Request);

        Assert.False(outcome.Published);
        Assert.Equal("ActivityDataSelection/activity.graph.variable.initial-value-invalid", outcome.ErrorCode);
        Assert.Equal(2, outcome.Diagnostics.Count);
        var variable = outcome.Diagnostics[0];
        Assert.Equal(("ActivityDataSelection/activity.graph.variable.initial-value-invalid", ActivityDiagnosticSeverity.Error), (variable.Code, variable.Severity));
        Assert.Equal("'initialValue' must be an expression object with a syntax and value.", variable.Message);
        Assert.Equal(("ActivityDraft", "draft-1", (long?)3), (variable.Subject.Kind, variable.Subject.Id, variable.Subject.Revision));
        Assert.Equal("/variables/0/initialValue", variable.Location!.JsonPointer);
        Assert.Equal("/variables/0/initialValue", variable.Metadata["authoredPath"]);
        Assert.False(string.IsNullOrWhiteSpace(variable.Remediation));
        var input = outcome.Diagnostics[1];
        Assert.Equal(("text", "write"), (input.Location!.ReferenceKey, Assert.Single(input.Location.NodeOrigin!).Id));
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public async Task A_publish_whose_schema_write_is_refused_refuses_with_the_family_and_both_versions()
    {
        // #5972: the store refuses a write that needs a later schema version than this host may write. The import reports
        // what Studio's publish endpoint reports for it, however deep the publisher wrapped it and wherever an aggregate
        // holds it, not "failed unexpectedly".
        var refusal = new StoreRefusal("activity-definitions", "3", "4");
        var publisher = new FakeActivityPublisher
        {
            Preflight = Preflight(isPublishable: true),
            PublishFailure = new InvalidOperationException("Saving failed.", new AggregateException(new TimeoutException("Unrelated."), refusal))
        };

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-a")).PublishAsync(Request);

        Assert.False(outcome.Published);
        Assert.Equal("schema-write-refused", outcome.ErrorCode);
        var diagnostic = Assert.Single(outcome.Diagnostics);
        Assert.Equal(("schema-write-refused", ActivityDiagnosticSeverity.Error), (diagnostic.Code, diagnostic.Severity));
        Assert.Equal(refusal.Message, diagnostic.Message);
        Assert.Equal(("ActivityDraft", "draft-1", (long?)3), (diagnostic.Subject.Kind, diagnostic.Subject.Id, diagnostic.Subject.Revision));
        Assert.Equal(
            ["family=activity-definitions", "requiredVersion=4", "writeVersion=3"],
            diagnostic.Metadata.Select(entry => $"{entry.Key}={entry.Value}").Order(StringComparer.Ordinal));
        Assert.Contains("'activity-definitions'", diagnostic.Remediation);
        Assert.Contains("new idempotency key", diagnostic.Remediation);
        Assert.Equal(1, publisher.PublishCalls);
    }

    [Fact]
    public async Task A_dormant_schema_refuses_with_its_own_code_feature_and_reason()
    {
        // A dormancy refusal is a schema write refusal with its own stable code; the preflight raises it unwrapped.
        var refusal = new SchemaDormancyRefusedException(
            "activity-definitions", "3", "4", "ActivityGraphs", "Version '4' is not finalized yet.");
        var publisher = new FakeActivityPublisher { PreflightFailure = refusal };

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-a")).PublishAsync(Request);

        Assert.False(outcome.Published);
        Assert.Equal("schema-version-not-finalized", outcome.ErrorCode);
        var diagnostic = Assert.Single(outcome.Diagnostics);
        Assert.Equal("schema-version-not-finalized", diagnostic.Code);
        Assert.Equal(refusal.Message, diagnostic.Message);
        Assert.Equal(
            ["family=activity-definitions", "feature=ActivityGraphs", "reason=Version '4' is not finalized yet.", "requiredVersion=4", "writeVersion=3"],
            diagnostic.Metadata.Select(entry => $"{entry.Key}={entry.Value}").Order(StringComparer.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Remediation));
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public void Imp_publishing_permission_constant_equals_WorkflowPublishingPermissions_Manage()
    {
        // The import duplicates the constant rather than reference Publishing; this pins the copy.
        var permissions = typeof(Elsa3ImportActivitiesFeature).Assembly.GetType("Elsa3.Activities.Design.Import.Endpoints.Elsa3ImportPermissions", throwOnError: true)!;
        var constant = permissions.GetField("PublishingManage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;

        Assert.Equal(Elsa.Workflows.Publishing.Api.Authorization.WorkflowPublishingPermissions.Manage, constant.GetRawConstantValue());
    }

    [Fact]
    public async Task An_applied_publish_names_the_plan_version_id_review_token_and_idempotency_key()
    {
        var publisher = new FakeActivityPublisher { Preflight = Preflight(isPublishable: true) };

        var outcome = await new PublisherReusableActivityImportPublisher(publisher, new Tenant("tenant-a")).PublishAsync(Request);

        Assert.True(outcome.Published);
        var published = Assert.IsType<PublishActivityDefinitionRequest>(publisher.LastPublish);
        Assert.Equal(
            ("draft-1", 3L, "head-1", "1.0.0", "review-token", Request.IdempotencyKey, "elsa3-activity-version-1"),
            (published.DraftId, published.ExpectedDraftRevision, published.ExpectedDefinitionHeadVersionId, published.Version,
                published.ReviewToken, published.IdempotencyKey, published.RequestedVersionId));
    }

    [Fact]
    public async Task The_feature_registers_exactly_one_publish_port()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var ports = host.Shell.ServiceProvider.CreateScope().ServiceProvider.GetServices<IReusableActivityImportPublisher>().ToArray();

        Assert.IsType<PublisherReusableActivityImportPublisher>(Assert.Single(ports));
    }

    [Fact]
    public async Task Without_the_feature_an_apply_with_a_reusable_is_refused_and_writes_nothing()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withPublishing: false);
        var handle = await host.UploadCollectionAsync("reusable-two-outcomes");
        var (planId, items) = await host.AnalyzeAsync(handle);
        var before = await host.CountDesignRowsAsync();

        var apply = await host.ApplyAsync(handle, planId, "without-publishing", Elsa3ImportHost.Required(Assert.Single(items), "sourceVersionId"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, apply.StatusCode);
        Assert.Contains(ReusableActivityImportDiagnosticCodes.PublishingUnavailable, apply.DiagnosticCodes);
        Assert.Equal(before, await host.CountDesignRowsAsync());
        var versionId = Elsa3ImportHost.Required(items[0], "activityDefinitionVersionId");
        Assert.Null(await host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionVersionPublicationStore>().FindAsync(versionId)));
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetImportStatusAsync("without-publishing")).StatusCode);
    }

    private static ActivityDiagnostic Diagnostic(string code, ActivityDiagnosticSeverity severity) =>
        new(code, severity, code, new ActivityDiagnosticSubject("ActivityDraft", "draft-1"));

    private static ActivityPublicationPreflightView Preflight(bool isPublishable, params ActivityDiagnostic[] diagnostics) =>
        new("draft-1", 3, "definition-1", "head-1", true, "review-token", isPublishable, "1.0.0", ["1.0.0"], null, [], [],
            new ActivityPublicationCapabilityReadinessView("provider", "elsa.graph", "2", "Ready", ["2"]), [], [], diagnostics);

    /// <summary>A store-level schema write refusal, as a persistence provider raises it.</summary>
    private sealed class StoreRefusal(string family, string writeVersion, string requiredVersion)
        : SchemaWriteRefusedException(family, writeVersion, requiredVersion,
            $"Writing to schema family '{family}' needs version '{requiredVersion}'; this host may write only '{writeVersion}'.");

    private sealed class Tenant(string? tenantId) : IActivityPublishingAuthorizationContext
    {
        public string? TenantId => tenantId;
        public bool CanAccessTenant(string? candidate) => candidate is null || candidate == tenantId;
    }

    /// <summary>The activity publisher, answering with the preflight and rejection a test sets, and recording its calls.</summary>
    private sealed class FakeActivityPublisher : IActivityDefinitionPublisher
    {
        public ActivityPublicationPreflightView? Preflight { get; init; }
        public ActivityPublicationRejectedException? Rejection { get; init; }
        public Exception? PreflightFailure { get; init; }
        public Exception? PublishFailure { get; init; }
        public int PreflightCalls { get; private set; }
        public int PublishCalls { get; private set; }
        public PublishActivityDefinitionRequest? LastPublish { get; private set; }

        public Task<ActivityPublicationPreflightView> PreflightAsync(PreflightActivityDefinitionPublicationRequest request, CancellationToken cancellationToken = default)
        {
            PreflightCalls++;
            if (PreflightFailure is not null)
                throw PreflightFailure;
            return Task.FromResult(Preflight ?? throw new InvalidOperationException("No preflight was set."));
        }

        public Task<ActivityPublicationReceipt> PublishReviewedAsync(PublishActivityDefinitionRequest request, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            LastPublish = request;
            if (Rejection is not null)
                throw Rejection;
            if (PublishFailure is not null)
                throw PublishFailure;
            return Task.FromResult(new ActivityPublicationReceipt(
                "tenant-a", request.IdempotencyKey, "fingerprint", ActivityPublicationReceiptStatus.Applied, request.DraftId,
                request.ExpectedDraftRevision, request.ExpectedDefinitionHeadVersionId, request.ReviewToken, request.Version,
                null, null, [], DateTimeOffset.UnixEpoch));
        }

        public ValueTask<ActivityPublicationReceipt> GetReceiptAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
