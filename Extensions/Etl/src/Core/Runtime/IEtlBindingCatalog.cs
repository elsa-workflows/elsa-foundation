namespace Elsa.Etl.Core.Runtime;

/// <summary>Lists safe host-approved binding names for one trusted ETL scope.</summary>
/// <remarks>Neither file paths, revision handles nor digests are authoring metadata.</remarks>
public interface IEtlBindingCatalog
{
    /// <summary>Returns at most <paramref name="take"/> entries in ordinal alias/role order after an exclusive pair.</summary>
    /// <remarks>The boundary pair must be both null or both present; take is between 1 and 1000.</remarks>
    IReadOnlyList<EtlBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take);
}

/// <summary>A current binding's safe alias and role: Source, Lookup, MainOutput or ErrorOutput.</summary>
public sealed record EtlBindingSummary(string Alias, string Role);
