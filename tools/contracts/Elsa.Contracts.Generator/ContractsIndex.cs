namespace Elsa.Contracts.Generator;

/// <summary>
/// docs/contracts/index.json — the entry point: every published identifier mapped to the one file that
/// carries it.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a benchmarked consumer given the contracts cost MORE than one given nothing
/// (209k tokens against a 163k baseline). The content was not the problem — the median fragment is about
/// 810 tokens, cheap for anyone who knows which file to open. Without an index there is no way to know, so
/// a consumer greps 95 files or opens the largest one hoping; the no-contracts baseline just asked a live
/// server fifteen targeted questions, which beats searching a corpus you cannot read.
/// </para>
/// <para>
/// An index is the whole fix, and it is a few KB. <c>Elsa.SetVariable</c> is the worked example: every
/// benchmark session needed it, and it lives in <c>Elsa.Activities.Design.Api.json</c> — a filename that
/// reads like API plumbing.
/// </para>
/// </remarks>
public sealed record ContractsIndex(
    string SchemaVersion,
    string HowToUse,
    IReadOnlyList<IndexEntry> Activities,
    IReadOnlyList<IndexEntry> Intrinsics,
    IReadOnlyList<IndexEntry> Structures,
    IReadOnlyList<IndexEntry> ExpressionTypes,
    IReadOnlyList<IndexEntry> Features,
    IReadOnlyList<IndexEntry> Routes);

/// <summary>One identifier and the file that carries it, relative to <c>docs/contracts/</c>.</summary>
public sealed record IndexEntry(string Key, string File);

public static class ContractsIndexBuilder
{
    public const string Guidance =
        "Resolve the key you need here, then open ONLY that file. The corpus is roughly 850k tokens and is "
        + "not meant to be read whole; a single fragment is about 810. `activities`, `intrinsics`, "
        + "`structures` and `expressionTypes` are keyed by the identifier you would author; `features` by "
        + "shells.json feature id; `routes` by 'VERB /path', pointing at the OpenAPI part that documents it.";

    public static ContractsIndex Build(
        IReadOnlyDictionary<string, ContractFragment> fragmentsByAssembly,
        IReadOnlyDictionary<string, string> routeToOpenApiPart)
    {
        var activities = new List<IndexEntry>();
        var intrinsics = new List<IndexEntry>();
        var structures = new List<IndexEntry>();
        var expressionTypes = new List<IndexEntry>();
        var features = new List<IndexEntry>();

        foreach (var (assembly, fragment) in fragmentsByAssembly)
        {
            var file = $"fragments/{assembly}.json";
            activities.AddRange(fragment.Activities.Select(activity => new IndexEntry(activity.ActivityTypeKey, file)));
            // Both the descriptor id (what a node carries as activityVersionId) and the type key: a consumer
            // may arrive with either, and having only one of them is the same dead end as having neither.
            intrinsics.AddRange(fragment.Intrinsics.Select(intrinsic => new IndexEntry(intrinsic.DescriptorId, file)));
            intrinsics.AddRange(fragment.Intrinsics.Select(intrinsic => new IndexEntry(intrinsic.ActivityTypeKey, file)));
            structures.AddRange(fragment.Structures.Select(structure => new IndexEntry(structure.Kind, file)));
            features.AddRange(fragment.Features.Select(feature => new IndexEntry(feature.Id, file)));
            if (fragment.Expressions is { } expressions)
                expressionTypes.AddRange(expressions.Descriptors.Select(descriptor => new IndexEntry(descriptor.Type, file)));
        }

        return new ContractsIndex(
            ContractFragment.CurrentSchemaVersion,
            Guidance,
            Sorted(activities),
            Sorted(intrinsics),
            Sorted(structures),
            Sorted(expressionTypes),
            Sorted(features),
            Sorted(routeToOpenApiPart.Select(pair => new IndexEntry(pair.Key, pair.Value))));
    }

    private static IReadOnlyList<IndexEntry> Sorted(IEnumerable<IndexEntry> entries) =>
        entries
            .DistinctBy(entry => (entry.Key, entry.File))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ThenBy(entry => entry.File, StringComparer.Ordinal)
            .ToArray();
}
