using System.Globalization;
using System.Text.RegularExpressions;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Text;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

public enum GroundworkNativeRouteProjectedValueKind
{
    String,
    Boolean,
    Int64,
    DateTime
}

public sealed record GroundworkNativeRouteProjectedValue
{
    public GroundworkNativeRouteProjectedValue(
        string field,
        GroundworkNativeRouteProjectedValueKind kind,
        string value,
        string? noiseValue = null)
    {
        Field = GroundworkNativeRoutePlanRequest.RequirePhysicalIdentifier(field, nameof(field));
        Kind = kind;
        Value = Normalize(value, kind);
        NoiseValue = Normalize(noiseValue ?? DefaultNoise(Value, kind), kind);
        if (string.Equals(Value, NoiseValue, StringComparison.Ordinal))
            throw new ArgumentException("Projected target and noise values must differ.", nameof(noiseValue));
    }

    public string Field { get; }
    public GroundworkNativeRouteProjectedValueKind Kind { get; }
    public string Value { get; }
    public string NoiseValue { get; }

    public object ToProviderValue() => ToProviderValue(Value);

    public object ToProviderNoiseValue() => ToProviderValue(NoiseValue);

    private object ToProviderValue(string value) => Kind switch
    {
        GroundworkNativeRouteProjectedValueKind.String => value,
        GroundworkNativeRouteProjectedValueKind.Boolean => bool.Parse(value),
        GroundworkNativeRouteProjectedValueKind.Int64 => long.Parse(value, CultureInfo.InvariantCulture),
        GroundworkNativeRouteProjectedValueKind.DateTime => DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown native-route projected value kind.")
    };

    public static GroundworkNativeRouteProjectedValue String(
        string field,
        string value,
        string? noiseValue = null) =>
        new(field, GroundworkNativeRouteProjectedValueKind.String, value, noiseValue);

    public static GroundworkNativeRouteProjectedValue Boolean(
        string field,
        bool value,
        bool? noiseValue = null) =>
        new(
            field,
            GroundworkNativeRouteProjectedValueKind.Boolean,
            value.ToString(),
            noiseValue?.ToString());

    public static GroundworkNativeRouteProjectedValue Int64(
        string field,
        long value,
        long? noiseValue = null) =>
        new(
            field,
            GroundworkNativeRouteProjectedValueKind.Int64,
            value.ToString(CultureInfo.InvariantCulture),
            noiseValue?.ToString(CultureInfo.InvariantCulture));

    public static GroundworkNativeRouteProjectedValue DateTime(
        string field,
        DateTimeOffset value,
        DateTimeOffset? noiseValue = null) =>
        new(
            field,
            GroundworkNativeRouteProjectedValueKind.DateTime,
            value.ToString("O", CultureInfo.InvariantCulture),
            noiseValue?.ToString("O", CultureInfo.InvariantCulture));

