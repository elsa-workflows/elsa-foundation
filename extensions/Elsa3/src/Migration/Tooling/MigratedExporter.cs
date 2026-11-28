using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Migration.Tooling;

/// <summary>A published workflow version the run can export (the wrapper of a reusable among them).</summary>
internal sealed record ExportWorkflow(string SourceVersionId, string WorkflowDefinitionId, string WorkflowVersionId);

/// <summary>A published reusable activity version the run can export; its wrapper is an <see cref="ExportWorkflow"/> of the same source version.</summary>
internal sealed record ExportReusable(string SourceVersionId, string ActivityDefinitionVersionId);

/// <summary>What the run published, for <see cref="MigratedExporter"/>.</summary>
internal sealed record ExportCandidates(IReadOnlyList<ExportWorkflow> Workflows, IReadOnlyList<ExportReusable> Reusables);

/// <summary>Where each exported source version went, as paths relative to <c>--out</c> with '/' separators.</summary>
internal sealed record MigratedExportResult(
    IReadOnlyDictionary<string, string> ExportedTo,
    IReadOnlyDictionary<string, string> WrapperExportedTo);

/// <summary>
/// Writes <c>migrated/</c> (spec 1237 T022, FR-023, FR-024): one file per published workflow definition under
/// <c>migrated/workflows/</c>, and <c>migrated/activities/activities.json</c>. Both always exist. The published versions
/// are read back from the design stores and written through the JSON catalog writers, so the format has one owner
/// (research R-8). Every reusable version id, and every <c>activityVersionId</c> that names one, becomes the id the engine
/// gives the loaded version, <see cref="ActivityCatalogStableIds.VersionId"/>.
/// </summary>
internal static class MigratedExporter
{
    public const string WorkflowsFolderName = "workflows";
    public const string ActivitiesFolderName = "activities";
    public const string ActivitiesFileName = "activities.json";

    private const string ActivityVersionIdMember = "activityVersionId";

    public static async Task<MigratedExportResult> ExportAsync(
        IServiceProvider services,
        ExportCandidates candidates,
        string migratedFolder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidates);
        var workflowsFolder = Path.Combine(migratedFolder, WorkflowsFolderName);
        var activitiesFolder = Path.Combine(migratedFolder, ActivitiesFolderName);
        Directory.CreateDirectory(workflowsFolder);
        Directory.CreateDirectory(activitiesFolder);

        var read = await ReadAsync(services, candidates, cancellationToken);
        var serializer = services.GetRequiredService<IPayloadSerializer>();
        var rewrites = read.Reusables.ToDictionary(reusable => reusable.StoredVersionId, reusable => reusable.CatalogVersionId, StringComparer.Ordinal);

        var exportedTo = new Dictionary<string, string>(StringComparer.Ordinal);
        var wrapperExportedTo = new Dictionary<string, string>(StringComparer.Ordinal);
        var reusableSources = read.Reusables.Select(reusable => reusable.SourceVersionId).ToHashSet(StringComparer.Ordinal);

        var activities = Order(read.Reusables, rewrites);
        services.GetRequiredService<IJsonActivityCatalogWriter>().Write(
            Path.Combine(activitiesFolder, ActivitiesFileName),
            activities.Select(reusable => ToModel(reusable, rewrites)).ToArray(),
            cancellationToken);
        foreach (var reusable in read.Reusables)
            exportedTo[reusable.SourceVersionId] = RelativePath(ActivitiesFolderName, ActivitiesFileName);

        var workflowWriter = services.GetRequiredService<IJsonWorkflowCatalogWriter>();
        var definitions = read.Workflows
            .GroupBy(workflow => workflow.Version.DefinitionId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var fileNames = FileNames(definitions.Select(definition => definition.Key).ToArray());
        foreach (var definition in definitions)
        {
            var fileName = fileNames[definition.Key];

            var envelopes = definition
                .OrderBy(workflow => workflow.Version.SemVerSortKey, StringComparer.Ordinal)
                .Select(workflow => ToModel(workflow, serializer, rewrites))
                .ToArray();
            workflowWriter.Write(Path.Combine(workflowsFolder, fileName), envelopes, cancellationToken);

            var path = RelativePath(WorkflowsFolderName, fileName);
            foreach (var workflow in definition)
            {
                if (reusableSources.Contains(workflow.SourceVersionId))
                    wrapperExportedTo[workflow.SourceVersionId] = path;
                else
                    exportedTo[workflow.SourceVersionId] = path;
            }
        }

        return new MigratedExportResult(exportedTo, wrapperExportedTo);
    }

