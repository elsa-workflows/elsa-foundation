using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Behavioral.Infrastructure;

/// <summary>The concrete activity types used by the side-effect profile contract test.</summary>
public static class ActivityContractInventory
{
    /// <summary>One anchor type per shipped activity assembly; the sweep runs over their assemblies.</summary>
    private static readonly Type[] Anchors =
    [
        typeof(Bpmn.Activities.BpmnProcess),
        typeof(Compensation.Activities.CompensationScope),
        typeof(If.Activities.If),
        typeof(DispatchWorkflow.Runtime.Activities.DispatchWorkflow),
        typeof(Flowchart.Activities.Flowchart),
        typeof(Graph.Runtime.Activities.GraphActivity),
        typeof(Http.Activities.HttpEndpoint),
        typeof(Primitives.Activities.WriteLine),
        typeof(Scheduling.Activities.Delay),
        typeof(Scripting.Activities.RunJavaScript),
        typeof(Sequence.Activities.Sequence)
    ];

    /// <summary>Every concrete activity type the library ships, in stable name order.</summary>
    public static IReadOnlyList<Type> Activities { get; } = Anchors
        .Select(anchor => anchor.Assembly)
        .Distinct()
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IActivity).IsAssignableFrom(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();
}