    private static string DefaultNoise(
        string value,
        GroundworkNativeRouteProjectedValueKind kind) =>
        kind switch
        {
            GroundworkNativeRouteProjectedValueKind.String =>
                string.Equals(value, "native-noise", StringComparison.Ordinal)
                    ? "native-other"
                    : "native-noise",
            GroundworkNativeRouteProjectedValueKind.Boolean => (!bool.Parse(value)).ToString(),
            GroundworkNativeRouteProjectedValueKind.Int64 =>
                checked(long.Parse(value, CultureInfo.InvariantCulture) + 1)
                    .ToString(CultureInfo.InvariantCulture),
            GroundworkNativeRouteProjectedValueKind.DateTime =>
                DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    .AddDays(1)
                    .ToString("O", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown native-route projected value kind.")
        };

    private static string Normalize(string value, GroundworkNativeRouteProjectedValueKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A nonblank projected value is required.", nameof(value));
        return kind switch
        {
            GroundworkNativeRouteProjectedValueKind.String => value,
            GroundworkNativeRouteProjectedValueKind.Boolean => bool.Parse(value).ToString().ToLowerInvariant(),
            GroundworkNativeRouteProjectedValueKind.Int64 => long.Parse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            GroundworkNativeRouteProjectedValueKind.DateTime => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind).ToString("O", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown native-route projected value kind.")
        };
    }
}

public sealed record GroundworkNativeRoutePlanRequest
{
    private static readonly IReadOnlySet<string> EnvelopeFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "document_kind",
        "storage_scope",
        "id",
        "id_comparison_key",
        "id_lookup_key",
        "schema_version",
        "version",
        "created_utc",
        "updated_utc"
    };
    private static readonly Regex PhysicalIdentifier = new(
        "^[A-Za-z_][A-Za-z0-9_.-]*$",
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
        int? limit,
        int acceptanceCardinality,
        string candidateDocumentId = "native-000000",
        string candidateContentJson = "{}",
        string candidateSchemaVersion = "1.0.1",
        IReadOnlyCollection<GroundworkNativeRouteProjectedValue>? projectedValues = null,
        IReadOnlyList<string>? requiredPredicateFields = null,
        IReadOnlyList<PhysicalDocumentQueryCommandKind>? expectedCommandKinds = null,
        IReadOnlyList<string>? indexFields = null,
        string evidenceKind = "identity-native-route-plan",
        string contentColumn = "document",
        string? primaryPhysicalName = null,
        string? expectedIndexName = null,
        int matchingCardinality = 1,
        IReadOnlyList<string>? varyingProjectedFields = null)
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
        ProjectedValues = (projectedValues ?? [])
            .GroupBy(value => value.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Distinct().Single(),
                StringComparer.Ordinal);
        var unknownProjectedValues = ProjectedValues.Keys.Except(ProjectedFields, StringComparer.Ordinal).ToArray();
        if (unknownProjectedValues.Length != 0)
            throw new ArgumentException("Seed values must target declared physical projections.", nameof(projectedValues));
        RequiredPredicateFields = (requiredPredicateFields ?? [RouteField])
            .Select(field => RequirePhysicalIdentifier(field, nameof(requiredPredicateFields)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (RequiredPredicateFields.Count == 0)
            throw new ArgumentException("At least one route predicate field is required.", nameof(requiredPredicateFields));
        IndexFields = (indexFields ?? [RouteField])
            .Select(field => RequirePhysicalIdentifier(field, nameof(indexFields)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (IndexFields.Count == 0 ||
            !IndexFields.All(field =>
                ProjectedFields.Contains(field, StringComparer.Ordinal) ||
                EnvelopeFields.Contains(field)))
            throw new ArgumentException("Index fields must target declared physical projections.", nameof(indexFields));
        ExpectedCommandKinds = (expectedCommandKinds ??
                                [
                                    PhysicalDocumentQueryCommandKind.Count,
                                    PhysicalDocumentQueryCommandKind.Page
                                ])
            .ToArray();
        if (ExpectedCommandKinds.Count == 0)
            throw new ArgumentException("At least one native command kind is required.", nameof(expectedCommandKinds));
        if (limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "A finite limit must be positive when present.");
        if (ExpectedCommandKinds.Any(kind => kind is
                PhysicalDocumentQueryCommandKind.Page or
                PhysicalDocumentQueryCommandKind.First) &&
            limit is null)
        {
            throw new ArgumentException("Materializing native commands require a finite positive limit.", nameof(limit));
        }
        if (acceptanceCardinality <= 0)
            throw new ArgumentOutOfRangeException(nameof(acceptanceCardinality), "A positive physical cardinality is required.");
        if (matchingCardinality <= 0 || matchingCardinality >= acceptanceCardinality)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchingCardinality),
                "Matching cardinality must be positive and lower than the physical cardinality.");
        }
        Limit = limit;
        AcceptanceCardinality = acceptanceCardinality;
        MatchingCardinality = matchingCardinality;
        CandidateDocumentId = RequireValue(candidateDocumentId, nameof(candidateDocumentId));
        CandidateContentJson = RequireValue(candidateContentJson, nameof(candidateContentJson));
        CandidateSchemaVersion = RequireValue(candidateSchemaVersion, nameof(candidateSchemaVersion));
        EvidenceKind = RequireProviderIdentifier(evidenceKind, nameof(evidenceKind));
        ContentColumn = RequirePhysicalIdentifier(contentColumn, nameof(contentColumn));
        PrimaryPhysicalName = primaryPhysicalName is null
            ? null
            : RequirePhysicalIdentifier(primaryPhysicalName, nameof(primaryPhysicalName));
        if (string.Equals(PrimaryPhysicalName, PhysicalName, StringComparison.Ordinal))
            throw new ArgumentException("Linked lookup and primary physical names must differ.", nameof(primaryPhysicalName));
        ExpectedIndexName = expectedIndexName is null
            ? null
            : RequireProviderIdentifier(expectedIndexName, nameof(expectedIndexName));
        VaryingProjectedFields = (varyingProjectedFields ?? [])
            .Select(field => RequirePhysicalIdentifier(field, nameof(varyingProjectedFields)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!VaryingProjectedFields.All(field => ProjectedFields.Contains(field, StringComparer.Ordinal)))
            throw new ArgumentException("Varying fields must target declared physical projections.", nameof(varyingProjectedFields));
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
    public int? Limit { get; }
    public int AcceptanceCardinality { get; }
    public int MatchingCardinality { get; }
    public string CandidateDocumentId { get; }
    public string CandidateContentJson { get; }
    public string CandidateSchemaVersion { get; }
    public string CandidateComparisonKey { get; }
    public string CandidateLookupKey { get; }
    public IReadOnlyDictionary<string, GroundworkNativeRouteProjectedValue> ProjectedValues { get; }
    public IReadOnlyList<string> RequiredPredicateFields { get; }
    public IReadOnlyList<PhysicalDocumentQueryCommandKind> ExpectedCommandKinds { get; }
    public IReadOnlyList<string> IndexFields { get; }
    public string EvidenceKind { get; }
    public string ContentColumn { get; }
    public string? PrimaryPhysicalName { get; }
    public string? ExpectedIndexName { get; }
    public IReadOnlyList<string> VaryingProjectedFields { get; }

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
        int? providerAppliedMaximumRows,
        IReadOnlyList<GroundworkNativeRouteCommandOrderEvidence> providerAppliedOrder,
        string nativePlanSha256)
    {
        Ordinal = ordinal;
        Kind = kind;
        Identity = identity;
        NativePlanFormat = nativePlanFormat;
        PlanClassification = planClassification;
        IndexNames = indexNames;
        PredicateFieldIdentifiers = predicateFieldIdentifiers;
        ProviderAppliedMaximumRows = providerAppliedMaximumRows;
        ProviderAppliedOrder = providerAppliedOrder;
        NativePlanSha256 = nativePlanSha256;
    }

    public int Ordinal { get; }
    public PhysicalDocumentQueryCommandKind Kind { get; }
    public string Identity { get; }
    public string NativePlanFormat { get; }
    public string PlanClassification { get; }
    public IReadOnlyList<string> IndexNames { get; }
    public IReadOnlyList<string> PredicateFieldIdentifiers { get; }
    public int? ProviderAppliedMaximumRows { get; }
    public IReadOnlyList<GroundworkNativeRouteCommandOrderEvidence> ProviderAppliedOrder { get; }
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
        var providerAppliedOrder = command.ProviderAppliedOrder
            .Select(order => new GroundworkNativeRouteCommandOrderEvidence(
                GroundworkNativeRoutePlanRequest.RequirePhysicalIdentifier(
                    order.FieldIdentifier,
                    nameof(command.ProviderAppliedOrder)),
                order.Direction,
                order.IsIdentityTieBreak))
            .ToArray();
        return new GroundworkNativeRouteCommandEvidence(
            ordinal,
            command.Kind,
            command.Identity,
            command.NativePlanFormat,
            planClassification,
            indexes,
            predicateFields,
            command.ProviderAppliedMaximumRows,
            providerAppliedOrder,
            StableDigest.FromText(command.NativePlan));
    }
}

