using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;

public static class ReleaseArchiveServiceCollectionExtensions
{
    /// <summary>Selects one scoped archive backend over a consumer's context; schema and migrations remain host-owned.</summary>
    /// <remarks>Uses connection-string options and fresh owned contexts. Deliberate replacement requires a coherent prior selection.</remarks>
    public static IServiceCollection AddReleaseArchiveStore<TContext>(this IServiceCollection services,
        Func<IServiceProvider, SourceScope> scopeFactory, ReleaseBundleLimits? limits = null, bool requireDurable = true,
        bool replaceExisting = false) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        var previous = services.Where(x => !x.IsKeyedService && x.ServiceType == typeof(ReleaseArchiveBackendSelection))
            .Select(x => x.ImplementationInstance).OfType<ReleaseArchiveBackendSelection>().SingleOrDefault();
        if (previous is not null && replaceExisting)
            previous.RemoveOwnedSelection();
        else if (previous is not null || services.Any(x => ReleaseArchiveBackendSelection.IsPort(x.ServiceType)))
            throw new InvalidOperationException("An archive backend is already selected; replacement must be explicit and coherent.");
        if (services.Any(x => x.ServiceType == typeof(EfReleaseArchiveStore<TContext>)))
            throw new InvalidOperationException("An archive backend facade is already registered.");

        ReleaseArchiveBackendSelection selection = null!;
        var facade = ServiceDescriptor.Scoped<EfReleaseArchiveStore<TContext>>(provider =>
        {
            selection.EnsureOwnsPorts();
            _ = provider.GetRequiredService<IOptions<ReleaseArchiveBackendOptions>>().Value;
            return new(provider.GetRequiredService<TContext>(), provider.GetRequiredService<IReleaseBundleCodec>(),
                scopeFactory(provider), provider.GetRequiredService<IPersistenceAccessContextAccessor>(), limits ?? new ReleaseBundleLimits(), requireDurable);
        });
        ServiceDescriptor[] ports = [
            ServiceDescriptor.Scoped<IReleaseArchiveReader>(provider => provider.GetRequiredService<EfReleaseArchiveStore<TContext>>()),
            ServiceDescriptor.Scoped<IReleaseArchiveWriter>(provider => provider.GetRequiredService<EfReleaseArchiveStore<TContext>>()),
            ServiceDescriptor.Scoped<IReleaseArchiveStoreCapabilities>(provider => provider.GetRequiredService<EfReleaseArchiveStore<TContext>>())
        ];
        selection = new(services, facade, ports);
        services.Add(facade);
        foreach (var port in ports) services.Add(port);
        services.AddSingleton(selection);
        services.AddSingleton<IValidateOptions<ReleaseArchiveBackendOptions>>(selection);
        services.AddOptions<ReleaseArchiveBackendOptions>().ValidateOnStart();
        return services;
    }
}

internal sealed class ReleaseArchiveBackendOptions;

internal sealed class ReleaseArchiveBackendSelection(IServiceCollection services, ServiceDescriptor facade,
    IReadOnlyList<ServiceDescriptor> ports) : IValidateOptions<ReleaseArchiveBackendOptions>
{
    internal static bool IsPort(Type type) => type == typeof(IReleaseArchiveReader) || type == typeof(IReleaseArchiveWriter) ||
        type == typeof(IReleaseArchiveStoreCapabilities);

    internal void EnsureOwnsPorts()
    {
        foreach (var port in ports)
        {
            var selected = services.Where(x => !x.IsKeyedService && x.ServiceType == port.ServiceType).ToArray();
            if (selected.Length != 1 || !ReferenceEquals(selected[0], port))
                throw new InvalidOperationException("Archive reader, writer and capabilities must belong to one coherent backend selection.");
        }
        var facades = services.Where(x => !x.IsKeyedService && x.ServiceType == facade.ServiceType).ToArray();
        if (facades.Length != 1 || !ReferenceEquals(facades[0], facade))
            throw new InvalidOperationException("The selected archive facade was replaced or duplicated.");
    }

    internal void RemoveOwnedSelection()
    {
        EnsureOwnsPorts();
        foreach (var port in ports) services.Remove(port);
        services.Remove(facade);
        services.RemoveAll<ReleaseArchiveBackendSelection>();
        services.RemoveAll<IValidateOptions<ReleaseArchiveBackendOptions>>();
    }

    public ValidateOptionsResult Validate(string? name, ReleaseArchiveBackendOptions options)
    {
        try { EnsureOwnsPorts(); return ValidateOptionsResult.Success; }
        catch (InvalidOperationException exception) { return ValidateOptionsResult.Fail(exception.Message); }
    }
}
