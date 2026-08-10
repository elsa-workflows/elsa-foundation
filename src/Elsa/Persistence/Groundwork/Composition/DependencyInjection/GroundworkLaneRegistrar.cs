using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers one persistence lane's Groundwork adapters against a named target.
/// <para>
/// The adapters take <see cref="IDocumentStore"/> (and sometimes <see cref="IBoundedDocumentStore"/>) by
/// plain constructor injection and know nothing about targets. Rather than rewrite every adapter to resolve
/// a keyed store, the registrar supplies the target's store as an explicit constructor argument and lets the
/// container fill the rest. A lane therefore moves to another database by changing one registration call.
/// </para>
/// </summary>
public sealed class GroundworkLaneRegistrar
{
    private static readonly ConcurrentDictionary<Type, StoreBoundFactory> Factories = new();

    private readonly IServiceCollection services;

    /// <summary>
    /// The name exactly as the caller gave it. A lane registered without one is <em>unspecified</em> rather
    /// than explicitly default, which is what lets an aggregate preset register a lane while the host still
    /// places it on a named target.
    /// </summary>
    private readonly string? configuredTargetName;

    internal GroundworkLaneRegistrar(IServiceCollection services, string? targetName)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        configuredTargetName = targetName;
        TargetName = GroundworkTargetNames.Normalize(targetName);
    }

    /// <summary>The target every adapter registered through this registrar reads and writes.</summary>
    public string TargetName { get; }

    /// <summary>
    /// Replaces <typeparamref name="TContract"/> with a target-bound <typeparamref name="TImplementation"/>.
    /// The replacement is unconditional so a Groundwork adapter wins over any previously registered store
    /// regardless of feature composition order, but it removes only unkeyed registrations: other targets'
    /// keyed registrations must survive.
    /// </summary>
    public GroundworkLaneRegistrar Replace<TContract, TImplementation>()
        where TContract : class
        where TImplementation : class, TContract
    {
        RemoveUnkeyed<TContract>();
        services.AddScoped<TContract>(serviceProvider => Create<TImplementation>(serviceProvider));
        return this;
    }

    /// <summary>Registers a target-bound <typeparamref name="TImplementation"/> only if nothing claims the contract.</summary>
    public GroundworkLaneRegistrar TryAdd<TContract, TImplementation>()
        where TContract : class
        where TImplementation : class, TContract
    {
        services.TryAddScoped<TContract>(serviceProvider => Create<TImplementation>(serviceProvider));
        return this;
    }

    /// <summary>Registers a target-bound concrete adapter that is resolved by its own type, replacing any prior one.</summary>
    public GroundworkLaneRegistrar AddSelf<TImplementation>()
        where TImplementation : class
    {
        RemoveUnkeyed<TImplementation>();
        services.AddScoped(serviceProvider => Create<TImplementation>(serviceProvider));
        return this;
    }

    /// <summary>Registers a target-bound concrete adapter that is resolved by its own type.</summary>
    public GroundworkLaneRegistrar TryAddSelf<TImplementation>()
        where TImplementation : class
    {
        services.TryAddScoped(serviceProvider => Create<TImplementation>(serviceProvider));
        return this;
    }

    /// <summary>
    /// Serves <typeparamref name="TContract"/> from an adapter this lane already registered. The target
    /// binding lives on that adapter's own registration; this only adds a further contract pointing at the
    /// same scoped instance, so several contracts share one adapter rather than each getting their own.
    /// </summary>
    public GroundworkLaneRegistrar Alias<TContract, TImplementation>()
        where TContract : class
        where TImplementation : class, TContract
    {
        RemoveUnkeyed<TContract>();
        services.AddScoped<TContract>(serviceProvider => serviceProvider.GetRequiredService<TImplementation>());
        return this;
    }

    /// <summary>Contributes this lane's storage manifest, recording that this target owns it.</summary>
    public GroundworkLaneRegistrar Manifest<TSource>()
        where TSource : class, IGroundworkStorageManifestSource
    {
        services.AddGroundworkManifestSource<TSource>(configuredTargetName);
        return this;
    }

    /// <summary>
    /// This target's document store, for the few registrations that construct an adapter by hand rather than
    /// through <see cref="Create{T}"/>. Applies the same default-target fallback.
    /// </summary>
    public IDocumentStore Store(IServiceProvider serviceProvider) =>
        (IDocumentStore)StoreBoundFactory.ResolveStore(serviceProvider, TargetName, typeof(IDocumentStore));

    /// <summary>Creates <typeparamref name="T"/> with this target's store supplied explicitly.</summary>
    public T Create<T>(IServiceProvider serviceProvider) where T : class =>
        (T)Factories.GetOrAdd(typeof(T), StoreBoundFactory.For)
            .Create(serviceProvider, TargetName);

    private void RemoveUnkeyed<TContract>()
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(TContract) && !descriptor.IsKeyedService)
                services.RemoveAt(index);
        }
    }

    /// <summary>
    /// A compiled factory for one adapter type, knowing which of its constructor parameters are document
    /// stores. Those are supplied from the target's keyed registration; everything else comes from the
    /// container as usual.
    /// </summary>
    private sealed class StoreBoundFactory
    {
        private readonly ObjectFactory factory;
        private readonly Type[] storeParameterTypes;

        private StoreBoundFactory(ObjectFactory factory, Type[] storeParameterTypes)
        {
            this.factory = factory;
            this.storeParameterTypes = storeParameterTypes;
        }

        public static StoreBoundFactory For(Type implementationType)
        {
            var constructor = SelectConstructor(implementationType);
            var parameters = constructor.GetParameters();
            var storeParameterTypes = parameters
                .Select(parameter => parameter.ParameterType)
                .Where(IsDocumentStore)
                .ToArray();
            if (storeParameterTypes.Length == 0)
                return new(ActivatorUtilities.CreateFactory(implementationType, []), storeParameterTypes);

            // Store parameters are filled positionally from the supplied arguments, in the same order they
            // were collected, so a constructor taking both contracts gets each one's own instance.
            var storeArgumentIndexByParameter = new int[parameters.Length];
            var storeArgumentIndex = 0;
            for (var index = 0; index < parameters.Length; index++)
            {
                storeArgumentIndexByParameter[index] = IsDocumentStore(parameters[index].ParameterType)
                    ? storeArgumentIndex++
                    : -1;
            }

            // Bind the constructor chosen above rather than letting ActivatorUtilities search by argument
            // type: several adapters have overlapping constructors that all accept a document store, and the
            // search rejects that as ambiguous. Compile it once — adapters are scoped, so this runs on every
            // DI scope and a reflective Invoke per resolution would land on the runtime checkpoint path.
            var providerParameter = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
            var argumentsParameter = Expression.Parameter(typeof(object?[]), "arguments");
            var argumentExpressions = new Expression[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                var storeIndex = storeArgumentIndexByParameter[index];
                argumentExpressions[index] = storeIndex >= 0
                    ? Expression.Convert(
                        Expression.ArrayIndex(argumentsParameter, Expression.Constant(storeIndex)),
                        parameter.ParameterType)
                    : Expression.Convert(
                        Expression.Call(
                            ResolveMethod,
                            providerParameter,
                            Expression.Constant(parameter, typeof(ParameterInfo))),
                        parameter.ParameterType);
            }

            var compiled = Expression
                .Lambda<Func<IServiceProvider, object?[], object>>(
                    Expression.New(constructor, argumentExpressions),
                    providerParameter,
                    argumentsParameter)
                .Compile();
            return new((serviceProvider, arguments) => compiled(serviceProvider, arguments!), storeParameterTypes);
        }

        private static readonly MethodInfo ResolveMethod =
            typeof(StoreBoundFactory).GetMethod(nameof(Resolve), BindingFlags.NonPublic | BindingFlags.Static)!;

        /// <summary>Fills a non-store parameter the way the container would, honoring optional defaults.</summary>
        private static object? Resolve(IServiceProvider serviceProvider, ParameterInfo parameter)
        {
            var service = serviceProvider.GetService(parameter.ParameterType);
            if (service is not null)
                return service;
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;
            return serviceProvider.GetRequiredService(parameter.ParameterType);
        }

        public object Create(IServiceProvider serviceProvider, string targetName)
        {
            if (storeParameterTypes.Length == 0)
                return factory(serviceProvider, null);

            // Resolve each store parameter by its own contract. The scoped Groundwork adapter happens to
            // satisfy both, but a host that registers a raw provider store as the ambient IDocumentStore
            // does not get a bounded store from it, so one instance cannot stand in for the other.
            var arguments = storeParameterTypes
                .Select(storeType => ResolveStore(serviceProvider, targetName, storeType))
                .ToArray();
            return factory(serviceProvider, arguments);
        }

        /// <summary>
        /// Resolves the target's store. The default lane falls back to an ambient unkeyed
        /// <see cref="IDocumentStore"/>, which is how a host that supplies its own store — a test harness with
        /// an in-memory one, say — keeps working without declaring a Groundwork target at all. A named lane
        /// gets no such fallback: quietly reading and writing the default database instead of the one the
        /// operator named is the exact failure this whole mechanism exists to prevent.
        /// </summary>
        internal static object ResolveStore(
            IServiceProvider serviceProvider,
            string targetName,
            Type storeType)
        {
            var keyed = serviceProvider.GetKeyedService(storeType, targetName);
            if (keyed is not null)
                return keyed;

            if (!GroundworkTargetNames.IsDefault(targetName))
            {
                throw new InvalidOperationException(
                    $"Groundwork target '{targetName}' has no {storeType.Name}. A lane bound to a named " +
                    "target must not fall back to the default store; declare the target on a provider feature.");
            }

            var ambient = serviceProvider.GetService(storeType);
            if (ambient is not null)
                return ambient;

            // A host may supply only a plain document store. Bounded queries are then served by that store
            // when it implements the contract, and are genuinely unavailable when it does not.
            if (storeType == typeof(IBoundedDocumentStore) &&
                serviceProvider.GetService<IDocumentStore>() is IBoundedDocumentStore bounded)
            {
                return bounded;
            }

            return serviceProvider.GetRequiredService(storeType);
        }

        /// <summary>
        /// Picks the constructor the container itself would pick: an explicitly marked one, otherwise the
        /// greediest public one. Adapters have a single public constructor in practice; the ordering only
        /// keeps the choice deterministic if that ever stops being true.
        /// </summary>
        private static ConstructorInfo SelectConstructor(Type implementationType)
        {
            var constructors = implementationType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Groundwork adapter '{implementationType.FullName}' has no public constructor.");
            }

            return constructors.FirstOrDefault(candidate =>
                       candidate.IsDefined(typeof(ActivatorUtilitiesConstructorAttribute), inherit: false))
                   ?? constructors
                       .OrderByDescending(candidate => candidate.GetParameters().Length)
                       .ThenBy(candidate => candidate.ToString(), StringComparer.Ordinal)
                       .First();
        }

        private static bool IsDocumentStore(Type parameterType) =>
            parameterType == typeof(IDocumentStore) || parameterType == typeof(IBoundedDocumentStore);
    }
}

/// <summary>Entry point for registering a persistence lane's Groundwork adapters against a target.</summary>
public static class GroundworkLaneRegistration
{
    /// <summary>Begins registering one lane's adapters against <paramref name="targetName"/>.</summary>
    public static GroundworkLaneRegistrar GroundworkLane(this IServiceCollection services, string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new(services, targetName);
    }
}
