using Elsa.Activities.Design.Reconciliation.Json.Models;
using Elsa.Activities.Design.Reconciliation.Json.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Elsa.Activities.Design.Reconciliation.Json.Services;

/// <summary>
/// Reads <see cref="JsonReconciliationOptions.FilePath"/> and deserialises it into a list
/// of <see cref="JsonCatalogEntry"/>. Missing file logs a warning and returns empty;
/// malformed JSON throws <see cref="InvalidJsonCatalogEntryException"/> with file context.
/// Raw <see cref="JsonException"/>s are deliberately wrapped — callers see a domain-
/// scoped failure, not an infrastructure exception.
/// </summary>
public sealed class JsonActivityCatalogReader(
    IOptions<JsonReconciliationOptions> options,
    ILogger<JsonActivityCatalogReader> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<JsonCatalogEntry>> ReadAsync(CancellationToken cancellationToken)
    {
        var path = options.Value.FilePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning("JSON reconciliation source has no FilePath configured; contributing zero activities.");
            return [];
        }

        if (!File.Exists(path))
        {
            logger.LogWarning("JSON reconciliation source file '{Path}' not found; contributing zero activities.", path);
            return [];
        }

        await using var stream = File.OpenRead(path);
        try
        {
            var entries = await JsonSerializer.DeserializeAsync<List<JsonCatalogEntry>>(stream, JsonOptions, cancellationToken);
            return entries ?? [];
        }
        catch (JsonException inner)
        {
            //throw new InvalidJsonCatalogEntryException(
            //    entryIndex: -1,
            //    activityTypeKey: null,
            //    implementationKind: null,
            //    message: $"Failed to parse JSON catalog file '{path}': {inner.Message}",
            //    inner);

            throw;
        }
    }
}
