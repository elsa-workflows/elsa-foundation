using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Api.Services.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
public class WorkflowsRuntimeApiFeature : FastEndpointsFeatureBase
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

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // The runtime execution spine is host-agnostic (RT-4): it is composed here so the API endpoints can drive it,
        // but a non-HTTP host (worker, test harness, another module) can compose the same runtime via
        // AddWorkflowRuntime() without this API feature. See RuntimeCoreServiceCollectionExtensions.
        services.AddWorkflowRuntime();
        services.AddHttpContextAccessor();
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

        // API-only wiring: the FastEndpoints request and command handlers this feature's endpoints dispatch through.
        var assembly = GetType().Assembly;
        services.AddRequestHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IActivityExecutionInspectionAuthorizationContext)))
        {
            if (services.Any(descriptor => descriptor.ServiceType == typeof(IActivityInspectionContext)))
                services.AddScoped<IActivityExecutionInspectionAuthorizationContext>(sp =>
                    sp.GetRequiredService<IActivityInspectionContext>());
            else
                services.AddScoped<IActivityExecutionInspectionAuthorizationContext, HttpContextActivityExecutionInspectionAuthorizationContext>();
        }
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IActivityInspectionContext)))
            services.AddScoped<IActivityInspectionContext>(sp =>
                sp.GetRequiredService<IActivityExecutionInspectionAuthorizationContext>() is IActivityInspectionContext current
                    ? current
                    : new LegacyActivityInspectionContextAdapter(sp.GetRequiredService<IActivityExecutionInspectionAuthorizationContext>()));
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IActivityInspectionContextAsync)))
        {
            if (services.Any(descriptor => descriptor.ServiceType == typeof(IActivityExecutionInspectionAuthorizationContextAsync)))
                services.AddScoped<IActivityInspectionContextAsync>(sp =>
                    sp.GetRequiredService<IActivityExecutionInspectionAuthorizationContextAsync>() is IActivityInspectionContextAsync current
                        ? current
                        : new LegacyActivityInspectionAsyncAliasAdapter(sp.GetRequiredService<IActivityExecutionInspectionAuthorizationContextAsync>()));
            else
                services.AddScoped<IActivityInspectionContextAsync>(sp =>
                    sp.GetRequiredService<IActivityInspectionContext>() is IActivityInspectionContextAsync asyncContext
                        ? asyncContext
                    : new LegacyActivityInspectionContextAdapter(sp.GetRequiredService<IActivityInspectionContext>()));
        }
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IActivityExecutionInspectionAuthorizationContextAsync)))
            services.AddScoped<IActivityExecutionInspectionAuthorizationContextAsync>(sp =>
                sp.GetRequiredService<IActivityInspectionContextAsync>());
        services.EnsureReplacementContract<IActivityInspectionContextAsync, HttpContextActivityExecutionInspectionAuthorizationContext>();
        // Resolves the host's effective checkpoint cadence for the instance detail view (ADR 0032 R3). Reads the
        // coalescing options optionally (via IEnumerable), so it is Immediate unless the persistence feature enabled Coalesced.
        services.TryAddScoped<RuntimeCheckpointCadenceInspector>();
        services.TryAddScoped<ActivityExecutionHierarchyReader>();
        services.TryAddScoped<ActivityExecutionLayoutReader>();
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
}
