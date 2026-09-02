using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Groundwork.Kernel;
using Microsoft.Data.Sqlite;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures the required Secret list route from a freshly executed public-repository fixture.
/// Groundwork routes use the provider's opt-in explain assertion artifact; the temporary EF oracle
/// obtains the equivalent SQLite plan from the same bounded query shape after invoking its public route.
/// </summary>
internal static class SecretNativePlanCapture
{
    private const string RouteIdentity = "list-filtered";
    private const string RouteContract = "provider-native-routes";

    public static async Task<string> CaptureAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observed);
        EnsureRequest(request, observed);

        return request.Adapter switch
        {
            BenchmarkAdapterRegistry.EfSecretRepositoryAdapterId =>
                await CaptureEfAsync(request, connectionString, outputDirectory, observed, cancellationToken),
            BenchmarkAdapterRegistry.GroundworkSecretRepositoryAdapterId =>
                await CaptureGroundworkAsync(request, connectionString, outputDirectory, observed, cancellationToken),
            _ => throw new PerformanceContractException(
                $"Secret native-plan capture does not support adapter '{request.Adapter}'.")
        };
    }

    private static async Task<string> CaptureEfAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Provider, "sqlite", StringComparison.Ordinal))
            throw new PerformanceContractException("The temporary EF Secret native-plan capture only supports sqlite.");

        await using var adapter = new EfSecretRepositoryAdapter(request, connectionString, outputDirectory);
        await adapter.PrepareAsync(cancellationToken);
        await new SecretCreateReadListWorkload().ExecuteAsync(adapter, cancellationToken);
        var concurrency = adapter.RequireConcurrencyEvidence();

        adapter.CommandObserver.ClearCommands();
        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        var page = await scopes.Primary.ListPageAsync(
            SecretCreateReadListWorkload.PrimaryTenantId,
            FilteredPage(),
            cancellationToken);
        var command = adapter.CommandObserver.Commands.FirstOrDefault(snapshot =>
            SecretRoutePredicateInspector.TryInspectSql(
                snapshot.CommandText,
                "TenantId",
                "Status",
                out _) &&
            SecretRoutePredicateInspector.HasBoundedPageShape(snapshot.CommandText));
        if (command is null)
            throw new PerformanceContractException(
                "EF Secret native route 'list-filtered' did not emit a bounded SQL query whose WHERE clause contains parameterized tenant and status equality predicates.");
        var predicateProof = SecretRoutePredicateInspector.InspectSql(
            command.CommandText,
            "TenantId",
            "Status");

        var (rawPlan, physicalCardinality) = await CaptureSqlitePlanAsync(
            connectionString,
            adapter.PhysicalTenantId(SecretCreateReadListWorkload.PrimaryTenantId),
            command,
            page,
            cancellationToken);
        var parsed = IamNativePlanParser.ParseSecret("sqlite", rawPlan);
        var rawPlanReference = ArtifactStore.RawPlanName(
            $"secret.{request.Provider}.{request.MeasurementSetId}.{RouteIdentity}.raw.txt");
        var rawPlanPath = Path.Combine(outputDirectory, rawPlanReference);
        WriteRawPlan(
            rawPlanPath,
            request.Provider,
            physicalCardinality,
            SecretCreateReadListWorkload.PageSize,
            page.Items.Count,
            parsed.Content);
        return NativePlanEvidenceStaging.Write(
            outputDirectory,
            CreateDocument(
                request,
                observed,
                concurrency,
                new NativeRouteEvidence(
                    RouteIdentity,
                    rawPlanReference,
                    NativePlanEvidenceStaging.Sha256(rawPlanPath),
                    parsed.PlanClassification,
                    parsed.PhysicalIndexName,
                    physicalCardinality,
                    predicateProof.HasStorageScopePredicate,
                    predicateProof.HasRoutePredicate,
                    SecretCreateReadListWorkload.PageSize,
                    page.Items.Count)));
    }

    private static async Task<string> CaptureGroundworkAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        var adapter = new GroundworkSecretRepositoryAdapter(request, connectionString, outputDirectory);
        await using (adapter)
        {
            await adapter.PrepareAsync(cancellationToken);
            await new SecretCreateReadListWorkload().ExecuteAsync(adapter, cancellationToken);
            var concurrency = adapter.RequireConcurrencyEvidence();

            var allScopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
            var all = await allScopes.Primary.ListPageAsync(
                SecretCreateReadListWorkload.PrimaryTenantId,
                new Elsa.Secrets.Core.Contracts.SecretRepositoryListRequest(take: 1),
                cancellationToken);
            adapter.CommandObserver.ClearCommands();

            var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
            var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
            var explainDirectory = Path.Combine(
                Path.GetTempPath(),
                $"groundwork-secret-explain-{request.Provider}-{request.MeasurementSetId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(explainDirectory);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", explainDirectory);
            try
            {
                var artifactsBefore = Directory.EnumerateFiles(explainDirectory).ToHashSet(StringComparer.Ordinal);
                var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
                var page = await scopes.Primary.ListPageAsync(
                    SecretCreateReadListWorkload.PrimaryTenantId,
                    FilteredPage(),
                    cancellationToken);
                var command = RequireGroundworkRouteCommand(adapter.CommandObserver.Commands, request.Provider);

                var nativePlanPath = RequireNativePlanArtifact(
                    explainDirectory,
                    artifactsBefore,
                    request.Provider);
                var rawNativePlan = File.ReadAllText(nativePlanPath);
                var nativePlan = IamNativePlanParser.ParseSecret(
                    request.Provider,
                    rawNativePlan);
                var predicateProof = string.Equals(request.Provider, "mongodb", StringComparison.Ordinal)
                    ? SecretRoutePredicateInspector.InspectMongoExplain(rawNativePlan)
                    : SecretRoutePredicateInspector.InspectSql(
                        command.CommandText,
                        "__groundwork_scope",
                        SecretsGroundworkStorageSchema.StatusField);
                if (!string.Equals(request.Provider, "mongodb", StringComparison.Ordinal) &&
                    !SecretRoutePredicateInspector.HasSqlParameterizedEquality(
                        command.CommandText,
                        SecretsGroundworkStorageSchema.TenantIdField))
                    throw new PerformanceContractException(
                        "Secret Groundwork SQL route must retain its tenantId equality predicate in addition to the provider scope and status predicates.");
                var normalizedPlan = IamNativePlanParser.NormalizeForArtifact(request.Provider, nativePlan.Content);
                nativePlan = IamNativePlanParser.ParseSecret(request.Provider, normalizedPlan);
                if (string.Equals(request.Provider, "mongodb", StringComparison.Ordinal))
                    _ = SecretRoutePredicateInspector.InspectMongoExplain(normalizedPlan);
                var rawPlanReference = ArtifactStore.RawPlanName(
                    $"secret.{request.Provider}.{request.MeasurementSetId}.{RouteIdentity}.raw{IamNativePlanParser.RawPlanExtension(request.Provider)}");
                var rawPlanPath = Path.Combine(outputDirectory, rawPlanReference);
                WriteRawPlan(
                    rawPlanPath,
                    request.Provider,
                    checked((int)all.TotalCount),
                    SecretCreateReadListWorkload.PageSize,
                    page.Items.Count,
                    normalizedPlan);

                return NativePlanEvidenceStaging.Write(
                    outputDirectory,
                    CreateDocument(
                        request,
                        observed,
                        concurrency,
                        new NativeRouteEvidence(
                            RouteIdentity,
                            rawPlanReference,
                            NativePlanEvidenceStaging.Sha256(rawPlanPath),
                            nativePlan.PlanClassification,
                            nativePlan.PhysicalIndexName,
                            checked((int)all.TotalCount),
                            predicateProof.HasStorageScopePredicate,
                            predicateProof.HasRoutePredicate,
                            SecretCreateReadListWorkload.PageSize,
                            page.Items.Count)));
            }
            finally
            {
                Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
                Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
                try
                {
                    if (Directory.Exists(explainDirectory))
                        Directory.Delete(explainDirectory, recursive: true);
                }
                catch
                {
                    // The retained route artifact is complete; diagnostics cleanup must not mask it.
                }
            }
        }
    }

    private static Elsa.Secrets.Core.Contracts.SecretRepositoryListRequest FilteredPage() =>
        new(status: SecretStatus.Active, skip: 0, take: SecretCreateReadListWorkload.PageSize);

    private static async Task<(string RawPlan, int PhysicalCardinality)> CaptureSqlitePlanAsync(
        string connectionString,
        string physicalTenantId,
        EfCommandSnapshot observedCommand,
        Elsa.Secrets.Core.Contracts.SecretRepositoryPage page,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // The observer captured this exact command before EF disposed its context. Reusing its SQL and
        // cloned parameter values keeps the native plan bound to the public repository's actual route,
        // including its projection, ordering, limit, and offset shape.
        command.CommandText = $"EXPLAIN QUERY PLAN {observedCommand.CommandText.Trim().TrimEnd(';')}";
        foreach (var parameter in observedCommand.Parameters)
        {
            var copy = command.CreateParameter();
            copy.ParameterName = parameter.Name;
            copy.DbType = parameter.DbType;
            copy.Size = parameter.Size;
            copy.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(copy);
        }
        var lines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                lines.Add(string.Join('\t', Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
        }

        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM \"Secrets\" WHERE \"TenantId\" = $tenant;";
        count.Parameters.AddWithValue("$tenant", physicalTenantId);
        var physicalCardinality = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (page.Items.Count != SecretCreateReadListWorkload.PageSize || lines.Count == 0 || physicalCardinality <= 0)
            throw new PerformanceContractException("EF Secret native route did not materialize a finite filtered page and physical cardinality.");
        return (string.Join(Environment.NewLine, lines), physicalCardinality);
    }

    private static NativePlanEvidenceDocument CreateDocument(
        RunRequest request,
        ProviderProbe.Result observed,
        SecretProviderConcurrencyEvidence concurrency,
        NativeRouteEvidence route) =>
        new NativePlanEvidenceDocument(
            2,
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.Adapter,
            request.PhysicalForm,
            request.Scale,
            request.CommitSha,
            request.HarnessAssemblySha256,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            observed.Version,
            observed.Topology,
            observed.Configuration,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            [route],
            RouteContract)
        {
            ProviderConcurrency = concurrency
        };

    private static string RequireNativePlanArtifact(
        string explainDirectory,
        IReadOnlySet<string> artifactsBefore,
        string provider)
    {
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var suffix = $"-{SecretsGroundworkStorageSchema.FilteredListIndex}{extension}";
        var matches = Directory.EnumerateFiles(explainDirectory)
            .Where(path => !artifactsBefore.Contains(path))
            .Where(path => Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"Secret native route '{RouteIdentity}' must emit exactly one provider-native explain artifact for logical index '{SecretsGroundworkStorageSchema.FilteredListIndex}'; observed {matches.Length}.");
        return matches[0];
    }

    private static ProviderCommandEvent RequireGroundworkRouteCommand(
        IReadOnlyList<ProviderCommandEvent> commands,
        string provider)
    {
        var expectedOperation = provider + ".query";
        var matches = commands
            .Where(command =>
                !command.IsProbe &&
                command.Kind == ProviderCommandKind.Read &&
                string.Equals(command.Operation, expectedOperation, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].CommandText))
            throw new PerformanceContractException(
                $"Groundwork Secret native route '{RouteIdentity}' must emit exactly one observable '{expectedOperation}' provider command; observed {matches.Length}.");
        return matches[0];
    }

    private static void WriteRawPlan(
        string path,
        string provider,
        int physicalCardinality,
        int finiteLimit,
        int materializedCandidateCount,
        string providerPlan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            SecretRetainedNativePlan.Create(
                provider,
                physicalCardinality,
                finiteLimit,
                materializedCandidateCount,
                providerPlan),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ArtifactStore.ValidateRawPlanFile(path);
    }

    private static void EnsureRequest(RunRequest request, ProviderProbe.Result observed)
    {
        if (!string.Equals(request.WorkloadId, SecretCreateReadListWorkload.WorkloadId, StringComparison.Ordinal) ||
            !string.Equals(request.WorkloadVersion, SecretCreateReadListWorkload.Version, StringComparison.Ordinal))
            throw new PerformanceContractException("Secret native-plan capture requires the executable secret-create-read-list v1.1 workload.");
        if (!string.Equals(request.PhysicalForm, EfSecretRepositoryAdapter.PhysicalForm, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Secret native-plan capture requires physical form '{EfSecretRepositoryAdapter.PhysicalForm}'.");
        if (!string.Equals(request.NativePlanEvidenceReference,
                           NativePlanEvidenceStaging.ReferenceFor(request.WorkloadId, request.Provider, request.MeasurementSetId),
                           StringComparison.Ordinal))
            throw new PerformanceContractException("Secret native-plan evidence must use the request-bound evidence reference.");
        if (!string.Equals(observed.Provider, request.Provider, StringComparison.Ordinal) ||
            !string.Equals(observed.Version, request.ProviderVersion, StringComparison.Ordinal) ||
            !string.Equals(observed.Topology, request.ProviderTopology, StringComparison.Ordinal) ||
            !observed.Configuration.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new PerformanceContractException(
                "The live provider probe does not match the requested Secret native-plan provider identity, topology, or sanitized configuration.");
    }
}

internal readonly record struct SecretRoutePredicateProof(
    bool HasStorageScopePredicate,
    bool HasRoutePredicate,
    string? MongoAggregateCollection = null);

/// <summary>
/// Proves the predicate facts from the provider command structure retained for the route. It does not
/// infer them from a table name, index name, or an unrelated occurrence of a field name in projection,
/// ordering, or metadata.
/// </summary>
internal static class SecretRoutePredicateInspector
{
    internal static bool HasBoundedPageShape(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;
        var tokens = SqlTokenizer.Tokenize(sql);
        return tokens.Any(token => token.IsIdentifier("order")) &&
               tokens.Any(token => token.IsIdentifier("limit"));
    }

    internal static bool TryInspectSql(
        string? sql,
        string storageScopeField,
        string routeField,
        out SecretRoutePredicateProof proof)
    {
        proof = default;
        if (string.IsNullOrWhiteSpace(sql))
            return false;
        foreach (var clause in SqlTokenizer.WhereClauses(sql))
        {
            var hasScope = SqlRequiredEqualityParser.Requires(clause, storageScopeField);
            var hasRoute = SqlRequiredEqualityParser.Requires(clause, routeField);
            if (!hasScope || !hasRoute)
                continue;
            proof = new SecretRoutePredicateProof(true, true);
            return true;
        }
        return false;
    }

    internal static SecretRoutePredicateProof InspectSql(
        string? sql,
        string storageScopeField,
        string routeField)
    {
        if (!TryInspectSql(sql, storageScopeField, routeField, out var proof))
            throw new PerformanceContractException(
                $"Secret native route '{RouteIdentityForError}' SQL WHERE clause must contain parameterized equality predicates for '{storageScopeField}' and '{routeField}'.");
        return proof;
    }

    internal static bool HasSqlParameterizedEquality(string? sql, string field)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;
        return SqlTokenizer.WhereClauses(sql).Any(clause => SqlRequiredEqualityParser.Requires(clause, field));
    }

    internal static SecretRoutePredicateProof InspectMongoExplain(string rawPlan)
    {
        ArgumentNullException.ThrowIfNull(rawPlan);
        try
        {
            using var document = JsonDocument.Parse(rawPlan);
            var commands = new List<JsonElement>();
            CollectAggregateCommands(document.RootElement, commands);
            if (commands.Count != 1)
                throw new PerformanceContractException(
                    $"Secret MongoDB native plan must retain exactly one actual aggregate command and pipeline; observed {commands.Count}.");

            var command = commands[0];
            var collection = command.GetProperty("aggregate").GetString();
            if (string.IsNullOrWhiteSpace(collection))
                throw new PerformanceContractException(
                    "Secret MongoDB native plan aggregate command did not expose its physical collection.");
            var pipeline = command.GetProperty("pipeline");
            var matches = pipeline.EnumerateArray()
                .Where(stage => stage.ValueKind == JsonValueKind.Object &&
                                stage.TryGetProperty("$match", out var match) &&
                                match.ValueKind == JsonValueKind.Object)
                .Select(stage => stage.GetProperty("$match"))
                .ToArray();
            var hasScope = matches.Any(match => MongoRequiresEquality(match, SecretsGroundworkStorageSchema.TenantIdField));
            var hasRoute = matches.Any(match => MongoRequiresEquality(match, SecretsGroundworkStorageSchema.StatusField));
            var hasFiniteLimit = pipeline.EnumerateArray().Any(stage =>
                stage.ValueKind == JsonValueKind.Object &&
                stage.TryGetProperty("$limit", out var limit) &&
                limit.TryGetInt32(out var value) &&
                value == SecretCreateReadListWorkload.PageSize);
            if (!hasScope || !hasRoute || !hasFiniteLimit)
                throw new PerformanceContractException(
                    "Secret MongoDB aggregate pipeline must contain $match equality predicates for tenantId and status plus the frozen finite $limit.");
            return new SecretRoutePredicateProof(hasScope, hasRoute, collection);
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException(
                $"Secret MongoDB native plan is not valid explain JSON: {exception.Message}");
        }
    }

    private const string RouteIdentityForError = "list-filtered";

    internal static bool HasParameterizedEqualityAtom(IReadOnlyList<SqlToken> clause, string field)
    {
        for (var index = 0; index < clause.Count; index++)
        {
            if (clause[index].Kind != SqlTokenKind.Equals)
                continue;
            if (index + 1 < clause.Count && IsRightParameterOperand(clause, index + 1) &&
                IsExactFieldReferenceEndingAt(clause, index - 1, field))
                return true;
            if (index > 0 && IsLeftParameterOperand(clause, index - 1) &&
                IsExactFieldReferenceStartingAt(clause, index + 1, field))
                return true;
        }
        return false;
    }

    private static bool IsExactFieldReferenceEndingAt(IReadOnlyList<SqlToken> tokens, int end, string field)
    {
        if (end >= 1 && tokens[end - 1].IsIdentifier("collate") && tokens[end].Kind == SqlTokenKind.Identifier)
            end -= 2;
        if (end < 0 || !tokens[end].IsIdentifier(field))
            return false;
        var start = end;
        while (start >= 2 && tokens[start - 1].Kind == SqlTokenKind.Dot && tokens[start - 2].Kind == SqlTokenKind.Identifier)
            start -= 2;
        return start == 0;
    }

    private static bool IsExactFieldReferenceStartingAt(IReadOnlyList<SqlToken> tokens, int start, string field)
    {
        var end = start;
        while (end + 2 < tokens.Count && tokens[end].Kind == SqlTokenKind.Identifier &&
               tokens[end + 1].Kind == SqlTokenKind.Dot && tokens[end + 2].Kind == SqlTokenKind.Identifier)
            end += 2;
        if (end >= tokens.Count || !tokens[end].IsIdentifier(field))
            return false;
        if (end + 2 < tokens.Count && tokens[end + 1].IsIdentifier("collate") &&
            tokens[end + 2].Kind == SqlTokenKind.Identifier)
            end += 2;
        return end == tokens.Count - 1;
    }

    private static bool IsExpressionBoundary(SqlToken token) =>
        token.Kind is SqlTokenKind.LeftParenthesis or SqlTokenKind.RightParenthesis ||
        token.IsIdentifier("and") || token.IsIdentifier("or");

    private static bool IsRightParameterOperand(IReadOnlyList<SqlToken> tokens, int index) =>
        tokens[index].Kind == SqlTokenKind.Parameter &&
        (index == tokens.Count - 1 || IsExpressionBoundary(tokens[index + 1]));

    private static bool IsLeftParameterOperand(IReadOnlyList<SqlToken> tokens, int index) =>
        tokens[index].Kind == SqlTokenKind.Parameter &&
        (index == 0 || IsExpressionBoundary(tokens[index - 1]));

    private static void CollectAggregateCommands(JsonElement value, List<JsonElement> commands)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("command", out var command) &&
                command.ValueKind == JsonValueKind.Object &&
                command.TryGetProperty("aggregate", out var aggregate) &&
                aggregate.ValueKind == JsonValueKind.String &&
                command.TryGetProperty("pipeline", out var pipeline) &&
                pipeline.ValueKind == JsonValueKind.Array)
                commands.Add(command.Clone());
            foreach (var property in value.EnumerateObject())
                CollectAggregateCommands(property.Value, commands);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                CollectAggregateCommands(item, commands);
        }
    }

    private static bool MongoRequiresEquality(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in value.EnumerateObject())
        {
            if (string.Equals(property.Name, field, StringComparison.Ordinal) &&
                IsMongoEqualityValue(property.Value))
                return true;
            if (string.Equals(property.Name, "$and", StringComparison.Ordinal) &&
                property.Value.ValueKind == JsonValueKind.Array &&
                property.Value.EnumerateArray().Any(item => MongoRequiresEquality(item, field)))
                return true;
            if (string.Equals(property.Name, "$or", StringComparison.Ordinal) &&
                property.Value.ValueKind == JsonValueKind.Array)
            {
                var branches = property.Value.EnumerateArray().ToArray();
                if (branches.Length != 0 && branches.All(item => MongoRequiresEquality(item, field)))
                    return true;
            }
        }
        return false;
    }

    private static bool IsMongoEqualityValue(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("$eq", out var equality) &&
        equality.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array;
}

