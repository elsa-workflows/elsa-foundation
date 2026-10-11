using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Behavioral.Infrastructure;

/// <summary>
/// The declared contract surface of every shipped activity, read from the same attributes the publish compiler
/// pins from. This is the <em>universe</em> the behavioural drive is measured against: a contract diff can prove an
/// outcome is declared, only a drive can prove it is reachable, and only comparing the two can prove a declared
/// outcome is <b>dead</b> — the failure mode a contract diff structurally cannot see (#1119).
/// </summary>
public static class ActivityContractInventory
{
    /// <summary>The implicit outcome the runtime completes with when an activity declares none.</summary>
    public const string ImplicitOutcome = "Done";

    /// <summary>One anchor type per shipped activity assembly; the sweep runs over their assemblies.</summary>
    private static readonly Type[] Anchors =
    [
        typeof(Bpmn.Activities.BpmnProcess),
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

    /// <summary>
    /// Activities that never complete, so the implicit-<c>Done</c> rule below does not apply to them: they have no
    /// reachable outcome at all and asserting one would be asserting a contradiction.
    /// </summary>
    /// <remarks>
    /// <c>Fault</c> always returns a fault transition. Worth knowing: because it declares no <c>[ActivityOutcome]</c>,
    /// the scanner emits no outcomes facet and the studio applies its own <c>Done</c> default — so the designer
    /// currently offers a <c>Done</c> port on <c>Fault</c> that can never be taken. That is a studio-side default
    /// rather than anything this activity declares, which is why it is recorded here instead of being "fixed" by
    /// adding an outcome the activity would still never emit.
    /// </remarks>
    private static readonly HashSet<Type> TerminalActivities = [typeof(Primitives.Activities.Fault)];

    /// <summary>
    /// The statically declared outcome ports of an activity. An activity with no <c>[ActivityOutcome]</c> still has
    /// the implicit <c>Done</c> completion, which is just as much a branch an author wires to, so it is included.
    /// </summary>
    /// <remarks>
    /// <c>[ActivityValueOutcomes]</c> ports are deliberately <em>not</em> listed: they are per-node, derived from an
    /// authored value at publish time, so there is no fixed set to enumerate here. They are covered by explicit
    /// drives instead (see the SendHttpRequest and RunJavaScript drivers).
    /// </remarks>
    public static IReadOnlyList<string> DeclaredOutcomes(Type activityType)
    {
        if (TerminalActivities.Contains(activityType))
            return [];

        var declared = activityType
            .GetCustomAttributes<ActivityOutcomeAttribute>(inherit: true)
            .Select(attribute => attribute.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return declared.Length == 0 ? [ImplicitOutcome] : declared;
    }

    /// <summary>The declared output projections of an activity's atomic result type, by contract key.</summary>
    public static IReadOnlyList<string> DeclaredOutputs(Type activityType)
    {
        var resultType = activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Distinct()
            .SingleOrDefault();

        if (resultType is null)
            return [];

        return resultType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<OutputAttribute>(inherit: true)))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => candidate.Attribute!.Key ?? candidate.Property.Name)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The required inputs of an activity, by contract key — the ones absence of must fault cleanly.</summary>
    public static IReadOnlyList<string> RequiredInputs(Type activityType) => activityType
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(property => (Property: property, Input: property.GetCustomAttribute<ActivityInputAttribute>(inherit: true)))
        .Where(candidate => candidate.Input is not null)
        .Where(candidate => candidate.Property.GetCustomAttribute<RequiredAttribute>(inherit: true) is not null ||
                            candidate.Property.GetCustomAttributes()
                                .Any(attribute => attribute.GetType().FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute"))
        .Select(candidate => candidate.Input!.Key ?? candidate.Property.Name)
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToArray();
}
