using Elsa.Workflows.Runtime.Inspection.Core;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Api.Services.Alterations;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsRuntimeApi",
    DisplayName = "Workflows Runtime API",
    Description = "Runtime workflow execution endpoints for published WorkflowExecutable artifacts.",
    DependsOn = new object[] { "ApiCapabilities" }
)]
public class WorkflowsRuntimeApiFeature : IWebShellFeature, IPostConfigureShellServices
{
    /// <summary>
    /// Persistent HMAC key for restart- and multi-instance-stable hierarchy cursors. Configure at least 32 UTF-8 bytes
    /// in production; when omitted, the in-memory development adapter uses a process-local random key.
    /// </summary>
    [ManifestSetting(Secret = true)]
    public string? ActivityExecutionHierarchyCursorSigningKey { get; set; }

    /// <summary>
    /// The selected restart-stable key identifier for durable alteration requests. Hosts must also configure the
    /// corresponding base64 AES-256 material in <see cref="WorkflowAlterationPayloadProtectionKeys"/>; leaving this
    /// unset preserves the in-memory development-only key configured by the Runtime core.
    /// </summary>
    public string? WorkflowAlterationPayloadProtectionActiveKeyId { get; set; }

    /// <summary>Key ring retained for as long as durable alteration plans may be read or cancelled.</summary>
    [ManifestSetting(Secret = true)]
    public IDictionary<string, string>? WorkflowAlterationPayloadProtectionKeys { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(new LegacyRuntimeApiSelected());
        services.AddWorkflowRuntimeApiHttpServices(includeLegacyUserTasks: true);
        // The runtime execution spine is host-agnostic: it is composed here so the API endpoints can drive it,
        // but a non-HTTP host (worker, test harness, another module) can compose the same runtime via
        // AddWorkflowRuntime() without this API feature. See RuntimeCoreServiceCollectionExtensions.
        services.AddWorkflowRuntime();
        var taskAccess = services.Where(x => x.ServiceType == typeof(Elsa.Workflows.Runtime.Core.UserTasks.IUserTaskAccess)).ToArray();
        if (taskAccess.Length != 1) throw new InvalidOperationException("Exactly one user-task eligibility adapter must be selected.");
        if (taskAccess[0].ImplementationType == typeof(Elsa.Workflows.Runtime.UserTasks.UnavailableUserTaskAccess))
            services.Replace(ServiceDescriptor.Scoped<Elsa.Workflows.Runtime.Core.UserTasks.IUserTaskAccess, UserTasks.TaskRequestAccess>());
        // Task responses name the people and groups they store, for every reader (#4198).
        services.TryAddScoped<UserTasks.UserTaskPeople>();
        services.AddApiCapabilitySource<UserTasks.UserTaskCapabilitySource>();

        services.Configure<ActivityExecutionHierarchyCursorOptions>(options =>
            options.SigningKey = ActivityExecutionHierarchyCursorSigningKey);
        if (!string.IsNullOrWhiteSpace(WorkflowAlterationPayloadProtectionActiveKeyId))
        {
            services.Configure<WorkflowAlterationPayloadProtectionOptions>(options =>
            {
                options.ActiveKeyId = WorkflowAlterationPayloadProtectionActiveKeyId;
                options.AllowEphemeralDevelopmentKey = false;
                if (WorkflowAlterationPayloadProtectionKeys is not null)
                    options.Keys = new Dictionary<string, string>(WorkflowAlterationPayloadProtectionKeys, StringComparer.Ordinal);
            });
        }
        RuntimeHttpInspectionServices.AddLegacyExecutableRetentionReader(services);
        RuntimeHttpInspectionServices.AddExecutableInspectionServices(services);
        services.TryAddScoped<StimulusDispatchService>();
        services.TryAddScoped<IStimulusDispatchService>(sp => sp.GetRequiredService<StimulusDispatchService>());
        services.TryAddScoped<WorkflowExecutionStartService>();
        services.TryAddScoped<IWorkflowExecutionStartService>(sp => sp.GetRequiredService<WorkflowExecutionStartService>());
        services.TryAddScoped<RuntimeHistoryInspectionService>(sp => new(
            sp.GetRequiredService<IWorkflowExecutionStateStore>(),
            sp.GetRequiredService<IActivityInspectionContextAsync>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IRuntimeHistoryStore>(),
            sp.GetService<IBookmarkStateStore>(),
            sp.GetService<IActivityExecutionStateStore>(),
            sp.GetService<ILogger<RuntimeHistoryInspectionService>>())
        {
            SourceLookup = () => sp.GetService<IPostCommitOutboxLookupStore>(),
            OccurrenceAccessBinding = (workflow, binding, token) =>
                sp.GetRequiredService<RuntimeHistoryOccurrenceAuthorization>().BindAsync(workflow, binding, token),
            TimerLookup = () => sp.GetService<IDurableTimerStore>() as IDurableTimerHistoryStore,
            CaptureAvailability = policy =>
            {
                var reader = sp.GetService<IRuntimeHistoryStore>();
                var writer = sp.GetService<IRuntimeCheckpointCommitStore>();
                return reader is not null && writer is { SupportsHistory: true } && writer.CanCommitHistory(reader) &&
                    sp.GetServices<IRuntimeCheckpointCommitEnricher>()
                        .OfType<IRuntimeHistoryCaptureSupport>().Any(source => source.CanCapture(policy));
            }
        });
        var history = LegacyRuntimeApiReaderRegistration.Add<IRuntimeHistoryPageReader, RuntimeHistoryInspectionService>(
            services, static sp => sp.GetRequiredService<RuntimeHistoryInspectionService>());
        var occurrences = LegacyRuntimeApiReaderRegistration.Add<IRuntimeHistoryOccurrencePageReader, RuntimeHistoryInspectionService>(
            services, static sp => sp.GetRequiredService<RuntimeHistoryInspectionService>());
        RuntimeHttpInspectionServices.AddStoredActivityInspection(services);
        services.TryAddScoped<WorkflowInstanceDeletionService>();
        var deletion = LegacyRuntimeApiReaderRegistration.Add<IWorkflowInstanceDeletionService, WorkflowInstanceDeletionService>(
            services, static sp => sp.GetRequiredService<WorkflowInstanceDeletionService>());
        services.TryAddScoped<WorkflowInstanceDetailsService>();
        var details = LegacyRuntimeApiReaderRegistration.Add<IWorkflowInstanceDetailsService, WorkflowInstanceDetailsService>(
            services, static sp => sp.GetRequiredService<WorkflowInstanceDetailsService>());
        services.TryAddScoped<WorkflowInstanceListService>();
        var list = LegacyRuntimeApiReaderRegistration.Add<IWorkflowInstanceListService, WorkflowInstanceListService>(
            services, static sp => sp.GetRequiredService<WorkflowInstanceListService>());
        services.TryAddSingleton(new LegacyRuntimeApiReaderOwnership(history, occurrences, deletion, details, list));
        services.TryAddScoped<ILegacyRuntimePreviewReaders, LegacyRuntimePreviewReaders>();
        services.TryAddScoped<WorkflowIncidentListService>();
        services.TryAddScoped<IWorkflowIncidentListService>(sp => sp.GetRequiredService<WorkflowIncidentListService>());
        RuntimeHttpInspectionServices.AddRunInspectionServices(services);
        services.TryAddScoped<WorkflowDispatchInspectionService>();
        services.TryAddScoped<IWorkflowDispatchInspectionService>(sp => sp.GetRequiredService<WorkflowDispatchInspectionService>());
        services.TryAddScoped<RuntimeDiagnosticsSettingsService>();
        services.TryAddScoped<IRuntimeDiagnosticsSettingsService>(sp => sp.GetRequiredService<RuntimeDiagnosticsSettingsService>());
        services.TryAddScoped<ActivityExecutionStateViewService>();
        services.TryAddScoped<IActivityExecutionStateViewService>(sp => sp.GetRequiredService<ActivityExecutionStateViewService>());
        services.TryAddSingleton<ActivityStateViewProjectorRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateActivityStateViewProjectorsStartupTask>());
        services.TryAddScoped<WorkflowAlterationPlanApiService>();
        services.TryAddScoped<IWorkflowAlterationPlanApiService>(sp => sp.GetRequiredService<WorkflowAlterationPlanApiService>());