public sealed record GroundworkNativeRouteCommandOrderEvidence(
    string FieldIdentifier,
    PhysicalSortDirection Direction,
    bool IsIdentityTieBreak);

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
        string contentColumn,
        string? primaryPhysicalName,
        int matchingCardinality,
        IReadOnlyList<string> varyingProjectedFields,
        IReadOnlySet<string> predicateFields,
        IReadOnlyDictionary<string, GroundworkNativeRouteProjectedValue> projectedValues)
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
        ContentColumn = contentColumn;
        PrimaryPhysicalName = primaryPhysicalName;
        MatchingCardinality = matchingCardinality;
        VaryingProjectedFields = varyingProjectedFields;
        PredicateFields = predicateFields;
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
    public string ContentColumn { get; }
    public string? PrimaryPhysicalName { get; }
    public int MatchingCardinality { get; }
    public IReadOnlyList<string> VaryingProjectedFields { get; }
    public IReadOnlySet<string> PredicateFields { get; }
    public string CrossScope { get; }
    public IReadOnlyDictionary<string, GroundworkNativeRouteProjectedValue> ProjectedValues { get; }

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
                !string.Equals(request.ContentColumn, first.ContentColumn, StringComparison.Ordinal) ||
                !string.Equals(request.PrimaryPhysicalName, first.PrimaryPhysicalName, StringComparison.Ordinal) ||
                request.MatchingCardinality != first.MatchingCardinality ||
                !request.VaryingProjectedFields.SequenceEqual(first.VaryingProjectedFields, StringComparer.Ordinal) ||
                !request.ProjectedFields.SequenceEqual(first.ProjectedFields, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Routes sharing a physical evidence dataset must agree on kind, table, scope, cardinality, candidate, and projections.");
        }

        var projectedValues = requests
            .SelectMany(request =>
            {
                var values = request.ProjectedValues.Values.ToList();
                if (request.ProjectedValues.TryGetValue(request.RouteField, out var routeValue))
                {
                    if (!string.Equals(routeValue.Value, request.RouteValue, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Native-route seed value for '{request.RouteField}' does not match its route value.");
                    }
                }
                else
                {
                    values.Add(GroundworkNativeRouteProjectedValue.String(request.RouteField, request.RouteValue));
                }
                return values;
            })
            .GroupBy(value => value.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Distinct().Single(),
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
            first.ContentColumn,
            first.PrimaryPhysicalName,
            first.MatchingCardinality,
            first.VaryingProjectedFields,
            requests.SelectMany(request => request.RequiredPredicateFields)
                .ToHashSet(StringComparer.Ordinal),
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
        int? finiteLimit,
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
    public int? FiniteLimit { get; }
    public int MaterializedCandidateCount { get; }
    public IReadOnlyList<GroundworkNativeRouteCommandEvidence> Commands { get; }
    public GroundworkSanitizedEvidence Evidence { get; }

    public static string SelectCompiledIndex(
        GroundworkNativeRoutePlanRequest request,
        IReadOnlyCollection<string> admittedLookupIndexes,
        IReadOnlyCollection<GroundworkNativeRouteCommandEvidence> commands)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(admittedLookupIndexes);
        ArgumentNullException.ThrowIfNull(commands);
        var admitted = admittedLookupIndexes.ToHashSet(StringComparer.Ordinal);
        var used = commands
            .Where(command => command.Kind != PhysicalDocumentQueryCommandKind.PrimaryHydration)
            .SelectMany(command => command.IndexNames)
            .Where(admitted.Contains)
            .ToArray();
        if (request.ExpectedIndexName is { } expected)
        {
            if (!admitted.Contains(expected))
            {
                throw new InvalidOperationException(
                    $"Compiled index '{expected}' is not present on lookup object '{request.PhysicalName}'.");
            }
            if (!used.Contains(expected, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Provider-native commands did not use compiled index '{expected}'. " +
                    $"Used admitted indexes: {string.Join(", ", used.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}.");
            }
            return expected;
        }

        return used
            .GroupBy(index => index, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Provider-native commands used no admitted lookup index.");
    }

    public static GroundworkNativeRoutePlanResult Create(
        GroundworkNativeRoutePlanRequest request,
        string providerKey,
        long physicalCardinality,
        string planClassification,
        string indexName,
        int? finiteLimit,
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
        if (finiteLimit != request.Limit)
            throw new InvalidOperationException("Native route evidence must preserve the requested materialization limit.");
        if (materializedCandidateCount < 0 ||
            finiteLimit is { } bound && materializedCandidateCount > bound ||
            finiteLimit is null && materializedCandidateCount != 0)
        {
            throw new InvalidOperationException("Materialized candidate count must be within the finite query bound.");
        }
        ArgumentNullException.ThrowIfNull(commands);
        if (!commands.Select(command => command.Kind).SequenceEqual(request.ExpectedCommandKinds))
            throw new InvalidOperationException("Native route evidence must preserve the exact ordered command contract.");
        for (var ordinal = 0; ordinal < commands.Count; ordinal++)
        {
            if (commands[ordinal].Ordinal != ordinal)
                throw new InvalidOperationException("Native command evidence must preserve contiguous production order.");
        }
        foreach (var command in commands)
        {
            if (command.ProviderAppliedMaximumRows is null)
                throw new InvalidOperationException("Every bounded native command must prove a provider-applied finite row maximum.");
            if (command.Kind is PhysicalDocumentQueryCommandKind.Page or PhysicalDocumentQueryCommandKind.First)
            {
                if (command.ProviderAppliedOrder.Count == 0)
                    throw new InvalidOperationException("Materializing native command evidence must preserve provider-applied order.");
            }
            else if (command.ProviderAppliedOrder.Count != 0)
            {
                throw new InvalidOperationException("Non-materializing native command evidence cannot claim provider-applied order.");
            }
        }
        var routeCommands = commands.Where(command =>
            command.Kind != PhysicalDocumentQueryCommandKind.PrimaryHydration).ToArray();
        if (!routeCommands.Any(command => command.IndexNames.Contains(indexName, StringComparer.Ordinal)))
            throw new InvalidOperationException("Native route evidence must prove the compiled plan's selected physical index.");
        var hasStorageScopePredicate = routeCommands.All(command =>
            command.PredicateFieldIdentifiers.Contains("storage_scope", StringComparer.Ordinal));
        var hasRoutePredicate = routeCommands.All(command =>
            request.RequiredPredicateFields.All(field =>
                command.PredicateFieldIdentifiers.Contains(field, StringComparer.Ordinal)));
        if (!hasStorageScopePredicate || !hasRoutePredicate)
            throw new InvalidOperationException("Every native command must prove storage scope and all required route predicates.");

        var commandEvidence = string.Join('\n', commands.Select(command =>
            $"command-{command.Ordinal}={command.Kind}|{command.Identity}|{command.NativePlanFormat}|" +
            $"{command.PlanClassification}|{string.Join(',', command.IndexNames)}|" +
            $"{string.Join(',', command.PredicateFieldIdentifiers)}|" +
            $"{command.ProviderAppliedMaximumRows?.ToString(CultureInfo.InvariantCulture)}|" +
            $"{string.Join(',', command.ProviderAppliedOrder.Select(order =>
                $"{order.FieldIdentifier}:{order.Direction}:{order.IsIdentityTieBreak.ToString().ToLowerInvariant()}"))}|" +
            $"{command.NativePlanSha256}"));

        var evidence = GroundworkSanitizedEvidence.Create(
            request.EvidenceKind,
            $"provider={providerKey}\n" +
            $"document-kind={request.DocumentKind}\n" +
            $"route={request.QueryIdentity}\n" +
            $"physical-name={request.PhysicalName}\n" +
            $"physical-cardinality={physicalCardinality}\n" +
            $"plan-classification={planClassification}\n" +
            $"index={indexName}\n" +
            $"storage-scope-predicate={hasStorageScopePredicate.ToString().ToLowerInvariant()}\n" +
            $"route-predicate={hasRoutePredicate.ToString().ToLowerInvariant()}\n" +
            $"finite-limit={(finiteLimit?.ToString(CultureInfo.InvariantCulture) ?? "none")}\n" +
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
