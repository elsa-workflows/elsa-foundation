using System.Security.Cryptography;
using System.Text.Json;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;
using Microsoft.Extensions.Options;

namespace Elsa3.Activities.Design.Import.Services;

public sealed class ReusableActivityImportOptions
{
    public long MaximumUploadBytes { get; set; } = 16 * 1024 * 1024;
    public int MaximumSourceVersions { get; set; } = 20_000;
    public int DefaultPageSize { get; set; } = 100;
    public int MaximumPageSize { get; set; } = 500;
    public TimeSpan CollectionLifetime { get; set; } = TimeSpan.FromHours(24);
}

public sealed class ReusableActivityImportOperationService(
    IReusableActivityImportOperationStore store,
    IReusableActivityCollectionImporter importer,
    IOptions<ReusableActivityImportOptions> options,
    TimeProvider timeProvider) : IReusableActivityImportOperationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ReusableActivityImportOptions _options = ValidateOptions(options.Value);

    /// <inheritdoc />
    public async ValueTask<ReusableActivityImportUploadResult> UploadAsync(
        Stream json,
        long? contentLength,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        ValidateAccessScope(accessScope);
        if (!json.CanRead)
            throw new ArgumentException("The Elsa 3 collection stream must be readable.", nameof(json));
        if (contentLength is < 0)
            throw new ReusableActivityImportPayloadException("The Elsa 3 collection content length cannot be negative.");
        if (contentLength > _options.MaximumUploadBytes)
            throw new ReusableActivityImportPayloadException($"The Elsa 3 collection exceeds the {_options.MaximumUploadBytes}-byte upload limit.");

        await using var bounded = new MemoryStream(contentLength is > 0 and <= int.MaxValue ? (int)contentLength.Value : 0);
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var read = await json.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                if (bounded.Length + read > _options.MaximumUploadBytes)
                    throw new ReusableActivityImportPayloadException($"The Elsa 3 collection exceeds the {_options.MaximumUploadBytes}-byte upload limit.");
                await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReusableActivityImportPayloadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPayloadException("The Elsa 3 collection stream could not be read.", exception);
        }

        Elsa3WorkflowDefinition[] definitions;
        try
        {
            bounded.Position = 0;
            definitions = await JsonSerializer.DeserializeAsync<Elsa3WorkflowDefinition[]>(bounded, Json, cancellationToken)
                          ?? throw new ReusableActivityImportPayloadException("The Elsa 3 collection payload must be a JSON array.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReusableActivityImportPayloadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ReusableActivityImportPayloadException("The Elsa 3 collection payload is not valid authored-definition JSON.", exception);
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPayloadException("The Elsa 3 collection payload contains an unsupported authored-definition JSON shape.", exception);
        }

        if (definitions.Length == 0)
            throw new ReusableActivityImportPayloadException("The Elsa 3 collection must contain at least one source version.");
        if (definitions.Length > _options.MaximumSourceVersions)
            throw new ReusableActivityImportPayloadException($"The Elsa 3 collection exceeds the {_options.MaximumSourceVersions}-source-version limit.");
        if (definitions.Any(x => x is null))
            throw new ReusableActivityImportPayloadException("The Elsa 3 collection cannot contain null source versions.");

        var now = timeProvider.GetUtcNow();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = ReusableActivityImportIdentity.Create(
                "collection",
                accessScope.TenantScope,
                accessScope.UserId,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
            var persisted = new ReusableActivityImportCollectionHandle(
                handle,
                accessScope,
                now,
                now.Add(_options.CollectionLifetime),
                bounded.Length,
                new(handle, definitions));
            if (await store.TryCreateCollectionAsync(persisted, cancellationToken))
                return new(handle, persisted.CreatedAt, persisted.ExpiresAt, definitions.Length, persisted.ContentLength);
        }

        throw new ReusableActivityImportPersistenceException(
            "create collection",
            accessScope.TenantScope,
            new InvalidOperationException("Unable to allocate a unique Elsa 3 collection handle."));
    }

    /// <inheritdoc />
    public async ValueTask<ReusableActivityImportAnalysisPage> AnalyzeAsync(
        string collectionHandle,
        int offset,
        int limit,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, limit);
        var collection = await LoadCollectionAsync(collectionHandle, accessScope, cancellationToken);
        var plan = await importer.AnalyzeAsync(collection.Collection, cancellationToken);
        var page = plan.Items.Skip(offset).Take(limit).ToArray();
        var processed = Math.Min(plan.Items.Count, offset + page.Length);
        var diagnosticPage = plan.Diagnostics.Skip(offset).Take(limit).ToArray();
        var processedDiagnostics = Math.Min(plan.Diagnostics.Count, offset + diagnosticPage.Length);
        var totalRows = Math.Max(plan.Items.Count, plan.Diagnostics.Count);
        var nextOffset = offset + limit < totalRows ? offset + limit : (int?)null;
        return new(
            collectionHandle,
            plan.PlanId,
            offset,
            limit,
            processed,
            plan.Items.Count,
            processedDiagnostics,
            plan.Diagnostics.Count,
            nextOffset is null,
            nextOffset,
            page,
            diagnosticPage);
    }

    /// <inheritdoc />
    public async ValueTask<ReusableActivityImportSelectionReadiness> ExpandSelectionAsync(
        string collectionHandle,
        string planId,
        IReadOnlyCollection<string> selectedSourceVersionIds,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedSourceVersionIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        var collection = await LoadCollectionAsync(collectionHandle, accessScope, cancellationToken);
        var plan = await importer.AnalyzeAsync(collection.Collection, cancellationToken);
        EnsurePlan(planId, plan);

        var requested = selectedSourceVersionIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var byId = plan.Items.ToDictionary(x => x.SourceVersionId, StringComparer.Ordinal);
        var diagnostics = new List<Elsa3MigrationDiagnostic>();
        foreach (var unknown in requested.Where(x => !byId.ContainsKey(x)))
            diagnostics.Add(SelectionDiagnostic(
                ReusableActivityImportDiagnosticCodes.SelectionInvalid,
                $"Source version '{unknown}' is not part of the reviewed collection.",
                unknown));

        var closure = requested.Where(byId.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var queue = new Queue<string>(closure.Order(StringComparer.Ordinal));
        while (queue.TryDequeue(out var sourceVersionId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var dependency in byId[sourceVersionId].Dependencies.OrderBy(x => x.TargetSourceVersionId, StringComparer.Ordinal))
            {
                if (closure.Add(dependency.TargetSourceVersionId))
                    queue.Enqueue(dependency.TargetSourceVersionId);
            }
        }

        var expanded = closure.Order(StringComparer.Ordinal).ToArray();
        diagnostics.AddRange(expanded.SelectMany(x => byId[x].Diagnostics));
        var orderedDiagnostics = diagnostics
            .DistinctBy(x => DiagnosticIdentity(x), StringComparer.Ordinal)
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Metadata.GetValueOrDefault("SourceVersionId"), StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();
        return new(
            collectionHandle,
            plan.PlanId,
            requested,
            expanded,
            expanded.Except(requested, StringComparer.Ordinal).ToArray(),
            orderedDiagnostics.All(x => !x.IsError) && requested.Length > 0,
            orderedDiagnostics);
    }

    /// <inheritdoc />
    public async ValueTask<ReusableActivityImportReceipt> ApplyAsync(
        string collectionHandle,
        string planId,
        IReadOnlyCollection<string> selectedSourceVersionIds,
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ValidateAccessScope(accessScope);
        ValidateIdempotencyKey(idempotencyKey);
        ArgumentNullException.ThrowIfNull(selectedSourceVersionIds);
        var selected = selectedSourceVersionIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var selectionFingerprint = SelectionFingerprint(collectionHandle, planId, selected, accessScope);
        var prior = await store.FindReceiptAsync(idempotencyKey, accessScope, cancellationToken);
        if (prior is not null)
        {
            if (!StringComparer.Ordinal.Equals(prior.CollectionHandle, collectionHandle) ||
                !StringComparer.Ordinal.Equals(prior.PlanId, planId) ||
                !StringComparer.Ordinal.Equals(prior.SelectionFingerprint, selectionFingerprint))
                throw new ReusableActivityImportIdempotencyConflictException(idempotencyKey);
            return prior with { Status = ReusableActivityImportReceiptStatus.AlreadyImported };
        }

        var collection = await LoadCollectionAsync(collectionHandle, accessScope, cancellationToken);
        var result = await importer.ApplyAsync(
            new(planId, collection.Collection, selected, accessScope, idempotencyKey),
            cancellationToken);
        return result.Receipt
               ?? throw new ReusableActivityImportPersistenceException(
                   "apply",
                   idempotencyKey,
                   new InvalidOperationException("The atomic import adapter did not return a durable receipt."));
    }

    /// <inheritdoc />
    public async ValueTask<ReusableActivityImportReceipt> GetStatusAsync(
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default)
    {
        ValidateAccessScope(accessScope);
        ValidateIdempotencyKey(idempotencyKey);
        return await store.FindReceiptAsync(idempotencyKey, accessScope, cancellationToken)
               ?? throw new ReusableActivityImportNotFoundException("The Elsa 3 import receipt was not found.");
    }

    private async ValueTask<ReusableActivityImportCollectionHandle> LoadCollectionAsync(
        string handle,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ValidateAccessScope(accessScope);
        var collection = await store.FindCollectionAsync(handle, accessScope, cancellationToken)
                         ?? throw new ReusableActivityImportNotFoundException("The Elsa 3 import collection was not found.");
        if (collection.ExpiresAt <= timeProvider.GetUtcNow())
            throw new ReusableActivityImportExpiredException(handle);
        return collection;
    }

    private void ValidatePage(int offset, int limit)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0 || limit > _options.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit), $"Page size must be between 1 and {_options.MaximumPageSize}.");
    }

    private static void EnsurePlan(string planId, ReusableActivityImportPlan plan)
    {
        if (!StringComparer.Ordinal.Equals(planId, plan.PlanId))
            throw new ReusableActivityImportValidationException(
                "The Elsa 3 collection plan is stale and must be reviewed again.",
                [SelectionDiagnostic(ReusableActivityImportDiagnosticCodes.PlanChanged, "The reviewed Plan ID no longer matches the collection.", plan.CollectionId)]);
    }

    private static Elsa3MigrationDiagnostic SelectionDiagnostic(string code, string message, string sourceVersionId) =>
        new(
            Elsa3MigrationDiagnosticSeverity.Error,
            code,
            message,
            guidance: "Review the current collection analysis and dependency closure before applying.",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["SourceVersionId"] = sourceVersionId });

    private static string DiagnosticIdentity(Elsa3MigrationDiagnostic diagnostic) =>
        string.Join('\u001f', diagnostic.Code, diagnostic.Path, diagnostic.Metadata.GetValueOrDefault("SourceVersionId"), diagnostic.Metadata.GetValueOrDefault("cyclePath"));

    public static string SelectionFingerprint(
        string collectionHandle,
        string planId,
        IReadOnlyCollection<string> selectedSourceVersionIds,
        ReusableActivityImportAccessScope accessScope) =>
        ReusableActivityImportIdentity.Create(
            "selection",
            collectionHandle,
            planId,
            accessScope.TenantScope,
            accessScope.UserId,
            string.Join('\u001f', selectedSourceVersionIds.Order(StringComparer.Ordinal)));

    private static void ValidateAccessScope(ReusableActivityImportAccessScope accessScope)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessScope.UserId);
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(idempotencyKey), "Idempotency keys cannot exceed 200 characters.");
    }

    private static ReusableActivityImportOptions ValidateOptions(ReusableActivityImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaximumUploadBytes <= 0 ||
            options.MaximumSourceVersions <= 0 ||
            options.DefaultPageSize <= 0 ||
            options.MaximumPageSize < options.DefaultPageSize ||
            options.CollectionLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Elsa 3 import bounds must all be positive and the maximum page size must cover the default.");
        return options;
    }
}
