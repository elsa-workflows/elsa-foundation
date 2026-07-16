using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Default in-memory <see cref="IActivityConstructorRegistry"/>. Enforces one constructor per exact
/// Runtime consumer/schema pair: every second claim for the same pair throws
/// <see cref="DuplicateActivityConstructorException"/>, including another instance of the same type.
/// </summary>
public sealed class ActivityConstructorRegistry : IActivityConstructorRegistry
{
    private readonly Dictionary<(string ConsumerKey, string SchemaVersion), IActivityConstructor> _constructors = new();
    private readonly Lock _gate = new();

    public void Add(IActivityConstructor constructor)
        => AddAll([constructor]);

    public void AddAll(IEnumerable<IActivityConstructor> constructors)
    {
        ArgumentNullException.ThrowIfNull(constructors);
        var additions = constructors.SelectMany(constructor => Claims(constructor).Select(key => (Key: key, Constructor: constructor))).ToArray();

        lock (_gate)
        {
            var pending = new Dictionary<(string ConsumerKey, string SchemaVersion), IActivityConstructor>();
            foreach (var addition in additions)
            {
                if (_constructors.TryGetValue(addition.Key, out var existing) || pending.TryGetValue(addition.Key, out existing))
                    throw new DuplicateActivityConstructorException(addition.Key.ConsumerKey, addition.Key.SchemaVersion, existing.GetType(), addition.Constructor.GetType());
                pending.Add(addition.Key, addition.Constructor);
            }

            foreach (var addition in pending)
                _constructors.Add(addition.Key, addition.Value);
        }
    }

    private static IReadOnlyCollection<(string ConsumerKey, string SchemaVersion)> Claims(IActivityConstructor constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        ArgumentException.ThrowIfNullOrWhiteSpace(constructor.ConsumerKey);
        if (constructor.SupportedSchemaVersions.Count == 0)
            throw new ArgumentException($"Activity constructor '{constructor.GetType().FullName}' must support at least one descriptor schema.", nameof(constructor));

        var schemas = constructor.SupportedSchemaVersions.ToArray();
        foreach (var schemaVersion in schemas)
            ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        if (schemas.Distinct(StringComparer.Ordinal).Count() != schemas.Length)
            throw new ArgumentException($"Activity constructor '{constructor.GetType().FullName}' declares a descriptor schema more than once.", nameof(constructor));
        return schemas.Select(schemaVersion => (constructor.ConsumerKey, schemaVersion)).ToArray();
    }

    public IActivityConstructor Resolve(string consumerKey, string schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        lock (_gate)
        {
            if (_constructors.TryGetValue((consumerKey, schemaVersion), out var constructor))
                return constructor;

            var supportedSchemas = _constructors.Keys
                .Where(key => StringComparer.Ordinal.Equals(key.ConsumerKey, consumerKey))
                .Select(key => key.SchemaVersion)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (supportedSchemas.Length == 0)
                throw new UnknownActivityConsumerException(consumerKey, schemaVersion);

            throw new UnsupportedActivityDescriptorSchemaException(consumerKey, schemaVersion, supportedSchemas);
        }
    }

    public bool TryResolve(string consumerKey, string schemaVersion, out IActivityConstructor? constructor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        lock (_gate)
            return _constructors.TryGetValue((consumerKey, schemaVersion), out constructor);
    }

    public IReadOnlyCollection<string> GetSupportedSchemaVersions(string consumerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        lock (_gate)
            return _constructors.Keys
                .Where(key => StringComparer.Ordinal.Equals(key.ConsumerKey, consumerKey))
                .Select(key => key.SchemaVersion)
                .Order(StringComparer.Ordinal)
                .ToArray();
    }
}
