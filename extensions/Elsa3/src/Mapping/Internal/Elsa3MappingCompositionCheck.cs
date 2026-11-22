using CShells.Lifecycle;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping.Internal;

/// <summary>The service collection the mapping feature was composed in, read again at shell activation.</summary>
internal sealed record Elsa3MappingComposition(IServiceCollection Services);

/// <summary>
/// Fails shell activation with <see cref="MultipleElsa3MappingRegistrationsException"/> when the host composes more
/// than one registration of a mapping replacement contract (<see cref="IElsa3DefinitionConverter"/>,
/// <see cref="IElsa3NodeConverter"/>, <see cref="IElsa3ActivityMap"/>), naming every registration, whatever order they
/// were registered in. Fails it with <see cref="PartialElsa3ConverterReplacementException"/> when a host replaces one
/// of the two converters but leaves the other on a default that does not follow it.
/// </summary>
/// <remarks>
/// A replacement-contract conflict must be prevented at registration time or detected at startup (framework §2.6.2).
/// The feature registers its defaults with <c>TryAdd</c>, so a host that registers its own first, or replaces a default
/// with <c>services.Replace(...)</c>, composes one. A registration sees only what was registered before it, so this
/// check reads the composed service collection once every feature has registered. It counts registrations rather than
/// resolving them.
/// <para>
/// The two converters follow one policy: the default node converter forwards to the composed definition converter. A
/// host node converter beside the default definition converter, or a host definition converter of a known type that
/// does not implement <see cref="IElsa3NodeConverter"/> beside the default node converter, is a partial replacement.
/// A host factory registration hides its type, so the second case is then reported when a node converter is resolved.
/// </para>
/// </remarks>
internal sealed class Elsa3MappingCompositionCheck(Elsa3MappingComposition composition) : IShellInitializer
{
    internal static readonly IReadOnlyList<Type> ReplacementContracts =
        [typeof(IElsa3DefinitionConverter), typeof(IElsa3NodeConverter), typeof(IElsa3ActivityMap)];

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var contract in ReplacementContracts)
        {
            var registrations = Registrations(contract);
            if (registrations.Length > 1)
                throw new MultipleElsa3MappingRegistrationsException(contract, registrations.Select(Describe).ToArray());
        }

        CheckConverterSet();
        return Task.CompletedTask;
    }

    private void CheckConverterSet()
    {
        if (Registrations(typeof(IElsa3DefinitionConverter)).SingleOrDefault() is not { } definition ||
            Registrations(typeof(IElsa3NodeConverter)).SingleOrDefault() is not { } node)
            return;

        var definitionIsDefault = Equals(definition.ImplementationFactory, Elsa3DefaultConverters.DefinitionConverter);
        var nodeIsDefault = Equals(node.ImplementationFactory, Elsa3DefaultConverters.NodeConverter);
        if (definitionIsDefault && !nodeIsDefault)
            throw new PartialElsa3ConverterReplacementException(typeof(IElsa3NodeConverter), Describe(node), typeof(IElsa3DefinitionConverter));

        if (nodeIsDefault && !definitionIsDefault &&
            ImplementationType(definition) is { } definitionType &&
            !typeof(IElsa3NodeConverter).IsAssignableFrom(definitionType))
            throw new PartialElsa3ConverterReplacementException(typeof(IElsa3DefinitionConverter), Describe(definition), typeof(IElsa3NodeConverter));
    }

    private ServiceDescriptor[] Registrations(Type contract) =>
        composition.Services
            .Where(descriptor => descriptor.ServiceType == contract && !descriptor.IsKeyedService)
            .ToArray();

    private static Type? ImplementationType(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    /// <summary>Registers the check once per service collection, in the Prepare phase, before any startup work.</summary>
    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(Elsa3MappingComposition)))
            return;

        services.AddSingleton(new Elsa3MappingComposition(services));
        services.AddShellInitializer<Elsa3MappingCompositionCheck>(LifecyclePhase.Prepare, 0);
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is { } implementationType
            ? $"'{implementationType}'"
            : descriptor.ImplementationInstance?.GetType().FullName is { } instanceType
                ? $"an instance of '{instanceType}'"
                : "a factory registration";
}