/// <summary>
/// Computes whether a positive parameterized field equality is required by a SQL boolean expression.
/// AND preserves a proof from either side, OR requires the proof on both sides, and NOT discards it.
/// This prevents an equality hidden in a permissive OR branch from masquerading as a route constraint.
/// </summary>
internal sealed class SqlRequiredEqualityParser
{
    private readonly IReadOnlyList<SqlToken> tokens;
    private readonly string field;
    private int index;
    private bool valid = true;

    private SqlRequiredEqualityParser(IReadOnlyList<SqlToken> tokens, string field)
    {
        this.tokens = tokens;
        this.field = field;
    }

    internal static bool Requires(IReadOnlyList<SqlToken> tokens, string field)
    {
        var parser = new SqlRequiredEqualityParser(tokens, field);
        var result = parser.ParseOr();
        return parser.valid && parser.index == tokens.Count && result;
    }

    private bool ParseOr()
    {
        var result = ParseAnd();
        while (Match("or"))
            result &= ParseAnd();
        return result;
    }

    private bool ParseAnd()
    {
        var result = ParseUnary();
        while (Match("and"))
            result |= ParseUnary();
        return result;
    }

    private bool ParseUnary()
    {
        if (Match("not"))
        {
            _ = ParseUnary();
            return false;
        }
        if (index < tokens.Count && tokens[index].Kind == SqlTokenKind.LeftParenthesis)
        {
            index++;
            var result = ParseOr();
            if (index >= tokens.Count || tokens[index].Kind != SqlTokenKind.RightParenthesis)
            {
                valid = false;
                return false;
            }
            index++;
            return result;
        }
        return ParseAtom();
    }

