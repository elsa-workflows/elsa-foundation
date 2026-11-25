using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// Serves a shell's publications on the clean-slate kernel (definitions on the kernel, owner decision 1). After every
/// other feature has registered, it puts the kernel's implementations of two replacement contracts in the place of the old
/// core's defaults: <see cref="KernelActivationCoordinator"/> for the activation coordinator and
/// <see cref="KernelTriggerBindingExtractor"/> for the trigger extractor. It replaces only those defaults; a shell that
/// registers another implementation of either is refused when it is built, with the implementation named, since one
/// implementation serves per shell (constitution 2.6.2). It drops the old core's HTTP route-table observer, which reads
/// old-core trigger bindings a kernel host does not write, and keeps every other trigger index observer (Studio's
/// workflow library among them).
/// </summary>
/// <remarks>
/// The host composes what the kernel shares across shells and registers each by instance, so every shell resolves the
/// host's own: <see cref="KernelServing"/> and <see cref="IScheduleActors"/>. A shell without them is refused when it is
/// built. Release 1 runs the kernel on one shell, the host's default. The host also hides the activities the kernel does not
/// run from that shell's toolbox (<c>NativeActivityCatalog</c>), in the shells <see cref="IsComposedIn"/> finds: the
/// toolbox's options are a design contract, which a runtime project does not reference.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Kernel",
    Description = "Serves this shell's publications on the clean-slate kernel: replaces the default activation coordinator and trigger extractor with the kernel's, and leaves out the old core's HTTP route table. Requires a host that composes the kernel's serving state.",
    DependsOn = new object[] { "WorkflowsPublishing", "WorkflowsRuntimeTriggers" })]
