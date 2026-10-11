using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Tests.Api.Support;

/// <summary>
/// Scenario fakes at the operation seams the endpoints dispatch to since the mediator wrappers
/// retired. One method per route, all funneled through two hooks that mirror the request/command
/// split of the sender fakes they replace, returning the same
/// <see cref="CaptureResponseFactory"/> payloads so the frozen corpus replays byte-identically.
/// </summary>
internal abstract class ActivitiesDesignDomainSeamsBase :
    IReusableActivityAuthoringService,
    IActivityDefinitionManagementProjectionService,
    IActivityDefinitionRecommendationService,
    IActivityVersionLifecycleService,
    IActivityVersionDiffService,
    IActivityForkService,
    IActivityContractProposalService,
    IActivityDependencyReader,
    IActivityUpgradeOperations,
    IActivityAvailabilityOperations,
    IActivityAuthoringCapabilitiesReader,
    IActivityAuthoringCatalogReader,
    IRecommendedActivityDefinitionReader
{
    protected abstract Task OnRequestAsync(object request, CancellationToken cancellationToken);
    protected abstract Task OnCommandAsync(object command, CancellationToken cancellationToken);

    private async Task<T> RequestAsync<T>(object request, CancellationToken cancellationToken) where T : notnull
    {
        await OnRequestAsync(request, cancellationToken);
        return (T)CaptureResponseFactory.Create(typeof(T));
    }

    private async Task<T> CommandAsync<T>(object command, CancellationToken cancellationToken) where T : notnull
    {
        await OnCommandAsync(command, cancellationToken);
        return (T)CaptureResponseFactory.Create(typeof(T));
    }

    public Task<ReusableActivityDefinitionMutationView> CreateDefinitionAsync(CreateReusableActivityDefinition command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityDefinitionMutationView>(command, cancellationToken);
    public Task<ActivityDefinitionIdentityView> UpdateDefinitionAsync(UpdateReusableActivityDefinition command, CancellationToken cancellationToken) => CommandAsync<ActivityDefinitionIdentityView>(command, cancellationToken);
    public Task<ReusableActivityDraftView> CreateDraftAsync(CreateReusableActivityDraft command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityDraftView>(command, cancellationToken);
    public Task<ReusableActivityDraftView> ReplaceDraftAsync(ReplaceReusableActivityDraft command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityDraftView>(command, cancellationToken);
    public Task<ReusableActivityDraftView> UpdateDraftPresentationAsync(UpdateReusableActivityDraftPresentation command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityDraftView>(command, cancellationToken);
    public Task<ReusableActivityDraftView> CreateConflictCopyAsync(CreateReusableActivityDraftConflictCopy command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityDraftView>(command, cancellationToken);
    public Task<ReusableActivityDraftView> MigrateDraftAsync(MigrateReusableActivityDraft command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityDraftView>(command, cancellationToken);
    public Task DiscardDraftAsync(DiscardReusableActivityDraft command, CancellationToken cancellationToken) => OnVoidCommandAsync(command, cancellationToken);
    public Task<ActivityDraftValidationView> ValidateDraftAsync(ValidateReusableActivityDraft command, CancellationToken cancellationToken) => CommandAsync<ActivityDraftValidationView>(command, cancellationToken);
    public Task<ReusableActivityDraftView> GetDraftAsync(GetReusableActivityDraft request, CancellationToken cancellationToken) => RequestAsync<ReusableActivityDraftView>(request, cancellationToken);
    public Task<ReusableActivityVersionView> GetVersionAsync(GetReusableActivityVersion request, CancellationToken cancellationToken) => RequestAsync<ReusableActivityVersionView>(request, cancellationToken);

    public Task<ActivityManagementPageView<ReusableActivityDefinitionManagementView>> ListDefinitionsAsync(ListReusableActivityDefinitions request, CancellationToken cancellationToken) => RequestAsync<ActivityManagementPageView<ReusableActivityDefinitionManagementView>>(request, cancellationToken);
    public Task<ReusableActivityDefinitionManagementView> GetDefinitionAsync(GetReusableActivityDefinition request, CancellationToken cancellationToken) => RequestAsync<ReusableActivityDefinitionManagementView>(request, cancellationToken);
    public Task<ActivityManagementPageView<ReusableActivityDraftManagementView>> ListDraftsAsync(ListReusableActivityDrafts request, CancellationToken cancellationToken) => RequestAsync<ActivityManagementPageView<ReusableActivityDraftManagementView>>(request, cancellationToken);
    public Task<ActivityManagementPageView<ReusableActivityVersionManagementView>> ListVersionsAsync(ListReusableActivityVersions request, CancellationToken cancellationToken) => RequestAsync<ActivityManagementPageView<ReusableActivityVersionManagementView>>(request, cancellationToken);

    public Task<ActivityDefinitionRecommendationView> SetAsync(SetRecommendedReusableActivityVersion command, CancellationToken cancellationToken) => CommandAsync<ActivityDefinitionRecommendationView>(command, cancellationToken);

    public Task<ReusableActivityVersionLifecycleView> RetireAsync(RetireReusableActivityVersion command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityVersionLifecycleView>(command, cancellationToken);
    public Task<ReusableActivityVersionLifecycleView> RestoreAsync(RestoreReusableActivityVersion command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityVersionLifecycleView>(command, cancellationToken);
    public Task<ReusableActivityVersionLifecycleView> RevokeAsync(RevokeReusableActivityVersion command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityVersionLifecycleView>(command, cancellationToken);

    public Task<ActivityVersionDiffView> CompareVersionsAsync(CompareActivityVersions request, CancellationToken cancellationToken) => RequestAsync<ActivityVersionDiffView>(request, cancellationToken);
    public Task<ActivityVersionDiffView> PreviewDraftAsync(PreviewActivityDraftDiff request, CancellationToken cancellationToken) => RequestAsync<ActivityVersionDiffView>(request, cancellationToken);

    public Task<ActivityForkPreviewView> PreviewAsync(PreviewReusableActivityFork command, CancellationToken cancellationToken) => CommandAsync<ActivityForkPreviewView>(command, cancellationToken);
    public Task<ActivityForkReceiptView> ApplyAsync(ApplyReusableActivityFork command, CancellationToken cancellationToken) => CommandAsync<ActivityForkReceiptView>(command, cancellationToken);
    public Task<ActivityForkReceiptView> GetStatusAsync(GetReusableActivityForkStatus request, CancellationToken cancellationToken) => RequestAsync<ActivityForkReceiptView>(request, cancellationToken);

    public Task<ActivityContractProposalView> ProposeAsync(ProposeReusableActivityContract request, CancellationToken cancellationToken) => RequestAsync<ActivityContractProposalView>(request, cancellationToken);
    public Task<ReusableActivityDraftView> ApplyAsync(ApplyReusableActivityContractProposal command, CancellationToken cancellationToken) => CommandAsync<ReusableActivityDraftView>(command, cancellationToken);

    public Task<ActivityDependencyPageView> ReadAsync(GetActivityDependencies request, CancellationToken cancellationToken) => RequestAsync<ActivityDependencyPageView>(request, cancellationToken);

    public Task<ActivityUpgradePlanView> CreatePlanAsync(CreateActivityUpgradePlan command, CancellationToken cancellationToken) => CommandAsync<ActivityUpgradePlanView>(command, cancellationToken);
    public Task<ActivityUpgradePlanView> GetPlanAsync(GetActivityUpgradePlan request, CancellationToken cancellationToken) => RequestAsync<ActivityUpgradePlanView>(request, cancellationToken);
    public Task<ActivityUpgradeApplyResultView> ApplyPlanAsync(ApplyActivityUpgradePlan command, CancellationToken cancellationToken) => CommandAsync<ActivityUpgradeApplyResultView>(command, cancellationToken);
    public Task<ActivityUpgradeApplyReceiptView> GetApplyReceiptAsync(GetActivityUpgradeApplyReceipt request, CancellationToken cancellationToken) => RequestAsync<ActivityUpgradeApplyReceiptView>(request, cancellationToken);
    public Task<ActivityUpgradePlanView> RefreshPlanAsync(RefreshActivityUpgradePlan command, CancellationToken cancellationToken) => CommandAsync<ActivityUpgradePlanView>(command, cancellationToken);

    public Task<ActivityAvailabilitySettings> GetSettingsAsync(GetActivityAvailabilitySettings request, CancellationToken cancellationToken) => RequestAsync<ActivityAvailabilitySettings>(request, cancellationToken);
    public Task<ActivityAvailabilityDiagnostics> ListDiagnosticsAsync(ListActivityAvailabilityDiagnostics request, CancellationToken cancellationToken) => RequestAsync<ActivityAvailabilityDiagnostics>(request, cancellationToken);
    public Task<ActivityAvailabilitySettings> SaveSettingsAsync(SaveActivityAvailabilitySettings command, CancellationToken cancellationToken) => CommandAsync<ActivityAvailabilitySettings>(command, cancellationToken);

    public Task<ActivityAuthoringCapabilitiesView> GetAsync(GetActivityAuthoringCapabilities request, CancellationToken cancellationToken) => RequestAsync<ActivityAuthoringCapabilitiesView>(request, cancellationToken);
    public Task<ActivityAuthoringCatalogView> ListAsync(ListActivityAuthoringCatalog request, CancellationToken cancellationToken) => RequestAsync<ActivityAuthoringCatalogView>(request, cancellationToken);
    public Task<RecommendedActivityDefinitionPageView> ListAsync(ListRecommendedActivityDefinitions request, CancellationToken cancellationToken) => RequestAsync<RecommendedActivityDefinitionPageView>(request, cancellationToken);

    private async Task OnVoidCommandAsync(object command, CancellationToken cancellationToken) =>
        await OnCommandAsync(command, cancellationToken);
}

/// <summary>
/// The compatibility-host variant: identity-header scenario keying and dispatch observability,
/// reproducing <c>CaptureRequestSender</c>/<c>CaptureCommandSender</c> verbatim at the new seams.
/// </summary>
internal sealed class CaptureDomainSeams(IHttpContextAccessor contextAccessor) : ActivitiesDesignDomainSeamsBase
{
    public object? LastRequest { get; private set; }
    public object? LastCommand { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }
    public CancellationToken RequestAbortedAtDispatch { get; private set; }
    public OperationCanceledException? LastCancellationException { get; private set; }

    protected override Task OnRequestAsync(object request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return DispatchAsync(cancellationToken, request: true);
    }

    protected override Task OnCommandAsync(object command, CancellationToken cancellationToken)
    {
        LastCommand = command;
        return DispatchAsync(cancellationToken, request: false);
    }

    private Task DispatchAsync(CancellationToken cancellationToken, bool request)
    {
        LastCancellationToken = cancellationToken;
        RequestAbortedAtDispatch = contextAccessor.HttpContext?.RequestAborted ?? default;
        var scenario = contextAccessor.HttpContext?.Request.Headers[ActivitiesDesignCompatibilityCases.IdentityHeader].ToString();
        switch (scenario)
        {
            case "trusted-unexpected-failure":
                throw new InvalidOperationException("capture-secret-internal-details");
            case "trusted-domain-not-found":
                throw new EntityNotFoundException("The deterministic capture entity was not found.");
            case "trusted-domain-conflict":
                throw new ActivityAuthoringException(409, "capture.domain-conflict", "Capture domain conflict", "The deterministic capture domain conflict was requested.");
            case "trusted-domain-failure":
                throw new ActivityAuthoringException(422, "capture.domain-failure", "Capture domain failure", "The deterministic capture domain failure was requested.");
            case "trusted-cancellation" when request:
                LastCancellationException = new OperationCanceledException("The deterministic capture request was canceled.", cancellationToken);
                throw LastCancellationException;
            case "trusted-cancellation":
                throw new OperationCanceledException("The deterministic capture command was canceled.", cancellationToken);
            default:
                return Task.CompletedTask;
        }
    }
}

/// <summary>
/// The authorization-host variant: records the same "domain work happened" counters the sender
/// fakes recorded, so denial-ordering assertions keep their meaning.
/// </summary>
internal sealed class AuthorizationDomainSeams(AuthorizationObservationState observations) : ActivitiesDesignDomainSeamsBase
{
    protected override Task OnRequestAsync(object request, CancellationToken cancellationToken)
    {
        observations.RecordRequestSender();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    protected override Task OnCommandAsync(object command, CancellationToken cancellationToken)
    {
        observations.RecordCommandSender();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

internal static class ActivitiesDesignDomainSeamRegistrations
{
    /// <summary>Overrides every operation seam of the owner with the given fake. Register after the feature.</summary>
    public static IServiceCollection AddActivitiesDesignDomainSeams<TSeams>(this IServiceCollection services)
        where TSeams : ActivitiesDesignDomainSeamsBase
    {
        services.AddSingleton<TSeams>();
        services.AddSingleton<IReusableActivityAuthoringService>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityDefinitionManagementProjectionService>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityDefinitionRecommendationService>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityVersionLifecycleService>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityVersionDiffService>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityForkService>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityContractProposalService>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityDependencyReader>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityUpgradeOperations>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityAvailabilityOperations>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityAuthoringCapabilitiesReader>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IActivityAuthoringCatalogReader>(provider => provider.GetRequiredService<TSeams>());
        services.AddSingleton<IRecommendedActivityDefinitionReader>(provider => provider.GetRequiredService<TSeams>());
        return services;
    }
}