    /// <summary>
    /// The workflow file name of each definition id: <c>&lt;definitionId&gt;.json</c>, unless the id holds a character
    /// outside <c>[A-Za-z0-9._-]</c> or equals another id when case is ignored (Windows and macOS file systems ignore case).
    /// Such an id gets its portable characters, the others as '_', and the first 8 hex digits of its SHA-256, so two
    /// definitions never share a file and every platform can write it. The host's folder reader reads every file, whatever
    /// its name.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> FileNames(IReadOnlyList<string> definitionIds)
    {
        var caseCounts = definitionIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return definitionIds.ToDictionary(id => id, id =>
        {
            var portable = id.Length > 0 && id.All(IsPortable) && id is not ("." or "..");
            if (portable && caseCounts[id] == 1)
                return id + ".json";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..8].ToLowerInvariant();
            return new string(id.Select(character => IsPortable(character) ? character : '_').ToArray()) + "-" + hash + ".json";
        }, StringComparer.Ordinal);

        static bool IsPortable(char character) => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
    }

    /// <summary>
    /// Reads every candidate back from the design stores, in each persistence scope the run imported into. A candidate
    /// is found in the scope that holds it.
    /// </summary>
    private static async Task<ReadStores> ReadAsync(IServiceProvider services, ExportCandidates candidates, CancellationToken cancellationToken)
    {
        var reusables = new Dictionary<string, ReadReusable>(StringComparer.Ordinal);
        var workflows = new Dictionary<string, ReadWorkflow>(StringComparer.Ordinal);
        if (candidates.Reusables.Count == 0 && candidates.Workflows.Count == 0)
            return new([], []);

        await services.GetRequiredService<IPersistenceScopeRunner>().RunAsync(async (_, operationScope, token) =>
        {
            var scoped = operationScope.ServiceProvider;
            var publications = scoped.GetRequiredService<IActivityDefinitionVersionPublicationStore>();
            var versions = scoped.GetRequiredService<IActivityDefinitionVersionStore>();
            var definitions = scoped.GetRequiredService<IActivityDefinitionStore>();
            foreach (var candidate in candidates.Reusables.Where(candidate => !reusables.ContainsKey(candidate.SourceVersionId)))
            {
                if (await publications.FindAsync(candidate.ActivityDefinitionVersionId, token) is not { } publication)
                    continue;
                var version = await versions.GetAsync(candidate.ActivityDefinitionVersionId, token);
                var definition = await definitions.GetAsync(publication.DefinitionId, token);
                var catalogVersionId = ActivityCatalogStableIds.VersionId(publication.ActivityTypeKey, publication.Version)
                                       ?? throw new InvalidOperationException($"The reusable activity version '{candidate.ActivityDefinitionVersionId}' has no activity type key.");
                reusables[candidate.SourceVersionId] = new(candidate.SourceVersionId, candidate.ActivityDefinitionVersionId, catalogVersionId, publication, version, definition);
            }

            var workflowVersions = scoped.GetRequiredService<IWorkflowDefinitionVersionStore>();
            foreach (var candidate in candidates.Workflows.Where(candidate => !workflows.ContainsKey(candidate.SourceVersionId)))
            {
                if (await workflowVersions.FindByIdAsync(candidate.WorkflowVersionId, token) is null)
                    continue;
                var version = await workflowVersions.GetWithDefinitionAsync(candidate.WorkflowVersionId, token);
                workflows[candidate.SourceVersionId] = new(candidate.SourceVersionId, version);
            }
        }, cancellationToken);

        var missing = candidates.Reusables.Select(candidate => candidate.SourceVersionId).Where(id => !reusables.ContainsKey(id))
            .Concat(candidates.Workflows.Select(candidate => candidate.SourceVersionId).Where(id => !workflows.ContainsKey(id)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"The published versions of {string.Join(", ", missing)} were not found in the design stores.");

        return new(reusables.Values.ToArray(), workflows.Values.ToArray());
    }

    /// <summary>
    /// The reusables in dependency order: a version follows every exported version its graph manifest uses. Among the
    /// versions that are ready, the ordinal order of (<c>ActivityTypeKey</c>, SemVer sort key) decides.
    /// </summary>
    private static IReadOnlyList<ReadReusable> Order(IReadOnlyList<ReadReusable> reusables, IReadOnlyDictionary<string, string> rewrites)
    {
        var byCatalogId = reusables.ToDictionary(reusable => reusable.CatalogVersionId, StringComparer.Ordinal);
        var dependencies = reusables.ToDictionary(
            reusable => reusable.CatalogVersionId,
            reusable => ActivityVersionIds(Rewrite(reusable.Publication.Provider.Payload, rewrites))
                .Where(id => byCatalogId.ContainsKey(id) && !StringComparer.Ordinal.Equals(id, reusable.CatalogVersionId))
                .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        var comparer = Comparer<ReadReusable>.Create((left, right) =>
        {
            var byKey = StringComparer.Ordinal.Compare(left.Publication.ActivityTypeKey, right.Publication.ActivityTypeKey);
            return byKey != 0 ? byKey : StringComparer.Ordinal.Compare(SemVer.ToSortKey(left.Publication.Version), SemVer.ToSortKey(right.Publication.Version));
        });
        var ordered = new List<ReadReusable>(reusables.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var remaining = new SortedSet<ReadReusable>(reusables, comparer);
        while (remaining.Count > 0)
        {
            // A cycle cannot publish; should one reach here, its versions follow in tie-break order.
            var next = remaining.FirstOrDefault(reusable => dependencies[reusable.CatalogVersionId].IsSubsetOf(placed)) ?? remaining.Min!;
            remaining.Remove(next);
            placed.Add(next.CatalogVersionId);
            ordered.Add(next);
        }

        return ordered;
    }

    /// <summary>Maps one reusable as research R-8's table says.</summary>
    private static ActivityVersionReconciliationModel ToModel(ReadReusable reusable, IReadOnlyDictionary<string, string> rewrites)
    {
        var publication = reusable.Publication;
        var version = reusable.Version;
        var definition = reusable.Definition;
        return new ActivityVersionReconciliationModel(
            Id: null,
            Version: publication.Version,
            ActivityTypeKey: publication.ActivityTypeKey,
            DisplayName: definition.DisplayName,
            Category: definition.Category,
            Description: definition.Description,
            ProviderKey: publication.Provider.ProviderKey,
            ProviderSchemaVersion: publication.Provider.SchemaVersion,
            ConsumerKey: version.ConsumerKey,
            ConsumerSchemaVersion: version.ConsumerSchemaVersion,
            Descriptor: Rewrite(publication.Provider.Payload, rewrites),
            Inputs: version.Inputs.ToArray(),
            Outputs: version.Outputs.ToArray(),
            DesignFacets: version.DesignFacets.ToArray(),
            ExecutionType: version.ExecutionType);
    }

    /// <summary>One workflow envelope, without a store timestamp or a content hash, so the engine computes the hash.</summary>
    private static WorkflowVersionReconciliationModel ToModel(ReadWorkflow workflow, IPayloadSerializer serializer, IReadOnlyDictionary<string, string> rewrites)
    {
        var version = workflow.Version;
        var definition = version.Definition ?? throw new InvalidOperationException($"The workflow version '{version.Id}' was read without its definition.");
        var state = rewrites.Count == 0
            ? version.State
            : serializer.Deserialize<WorkflowDefinitionState>(Rewrite(serializer.SerializeToElement(version.State), rewrites));
        return new WorkflowVersionReconciliationModel(
            definition.Id,
            definition.Name,
            definition.Description,
            version.Version,
            state,
            SourceCreatedAt: null,
            ContentHash: null);
    }

    /// <summary>
    /// Replaces every <c>activityVersionId</c> member whose value is a stored reusable version id with its catalog id,
    /// at any depth: also inside a node's opaque <c>Structure</c> payload.
    /// </summary>
    internal static JsonElement Rewrite(JsonElement json, IReadOnlyDictionary<string, string> rewrites)
    {
        if (rewrites.Count == 0)
            return json.Clone();
        var node = JsonNode.Parse(json.GetRawText());
        Rewrite(node, rewrites);
        return node is null ? json.Clone() : JsonSerializer.SerializeToElement(node, JsonBaseline.Options);
    }

    private static void Rewrite(JsonNode? node, IReadOnlyDictionary<string, string> rewrites)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (name, value) in members.ToArray())
                {
                    if (IsActivityVersionId(name)
                        && value is JsonValue text
                        && text.TryGetValue<string>(out var id)
                        && rewrites.TryGetValue(id, out var catalogId))
                        members[name] = catalogId;
                    else
                        Rewrite(value, rewrites);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                    Rewrite(item, rewrites);
                break;
        }
    }

    /// <summary>Every <c>activityVersionId</c> value at any depth.</summary>
    private static IEnumerable<string> ActivityVersionIds(JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in json.EnumerateObject())
                {
                    if (IsActivityVersionId(member.Name) && member.Value.ValueKind == JsonValueKind.String)
                        yield return member.Value.GetString()!;
                    else
                        foreach (var id in ActivityVersionIds(member.Value))
                            yield return id;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in json.EnumerateArray())
                foreach (var id in ActivityVersionIds(item))
                    yield return id;
                break;
        }
    }

    private static bool IsActivityVersionId(string name) => StringComparer.OrdinalIgnoreCase.Equals(name, ActivityVersionIdMember);

    private static string RelativePath(string folder, string fileName) => $"{OutputSwap.MigratedName}/{folder}/{fileName}";

    private sealed record ReadStores(IReadOnlyList<ReadReusable> Reusables, IReadOnlyList<ReadWorkflow> Workflows);

    private sealed record ReadReusable(
        string SourceVersionId,
        string StoredVersionId,
        string CatalogVersionId,
        ActivityDefinitionVersionPublication Publication,
        ActivityDefinitionVersion Version,
        ActivityDefinition Definition);

    private sealed record ReadWorkflow(string SourceVersionId, Elsa.Workflows.Design.Persistence.Core.Entities.WorkflowDefinitionVersion Version);
}
