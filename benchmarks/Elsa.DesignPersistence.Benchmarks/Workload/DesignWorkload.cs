using Elsa.Primitives.Versioning;

namespace Elsa.DesignPersistence.Benchmarks.Workload;

/// <summary>
/// The fixed, deterministic design workload shared by every adapter and physical form. Given a scale
/// and seed the produced identities, payloads, predicates, and requested-id sets are byte-for-byte
/// identical across processes and adapters, so correctness result hashes can be compared before any
/// timing. Records are produced by streaming generators so the 1M form scale never holds the whole
/// catalog in memory.
/// </summary>
public sealed class DesignWorkload
{
    /// <summary>Single storage scope. The temporary EF oracle is single-scope by design, so the
    /// whole harness pins one scope and expresses read selectivity through predicate buckets rather
    /// than cross-scope distribution.</summary>
    public const string Scope = "design-scope-a";

    public static readonly DateTimeOffset Epoch = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);

    // Roughly 1.5% of definitions land in the selective predicate bucket (the catalog's "1-2%").
    private const int SelectiveEvery = 67;

    // 90/10 identity hit/miss ratio for the identity read plans.
    private const int IdentityRequestCount = 200;

    private readonly int _seed;

    public DesignWorkload(WorkloadScale scale, int seed = 0x5EED)
    {
        Scale = scale;
        _seed = seed;
    }

    public WorkloadScale Scale { get; }

    public int WorkflowDefinitionCount => Scale.WorkflowDefinitions;
    public int ActivityDefinitionCount => Scale.ActivityDefinitions;

    // --- Workflow catalog -------------------------------------------------------------------

    public IEnumerable<WorkflowDefinitionRecord> WorkflowDefinitions()
    {
        var payload = BuildPayload(Scale.PayloadBytes);
        for (var i = 0; i < Scale.WorkflowDefinitions; i++)
        {
            var selective = i % SelectiveEvery == 0;
            yield return new WorkflowDefinitionRecord(
                Id: WorkflowDefinitionId(i),
                Name: selective ? $"Alpha workflow {i:D7}" : $"Bulk workflow {i:D7}",
                Description: selective ? "Selective alpha catalog entry." : "Bulk catalog entry.",
                SearchBucket: selective ? "Alpha" : "Bulk",
                DraftId: $"wf-{i:D7}-draft",
                Payload: payload);
        }
    }

    /// <summary>Every third definition below <see cref="WorkloadScale.VersionedDefinitions"/> gets a
    /// deterministic version ladder (1 or 2 versions) so latest/exists reads have real fan-out.</summary>
    public IEnumerable<WorkflowVersionRecord> WorkflowVersions()
    {
        for (var i = 0; i < Scale.VersionedDefinitions; i++)
        {
            var definitionId = WorkflowDefinitionId(i);
            var ladder = i % 3 == 0 ? 2 : 1;
            for (var v = 1; v <= ladder; v++)
            {
                var version = $"{v}.0.0";
                yield return new WorkflowVersionRecord(
                    Id: $"{definitionId}-v{version}",
                    DefinitionId: definitionId,
                    Version: version,
                    SemVerSortKey: SemVer.ToSortKey(version));
            }
        }
    }

    // --- Activity catalog -------------------------------------------------------------------

    public IEnumerable<ActivityDefinitionRecord> ActivityDefinitions()
    {
        var categories = new[] { "HTTP", "Flow", "Timers", "Scripting" };
        for (var i = 0; i < Scale.ActivityDefinitions; i++)
        {
            var selective = i % SelectiveEvery == 0;
            var category = categories[i % categories.Length];
            yield return new ActivityDefinitionRecord(
                Id: ActivityDefinitionId(i),
                ActivityTypeKey: $"Elsa.Bench.Activity{i:D7}",
                Category: category,
                DisplayName: selective ? $"Alpha activity {i:D7}" : $"Bulk activity {i:D7}",
                Description: selective ? "Selective alpha activity." : "Bulk activity.");
        }
    }

    public IEnumerable<ActivityVersionRecord> ActivityVersions()
    {
        for (var i = 0; i < Scale.VersionedActivities; i++)
        {
            var definitionId = ActivityDefinitionId(i);
            var ladder = i % 3 == 0 ? 2 : 1;
            for (var v = 1; v <= ladder; v++)
            {
                var version = $"{v}.0.0";
                yield return new ActivityVersionRecord(
                    Id: $"{definitionId}-v{version}",
                    DefinitionId: definitionId,
                    Version: version,
                    SemVerSortKey: SemVer.ToSortKey(version));
            }
        }
    }

    // --- Request plans (bounded, deterministic) --------------------------------------------

    /// <summary>90% existing / 10% missing scoped identity requests for the workflow-definition
    /// identity read.</summary>
    public IReadOnlyList<IdentityRequest> WorkflowIdentityRequests()
    {
        var rng = new Random(_seed);
        var requests = new List<IdentityRequest>(IdentityRequestCount);
        for (var i = 0; i < IdentityRequestCount; i++)
        {
            var hit = i % 10 != 0; // 90% hits
            var index = rng.Next(Scale.WorkflowDefinitions);
            requests.Add(hit
                ? new IdentityRequest(WorkflowDefinitionId(index), true)
                : new IdentityRequest($"wf-missing-{index:D7}", false));
        }

        return requests;
    }

    public IReadOnlyList<IdentityRequest> ActivityIdentityRequests()
    {
        var rng = new Random(_seed ^ 0x1234);
        var requests = new List<IdentityRequest>(IdentityRequestCount);
        for (var i = 0; i < IdentityRequestCount; i++)
        {
            var hit = i % 10 != 0;
            var index = rng.Next(Scale.ActivityDefinitions);
            requests.Add(hit
                ? new IdentityRequest(ActivityDefinitionId(index), true)
                : new IdentityRequest($"act-missing-{index:D7}", false));
        }

        return requests;
    }

    /// <summary>A fixed set of definition ids whose version ladders are requested for the exact/latest
    /// identity reads. All exist and are versioned.</summary>
    public IReadOnlyList<string> VersionedWorkflowDefinitionIds()
    {
        var rng = new Random(_seed ^ 0x2222);
        var ids = new List<string>(50);
        for (var i = 0; i < 50; i++)
        {
            // index divisible by 3 guarantees a 2-version ladder
            var index = rng.Next(Scale.VersionedDefinitions / 3) * 3;
            ids.Add(WorkflowDefinitionId(index));
        }

        return ids;
    }

    public IReadOnlyList<string> VersionedActivityDefinitionIds()
    {
        var rng = new Random(_seed ^ 0x3333);
        var ids = new List<string>(50);
        for (var i = 0; i < 50; i++)
        {
            var index = rng.Next(Scale.VersionedActivities / 3) * 3;
            ids.Add(ActivityDefinitionId(index));
        }

        return ids;
    }

    /// <summary>The selective catalog predicate for the workflow filtered listing (~1.5% of rows).</summary>
    public string WorkflowSearchTerm => "Alpha";

    /// <summary>The selective catalog predicate for the activity filtered listing.</summary>
    public string ActivitySearchTerm => "Alpha";

    /// <summary>A fixed 50-item definition-id set for the list projection / version batch route.</summary>
    public IReadOnlyList<string> ProjectionDefinitionIds()
    {
        var rng = new Random(_seed ^ 0x4444);
        var ids = new List<string>(50);
        for (var i = 0; i < 50; i++)
            ids.Add(WorkflowDefinitionId(rng.Next(Scale.VersionedDefinitions)));
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    public static string WorkflowDefinitionId(int index) => $"wf-{index:D7}";
    public static string ActivityDefinitionId(int index) => $"act-{index:D7}";

    private string BuildPayload(int bytes)
    {
        // Deterministic, incompressible-ish payload of the requested size.
        if (bytes <= 0)
            return string.Empty;
        var rng = new Random(_seed ^ 0x7777);
        var chars = new char[bytes];
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        for (var i = 0; i < bytes; i++)
            chars[i] = alphabet[rng.Next(alphabet.Length)];
        return new string(chars);
    }
}

public sealed record WorkflowDefinitionRecord(
    string Id,
    string Name,
    string Description,
    string SearchBucket,
    string DraftId,
    string Payload);

public sealed record WorkflowVersionRecord(string Id, string DefinitionId, string Version, string SemVerSortKey);

public sealed record ActivityDefinitionRecord(
    string Id,
    string ActivityTypeKey,
    string Category,
    string DisplayName,
    string Description);

public sealed record ActivityVersionRecord(string Id, string DefinitionId, string Version, string SemVerSortKey);

public sealed record IdentityRequest(string Id, bool ExpectedHit);
