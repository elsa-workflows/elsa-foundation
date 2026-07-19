using System.Text.RegularExpressions;
using Groundwork.Core.Text;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

public sealed record GroundworkNativeRoutePlanRequest
{
    private static readonly Regex PhysicalIdentifier = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ProviderIdentifier = new(
        "^[A-Za-z_][A-Za-z0-9_.-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public GroundworkNativeRoutePlanRequest(
        string documentKind,
        string queryIdentity,
        string physicalName,
        string routeField,
        IReadOnlyList<string> projectedFields,
        string storageScope,
        string routeValue,
        int limit,
        int acceptanceCardinality,
        string candidateDocumentId = "native-000000",
        string candidateContentJson = "{}",
        string candidateSchemaVersion = "1.0.1")
    {
        DocumentKind = RequireValue(documentKind, nameof(documentKind));
        QueryIdentity = RequireValue(queryIdentity, nameof(queryIdentity));
        PhysicalName = RequirePhysicalIdentifier(physicalName, nameof(physicalName));
        RouteField = RequirePhysicalIdentifier(routeField, nameof(routeField));
        StorageScope = RequireValue(storageScope, nameof(storageScope));
        RouteValue = RequireValue(routeValue, nameof(routeValue));
        if (projectedFields is null || projectedFields.Count == 0)
            throw new ArgumentException("At least one projected field is required.", nameof(projectedFields));
        ProjectedFields = projectedFields.Select(field => RequirePhysicalIdentifier(field, nameof(projectedFields)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!ProjectedFields.Contains(RouteField, StringComparer.Ordinal))
            throw new ArgumentException("The route field must be part of the physical projection.", nameof(routeField));
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "A finite positive limit is required.");
        if (acceptanceCardinality <= 0)
            throw new ArgumentOutOfRangeException(nameof(acceptanceCardinality), "A positive physical cardinality is required.");
        Limit = limit;
        AcceptanceCardinality = acceptanceCardinality;
        CandidateDocumentId = RequireValue(candidateDocumentId, nameof(candidateDocumentId));
        CandidateContentJson = RequireValue(candidateContentJson, nameof(candidateContentJson));
        CandidateSchemaVersion = RequireValue(candidateSchemaVersion, nameof(candidateSchemaVersion));
        var identity = PortableStringComparison.ProjectIdentity(
            CandidateDocumentId,
            PortableStringComparisonPolicy.Ordinal);
        CandidateComparisonKey = identity.ComparisonKey;
        CandidateLookupKey = identity.LookupKey;
    }

    public string DocumentKind { get; }
    public string QueryIdentity { get; }
    public string PhysicalName { get; }
    public string RouteField { get; }
    public IReadOnlyList<string> ProjectedFields { get; }
    public string StorageScope { get; }
    public string RouteValue { get; }
    public int Limit { get; }
    public int AcceptanceCardinality { get; }
    public string CandidateDocumentId { get; }
    public string CandidateContentJson { get; }
    public string CandidateSchemaVersion { get; }
    public string CandidateComparisonKey { get; }
    public string CandidateLookupKey { get; }

    private static string RequireValue(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A nonblank value is required.", parameterName);

    internal static string RequirePhysicalIdentifier(string value, string parameterName) =>
        PhysicalIdentifier.IsMatch(value ?? string.Empty)
            ? value!
            : throw new ArgumentException("A portable physical identifier is required.", parameterName);

    internal static string RequireProviderIdentifier(string value, string parameterName) =>
        ProviderIdentifier.IsMatch(value ?? string.Empty)
            ? value!
            : throw new ArgumentException("A safe provider identifier is required.", parameterName);
}

public sealed record GroundworkNativeRouteCommandEvidence
{
    private static readonly IReadOnlyDictionary<PhysicalDocumentQueryCommandKind, string> CommandIdentities =
        new Dictionary<PhysicalDocumentQueryCommandKind, string>
        {
            [PhysicalDocumentQueryCommandKind.LinkedIdentityCollisionCheck] = PhysicalDocumentQueryCommandIdentities.LinkedIdentityCollisionCheck,
            [PhysicalDocumentQueryCommandKind.Count] = PhysicalDocumentQueryCommandIdentities.Count,
            [PhysicalDocumentQueryCommandKind.Page] = PhysicalDocumentQueryCommandIdentities.Page,
            [PhysicalDocumentQueryCommandKind.First] = PhysicalDocumentQueryCommandIdentities.First,
            [PhysicalDocumentQueryCommandKind.Any] = PhysicalDocumentQueryCommandIdentities.Any,
            [PhysicalDocumentQueryCommandKind.PrimaryHydration] = PhysicalDocumentQueryCommandIdentities.PrimaryHydration
        };
    private static readonly IReadOnlySet<string> NativePlanFormats = new HashSet<string>(StringComparer.Ordinal)
    {
        "sqlite-query-plan",
        "postgresql-json",
        "sqlserver-statistics-xml",
        "mongodb-json"
    };
    private static readonly IReadOnlySet<string> PlanClassifications = new HashSet<string>(StringComparer.Ordinal)
    {
        "index-search",
        "index-only-scan",
        "index-scan",
        "index-seek"
    };

