using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Default in-memory <see cref="IActivityConstructorRegistry"/>. One instance per host; populated at
/// startup, read synchronously afterward. Enforces one-constructor-per-descriptor-type: a second,
/// differently-typed constructor for the same descriptor type throws
/// <see cref="DuplicateActivityConstructorException"/> (re-adding the same constructor type is idempotent).
/// </summary>
public sealed class ActivityConstructorRegistry : IActivityConstructorRegistry
{
    private readonly Dictionary<string, IActivityConstructor> _byType = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public void Add(IActivityConstructor constructor)
    {
        lock (_gate)
        {
            if (_byType.TryGetValue(constructor.DescriptorType, out var existing) && existing.GetType() != constructor.GetType())
                throw new DuplicateActivityConstructorException(constructor.DescriptorType, existing.GetType(), constructor.GetType());

            _byType[constructor.DescriptorType] = constructor;
        }
    }

    public void AddAll(IEnumerable<IActivityConstructor> constructors)
    {
        foreach (var constructor in constructors)
            Add(constructor);
    }

    public IActivityConstructor Resolve(string descriptorType)
    {
        lock (_gate)
        {
            if (!_byType.TryGetValue(descriptorType, out var constructor))
                throw new UnknownDescriptorTypeException(descriptorType);

            return constructor;
        }
    }
}
