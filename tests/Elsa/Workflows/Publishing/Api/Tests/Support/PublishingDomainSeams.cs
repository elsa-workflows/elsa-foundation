using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Diagnostics;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// Deterministic capture seams for the domain services the endpoints now own their dispatch to.
/// Each fake reproduces exactly what <see cref="CaptureRequestSender"/> answered for the same
/// operation — the same canned payloads and the same scenario-keyed failures — so the frozen
/// evidence stays comparable while the endpoints run their real handling.
/// </summary>
public static class PublishingDomainSeams
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<CaptureActivityTestRunService>();
        services.AddSingleton<IActivityDraftTestRunService>(sp => sp.GetRequiredService<CaptureActivityTestRunService>());
        services.AddSingleton<CaptureWorkflowTestRunStarter>();
        services.AddSingleton<IWorkflowTestRunStarter>(sp => sp.GetRequiredService<CaptureWorkflowTestRunStarter>());
        services.AddSingleton<CaptureActivityPublisher>();
        services.AddSingleton<IActivityDefinitionPublisher>(sp => sp.GetRequiredService<CaptureActivityPublisher>());
        services.AddSingleton<CaptureRuntimeRequirementPreflight>();
        services.AddSingleton<IRuntimeRequirementPreflight>(sp => sp.GetRequiredService<CaptureRuntimeRequirementPreflight>());
        services.AddSingleton<CaptureSlotLifecycle>();
        services.AddSingleton<IPublicationSlotUnpublisher>(sp => sp.GetRequiredService<CaptureSlotLifecycle>());
        services.AddSingleton<IPublicationSlotRestorer>(sp => sp.GetRequiredService<CaptureSlotLifecycle>());
        services.AddSingleton<IActivityDefinitionVersionStore, CaptureActivityDefinitionVersionStore>();
        services.AddSingleton<IIncidentStrategyCatalog, CaptureIncidentStrategyCatalog>();
        services.AddSingleton<IValueConversionProfileRegistry, CaptureValueConversionProfileRegistry>();
    }
}

internal static class CaptureScenarios
{
    public static string Current(IHttpContextAccessor accessor) =>
        accessor.HttpContext?.Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString() ?? "";

    /// <summary>The scenario branches every capture seam shares, mirroring the former sender fake.</summary>
    public static void ThrowIfRequested(IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (Current(accessor))
        {
            case "trusted-cancellation":
                throw new OperationCanceledException("The deterministic capture request was canceled.", cancellationToken);
            case "trusted-domain-not-found":
                throw new EntityNotFoundException("The deterministic capture entity was not found.");
            case "trusted-generic-500":
                throw new InvalidOperationException("The deterministic unexpected failure was requested.");
        }
    }
}

public sealed class CaptureActivityTestRunService(IHttpContextAccessor accessor) : IActivityDraftTestRunService
{
    public StartActivityDraftTestRun? LastStart { get; private set; }
    public string? LastGetTestRunId { get; private set; }
    public (string DraftId, string IdempotencyKey)? LastByIdempotencyKey { get; private set; }
    public string? LastCancelTestRunId { get; private set; }
    public CancellationToken LastCancellation { get; private set; }
    public Exception? Failure { get; set; }

    public Task<ActivityDraftTestRunView> StartAsync(StartActivityDraftTestRun request, CancellationToken cancellationToken = default)
    {
        LastStart = request;
        return CannedAsync(cancellationToken);
    }

    public Task<ActivityDraftTestRunView> GetAsync(string testRunId, CancellationToken cancellationToken = default)
    {
        LastGetTestRunId = testRunId;
        return CannedAsync(cancellationToken);
    }

    public Task<ActivityDraftTestRunView> GetByIdempotencyKeyAsync(string draftId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        LastByIdempotencyKey = (draftId, idempotencyKey);
        return CannedAsync(cancellationToken);
    }

    public Task<ActivityDraftTestRunView> CancelAsync(string testRunId, CancellationToken cancellationToken = default)
    {
        LastCancelTestRunId = testRunId;
        return CannedAsync(cancellationToken);
    }

    private Task<ActivityDraftTestRunView> CannedAsync(CancellationToken cancellationToken)
    {
        LastCancellation = cancellationToken;
        if (Failure is not null)
            throw Failure;
        CaptureScenarios.ThrowIfRequested(accessor, cancellationToken);
        return Task.FromResult((ActivityDraftTestRunView)CaptureResponseFactory.Create(typeof(ActivityDraftTestRunView)));
    }
}

public sealed class CaptureWorkflowTestRunStarter(IHttpContextAccessor accessor) : IWorkflowTestRunStarter
{
    public StartWorkflowTestRun? LastStart { get; set; }
    public StartWorkflowDraftTestRun? LastStartDraft { get; set; }

    public Task<WorkflowTestRunView> StartAsync(StartWorkflowTestRun request, CancellationToken cancellationToken)
    {
        LastStart = request;
        return CannedAsync(cancellationToken);
    }

    public Task<WorkflowTestRunView> StartDraftAsync(StartWorkflowDraftTestRun request, CancellationToken cancellationToken)
    {
        LastStartDraft = request;
        return CannedAsync(cancellationToken);
    }

    private Task<WorkflowTestRunView> CannedAsync(CancellationToken cancellationToken)
    {
        CaptureScenarios.ThrowIfRequested(accessor, cancellationToken);
        return Task.FromResult((WorkflowTestRunView)CaptureResponseFactory.Create(typeof(WorkflowTestRunView)));
    }
}