public class WorkflowsRuntimeKernelFeature : IShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsRuntimeKernel";

    /// <summary>The old core's activation coordinator, the default this feature replaces; named so that this project needs no reference to its module.</summary>
    public const string DefaultCoordinatorTypeName = "Elsa.Workflows.Runtime.Services.Executables.WorkflowActivationCoordinator";

    /// <summary>The old core's trigger extractor, the default this feature replaces.</summary>
    public const string DefaultExtractorTypeName = "Elsa.Workflows.Runtime.Services.Triggers.WorkflowTriggerBindingExtractor";

    /// <summary>The old core's HTTP route-table observer, which this feature leaves out.</summary>
    public const string RouteTableObserverTypeName = "Elsa.Workflows.Runtime.Http.Services.RouteTableTriggerIndexObserver";

    /// <summary>
    /// The old core's HTTP endpoint middleware, which serves workflow HTTP endpoints at its base path (<c>/workflows/http</c>).
    /// On a kernel shell the host's kernel edge serves that path, so the feature mounts this middleware with no base path,
    /// which passes every request on. Its other middleware (test runs, standalone triggers) and its options stay.
    /// </summary>
    public const string HttpEndpointMiddlewareTypeName = "Elsa.Activities.Http.Middleware.HttpEndpointMiddleware";

    /// <summary>The middleware the old core's HTTP activities feature registers beside its endpoint middleware: present, the endpoint middleware must be found.</summary>
    public const string HttpTestRunMiddlewareTypeName = "Elsa.Activities.Http.Middleware.HttpTestRunIngressMiddleware";

    /// <summary>The options the endpoint middleware reads its base path from.</summary>
    public const string HttpEndpointOptionsTypeName = "Elsa.Activities.Http.Options.HttpEndpointOptions";

    /// <summary>The contract the old core's HTTP feature registers beside its route-table observer: present, the observer must be found.</summary>
    public const string RouteTableSynchronizerTypeName = "Elsa.Workflows.Runtime.Http.Contracts.IHttpEndpointRouteTableSynchronizer";

    public virtual void ConfigureServices(IServiceCollection services) { }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        RequireShared<KernelServing>(services);
        RequireShared<IScheduleActors>(services);

        ReplaceDefault<IWorkflowTriggerBindingExtractor>(services, DefaultExtractorTypeName);
        // A singleton, as the default it replaces is: it is stateless, so a consumer of any lifetime resolves it.
        services.AddSingleton<IWorkflowTriggerBindingExtractor, KernelTriggerBindingExtractor>();

        DropRouteTableObserver(services);
        UnmountHttpEndpointMiddleware(services);

        ReplaceDefault<IWorkflowActivationCoordinator>(services, DefaultCoordinatorTypeName);
        services.AddScoped<IWorkflowActivationCoordinator>(provider => new KernelActivationCoordinator(
            provider.GetRequiredService<IWorkflowActivationAuthority>(),
            provider.GetRequiredService<IWorkflowActivationSwitch>(),
            provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
            provider.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>(),
            provider.GetRequiredService<IWorkflowExecutableStore>(),
            provider.GetRequiredService<IWorkflowTriggerBindingStore>(),
            provider.GetRequiredService<KernelServing>(),
            provider.GetRequiredService<IScheduleActors>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetService<IRecurringTriggerScheduleStore>(),
            provider.GetService<IOptions<ActivationChannelOptions>>()?.Value,
            provider.GetServices<IWorkflowTriggerIndexObserver>(),
            provider.GetService<ILogger<KernelActivationCoordinator>>()));
    }

    /// <summary>Whether the shell whose services these are composes this feature: its trigger extractor is the kernel's.</summary>
    public static bool IsComposedIn(IServiceProvider shellServices) =>
        shellServices.GetService<IWorkflowTriggerBindingExtractor>() is KernelTriggerBindingExtractor;

    /// <summary>
    /// Removes the old core's default of <typeparamref name="TContract"/>; refuses the shell when it registers anything else
    /// for it, which this feature would otherwise replace without a word.
    /// </summary>
    private static void ReplaceDefault<TContract>(IServiceCollection services, string defaultTypeName)
    {
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(TContract) && !descriptor.IsKeyedService).ToArray())
        {
            if (descriptor.ImplementationType?.FullName == defaultTypeName)
            {
                services.Remove(descriptor);
                continue;
            }
            throw new InvalidOperationException(
                $"The {FeatureName} feature replaces the shell's {typeof(TContract).Name} with the kernel's, and this shell registers " +
                $"{Describe(descriptor)} for it, which is not the default it replaces ({defaultTypeName}). One implementation serves per shell: " +
                $"remove that registration, or leave the {FeatureName} feature out of the shell.");
        }
    }

    /// <summary>
    /// Re-registers the old core's HTTP endpoint middleware with options of its own: a new instance at its defaults whose
    /// base path is empty, so that it passes every request on and the kernel edge serves <c>/workflows/http</c>. The
    /// shell's other HTTP endpoint settings don't reach it, and a middleware that serves no request has no use for them. A shell that registers the HTTP
    /// activities' test-run middleware without an endpoint middleware of that name is refused: its type was renamed.
    /// </summary>
    private static void UnmountHttpEndpointMiddleware(IServiceCollection services)
    {
        var endpoint = services.SingleOrDefault(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.FullName == HttpEndpointMiddlewareTypeName);
        if (endpoint is null)
        {
            if (services.Any(descriptor => descriptor.ServiceType.FullName == HttpTestRunMiddlewareTypeName))
                throw new InvalidOperationException(
                    $"The {FeatureName} feature unmounts the old core's HTTP endpoint middleware ({HttpEndpointMiddlewareTypeName}) from the shell, " +
                    "and this shell composes the HTTP activities without a middleware of that name. If its type was renamed, update the name this feature looks for.");
            return;
        }
        var middleware = endpoint.ServiceType;
        var optionsType = middleware.Assembly.GetType(HttpEndpointOptionsTypeName) is { } found && found.GetProperty("BasePath") is { CanWrite: true } basePath
            ? (found, basePath) : throw new InvalidOperationException(
                $"The {FeatureName} feature unmounts {HttpEndpointMiddlewareTypeName} through its options' BasePath ({HttpEndpointOptionsTypeName}), which it cannot find.");
        var unmounted = Activator.CreateInstance(optionsType.found)!;
        optionsType.basePath.SetValue(unmounted, "");
        var wrapped = typeof(Options).GetMethod(nameof(Options.Create))!.MakeGenericMethod(optionsType.found).Invoke(null, [unmounted])!;
        services.Remove(endpoint);
        services.Add(ServiceDescriptor.Describe(middleware, provider => ActivatorUtilities.CreateInstance(provider, middleware, wrapped), endpoint.Lifetime));
    }

    private static void DropRouteTableObserver(IServiceCollection services)
    {
        var observers = services.Where(IsRouteTableObserver).ToArray();
        foreach (var observer in observers) services.Remove(observer);
        // The old core's HTTP feature registers its observer beside its synchronizer: with the one and not the other, the
        // observer was renamed, and it would keep reading bindings this host does not write.
        if (observers.Length == 0 && services.Any(descriptor => descriptor.ServiceType.FullName == RouteTableSynchronizerTypeName))
            throw new InvalidOperationException(
                $"The {FeatureName} feature leaves the old core's HTTP route-table observer ({RouteTableObserverTypeName}) out of the shell, " +
                "and this shell composes the old core's HTTP feature without an observer of that name. If the observer's type was renamed, update the name this feature looks for.");
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ??
        $"a factory ({descriptor.ImplementationFactory?.Method.DeclaringType?.FullName ?? "unknown"})";

    private static bool IsRouteTableObserver(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IWorkflowTriggerIndexObserver) && !descriptor.IsKeyedService &&
        (descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType())?.FullName == RouteTableObserverTypeName;

    private static void RequireShared<TService>(IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService))
            throw new InvalidOperationException(
                $"The {FeatureName} feature serves on the kernel the host composes, and this host composes no {typeof(TService).Name}. " +
                "Enable the host's kernel composition (on the Workbench, Elsa:CorePreview:Publications), or leave this feature out of the shell.");
    }
}
