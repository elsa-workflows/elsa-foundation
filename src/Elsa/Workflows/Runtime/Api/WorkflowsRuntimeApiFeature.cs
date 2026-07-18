using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Services;
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
        services.TryAddScoped<WorkflowExecutableInspector>();

        // API-only wiring: the FastEndpoints request handlers this feature's endpoints dispatch through.
        services.AddRequestHandlersFrom(GetType().Assembly);
        services.TryAddScoped<IActivityExecutionInspectionAuthorizationContext, HttpContextActivityExecutionInspectionAuthorizationContext>();
        services.TryAddScoped<ActivityExecutionHierarchyReader>();
        services.TryAddScoped<ActivityExecutionLayoutReader>();
        services.TryAddScoped<IActivityExecutionValuePayloadReader, ActivityExecutionValuePayloadReader>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IActivityExecutionValuePayloadAuditSink, LoggingActivityExecutionValuePayloadAuditSink>();
        services.AddApiCapability(RuntimeApiCapabilities.StaticDeclaration);
        services.AddApiCapabilitySource<RuntimeOperationalCapabilitySource>();
    }
}
