using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Events;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.EventSurfaceTests;

/// <summary>
/// SC-015 + framework §2.6.1 intent-revealing-methods sub-rule: no <see cref="IDomainEvent"/>
/// in either Core assembly exposes a public mutable collection. Acceptable read shapes are
/// <see cref="IReadOnlyList{T}"/>, <see cref="IReadOnlyCollection{T}"/>,
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/>. Contribution APIs are <c>Add*(...)</c>
/// methods on the event, never raw collection access.
/// </summary>
public sealed class NoRawCollectionsTests
{
    private static readonly Type[] ForbiddenPublicReadCollectionTypes =
    [
        typeof(ICollection<>),
        typeof(IList<>),
        typeof(List<>),
        typeof(HashSet<>),
        typeof(IDictionary<,>),
        typeof(Dictionary<,>),
    ];

    public static IEnumerable<object[]> AllDomainEventTypes()
    {
        var coreAssembly = typeof(OnDraftCreated).Assembly;
        var validationsCoreAssembly = typeof(OnDraftValidating).Assembly;

        foreach (var t in DomainEventTypesIn(coreAssembly))
            yield return [t];
        foreach (var t in DomainEventTypesIn(validationsCoreAssembly))
            yield return [t];
    }

    [Theory]
    [MemberData(nameof(AllDomainEventTypes))]
    public void Event_does_not_expose_public_mutable_collection(Type eventType)
    {
        var offending = eventType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => IsForbidden(p.PropertyType))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(offending);
    }

    private static bool IsForbidden(Type t)
    {
        if (!t.IsGenericType) return false;
        var def = t.GetGenericTypeDefinition();
        return ForbiddenPublicReadCollectionTypes.Contains(def);
    }

    private static IEnumerable<Type> DomainEventTypesIn(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t));
}
