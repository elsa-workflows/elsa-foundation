using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;

namespace Elsa.Etl.Design;

internal sealed partial class EtlArtifactCompiler
{
    public EtlCompilationResult Compile(string definitionId, int definitionVersion, EtlPipelineV2 pipeline)
    {
        var diagnostics = new DiagnosticCollector();
        ValidateDefinitionIdentity(definitionId, definitionVersion, diagnostics);
        if (pipeline is null)
        {
            diagnostics.Add("pipeline.required", "pipeline", "A pipeline definition is required.");
            return Failure(diagnostics);
        }

        ValidateConnectorLimits(pipeline.Limits, pipeline.Http, diagnostics);
        var artifactLimit = pipeline.Limits is { MaxArtifactBytes: > 0 } limits
            ? Math.Min(limits.MaxArtifactBytes, MaximumArtifactBytes)
            : MaximumArtifactBytes;
        if (!CheckConnectorStructureAndStrings(pipeline, definitionId, artifactLimit))
        {
            diagnostics.Add("artifact.too-large", "pipeline", "The pipeline declarations exceed the artifact byte limit.");
            return Failure(diagnostics);
        }

        var source = ValidateConnectorSource(pipeline.Source, pipeline.Limits?.MaxFields, diagnostics);
        var lookup = new Dictionary<string, EtlColumn>(StringComparer.Ordinal);
        if (pipeline.Lookup is { } declaration)
        {
            ValidateAlias(declaration.Binding, "lookup.binding", diagnostics);
            lookup = ValidateSchema(declaration.Schema, "lookup.schema", pipeline.Limits?.MaxFields, diagnostics);
            ValidateLookupKeys(declaration.SourceKeyField, declaration.LookupKeyField,
                "lookup.sourceKeyField", "lookup.lookupKeyField", source, lookup, diagnostics);
        }
        if (pipeline.Routes is null)
            diagnostics.Add("routes.required", "routes", "Both output routes must be declared.");
        else
        {
            ValidateDestination(pipeline.Routes.Main, "routes.main", diagnostics);
            ValidateDestination(pipeline.Routes.Errors, "routes.errors", diagnostics);
        }
        var mapped = ValidateMapping(pipeline.Mapping, source, lookup, diagnostics);
        ValidateFilters(pipeline.Filters, mapped, diagnostics);
        ValidateSqlDestination(pipeline, mapped, diagnostics);
        ValidateRabbitMqDestination(pipeline, diagnostics);
        if (pipeline.Source is not null && EtlContinuousRunPolicy.FindViolation(pipeline) is { } continuousViolation)
            diagnostics.Add(continuousViolation["etl.".Length..], "continuous", continuousViolation switch
            {
                "etl.continuous-poll-interval-invalid" => "The poll interval must be between 1 and 86,400 seconds.",
                "etl.continuous-pagination-unsupported" => "Continuous runs need token or next-link pagination.",
                _ => "Only HTTP sources can run continuously."
            });
        if (diagnostics.HasErrors)
            return Failure(diagnostics);

        JsonElement document;
        try
        {
            document = EtlArtifactV2Json.Write(pipeline);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            diagnostics.Add("pipeline.serialization-failed", "pipeline", "The pipeline cannot be represented as a supported v2 artifact.");
            return Failure(diagnostics);
        }
        if (Encoding.UTF8.GetByteCount(document.GetRawText()) > artifactLimit)
        {
            diagnostics.Add("artifact.too-large", "artifact", "The serialized artifact exceeds the configured byte limit.");
            return Failure(diagnostics);
        }

        var hash = EtlArtifactJson.Hash(document);
        var artifact = new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, definitionId, definitionVersion, hash),
            hash, definitionId, definitionVersion, document);
        if (JsonSerializer.SerializeToUtf8Bytes(artifact).Length > artifactLimit)
        {
            diagnostics.Add("artifact.too-large", "artifact", "The serialized artifact envelope exceeds the configured byte limit.");
            return Failure(diagnostics);
        }
        return new EtlCompilationResult(artifact, Array.Empty<EtlDiagnostic>());
    }

    private static void ValidateConnectorLimits(EtlConnectorLimits? limits, EtlHttpPolicy? http, DiagnosticCollector diagnostics)
    {
        if (limits is null)
            diagnostics.Add("limits.required", "limits", "Finite pipeline limits are required.");
        else
        {
            if (limits.FilePartitionCount <= 0 || limits.MaxPartitions <= 0 || limits.FilePartitionCount > limits.MaxPartitions ||
                limits.MaxRecordBytes <= 0 || limits.MaxFields <= 0 || limits.MaxBatchRecords <= 0 ||
                limits.MaxBatchBytes <= 0 || limits.MaxOutstandingRecords <= 0 || limits.MaxOutstandingBytes <= 0 ||
                limits.MaxLookupRecords <= 0 || limits.MaxLookupBytes <= 0 || limits.MaxOutputBatchBytes <= 0 ||
                limits.MaxRetainedOutputBytes <= 0 ||
                limits.MaxSnapshotBytes <= 0 || limits.MaxArtifactBytes <= 0 ||
                limits.MaxBatchRecords > limits.MaxOutstandingRecords || limits.MaxBatchBytes > limits.MaxOutstandingBytes ||
                limits.MaxRecordBytes > limits.MaxBatchBytes || limits.MaxLookupBytes > limits.MaxSnapshotBytes)
                diagnostics.Add("limits.invalid", "limits", "Positive partition, batch, lookup and outstanding limits must fit their enclosing limits.");
            if (limits.MaxArtifactBytes > MaximumArtifactBytes)
                diagnostics.Add("limits.artifact-too-large", "limits.maxArtifactBytes", "The artifact limit cannot exceed 256 KiB.");
        }
        if (http is null)
            diagnostics.Add("http.required", "http", "Finite HTTP page and request policy must be declared.");
        else if (http.MaxPageBytes <= 0 || http.MaxPageRecords <= 0 || http.MaxJsonDepth is <= 0 or > 64 ||
                 http.MaxContinuationBytes <= 0 || http.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes ||
                 http.MaxPagesPerPartition <= 0 || http.MaxRetainedInputBytes <= 0 || http.MaxRetainedMetadataBytes <= 0 ||
                 http.MaxAttempts <= 0 || http.DeadlineSeconds <= 0 || http.RequestTimeoutSeconds <= 0 ||
                 http.InitialDelaySeconds <= 0 || http.MaxDelaySeconds <= 0 ||
                 http.MaxPageBytes > http.MaxRetainedInputBytes || http.MaxContinuationBytes > http.MaxPageBytes ||
                 http.RequestTimeoutSeconds > http.DeadlineSeconds || http.InitialDelaySeconds > http.MaxDelaySeconds ||
                 http.MaxDelaySeconds > http.DeadlineSeconds)
            diagnostics.Add("http.limits.invalid", "http", "Positive HTTP bounds must fit their page, retention and deadline limits; JSON depth cannot exceed 64.");
    }

    private static Dictionary<string, EtlColumn> ValidateConnectorSource(EtlSource? source, int? maximumFields, DiagnosticCollector diagnostics)
    {
        switch (source)
        {
            case EtlCsvSource csv:
                ValidateAlias(csv.Binding, "source.binding", diagnostics);
                return ValidateSchema(csv.Schema, "source.schema", maximumFields, diagnostics);
            case EtlHttpJsonSource http:
                ValidateAlias(http.Binding, "source.binding", diagnostics);
                ValidatePointer(http.RecordsPointer, "source.recordsPointer", diagnostics);
                ValidatePagination(http.Pagination, diagnostics);
                return ValidateJsonFields(http.Fields, maximumFields, diagnostics);
            case EtlJsonlSource jsonl:
                ValidateAlias(jsonl.Binding, "source.binding", diagnostics);
                return ValidateJsonFields(jsonl.Fields, maximumFields, diagnostics);
            case EtlSqlSource sql:
                ValidateAlias(sql.Binding, "source.binding", diagnostics);
                var columns = ValidateSchema(sql.Columns, "source.columns", maximumFields, diagnostics);
                ValidateSqlSource(sql, columns, diagnostics);
                return columns;
            case EtlRabbitMqSource rabbitMq:
                ValidateAlias(rabbitMq.Binding, "source.binding", diagnostics);
                // The queue goes to the broker as a protocol short string, and the maximum bounds what one run holds (spec 1201).
                if (!IsRabbitMqQueue(rabbitMq.Queue))
                    diagnostics.Add("source.rabbitmq-name-invalid", "source.queue",
                        "The queue must be 1 to 255 letters, digits, underscores, hyphens, periods or colons.");
                if (rabbitMq.MaxMessages is < 1 or > EtlRabbitMqSource.MaxMessagesLimit)
                    diagnostics.Add("source.rabbitmq-max-messages-invalid", "source.maxMessages",
                        "The message maximum must be 1 to 100,000.");
                return ValidateJsonFields(rabbitMq.Fields, maximumFields, diagnostics);
            default:
                diagnostics.Add(source is null ? "source.required" : "source.unsupported", "source", "A supported input source is required.");
                return new Dictionary<string, EtlColumn>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, EtlColumn> ValidateJsonFields(IReadOnlyList<EtlJsonField>? fields, int? maximumFields,
        DiagnosticCollector diagnostics)
    {
        if (fields is null)
            return ValidateSchema(null, "source.fields", maximumFields, diagnostics);
        var columns = new EtlColumn[fields.Count];
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field is null)
            {
                columns[index] = null!;
                continue;
            }
            ValidatePointer(field.Pointer, $"source.fields[{index}].pointer", diagnostics);
            columns[index] = new EtlColumn(field.Name, field.Type, field.Required);
        }
        // Share scalar declaration rules without treating JSON records as CSV at execution time.
        return ValidateSchema(columns, "source.fields", maximumFields, diagnostics);
    }

    private static void ValidateDestination(EtlDestination? destination, string path, DiagnosticCollector diagnostics)
    {
        switch (destination)
        {
            case EtlJsonlDestination file: ValidateAlias(file.Binding, path + ".binding", diagnostics); break;
            case EtlHttpDestination http: ValidateAlias(http.Binding, path + ".binding", diagnostics); break;
            case EtlCsvDestination csv: ValidateAlias(csv.Binding, path + ".binding", diagnostics); break;
            case EtlSqlDestination sql: ValidateAlias(sql.Binding, path + ".binding", diagnostics); break;
            case EtlRabbitMqDestination rabbitMq: ValidateAlias(rabbitMq.Binding, path + ".binding", diagnostics); break;
            default:
                diagnostics.Add(destination is null ? "destination.required" : "destination.unsupported", path, "A supported output destination is required.");
                break;
        }
    }

    /// <summary>
    /// A SQL destination's table and columns are identifiers the dialect quotes, and its key must never be null, because a
    /// null key never conflicts (spec 1197). Only the main route may be SQL: the error route's shape is the runtime's.
    /// </summary>
    private static void ValidateSqlDestination(EtlPipelineV2 pipeline, IReadOnlyDictionary<string, EtlColumn> mapped,
        DiagnosticCollector diagnostics)
    {
        if (pipeline.Routes?.Errors is EtlSqlDestination)
            diagnostics.Add("destination.sql-route-unsupported", "routes.errors", "Only the main route can deliver to SQL.");
        if (pipeline.Routes?.Main is not EtlSqlDestination sql)
            return;
        if (!IsSqlTable(sql.Table))
            diagnostics.Add("destination.sql-identifier-invalid", "routes.main.table",
                "The table must be name or schema.name, each a letter or underscore followed by up to 62 letters, digits or underscores.");
        var names = pipeline.Mapping?.Where(field => field is not null).Select(field => field.Name).ToArray() ?? [];
        if (!names.All(IsSqlIdentifier) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            diagnostics.Add("destination.sql-identifier-invalid", "mapping",
                "SQL columns are the output field names, which must be identifiers that stay distinct when case is ignored.");
        if (sql.Key is not { Count: >= 1 and <= 8 } key || key.Distinct(StringComparer.Ordinal).Count() != key.Count ||
            !key.All(field => field is not null && mapped.TryGetValue(field, out var column) && column.Required &&
                pipeline.Mapping!.Any(output => output?.Name == field && output.Origin == EtlFieldOrigin.Source)))
            diagnostics.Add("destination.sql-key-invalid", "routes.main.key",
                "The key must be one to eight distinct output fields, each mapped from a required source field.");
    }

    /// <summary>
    /// A RabbitMQ destination's exchange is empty (the default exchange) or a broker-safe name, and its routing key is printable
    /// ASCII; both fit the protocol's short strings. Only the main route may publish (spec 1200).
    /// </summary>
    private static void ValidateRabbitMqDestination(EtlPipelineV2 pipeline, DiagnosticCollector diagnostics)
    {
        if (pipeline.Routes?.Errors is EtlRabbitMqDestination)
            diagnostics.Add("destination.rabbitmq-route-unsupported", "routes.errors", "Only the main route can publish to RabbitMQ.");
        if (pipeline.Routes?.Main is not EtlRabbitMqDestination rabbitMq)
            return;
        if (!IsRabbitMqExchange(rabbitMq.Exchange))
            diagnostics.Add("destination.rabbitmq-name-invalid", "routes.main.exchange",
                "The exchange must be empty or up to 255 letters, digits, underscores, hyphens, periods or colons.");
        if (!IsRabbitMqRoutingKey(rabbitMq.RoutingKey))
            diagnostics.Add("destination.rabbitmq-name-invalid", "routes.main.routingKey",
                "The routing key must be up to 255 printable ASCII characters without spaces.");
    }

    private static bool IsRabbitMqQueue(string? queue) => queue is { Length: >= 1 } && IsRabbitMqExchange(queue);

    private static bool IsRabbitMqExchange(string? exchange) => exchange is { Length: <= 255 } &&
        exchange.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':');

    private static bool IsRabbitMqRoutingKey(string? routingKey) => routingKey is { Length: <= 255 } &&
        routingKey.All(character => character is >= '!' and <= '~');

    /// <summary>The table and columns are identifiers the dialect quotes, and the key orders the snapshot (spec 1198).</summary>
    private static void ValidateSqlSource(EtlSqlSource sql, IReadOnlyDictionary<string, EtlColumn> columns,
        DiagnosticCollector diagnostics)
    {
        if (!IsSqlTable(sql.Table))
            diagnostics.Add("source.sql-identifier-invalid", "source.table",
                "The table must be name or schema.name, each a letter or underscore followed by up to 62 letters, digits or underscores.");
        var names = sql.Columns?.Where(column => column is not null).Select(column => column.Name).ToArray() ?? [];
        if (!names.All(IsSqlIdentifier) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            diagnostics.Add("source.sql-identifier-invalid", "source.columns",
                "SQL columns must be identifiers that stay distinct when case is ignored.");
        if (sql.Key is not { Count: >= 1 and <= 8 } key || key.Distinct(StringComparer.Ordinal).Count() != key.Count ||
            !key.All(name => name is not null && columns.TryGetValue(name, out var column) && column.Required))
            diagnostics.Add("source.sql-key-invalid", "source.key", "The key must be one to eight distinct required columns.");
        // The checkpoint compares 64-bit integers; a NULL watermark can never be compared, so the column is required (spec 1199).
        if (sql.Watermark is { } watermark &&
            !(columns.TryGetValue(watermark, out var column) && column.Type == EtlColumnType.Int64 && column.Required))
            diagnostics.Add("source.sql-watermark-invalid", "source.watermark", "The watermark must be a declared, required Int64 column.");
    }

    private static bool IsSqlTable(string? table) => table?.Split('.') is { Length: 1 or 2 } parts && parts.All(IsSqlIdentifier);

    private static bool IsSqlIdentifier(string? value) =>
        value is { Length: >= 1 and <= 63 } && (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static void ValidatePagination(EtlHttpPagination? pagination, DiagnosticCollector diagnostics)
    {
        switch (pagination)
        {
            case EtlSinglePagePagination: break;
            case EtlNextLinkPagination link:
                ValidatePointer(link.ContinuationPointer, "source.pagination.continuationPointer", diagnostics);
                break;
            case EtlTokenPagination token:
                ValidatePointer(token.ContinuationPointer, "source.pagination.continuationPointer", diagnostics);
                if (token.QueryParameter is not { Length: > 0 and <= 128 } ||
                    token.QueryParameter.Any(char.IsControl) || Encoding.UTF8.GetByteCount(token.QueryParameter) > 128)
                    diagnostics.Add("pagination.parameter.invalid", "source.pagination.queryParameter", "The query parameter must be nonempty, contain no control characters and fit within 128 UTF-8 bytes.");
                break;
            default:
                diagnostics.Add(pagination is null ? "pagination.required" : "pagination.unsupported", "source.pagination", "A supported pagination declaration is required.");
                break;
        }
    }

    private static void ValidatePointer(string? pointer, string path, DiagnosticCollector diagnostics)
    {
        var valid = pointer is not null && (pointer.Length == 0 || pointer[0] == '/');
        if (valid)
            for (var index = 0; index < pointer!.Length; index++)
                if (pointer[index] == '~' && (++index >= pointer.Length || pointer[index] is not ('0' or '1')))
                {
                    valid = false;
                    break;
                }
        if (!valid)
            diagnostics.Add("json.pointer.invalid", path, "A JSON Pointer must be empty or start with '/', using only '~0' and '~1' escapes.");
    }

    private static bool CheckConnectorStructureAndStrings(EtlPipelineV2 pipeline, string definitionId, int budget)
    {
        var sourceCount = pipeline.Source switch { EtlCsvSource csv => csv.Schema?.Count ?? 0, EtlHttpJsonSource http => http.Fields?.Count ?? 0, EtlJsonlSource jsonl => jsonl.Fields?.Count ?? 0, EtlSqlSource sql => sql.Columns?.Count ?? 0, EtlRabbitMqSource rabbitMq => rabbitMq.Fields?.Count ?? 0, _ => 0 };
        var itemCount = (long)sourceCount + (pipeline.Lookup?.Schema?.Count ?? 0) + (pipeline.Mapping?.Count ?? 0) + (pipeline.Filters?.Count ?? 0);
        if (itemCount > budget / 32 || !AddText(definitionId, ref budget))
            return false;
        switch (pipeline.Source)
        {
            case EtlCsvSource csv:
                if (!AddText(csv.Binding, ref budget) || !AddSchemaText(csv.Schema, ref budget)) return false;
                break;
            case EtlHttpJsonSource http:
                if (!AddText(http.Binding, ref budget) || !AddText(http.RecordsPointer, ref budget)) return false;
                if (http.Fields is not null)
                    foreach (var field in http.Fields)
                        if (field is not null && (!AddText(field.Name, ref budget) || !AddText(field.Pointer, ref budget))) return false;
                if (http.Pagination is EtlTokenPagination token &&
                    (!AddText(token.ContinuationPointer, ref budget) || !AddText(token.QueryParameter, ref budget))) return false;
                if (http.Pagination is EtlNextLinkPagination link && !AddText(link.ContinuationPointer, ref budget)) return false;
                break;
            case EtlJsonlSource jsonl:
                if (!AddText(jsonl.Binding, ref budget)) return false;
                if (jsonl.Fields is not null)
                    foreach (var field in jsonl.Fields)
                        if (field is not null && (!AddText(field.Name, ref budget) || !AddText(field.Pointer, ref budget))) return false;
                break;
            case EtlSqlSource sql:
                if (!AddText(sql.Binding, ref budget) || !AddText(sql.Table, ref budget) || !AddSchemaText(sql.Columns, ref budget) ||
                    !AddKeyText(sql.Key, ref budget) || sql.Watermark is { } watermark && !AddText(watermark, ref budget)) return false;
                break;
            case EtlRabbitMqSource rabbitMq:
                if (!AddText(rabbitMq.Binding, ref budget) || !AddText(rabbitMq.Queue, ref budget)) return false;
                if (rabbitMq.Fields is not null)
                    foreach (var field in rabbitMq.Fields)
                        if (field is not null && (!AddText(field.Name, ref budget) || !AddText(field.Pointer, ref budget))) return false;
                break;
        }
        if (pipeline.Lookup is { } lookup && (!AddText(lookup.Binding, ref budget) || !AddSchemaText(lookup.Schema, ref budget) ||
            !AddText(lookup.SourceKeyField, ref budget) || !AddText(lookup.LookupKeyField, ref budget))) return false;
        if (!AddDestinationText(pipeline.Routes?.Main, ref budget) || !AddDestinationText(pipeline.Routes?.Errors, ref budget) ||
            !AddMappingText(pipeline.Mapping, ref budget)) return false;
        if (pipeline.Filters is not null)
            foreach (var filter in pipeline.Filters)
                if (filter is not null && filter.Value.ValueKind != JsonValueKind.Undefined && JsonMarshal.GetRawUtf8Value(filter.Value).Length > budget)
                    return false;
        return AddFilterText(pipeline.Filters, ref budget);
    }

    private static bool AddDestinationText(EtlDestination? destination, ref int budget) => destination switch
    {
        EtlJsonlDestination file => AddText(file.Binding, ref budget),
        EtlHttpDestination http => AddText(http.Binding, ref budget),
        EtlCsvDestination csv => AddText(csv.Binding, ref budget),
        EtlSqlDestination sql => AddText(sql.Binding, ref budget) && AddText(sql.Table, ref budget) && AddKeyText(sql.Key, ref budget),
        EtlRabbitMqDestination rabbitMq => AddText(rabbitMq.Binding, ref budget) && AddText(rabbitMq.Exchange, ref budget) &&
            AddText(rabbitMq.RoutingKey, ref budget),
        _ => true
    };

    private static bool AddKeyText(IReadOnlyList<string>? key, ref int budget)
    {
        if (key is null) return true;
        foreach (var field in key)
            if (!AddText(field, ref budget)) return false;
        return true;
    }
}