    private GroundworkNativeRouteCommandEvidence(
        int ordinal,
        PhysicalDocumentQueryCommandKind kind,
        string identity,
        string nativePlanFormat,
        string planClassification,
        IReadOnlyList<string> indexNames,
        IReadOnlyList<string> predicateFieldIdentifiers,
        string nativePlanSha256)
    {
        Ordinal = ordinal;
        Kind = kind;
        Identity = identity;
        NativePlanFormat = nativePlanFormat;
        PlanClassification = planClassification;
        IndexNames = indexNames;
        PredicateFieldIdentifiers = predicateFieldIdentifiers;
        NativePlanSha256 = nativePlanSha256;
    }

    public int Ordinal { get; }
    public PhysicalDocumentQueryCommandKind Kind { get; }
    public string Identity { get; }
    public string NativePlanFormat { get; }
    public string PlanClassification { get; }
    public IReadOnlyList<string> IndexNames { get; }
    public IReadOnlyList<string> PredicateFieldIdentifiers { get; }
    public string NativePlanSha256 { get; }

    public static GroundworkNativeRouteCommandEvidence Create(
        int ordinal,
        PhysicalDocumentQueryCommandExplanation command,
        string planClassification,
        IReadOnlyCollection<string> indexNames)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        ArgumentNullException.ThrowIfNull(command);
        if (!CommandIdentities.TryGetValue(command.Kind, out var expectedIdentity) ||
            !string.Equals(command.Identity, expectedIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("Native command evidence requires the closed Groundwork command identity for its kind.");
        if (!NativePlanFormats.Contains(command.NativePlanFormat))
            throw new InvalidOperationException("Native command evidence requires a recognized provider plan format.");
        if (!PlanClassifications.Contains(planClassification))
            throw new InvalidOperationException("Native command evidence requires an allowlisted plan classification.");
        ArgumentNullException.ThrowIfNull(indexNames);
        var indexes = indexNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => GroundworkNativeRoutePlanRequest.RequireProviderIdentifier(name, nameof(indexNames)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (indexes.Length == 0)
            throw new InvalidOperationException("A scan-free native command must expose at least one allowlisted index name.");
        var predicateFields = command.PredicateFieldIdentifiers
            .Select(identifier => GroundworkNativeRoutePlanRequest.RequirePhysicalIdentifier(
                identifier,
                nameof(command.PredicateFieldIdentifiers)))
            .ToArray();
        return new GroundworkNativeRouteCommandEvidence(
            ordinal,
            command.Kind,
            command.Identity,
            command.NativePlanFormat,
            planClassification,
            indexes,
            predicateFields,
            StableDigest.FromText(command.NativePlan));
    }
}

public sealed record GroundworkNativeRouteDataset
{
    private GroundworkNativeRouteDataset(
        string documentKind,
        string physicalName,
        string storageScope,
        int acceptanceCardinality,
        string candidateDocumentId,
        string candidateComparisonKey,
        string candidateLookupKey,
        string candidateContentJson,
        string candidateSchemaVersion,
        IReadOnlyDictionary<string, string> projectedValues)
    {
        DocumentKind = documentKind;
        PhysicalName = physicalName;
        StorageScope = storageScope;
        AcceptanceCardinality = acceptanceCardinality;
        CandidateDocumentId = candidateDocumentId;
        CandidateComparisonKey = candidateComparisonKey;
        CandidateLookupKey = candidateLookupKey;
        CandidateContentJson = candidateContentJson;
        CandidateSchemaVersion = candidateSchemaVersion;
        CrossScope = $"{storageScope}-native-other";
        ProjectedValues = projectedValues;
    }

    public string DocumentKind { get; }
    public string PhysicalName { get; }
    public string StorageScope { get; }
    public int AcceptanceCardinality { get; }
    public string CandidateDocumentId { get; }
    public string CandidateComparisonKey { get; }
    public string CandidateLookupKey { get; }
    public string CandidateContentJson { get; }
    public string CandidateSchemaVersion { get; }
    public string CrossScope { get; }
    public IReadOnlyDictionary<string, string> ProjectedValues { get; }

    public static GroundworkNativeRouteDataset Create(
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            throw new ArgumentException("A physical native-route dataset requires at least one route.", nameof(requests));
        var first = requests.First();
        if (requests.Any(request =>
                !string.Equals(request.DocumentKind, first.DocumentKind, StringComparison.Ordinal) ||
                !string.Equals(request.PhysicalName, first.PhysicalName, StringComparison.Ordinal) ||
                !string.Equals(request.StorageScope, first.StorageScope, StringComparison.Ordinal) ||
                request.AcceptanceCardinality != first.AcceptanceCardinality ||
                !string.Equals(request.CandidateDocumentId, first.CandidateDocumentId, StringComparison.Ordinal) ||
                !string.Equals(request.CandidateComparisonKey, first.CandidateComparisonKey, StringComparison.Ordinal) ||
                !string.Equals(request.CandidateLookupKey, first.CandidateLookupKey, StringComparison.Ordinal) ||
                !string.Equals(request.CandidateContentJson, first.CandidateContentJson, StringComparison.Ordinal) ||
                !string.Equals(request.CandidateSchemaVersion, first.CandidateSchemaVersion, StringComparison.Ordinal) ||
                !request.ProjectedFields.SequenceEqual(first.ProjectedFields, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Routes sharing a physical evidence dataset must agree on kind, table, scope, cardinality, candidate, and projections.");
        }

        var projectedValues = requests
            .GroupBy(request => request.RouteField, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(request => request.RouteValue).Distinct(StringComparer.Ordinal).Single(),
                StringComparer.Ordinal);
        var missing = first.ProjectedFields.Except(projectedValues.Keys, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"Native-route dataset '{first.PhysicalName}' has no captured value for projected fields: {string.Join(", ", missing)}.");
        }

        return new GroundworkNativeRouteDataset(
            first.DocumentKind,
            first.PhysicalName,
            first.StorageScope,
            first.AcceptanceCardinality,
            first.CandidateDocumentId,
            first.CandidateComparisonKey,
            first.CandidateLookupKey,
            first.CandidateContentJson,
            first.CandidateSchemaVersion,
            projectedValues);
    }
}

public sealed record GroundworkNativeRoutePlanResult
{
    private GroundworkNativeRoutePlanResult(
        GroundworkNativeRoutePlanRequest request,
        string providerKey,
        long physicalCardinality,
        string planClassification,
        string indexName,
        bool hasStorageScopePredicate,
        bool hasRoutePredicate,
        int finiteLimit,
        int materializedCandidateCount,
        IReadOnlyList<GroundworkNativeRouteCommandEvidence> commands,
        GroundworkSanitizedEvidence evidence)
    {
        Request = request;
        ProviderKey = providerKey;
        PhysicalCardinality = physicalCardinality;
        PlanClassification = planClassification;
        IndexName = indexName;
        HasStorageScopePredicate = hasStorageScopePredicate;
        HasRoutePredicate = hasRoutePredicate;
        FiniteLimit = finiteLimit;
        MaterializedCandidateCount = materializedCandidateCount;
        Commands = commands;
        Evidence = evidence;
    }

    public GroundworkNativeRoutePlanRequest Request { get; }
    public string ProviderKey { get; }
    public long PhysicalCardinality { get; }
    public string PlanClassification { get; }
    public string IndexName { get; }
    public bool HasStorageScopePredicate { get; }
    public bool HasRoutePredicate { get; }
    public int FiniteLimit { get; }
    public int MaterializedCandidateCount { get; }
    public IReadOnlyList<GroundworkNativeRouteCommandEvidence> Commands { get; }
    public GroundworkSanitizedEvidence Evidence { get; }

    public static GroundworkNativeRoutePlanResult Create(
        GroundworkNativeRoutePlanRequest request,
        string providerKey,
        long physicalCardinality,
        string planClassification,
        string indexName,
        int finiteLimit,
        int materializedCandidateCount,
        IReadOnlyList<GroundworkNativeRouteCommandEvidence> commands)
    {
        ArgumentNullException.ThrowIfNull(request);
        EvidenceCatalog.EnsureProviderKey(providerKey);
        if (physicalCardinality != request.AcceptanceCardinality)
            throw new InvalidOperationException(
                $"Physical cardinality {physicalCardinality} does not match required acceptance cardinality {request.AcceptanceCardinality}.");
        if (planClassification is not ("indexed" or "index-search" or "index-only-scan" or "index-scan" or "index-seek"))
            throw new ArgumentException("Native route evidence requires an allowlisted aggregate plan classification.", nameof(planClassification));
        indexName = GroundworkNativeRoutePlanRequest.RequireProviderIdentifier(indexName, nameof(indexName));
        if (finiteLimit != request.Limit || finiteLimit <= 0)
            throw new InvalidOperationException("Native route evidence must preserve the requested finite limit.");
        if (materializedCandidateCount < 0 || materializedCandidateCount > finiteLimit)
            throw new InvalidOperationException("Materialized candidate count must be within the finite query bound.");
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count != 2 ||
            commands[0].Kind != PhysicalDocumentQueryCommandKind.Count ||
            commands[1].Kind != PhysicalDocumentQueryCommandKind.Page)
            throw new InvalidOperationException("Authoritative document-route evidence requires exact ordered Count then Page receipts.");
        for (var ordinal = 0; ordinal < commands.Count; ordinal++)
        {
            if (commands[ordinal].Ordinal != ordinal)
                throw new InvalidOperationException("Native command evidence must preserve contiguous production order.");
            if (!commands[ordinal].IndexNames.Contains(indexName, StringComparer.Ordinal))
                throw new InvalidOperationException("Every native command must prove the selected physical index.");
        }
        var hasStorageScopePredicate = commands.All(command =>
            command.PredicateFieldIdentifiers.Contains("storage_scope", StringComparer.Ordinal));
        var hasRoutePredicate = commands.All(command =>
            command.PredicateFieldIdentifiers.Contains(request.RouteField, StringComparer.Ordinal));
        if (!hasStorageScopePredicate || !hasRoutePredicate)
            throw new InvalidOperationException("Every native command must prove both storage-scope and route predicates.");

        var commandEvidence = string.Join('\n', commands.Select(command =>
            $"command-{command.Ordinal}={command.Kind}|{command.Identity}|{command.NativePlanFormat}|" +
            $"{command.PlanClassification}|{string.Join(',', command.IndexNames)}|" +
            $"{string.Join(',', command.PredicateFieldIdentifiers)}|{command.NativePlanSha256}"));

        var evidence = GroundworkSanitizedEvidence.Create(
            "identity-native-route-plan",
            $"provider={providerKey}\n" +
            $"document-kind={request.DocumentKind}\n" +
            $"route={request.QueryIdentity}\n" +
            $"physical-name={request.PhysicalName}\n" +
            $"physical-cardinality={physicalCardinality}\n" +
            $"plan-classification={planClassification}\n" +
            $"index={indexName}\n" +
            $"storage-scope-predicate={hasStorageScopePredicate.ToString().ToLowerInvariant()}\n" +
            $"route-predicate={hasRoutePredicate.ToString().ToLowerInvariant()}\n" +
            $"finite-limit={finiteLimit}\n" +
            $"materialized-candidate-count={materializedCandidateCount}\n" +
            $"command-count={commands.Count}\n{commandEvidence}");
        return new GroundworkNativeRoutePlanResult(
            request,
            providerKey,
            physicalCardinality,
            planClassification,
            indexName,
            hasStorageScopePredicate,
            hasRoutePredicate,
            finiteLimit,
            materializedCandidateCount,
            commands.ToArray(),
            evidence);
    }
}
