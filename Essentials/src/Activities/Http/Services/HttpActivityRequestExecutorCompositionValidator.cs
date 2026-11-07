using CShells.Lifecycle;
using Elsa.Activities.Http.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.Services;

internal sealed record HttpActivityRequestExecutorComposition(IServiceCollection Services);

// Count the completed collection before startup work, without constructing either transport provider.
internal sealed class HttpActivityRequestExecutorCompositionValidator(HttpActivityRequestExecutorComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = composition.Services.Count(x => x.ServiceType == typeof(IHttpActivityRequestExecutor) && !x.IsKeyedService);
        if (count != 1)
            throw new InvalidOperationException($"HTTP activities require exactly one unkeyed {nameof(IHttpActivityRequestExecutor)} registration; found {count}.");
        return Task.CompletedTask;
    }

    internal static void Register(IServiceCollection services)
    {
        var previous = services.LastOrDefault(x => x.ServiceType == typeof(HttpActivityRequestExecutorComposition))
            ?.ImplementationInstance as HttpActivityRequestExecutorComposition;
        if (ReferenceEquals(previous?.Services, services)) return;

        // A child collection may copy its parent's descriptors. Bind the check to this actual collection.
        services.RemoveAll<HttpActivityRequestExecutorComposition>();
        services.AddSingleton(new HttpActivityRequestExecutorComposition(services));
        if (previous is null)
            services.AddShellInitializer<HttpActivityRequestExecutorCompositionValidator>(LifecyclePhase.Prepare, -200);
    }
}