public sealed class CaptureActivityPublisher(IHttpContextAccessor accessor) : IActivityDefinitionPublisher
{
    public PublishActivityDefinitionRequest? LastPublish { get; private set; }
    public PreflightActivityDefinitionPublicationRequest? LastPreflight { get; private set; }
    public ActivityPublicationReceipt? Receipt { get; set; }
    public Exception? Failure { get; set; }
    public string? LastReceiptKey { get; private set; }

    public Task<ActivityPublicationPreflightView> PreflightAsync(
        PreflightActivityDefinitionPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastPreflight = request;
        ThrowScenarioIfRequested(cancellationToken);
        return Task.FromResult((ActivityPublicationPreflightView)CaptureResponseFactory.Create(typeof(ActivityPublicationPreflightView)));
    }

    public Task<ActivityPublicationReceipt> PublishReviewedAsync(
        PublishActivityDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        LastPublish = request;
        ThrowScenarioIfRequested(cancellationToken);
        return Task.FromResult(CannedReceipt());
    }

    public ValueTask<ActivityPublicationReceipt> GetReceiptAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        LastReceiptKey = idempotencyKey;
        ThrowScenarioIfRequested(cancellationToken);
        return ValueTask.FromResult(CannedReceipt());
    }

    private ActivityPublicationReceipt CannedReceipt() =>
        Receipt ?? (ActivityPublicationReceipt)CaptureResponseFactory.Create(typeof(ActivityPublicationReceipt));

    private void ThrowScenarioIfRequested(CancellationToken cancellationToken)
    {
        if (Failure is not null)
            throw Failure;
        switch (CaptureScenarios.Current(accessor))
        {
            case "trusted-domain-conflict":
                throw new ActivityPublicationRejectedException(
                    "activity.draft.stale-revision",
                    "The deterministic activity publication conflict was requested.",
                    [],
                    isConflict: true);
            case "trusted-unprocessable":
                throw new ActivityPublicationRejectedException(
                    "activity.publication.invalid",
                    "The deterministic activity validation failure was requested.",
                    [],
                    isConflict: false);
            case "trusted-activity-diagnostics":
                var subject = new ActivityDiagnosticSubject("ActivityDraft", "draft-route", "definition-route", Revision: 8);
                throw new ActivityPublicationRejectedException(
                    "activity.publication.invalid",
                    "Activity publication validation failed.",
                    [new ActivityDiagnostic("activity.contract.invalid", ActivityDiagnosticSeverity.Error, "The activity contract is invalid.", subject)]);
        }

        CaptureScenarios.ThrowIfRequested(accessor, cancellationToken);
    }
}

public sealed class CaptureRuntimeRequirementPreflight(IHttpContextAccessor accessor) : IRuntimeRequirementPreflight
{
    public Exception? Failure { get; set; }

    public ValueTask<RuntimeRequirementPreflightView> RunAsync(
        string scope,
        IReadOnlyList<string>? artifactIds,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
            throw Failure;
        if (CaptureScenarios.Current(accessor) == "trusted-domain-unavailable")
            throw new RuntimeRequirementPreflightRequestException("The deterministic runtime requirement provider is unavailable.");
        CaptureScenarios.ThrowIfRequested(accessor, cancellationToken);
        return ValueTask.FromResult((RuntimeRequirementPreflightView)CaptureResponseFactory.Create(typeof(RuntimeRequirementPreflightView)));
    }
}

public sealed class CaptureSlotLifecycle(IHttpContextAccessor accessor) : IPublicationSlotUnpublisher, IPublicationSlotRestorer
{
    public Task<PublicationSlot> UnpublishAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken) =>
        CannedAsync(cancellationToken);

    public Task<PublicationSlot> RestoreAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken) =>
        CannedAsync(cancellationToken);

    private Task<PublicationSlot> CannedAsync(CancellationToken cancellationToken)
    {
        CaptureScenarios.ThrowIfRequested(accessor, cancellationToken);
        return Task.FromResult((PublicationSlot)CaptureResponseFactory.Create(typeof(PublicationSlot)));
    }
}

public sealed class CaptureActivityDefinitionVersionStore : IActivityDefinitionVersionStore
{
    private static ActivityDefinitionVersion Canned()
    {
        using var document = JsonDocument.Parse("{}");
        // The ctor validates SemVer; neither ctor argument surfaces in the constructed view.
        return new ActivityDefinitionVersion("1.0.0", "capture")
        {
            Id = "capture",
            DescriptorType = "capture",
            DescriptorPayload = document.RootElement.Clone()
        };
    }

    public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Canned());

    public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Canned());

    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActivityDefinitionVersion?>(null);

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
}

public sealed class CaptureIncidentStrategyCatalog : IIncidentStrategyCatalog
{
    public IReadOnlyCollection<IncidentStrategyDescriptor> List() => [];
    public IncidentStrategyReference DefaultStrategy => new("capture", "capture");
    public bool TryGet(IncidentStrategyReference reference, out IncidentStrategyDescriptor descriptor)
    {
        descriptor = null!;
        return false;
    }
}

public sealed class CaptureValueConversionProfileRegistry : IValueConversionProfileRegistry
{
    public bool TryGet(ValueConversionProfileReference profile, out ValueConversionProfileDefinition definition)
    {
        definition = null!;
        return false;
    }

    public IReadOnlyCollection<ValueConversionProfileDefinition> List() => [];
}
