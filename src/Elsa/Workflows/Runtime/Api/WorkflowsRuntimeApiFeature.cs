using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Api.Services.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
public class WorkflowsRuntimeApiFeature : IWebShellFeature
{
    /// <summary>
    /// Persistent HMAC key for restart- and multi-instance-stable hierarchy cursors. Configure at least 32 UTF-8 bytes
    /// in production; when omitted, the in-memory development adapter uses a process-local random key.
    /// </summary>
    public string? ActivityExecutionHierarchyCursorSigningKey { get; set; }

    /// <summary>
    /// The selected restart-stable key identifier for durable alteration requests. Hosts must also configure the
    /// corresponding base64 AES-256 material in <see cref="WorkflowAlterationPayloadProtectionKeys"/>; leaving this
    /// unset preserves the in-memory development-only key configured by the Runtime core.
    /// </summary>
    public string? WorkflowAlterationPayloadProtectionActiveKeyId { get; set; }

    /// <summary>Key ring retained for as long as durable alteration plans may be read or cancelled.</summary>
    public IDictionary<string, string>? WorkflowAlterationPayloadProtectionKeys { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        // The runtime execution spine is host-agnostic (RT-4): it is composed here so the API endpoints can drive it,
        // but a non-HTTP host (worker, test harness, another module) can compose the same runtime via
        // AddWorkflowRuntime() without this API feature. See RuntimeCoreServiceCollectionExtensions.
        services.AddWorkflowRuntime();
        services.AddHttpContextAccessor();
        services.AddDynamicEndpointApiExplorerRefresh();
        // The owner's failure services are keyed so hosts composing several modules keep each
        // module's own error shapes; the endpoint pipeline falls back to unkeyed registrations.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, Endpoints.WorkflowsRuntimeProblemWriter>(WorkflowsRuntimeApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, Endpoints.WorkflowsRuntimeFaultRenderer>(WorkflowsRuntimeApi.OwnerId);
        services.ConfigureHttpJsonOptions(options =>
        {
            if (!options.SerializerOptions.TypeInfoResolverChain.Any(resolver => resolver is WorkflowsRuntimeJsonTypeInfoResolver))
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, new WorkflowsRuntimeJsonTypeInfoResolver());
        });
        services.TryAddScoped<HttpContextActivityExecutionInspectionAuthorizationContext>();
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
        services.TryAddScoped<WorkflowExecutableInspector>();

        // The operation seams the endpoint classes dispatch to. Registered against the concrete
        // services so a replacement of either registration keeps the other coherent.
        services.TryAddScoped<IWorkflowExecutableInspector>(sp => sp.GetRequiredService<WorkflowExecutableInspector>());
        services.TryAddScoped<StimulusDispatchService>();
        services.TryAddScoped<IStimulusDispatchService>(sp => sp.GetRequiredService<StimulusDispatchService>());
        services.TryAddScoped<WorkflowExecutionStartService>();
        services.TryAddScoped<IWorkflowExecutionStartService>(sp => sp.GetRequiredService<WorkflowExecutionStartService>());
        services.TryAddScoped<ActivityExecutionInspectionService>();
        services.TryAddScoped<IActivityExecutionInspectionService>(sp => sp.GetRequiredService<ActivityExecutionInspectionService>());
        services.TryAddScoped<WorkflowInstanceDetailsService>();
        services.TryAddScoped<IWorkflowInstanceDetailsService>(sp => sp.GetRequiredService<WorkflowInstanceDetailsService>());
        services.TryAddScoped<WorkflowInstanceListService>();
        services.TryAddScoped<IWorkflowInstanceListService>(sp => sp.GetRequiredService<WorkflowInstanceListService>());
        services.TryAddScoped<WorkflowIncidentListService>();
        services.TryAddScoped<IWorkflowIncidentListService>(sp => sp.GetRequiredService<WorkflowIncidentListService>());
        services.TryAddScoped<WorkflowDispatchInspectionService>();
        services.TryAddScoped<IWorkflowDispatchInspectionService>(sp => sp.GetRequiredService<WorkflowDispatchInspectionService>());
        services.TryAddScoped<RuntimeDiagnosticsSettingsService>();
        services.TryAddScoped<IRuntimeDiagnosticsSettingsService>(sp => sp.GetRequiredService<RuntimeDiagnosticsSettingsService>());
        services.TryAddScoped<WorkflowAlterationPlanApiService>();
        services.TryAddScoped<IWorkflowAlterationPlanApiService>(sp => sp.GetRequiredService<WorkflowAlterationPlanApiService>());

        var hasAsyncInspectionHost = services.Any(descriptor => descriptor.ServiceType == typeof(IActivityInspectionContextAsync));
#pragma warning disable CS0618
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IActivityExecutionInspectionAuthorizationContext)))
            services.AddScoped<IActivityExecutionInspectionAuthorizationContext>(sp => sp.GetRequiredService<HttpContextActivityExecutionInspectionAuthorizationContext>());
        if (!hasAsyncInspectionHost)
            services.AddScoped<IActivityInspectionContextAsync>(sp =>
                sp.GetRequiredService<IActivityExecutionInspectionAuthorizationContext>() is IActivityInspectionContextAsync asyncContext
                    ? asyncContext
                    : new LegacyActivityInspectionContextAdapter(sp.GetRequiredService<IActivityExecutionInspectionAuthorizationContext>()));
#pragma warning restore CS0618
        services.EnsureReplacementContract<IActivityInspectionContextAsync, HttpContextActivityExecutionInspectionAuthorizationContext>();
        // Resolves the host's effective checkpoint cadence for the instance detail view (ADR 0032 R3). Reads the
        // coalescing options optionally (via IEnumerable), so it is Immediate unless the persistence feature enabled Coalesced.
        services.TryAddScoped<RuntimeCheckpointCadenceInspector>();
        services.TryAddScoped<ActivityExecutionHierarchyReader>();
        services.TryAddScoped<IActivityExecutionDescendantsReader>(sp => sp.GetRequiredService<ActivityExecutionHierarchyReader>());
        services.TryAddScoped<ActivityExecutionLayoutReader>();
        services.TryAddScoped<IActivityExecutionLayoutReader>(sp => sp.GetRequiredService<ActivityExecutionLayoutReader>());
        services.TryAddScoped<IActivityExecutionValuePayloadReader, ActivityExecutionValuePayloadReader>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IActivityExecutionValuePayloadAuditSink, LoggingActivityExecutionValuePayloadAuditSink>();
        services.TryAddScoped<IWorkflowAlterationRequestContext, HttpContextWorkflowAlterationRequestContext>();
        services.TryAddScoped<IWorkflowAlterationAdmissionGate, AllowWorkflowAlterationAdmissionGate>();
        services.AddApiCapability(RuntimeApiCapabilities.StaticDeclaration);
        services.AddApiCapabilitySource<RuntimeOperationalCapabilitySource>();
        services.AddApiCapabilitySource<RuntimeAlterationCapabilitySource>();
        services.AddPermissionContributor<WorkflowRuntimePermissionContributor>();
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(endpoints);
}
