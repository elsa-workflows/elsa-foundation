namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// What a structure states about how two of its direct children relate in time within one run of the structure
/// (spec 266, D-2). Asked about one child paired with itself, it states whether two runs of that child can be in
/// flight together: <see cref="Unordered"/> means they can; any other declared value means they never overlap.
/// </summary>
public enum ActivityChildTimeRelation
{
    /// <summary>
    /// The structure does not say. For two different children this is treated as unordered, and the finding says that
    /// the structure does not declare how its children are ordered. For a child paired with itself it is treated as
    /// "runs never overlap".
    /// </summary>
    Unspecified,

    /// <summary>Every run of the first child ends before any run of the second child starts.</summary>
    FirstBeforeSecond,

    /// <summary>Every run of the second child ends before any run of the first child starts.</summary>
    SecondBeforeFirst,

    /// <summary>Both can be in flight together: the definition does not fix their order.</summary>
    Unordered,

    /// <summary>At most one of the two runs in one run of the structure.</summary>
    MutuallyExclusive
}

internal static class ActivityChildTimeRelationDefaults
{
    /// <summary>Adapts a completion-order declaration: a child completed before another runs before it.</summary>
    public static ActivityChildTimeRelation FromCompletedBefore(
        Func<ActivityNode, string, IReadOnlyList<string>?> completedBefore,
        ActivityNode activity,
        string firstChildNodeId,
        string secondChildNodeId)
    {
        if (StringComparer.Ordinal.Equals(firstChildNodeId, secondChildNodeId))
            return ActivityChildTimeRelation.Unspecified;
        if (completedBefore(activity, secondChildNodeId)?.Contains(firstChildNodeId, StringComparer.Ordinal) == true)
            return ActivityChildTimeRelation.FirstBeforeSecond;
        if (completedBefore(activity, firstChildNodeId)?.Contains(secondChildNodeId, StringComparer.Ordinal) == true)
            return ActivityChildTimeRelation.SecondBeforeFirst;
        return ActivityChildTimeRelation.Unspecified;
    }
}
