using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Events;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.EventSurfaceTests;

/// <summary>
/// SC-015 + framework §2.6.1 intent-revealing-methods sub-rule (Unit C Phase-3): every
/// event in <c>Workflows.Design.Core</c> and <c>Workflows.Design.Validations.Core</c> is a
/// <c>sealed class</c> — NOT a <c>record</c>. Scans every <see cref="IEvent"/>.
/// Records' value-equality semantics conflict with the contribution pattern (two events
/// with the same payload would compare equal); the sealed-class form is the canonical
/// per-§2.6.1 shape.
/// </summary>
public sealed class MethodPatternTests
{
    public static IEnumerable<object[]> AllPublishedEventTypes()
    {
        var coreAssembly = typeof(DraftCreated).Assembly;
        var validationsCoreAssembly = typeof(DraftValidating).Assembly;

        foreach (var t in PublishedEventTypesIn(coreAssembly))
            yield return [t];
        foreach (var t in PublishedEventTypesIn(validationsCoreAssembly))
            yield return [t];
    }

    [Theory]
    [MemberData(nameof(AllPublishedEventTypes))]
    public void Event_is_sealed_class(Type eventType)
    {
        Assert.True(eventType.IsClass, $"{eventType.Name} must be a class, not a struct or interface");
        Assert.True(eventType.IsSealed, $"{eventType.Name} must be sealed per framework §2.6.1");
    }

    [Theory]
    [MemberData(nameof(AllPublishedEventTypes))]
    public void Event_is_not_a_record(Type eventType)
    {
        // C# records compile to classes with a synthesised <Clone>$ method and an EqualityContract
        // property. Both presences are reliable record markers.
        var cloneMethod = eventType.GetMethod(
            "<Clone>$",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        );
        var equalityContract = eventType.GetProperty(
            "EqualityContract",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
        );

        Assert.True(
            cloneMethod is null && equalityContract is null,
            $"{eventType.Name} must be a sealed class, not a record"
        );
    }

    private static IEnumerable<Type> PublishedEventTypesIn(System.Reflection.Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IEvent).IsAssignableFrom(t));
}
