namespace Elsa3.Mapping.ActivityMap;

/// <summary>What an Elsa 3 activity type becomes in Elsa 4. A closed hierarchy: see the nested records.</summary>
public abstract record Elsa3ActivityTarget
{
    private Elsa3ActivityTarget()
    {
    }

    /// <summary>An Elsa 4 catalog activity, keyed by its CLR full type name; the version is resolved from the catalog.</summary>
    public sealed record CatalogActivity(string ActivityTypeKey) : Elsa3ActivityTarget;

    /// <summary>An engine intrinsic, placed by its stable version id; it has no catalog row.</summary>
    public sealed record Intrinsic(string ActivityVersionId) : Elsa3ActivityTarget;

    /// <summary>Graph-level structure that a later conversion stage owns. No built-in entry uses it since spec 239.</summary>
    public sealed record GraphLevel(string Reason, string OwningIssue) : Elsa3ActivityTarget;

    /// <summary>A flowchart construct graph conversion consumes: it becomes connections or the start node, never a node.</summary>
    public sealed record GraphConstruct(Elsa3GraphConstructKind Kind) : Elsa3ActivityTarget;

    /// <summary>An activity Elsa 4 cannot express.</summary>
    public sealed record NoCounterpart(string Reason) : Elsa3ActivityTarget;
}
