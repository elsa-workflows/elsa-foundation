using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Executes the catalog-owned trigger-binding and executable-source-reference correctness baseline through
/// public runtime stores. Logical scope selection is supplied by the host adapter; this runner deliberately
/// contains no provider, timing, persistence, or physical-form concerns.
/// </summary>
public sealed class RuntimeTriggerBindingStimulusLookupWorkload
{
    private const string PrimaryStimulusType = "runtime-trigger-stimulus";
    private const string PrimaryStimulusHash = "sha256:trigger-binding-primary";
    private const string SecondaryStimulusType = "runtime-trigger-stimulus-secondary";
    private const string SecondaryStimulusHash = "sha256:trigger-binding-secondary";
    private const string AlternateStimulusHash = "sha256:trigger-binding-alternate";
    private const string DifferentStimulusType = "runtime-trigger-stimulus-other";
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const string WorkloadId = "trigger-binding-stimulus-lookup";
    public const string ExpectedInputFingerprint = "4f2515dfa9549935712019f178283f79e6ac1cc9428e810524e733cfdea4cabc";
    public const string ExpectedResultDigest = "00b6651345cdb8b6724a205b094c712d383c7a19ef87dcce6fdf026bc7dd7c8a";
    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int TenantCount => Int("tenantCount");
    public static int PublicationCount => Int("publicationCount");
    public static int BindingsPerPublication => Int("bindingsPerPublication");
    public static int ActiveMatches => Int("activeMatches");
    public static int RetiredBindings => Int("retiredBindings");
    public static int PageSize => Int("pageSize");

    public async ValueTask<RuntimeTriggerBindingStimulusLookupResult> ExecuteAsync(
        IRuntimeTriggerBindingStimulusLookupWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        RequireIsolatedScopes(scopes);

        var operations = new List<string>();
        var primaryExpected = await SeedScopeAsync(scopes.Primary, "primary", cancellationToken);
        var secondaryExpected = await SeedScopeAsync(scopes.Secondary, "secondary", cancellationToken);
        operations.Add("seed-publications-and-bindings");

        var primaryExact = await ReadStimulusAsync(
            scopes.Primary.TriggerBindings,
            PrimaryStimulusType,
            PrimaryStimulusHash,
            primaryExpected.ActiveExactBindings,
            cancellationToken);
        var primaryType = await ReadStimulusTypeAsync(
            scopes.Primary.TriggerBindings,
            PrimaryStimulusType,
            primaryExpected.ActiveTypeBindings,
            cancellationToken);
        await VerifyPublicationProjectionsAsync(scopes.Primary.TriggerBindings, primaryExpected.BindingsByPublication, cancellationToken);
        await VerifyPublicationProjectionsAsync(scopes.Secondary.TriggerBindings, secondaryExpected.BindingsByPublication, cancellationToken);
        operations.Add("lookup-active-bindings-by-stimulus-type");

        var primarySources = await ReadLivePublishedSourcesAsync(scopes.Primary.SourceReferences, primaryExpected.LivePublishedReferences, cancellationToken);
        var secondarySources = await ReadLivePublishedSourcesAsync(scopes.Secondary.SourceReferences, secondaryExpected.LivePublishedReferences, cancellationToken);
        var executableSourceReferenceCount = CorrelateLiveSources(primaryExact.Items, primarySources, primaryExpected.TenantId);
        if (executableSourceReferenceCount != ActiveMatches)
            throw new InvalidOperationException("The returned active bindings did not resolve to exactly the returned live source references.");
        operations.Add("load-executable-source-references");

        var primaryInSecondary = await ReadStimulusAsync(
            scopes.Secondary.TriggerBindings,
            PrimaryStimulusType,
            PrimaryStimulusHash,
            [],
            cancellationToken);
        var secondaryInPrimary = await ReadStimulusAsync(
            scopes.Primary.TriggerBindings,
            SecondaryStimulusType,
            SecondaryStimulusHash,
            [],
            cancellationToken);
        RequireSourceScopeFacts(primarySources, primaryExpected.TenantId, "primary");
        RequireSourceScopeFacts(secondarySources, secondaryExpected.TenantId, "secondary");
        operations.Add("verify-publication-and-scope-isolation");

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The trigger-binding workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["activeBindingIdentityDigest"] = Hash(JsonSerializer.Serialize(primaryExact.Items.Select(binding => binding.TriggerBindingId), CanonicalJsonOptions)),
            ["crossScopeResultCount"] = primaryInSecondary.Items.Count + secondaryInPrimary.Items.Count,
            ["executableSourceReferenceCount"] = executableSourceReferenceCount,
            ["firstPageCount"] = primaryExact.PageSizes[0],
            ["retiredBindingResultCount"] = primaryExact.Items.Count(binding => !binding.IsActive),
            ["secondPageCount"] = primaryExact.PageSizes[1]
        };
        var expectedObservations = scenario.CreateExpectedObservations();
        if (!ObservationsMatch(actualObservations, expectedObservations))
            throw new InvalidOperationException("The trigger-binding observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (resultDigest != ExpectedResultDigest)
            throw new InvalidOperationException("The trigger-binding observable result digest no longer matches its ratified value.");

        return new RuntimeTriggerBindingStimulusLookupResult(
            scenario.ComputeInputFingerprint(),
            resultDigest,
            operations,
            actualObservations);
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" ||
            Scenario.ScenarioId != "runtime-trigger-binding-stimulus-lookup" ||
            Scenario.Seed != "spec094-trigger-binding-stimulus-lookup-v1.1" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint ||
            golden.ResultDigest != ExpectedResultDigest ||
            TenantCount != 2 || PublicationCount != 96 || BindingsPerPublication != 48 || ActiveMatches != 31 ||
            RetiredBindings != 17 || PageSize != 20)
        {
            throw new InvalidOperationException("The trigger-binding scenario no longer matches its frozen v1.1 catalog contract.");
        }