        // Resolves the persistence mode for the instance detail view: the mode a run recorded, otherwise the host default
        // from the options the persistence modes register. It reads them only when it reports a default, so the view
        // resolves on a host that has not added them yet.
        services.TryAddScoped<RuntimePersistenceModeInspector>();
        services.TryAddScoped<ActivityExecutionHierarchyReader>();
        services.TryAddScoped<IActivityExecutionDescendantsReader>(sp => sp.GetRequiredService<ActivityExecutionHierarchyReader>());
        services.TryAddScoped<ActivityExecutionLayoutReader>();
        services.TryAddScoped<IActivityExecutionLayoutReader>(sp => sp.GetRequiredService<ActivityExecutionLayoutReader>());
        services.TryAddScoped<IActivityExecutionValuePayloadReader, ActivityExecutionValuePayloadReader>();
        services.TryAddScoped<IActivityExecutionValuePayloadAuditSink, LoggingActivityExecutionValuePayloadAuditSink>();
        services.TryAddScoped<IWorkflowAlterationRequestContext, HttpContextWorkflowAlterationRequestContext>();
        services.TryAddScoped<IWorkflowAlterationAdmissionGate, AllowWorkflowAlterationAdmissionGate>();
        services.AddApiCapability(RuntimeApiCapabilities.StaticDeclaration);
        services.AddApiCapabilitySource<RuntimeHistoryCapabilitySource>();
        services.AddApiCapabilitySource<RuntimeOperationalCapabilitySource>();
        services.AddApiCapabilitySource<RuntimeActivityExecutionBrowsingCapabilitySource>();
        services.AddApiCapabilitySource<RuntimeAlterationCapabilitySource>();
        services.AddApiCapabilitySource<RuntimeRunDeletionCapabilitySource>();
        services.AddApiCapabilitySource<RuntimeActivityStateViewCapabilitySource>();
    }

    /// <summary>
    /// Adds the services that run every persistence mode, so a shell runs Balanced by default without listing another
    /// feature, and a workflow that chooses a mode always gets it. This runs after every feature's ConfigureServices
    /// because it wraps the runtime stores the shell's persistence provider registered, and it keeps the options the
    /// WorkflowsRuntimeCheckpointPersistence feature registered, whichever of the two features CShells composes first.
    /// </summary>
    public virtual void PostConfigureServices(IServiceCollection services) => services.AddPersistenceModes();

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(endpoints);
}
