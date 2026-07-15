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
    {
        lock (_gate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(constructor.ConsumerKey);
            if (constructor.SupportedSchemaVersions.Count == 0)
                throw new ArgumentException($"Activity constructor '{constructor.GetType().FullName}' must support at least one descriptor schema.", nameof(constructor));

            var keys = constructor.SupportedSchemaVersions.Select(schemaVersion =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
                return (ConsumerKey: constructor.ConsumerKey, SchemaVersion: schemaVersion);
            }).ToArray();

            // Preflight every claim before mutating so a conflict on a later schema cannot leave a
            // partially registered multi-schema constructor.
            foreach (var key in keys)
            {
                if (_constructors.TryGetValue(key, out var existing))
                    throw new DuplicateActivityConstructorException(key.ConsumerKey, key.SchemaVersion, existing.GetType(), constructor.GetType());
            }

            foreach (var key in keys)
                _constructors[key] = constructor;
        }
    }

    public void AddAll(IEnumerable<IActivityConstructor> constructors)
    {
        foreach (var constructor in constructors)
            Add(constructor);
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
}
