using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Events;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.EventSurfaceTests;

/// <summary>
/// SC-015 + framework §2.6.1 intent-revealing-methods sub-rule (Unit C Phase-3): every
/// <see cref="IDomainEvent"/> in <c>Workflows.Design.Core</c> and
/// <c>Workflows.Design.Validations.Core</c> is a <c>sealed class</c> — NOT a <c>record</c>.
/// Records' value-equality semantics conflict with the contribution pattern (two events
/// with the same payload would compare equal); the sealed-class form is the canonical
/// per-§2.6.1 shape.
/// </summary>
public sealed class MethodPatternTests
{
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
    public void Event_is_sealed_class(Type eventType)
    {
        Assert.True(eventType.IsClass, $"{eventType.Name} must be a class, not a struct or interface");
        Assert.True(eventType.IsSealed, $"{eventType.Name} must be sealed per framework §2.6.1");
    }

    [Theory]
    [MemberData(nameof(AllDomainEventTypes))]
    public void Event_is_not_a_record(Type eventType)
    {
        // C# records compile to classes with a synthesised <Clone>$ method and an EqualityContract
        // property. Both presences are reliable record markers.
        var cloneMethod = eventType.GetMethod("<Clone>$",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var equalityContract = eventType.GetProperty("EqualityContract",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        Assert.True(cloneMethod is null && equalityContract is null,
            $"{eventType.Name} must be a sealed class, not a record");
    }

    private static IEnumerable<Type> DomainEventTypesIn(System.Reflection.Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t));
}
