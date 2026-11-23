using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Xunit;

namespace Elsa.Activities.Design.Tests.Contracts;

/// <summary>
/// Spec 1208 FR-002 and FR-002b (the <c>Done</c> rule) over every shipped activity: an activity whose static outcomes
/// have no <c>Done</c> must guarantee a selection on every completion (<see cref="SelectsOutcomeOnEveryCompletionAttribute"/>),
/// or publication refuses each of its nodes with <c>VF-AUTH-013</c>; and an activity that declares <c>Done</c> must not
/// claim that guarantee, because <c>Done</c> is never selected together with a branch.
/// </summary>
public sealed class OutcomeSelectionGuaranteeConformanceTests
{
    private const string Done = "Done";

    [Fact]
    public void Every_shipped_activity_without_Done_guarantees_a_selection()
    {
        var missing = ActivityTypes()
            .Where(type => Outcomes(type) is { Count: > 0 } outcomes && !outcomes.Contains(Done))
            .Where(type => !type.IsDefined(typeof(SelectsOutcomeOnEveryCompletionAttribute), inherit: true))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void No_shipped_activity_declares_Done_next_to_a_selection_guarantee()
    {
        var conflicting = ActivityTypes()
            .Where(type => Outcomes(type).Contains(Done) && type.IsDefined(typeof(SelectsOutcomeOnEveryCompletionAttribute), inherit: true))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(conflicting);
    }

    [Fact]
    public void The_known_branching_activities_carry_the_guarantee()
    {
        var guaranteed = ActivityTypes()
            .Where(type => type.IsDefined(typeof(SelectsOutcomeOnEveryCompletionAttribute), inherit: true))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("If", guaranteed);
        Assert.Contains("Switch", guaranteed);
        Assert.Contains("DispatchWorkflow", guaranteed);
    }

    private static IReadOnlyCollection<string> Outcomes(Type type) =>
        type.GetCustomAttributes<ActivityOutcomeAttribute>(inherit: true).Select(attribute => attribute.Key).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<Type> ActivityTypes() =>
        ActivityContractSurfaceSnapshotTests.ShippedActivityAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.GetCustomAttributes<ActivityOutcomeAttribute>(inherit: true).Any());
}