    private bool ParseAtom()
    {
        var start = index;
        var depth = 0;
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (depth == 0 &&
                (token.Kind == SqlTokenKind.RightParenthesis || token.IsIdentifier("and") || token.IsIdentifier("or")))
                break;
            depth += token.Kind switch
            {
                SqlTokenKind.LeftParenthesis => 1,
                SqlTokenKind.RightParenthesis => -1,
                _ => 0
            };
            if (depth < 0)
                break;
            index++;
        }
        if (start == index || depth != 0)
        {
            valid = false;
            return false;
        }
        return SecretRoutePredicateInspector.HasParameterizedEqualityAtom(
            tokens.Skip(start).Take(index - start).ToArray(),
            field);
    }

    private bool Match(string keyword)
    {
        if (index >= tokens.Count || !tokens[index].IsIdentifier(keyword))
            return false;
        index++;
        return true;
    }
}

internal enum SqlTokenKind
{
    Identifier,
    Parameter,
    Dot,
    Equals,
    LeftParenthesis,
    RightParenthesis,
    Other
}

internal readonly record struct SqlToken(SqlTokenKind Kind, string Value)
{
    internal bool IsIdentifier(string value) =>
        Kind == SqlTokenKind.Identifier && string.Equals(Value, value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Minimal provider-neutral SQL lexer used only for benchmark evidence. Comments and string literals are
/// discarded, quoted identifiers are decoded as complete tokens, and WHERE clause boundaries are found
/// by parenthesis depth. It deliberately does not attempt to execute or normalize SQL.
/// </summary>
internal static class SqlTokenizer
{
    internal static IReadOnlyList<SqlToken> Tokenize(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var tokens = new List<SqlToken>();
        for (var index = 0; index < sql.Length;)
        {
            var current = sql[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not '\r' and not '\n')
                    index++;
                continue;
            }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                var close = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? sql.Length : close + 2;
                continue;
            }
            if (current == '\'')
            {
                SkipDelimited(sql, ref index, '\'', '\'', doubledEscape: true);
                continue;
            }
            if (current is '"' or '`' or '[')
            {
                var close = current == '[' ? ']' : current;
                tokens.Add(new SqlToken(SqlTokenKind.Identifier, ReadDelimited(sql, ref index, current, close)));
                continue;
            }
            if (current is '@' or ':' or '$' or '?')
            {
                var start = index++;
                while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] == '_'))
                    index++;
                tokens.Add(new SqlToken(SqlTokenKind.Parameter, sql[start..index]));
                continue;
            }
            if (char.IsLetter(current) || current == '_')
            {
                var start = index++;
                while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '$'))
                    index++;
                tokens.Add(new SqlToken(SqlTokenKind.Identifier, sql[start..index]));
                continue;
            }
            tokens.Add(new SqlToken(current switch
            {
                '.' => SqlTokenKind.Dot,
                '=' => SqlTokenKind.Equals,
                '(' => SqlTokenKind.LeftParenthesis,
                ')' => SqlTokenKind.RightParenthesis,
                _ => SqlTokenKind.Other
            }, current.ToString()));
            index++;
        }
        return tokens;
    }

    internal static IReadOnlyList<IReadOnlyList<SqlToken>> WhereClauses(string sql)
    {
        var tokens = Tokenize(sql);
        var clauses = new List<IReadOnlyList<SqlToken>>();
        var depth = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            depth += DepthDelta(tokens[index]);
            if (!tokens[index].IsIdentifier("where"))
                continue;
            var whereDepth = depth;
            var end = index + 1;
            var clauseDepth = whereDepth;
            for (; end < tokens.Count; end++)
            {
                if (clauseDepth == whereDepth && IsClauseTerminator(tokens, end))
                    break;
                clauseDepth += DepthDelta(tokens[end]);
                if (clauseDepth < whereDepth)
                    break;
            }
            clauses.Add(tokens.Skip(index + 1).Take(end - index - 1).ToArray());
        }
        return clauses;
    }

    private static bool IsClauseTerminator(IReadOnlyList<SqlToken> tokens, int index) =>
        tokens[index] is { Kind: SqlTokenKind.Other, Value: ";" } ||
        tokens[index].IsIdentifier("limit") || tokens[index].IsIdentifier("offset") ||
        tokens[index].IsIdentifier("returning") ||
        (tokens[index].IsIdentifier("group") || tokens[index].IsIdentifier("order")) &&
        index + 1 < tokens.Count && tokens[index + 1].IsIdentifier("by");

    private static int DepthDelta(SqlToken token) => token.Kind switch
    {
        SqlTokenKind.LeftParenthesis => 1,
        SqlTokenKind.RightParenthesis => -1,
        _ => 0
    };

    private static string ReadDelimited(string sql, ref int index, char open, char close)
    {
        index++;
        var result = new StringBuilder();
        while (index < sql.Length)
        {
            var current = sql[index++];
            if (current != close)
            {
                result.Append(current);
                continue;
            }
            if (index < sql.Length && sql[index] == close && open != '[')
            {
                result.Append(close);
                index++;
                continue;
            }
            return result.ToString();
        }
        return result.ToString();
    }

    private static void SkipDelimited(
        string sql,
        ref int index,
        char open,
        char close,
        bool doubledEscape)
    {
        index++;
        while (index < sql.Length)
        {
            var current = sql[index++];
            if (current != close)
                continue;
            if (doubledEscape && index < sql.Length && sql[index] == close)
            {
                index++;
                continue;
            }
            return;
        }
    }
}