        return Scenario;
    }

    private static async ValueTask<SeededScope> SeedScopeAsync(
        RuntimeTriggerBindingStimulusLookupScope scope,
        string logicalScope,
        CancellationToken cancellationToken)
    {
        var tenantId = $"tenant-{logicalScope}";
        var bindingsByPublication = new Dictionary<string, IReadOnlyList<WorkflowTriggerBinding>>(StringComparer.Ordinal);
        var references = new List<WorkflowExecutableSourceReference>();

        for (var publication = 0; publication < PublicationCount; publication++)
        {
            var activationId = ActivationId(logicalScope, publication);
            var bindings = CreateBindings(logicalScope, publication, activationId).ToArray();
            await scope.TriggerBindings.PrepareActivationAsync(activationId, bindings, cancellationToken);
            bindingsByPublication.Add(
                activationId,
                bindings
                    .Select(binding => binding with { IsActive = publication != PublicationCount - 1 })
                    .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
                    .ToArray());
            await scope.SourceReferences.SaveAsync(CreateLiveReference(logicalScope, publication, tenantId), cancellationToken);
        }

        // Exercise replacement without changing any of the 31 primary exact matches.
        var replacedActivationId = ActivationId(logicalScope, PublicationCount - 1);
        await scope.TriggerBindings.ActivateAsync(replacedActivationId, replacedActivationId: null, cancellationToken);
        for (var publication = 0; publication < PublicationCount - 1; publication++)
        {
            var replaced = publication == PublicationCount - 2 ? replacedActivationId : null;
            await scope.TriggerBindings.ActivateAsync(ActivationId(logicalScope, publication), replaced, cancellationToken);
        }

        for (var publication = 0; publication < PublicationCount; publication++)
            references.Add(CreateLiveReference(logicalScope, publication, tenantId));

        await scope.SourceReferences.SaveAsync(CreateExpiredReference(logicalScope, tenantId), cancellationToken);
        await scope.SourceReferences.SaveAsync(CreateRetiredReference(logicalScope, tenantId), cancellationToken);
        await scope.SourceReferences.SaveAsync(CreateTestRunReference(logicalScope, tenantId), cancellationToken);

        var allBindings = bindingsByPublication.Values.SelectMany(bindings => bindings).OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal).ToArray();
        var activeExact = allBindings.Where(binding => binding.IsActive && binding.StimulusType == StimulusType(logicalScope) && binding.StimulusHash == StimulusHash(logicalScope)).ToArray();
        var activeType = allBindings.Where(binding => binding.IsActive && binding.StimulusType == StimulusType(logicalScope)).ToArray();
        var retiredIds = allBindings.Where(binding => !binding.IsActive && binding.StimulusType == StimulusType(logicalScope) && binding.StimulusHash == StimulusHash(logicalScope)).Select(binding => binding.TriggerBindingId).ToHashSet(StringComparer.Ordinal);

        if (allBindings.Length != PublicationCount * BindingsPerPublication || activeExact.Length != ActiveMatches ||
            retiredIds.Count != RetiredBindings || activeType.Length != ActiveMatches + 1)
        {
            throw new InvalidOperationException("The trigger-binding seed no longer reproduces the frozen vector cardinalities.");
        }

        return new SeededScope(tenantId, bindingsByPublication, activeExact, activeType, references.OrderBy(reference => reference.SourceReferenceId, StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<WorkflowTriggerBinding> CreateBindings(string logicalScope, int publication, string activationId)
    {
        for (var binding = 0; binding < BindingsPerPublication; binding++)
        {
            var global = publication * BindingsPerPublication + binding;
            var type = $"noise-{logicalScope}";
            var hash = $"sha256:noise-{logicalScope}-{global:D4}";
            var id = $"noise-binding-{logicalScope}-{global:D4}";
            if (global < ActiveMatches)
            {
                type = StimulusType(logicalScope);
                hash = StimulusHash(logicalScope);
                id = $"active-binding-{global:D4}";
            }
            else if (publication == PublicationCount - 1 && binding < RetiredBindings)
            {
                type = StimulusType(logicalScope);
                hash = StimulusHash(logicalScope);
                id = $"retired-binding-{binding:D4}";
            }
            else if (global == BindingsPerPublication)
            {
                type = StimulusType(logicalScope);
                hash = AlternateStimulusHash;
                id = $"alternate-binding-{logicalScope}";
            }
            else if (global == BindingsPerPublication + 1)
            {
                type = DifferentStimulusType;
                hash = StimulusHash(logicalScope);
                id = $"different-type-binding-{logicalScope}";
            }

            yield return new WorkflowTriggerBinding(
                id,
                ArtifactId(logicalScope, publication),
                $"definition-{logicalScope}-{publication:D4}",
                "1.0.0",
                $"sha256:artifact-{logicalScope}-{publication:D4}",
                $"node-{binding:D4}",
                type,
                hash,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["seed"] = Seed, ["scope"] = logicalScope },
                FixedNowUtc.AddMinutes(-global - 1),
                activationId,
                $"slot-{binding:D4}",
                TriggerCardinality.FanOut,
                IsActive: false);
        }
    }

    private static WorkflowExecutableSourceReference CreateLiveReference(string logicalScope, int publication, string tenantId) =>
        new(
            $"source-reference-{logicalScope}-{publication:D4}",
            ArtifactId(logicalScope, publication),
            "workflow-definition",
            $"definition-{logicalScope}-{publication:D4}",
            "1.0.0",
            $"definition-{logicalScope}-{publication:D4}",
            $"definition-version-{logicalScope}-{publication:D4}",
            "1.0.0",
            FixedNowUtc.AddDays(-1),
            FixedNowUtc.AddHours(-1),
            WorkflowExecutableReferenceScope.Published,
            ActivationId: ActivationId(logicalScope, publication),
            SlotId: $"slot-{publication:D4}",
            TenantId: tenantId);

    private static WorkflowExecutableSourceReference CreateExpiredReference(string logicalScope, string tenantId) =>
        CreateLiveReference(logicalScope, PublicationCount, tenantId) with
        {
            SourceReferenceId = $"source-reference-expired-{logicalScope}",
            ExpiresAt = FixedNowUtc,
            ActivationId = $"publication-expired-{logicalScope}"
        };

    private static WorkflowExecutableSourceReference CreateRetiredReference(string logicalScope, string tenantId) =>
        CreateLiveReference(logicalScope, PublicationCount + 1, tenantId) with
        {
            SourceReferenceId = $"source-reference-retired-{logicalScope}",
            DeletedAt = FixedNowUtc.AddTicks(-1),
            DeletedReason = "retired",
            ActivationId = $"publication-retired-{logicalScope}"
        };

    private static WorkflowExecutableSourceReference CreateTestRunReference(string logicalScope, string tenantId) =>
        CreateLiveReference(logicalScope, PublicationCount + 2, tenantId) with
        {
            SourceReferenceId = $"source-reference-test-run-{logicalScope}",
            Scope = WorkflowExecutableReferenceScope.TestRun,
            ExpiresAt = FixedNowUtc.AddHours(1),
            ActivationId = null,
            SlotId = null
        };

    private static async ValueTask<PagedBindings> ReadStimulusAsync(
        IWorkflowTriggerBindingStore store,
        string stimulusType,
        string stimulusHash,
        IReadOnlyList<WorkflowTriggerBinding> expected,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkflowTriggerBinding>();
        var pageSizes = new List<int>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            var query = new WorkflowTriggerBindingPageQuery(stimulusType, stimulusHash, PageSize, token);
            var page = await store.ListByStimulusAsync(query, cancellationToken);
            RequireBindingPage(page, query, expected, results.Count, "exact stimulus lookup", seenTokens);
            results.AddRange(page.Items);
            pageSizes.Add(page.Items.Count);
            token = page.NextContinuationToken;
        } while (token is not null);

        RequireBindingSequence(results, expected, "exact stimulus lookup");
        return new PagedBindings(results, pageSizes);
    }

    private static async ValueTask<PagedBindings> ReadStimulusTypeAsync(
        IWorkflowTriggerBindingStore store,
        string stimulusType,
        IReadOnlyList<WorkflowTriggerBinding> expected,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkflowTriggerBinding>();
        var pageSizes = new List<int>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            var query = new WorkflowTriggerBindingTypePageQuery(stimulusType, PageSize, token);
            var page = await store.ListByStimulusTypeAsync(query, cancellationToken);
            RequireBindingPage(page, query, expected, results.Count, "stimulus-type lookup", seenTokens);
            results.AddRange(page.Items);
            pageSizes.Add(page.Items.Count);
            token = page.NextContinuationToken;
        } while (token is not null);

        RequireBindingSequence(results, expected, "stimulus-type lookup");
        return new PagedBindings(results, pageSizes);
    }

    private static async ValueTask VerifyPublicationProjectionsAsync(
        IWorkflowTriggerBindingStore store,
        IReadOnlyDictionary<string, IReadOnlyList<WorkflowTriggerBinding>> expectedByPublication,
        CancellationToken cancellationToken)
    {
        foreach (var (activationId, expected) in expectedByPublication.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var results = new List<WorkflowTriggerBinding>();
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            string? token = null;
            do
            {
                var query = new WorkflowTriggerBindingActivationPageQuery(activationId, PageSize, token);
                var page = await store.ListByActivationAsync(query, cancellationToken);
                RequireBindingPage(page, query, expected, results.Count, $"publication projection '{activationId}'", seenTokens);
                results.AddRange(page.Items);
                token = page.NextContinuationToken;
            } while (token is not null);

            RequireBindingSequence(results, expected, $"publication projection '{activationId}'");
        }
    }

    private static async ValueTask<IReadOnlyList<WorkflowExecutableSourceReference>> ReadLivePublishedSourcesAsync(
        IWorkflowExecutableSourceReferenceStore store,
        IReadOnlyList<WorkflowExecutableSourceReference> expected,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkflowExecutableSourceReference>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            var query = new WorkflowExecutableSourceReferencePageQuery(
                WorkflowExecutableReferenceScope.Published,
                liveOnly: true,
                now: FixedNowUtc,
                limit: PageSize,
                continuationToken: token);
            var page = await store.ListPageAsync(query, cancellationToken);
            ArgumentNullException.ThrowIfNull(page);
            if (page.Items.Count > query.Limit ||
                !page.Items.SequenceEqual(expected.Skip(results.Count).Take(query.Limit), SourceReferenceComparer.Instance) ||
                (results.Count + page.Items.Count < expected.Count && page.NextContinuationToken is null) ||
                (results.Count + page.Items.Count == expected.Count && page.NextContinuationToken is not null) ||
                (page.NextContinuationToken is not null && !seenTokens.Add(page.NextContinuationToken)))
            {
                throw new InvalidOperationException("The live published source-reference page does not match the exact bounded contract.");
            }

            results.AddRange(page.Items);
            token = page.NextContinuationToken;
        } while (token is not null);

        if (!results.SequenceEqual(expected, SourceReferenceComparer.Instance))
            throw new InvalidOperationException("The complete live published source-reference traversal does not match the expected records.");
        return results;
    }

    private static int CorrelateLiveSources(
        IReadOnlyList<WorkflowTriggerBinding> bindings,
        IReadOnlyList<WorkflowExecutableSourceReference> references,
        string tenantId)
    {
        var referencesByPublication = references
            .Where(reference => reference.Scope == WorkflowExecutableReferenceScope.Published && reference.IsLive(FixedNowUtc) && reference.TenantId == tenantId)
            .ToDictionary(reference => reference.ActivationId!, StringComparer.Ordinal);
        return bindings.Count(binding =>
            binding.ActivationId is not null &&
            referencesByPublication.TryGetValue(binding.ActivationId, out var reference) &&
            reference.ArtifactId == binding.ArtifactId);
    }

    private static void RequireBindingPage(
        WorkflowTriggerBindingPage? page,
        WorkflowTriggerBindingPageRequest query,
        IReadOnlyList<WorkflowTriggerBinding> expected,
        int offset,
        string operation,
        ISet<string> seenTokens)
    {
        if (page is null || page.Items.Count > query.Limit || page.TotalCount != expected.Count ||
            !page.Items.SequenceEqual(expected.Skip(offset).Take(query.Limit), BindingComparer.Instance) ||
            (offset + page.Items.Count < expected.Count && page.NextContinuationToken is null) ||
            (offset + page.Items.Count == expected.Count && page.NextContinuationToken is not null) ||
            (page.NextContinuationToken is not null && !seenTokens.Add(page.NextContinuationToken)))
        {
            throw new InvalidOperationException($"The {operation} does not match the exact bounded trigger-binding contract.");
        }
    }

    private static void RequireBindingSequence(
        IReadOnlyList<WorkflowTriggerBinding> actual,
        IReadOnlyList<WorkflowTriggerBinding> expected,
        string operation)
    {
        if (!actual.SequenceEqual(expected, BindingComparer.Instance))
            throw new InvalidOperationException($"The complete {operation} traversal does not match the expected trigger bindings.");
    }

    private static void RequireSourceScopeFacts(IReadOnlyList<WorkflowExecutableSourceReference> references, string tenantId, string logicalScope)
    {
        if (references.Any(reference => reference.TenantId != tenantId || reference.ActivationId is null || !reference.ArtifactId.Contains(logicalScope, StringComparison.Ordinal)))
            throw new InvalidOperationException($"The {logicalScope} logical scope exposed source references from another scope.");
    }

    private static void RequireIsolatedScopes(RuntimeTriggerBindingStimulusLookupScopes? scopes)
    {
        if (scopes?.Primary is null || scopes.Secondary is null ||
            ReferenceEquals(scopes.Primary.TriggerBindings, scopes.Secondary.TriggerBindings) ||
            ReferenceEquals(scopes.Primary.SourceReferences, scopes.Secondary.SourceReferences))
        {
            throw new InvalidOperationException("The trigger-binding workload adapter must open two isolated logical public-store scopes.");
        }
    }

    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));

    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static string StimulusType(string logicalScope) => logicalScope == "primary" ? PrimaryStimulusType : SecondaryStimulusType;
    private static string StimulusHash(string logicalScope) => logicalScope == "primary" ? PrimaryStimulusHash : SecondaryStimulusHash;
    private static string ActivationId(string logicalScope, int publication) => $"publication-{logicalScope}-{publication:D4}";
    private static string ArtifactId(string logicalScope, int publication) => $"artifact-{logicalScope}-{publication:D4}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record SeededScope(
        string TenantId,
        IReadOnlyDictionary<string, IReadOnlyList<WorkflowTriggerBinding>> BindingsByPublication,
        IReadOnlyList<WorkflowTriggerBinding> ActiveExactBindings,
        IReadOnlyList<WorkflowTriggerBinding> ActiveTypeBindings,
        IReadOnlyList<WorkflowExecutableSourceReference> LivePublishedReferences);

    private sealed record PagedBindings(IReadOnlyList<WorkflowTriggerBinding> Items, IReadOnlyList<int> PageSizes);

    private sealed class BindingComparer : IEqualityComparer<WorkflowTriggerBinding>
    {
        public static BindingComparer Instance { get; } = new();

        public bool Equals(WorkflowTriggerBinding? left, WorkflowTriggerBinding? right) =>
            ReferenceEquals(left, right) ||
            left is not null && right is not null &&
            left.TriggerBindingId == right.TriggerBindingId && left.ArtifactId == right.ArtifactId &&
            left.DefinitionId == right.DefinitionId && left.ArtifactVersion == right.ArtifactVersion &&
            left.ArtifactHash == right.ArtifactHash && left.ExecutableNodeId == right.ExecutableNodeId &&
            left.StimulusType == right.StimulusType && left.StimulusHash == right.StimulusHash &&
            left.CorrelationScope == right.CorrelationScope && left.CreatedAt == right.CreatedAt &&
            left.ActivationId == right.ActivationId && left.SlotId == right.SlotId &&
            left.Cardinality == right.Cardinality && left.IsActive == right.IsActive &&
            left.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(right.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal));

        public int GetHashCode(WorkflowTriggerBinding binding) => StringComparer.Ordinal.GetHashCode(binding.TriggerBindingId);
    }

    private sealed class SourceReferenceComparer : IEqualityComparer<WorkflowExecutableSourceReference>
    {
        public static SourceReferenceComparer Instance { get; } = new();

        public bool Equals(WorkflowExecutableSourceReference? left, WorkflowExecutableSourceReference? right) =>
            ReferenceEquals(left, right) ||
            left is not null && right is not null &&
            left.SourceReferenceId == right.SourceReferenceId && left.ArtifactId == right.ArtifactId &&
            left.SourceKind == right.SourceKind && left.SourceId == right.SourceId && left.SourceVersion == right.SourceVersion &&
            left.DefinitionId == right.DefinitionId && left.DefinitionVersionId == right.DefinitionVersionId &&
            left.ArtifactVersion == right.ArtifactVersion && left.CreatedAt == right.CreatedAt && left.PublishedAt == right.PublishedAt &&
            left.Scope == right.Scope && left.ExpiresAt == right.ExpiresAt && left.DeletedAt == right.DeletedAt &&
            left.DeletedReason == right.DeletedReason && left.ActivationId == right.ActivationId && left.SlotId == right.SlotId &&
            left.TenantId == right.TenantId;

        public int GetHashCode(WorkflowExecutableSourceReference reference) => StringComparer.Ordinal.GetHashCode(reference.SourceReferenceId);
    }
}

/// <summary>Host-selected isolated logical scopes for the trigger-binding correctness baseline.</summary>
public interface IRuntimeTriggerBindingStimulusLookupWorkloadAdapter
{
    ValueTask<RuntimeTriggerBindingStimulusLookupScopes> OpenIsolatedScopesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Two logical public-store scopes used to prove binding and source-reference isolation in both directions.</summary>
public sealed record RuntimeTriggerBindingStimulusLookupScopes(
    RuntimeTriggerBindingStimulusLookupScope Primary,
    RuntimeTriggerBindingStimulusLookupScope Secondary);

/// <summary>One logical scope exposes only the public runtime stores used by the workload.</summary>
public sealed record RuntimeTriggerBindingStimulusLookupScope(
    IWorkflowTriggerBindingStore TriggerBindings,
    IWorkflowExecutableSourceReferenceStore SourceReferences);

public sealed record RuntimeTriggerBindingStimulusLookupResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
