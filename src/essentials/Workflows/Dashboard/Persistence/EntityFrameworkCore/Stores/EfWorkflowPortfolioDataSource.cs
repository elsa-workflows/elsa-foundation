using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Reads the Dashboard workflow portfolio from the EF-owned Design and Runtime projections.
/// </summary>
/// <remarks>
/// Dashboard does not own a persistence context or duplicate either module's entities. Design definitions
/// and drafts are read through <see cref="WorkflowsDesignDbContext"/>; published executable source references
/// are read through the shared Runtime artifact context. The two lanes are intentionally correlated in memory,
/// because they can be separate physical stores.
/// </remarks>
public sealed class EfWorkflowPortfolioDataSource(
    WorkflowsDesignDbContext designContext,
    RuntimeDbContext runtimeContext,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPayloadSerializer payloadSerializer) : IWorkflowPortfolioDataSource
{
    /// <summary>Maximum number of projection rows correlated by one portfolio query.</summary>
    public const int MaximumSourceRows = 100_000;

    private const string PublishedScope = nameof(WorkflowExecutableReferenceScope.Published);
    private static readonly JsonSerializerOptions RuntimeJsonOptions = CreateRuntimeJsonOptions();

    public bool IsAvailable => true;

    public async ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var scope = RequireScope(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var activeDefinitions = await ReadActiveDefinitionsAsync(scope, cancellationToken);
        if (activeDefinitions.Count == 0)
            return new WorkflowPortfolioBaseCounts(0, 0, 0);

        var publishedDefinitionIds = await ReadPublishedDefinitionIdsAsync(scope, asOf, cancellationToken);
        var currentDraftDefinitionIds = await ReadCurrentDraftDefinitionIdsAsync(
            scope,
            activeDefinitions,
            cancellationToken);

        return new WorkflowPortfolioBaseCounts(
            activeDefinitions.Count,
            publishedDefinitionIds.Count(activeDefinitions.Contains),
            currentDraftDefinitionIds.Count);
    }

    public async IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scope = RequireScope(tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        var activeDefinitions = await ReadActiveDefinitionsAsync(scope, cancellationToken);
        if (activeDefinitions.Count == 0)
            yield break;

        string? previousDefinitionId = null;
        foreach (var projected in await ReadCurrentDraftRowsAsync(scope, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = projected.Entity;
            ValidateDraftProjection(row, projected.ScopeKey, scope);
            if (StringComparer.Ordinal.Equals(previousDefinitionId, row.WorkflowDefinitionId))
                continue;

            previousDefinitionId = row.WorkflowDefinitionId;
            if (!activeDefinitions.Contains(row.WorkflowDefinitionId))
                continue;

            yield return ReadDraftState(row);
        }
    }

    private async Task<IReadOnlySet<string>> ReadActiveDefinitionsAsync(
        string scope,
        CancellationToken cancellationToken)
    {
        var rows = await designContext.Definitions
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "ScopeKey") == ScopeKey(scope) && row.TenantId == scope && row.DeletedAt == null)
            .OrderBy(row => row.Id)
            .ThenBy(row => row.IdLookupHash)
            .Take(MaximumSourceRows + 1)
            .Select(row => new DefinitionProjection(
                row,
                EF.Property<string>(row, "ScopeKey")))
            .ToArrayAsync(cancellationToken);
        if (rows.Length > MaximumSourceRows)
            throw new InvalidDataException(
                $"The workflow portfolio read more than {MaximumSourceRows} active workflow definitions.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var projected in rows)
        {
            var row = projected.Entity;
            ValidateDefinitionProjection(row, projected.ScopeKey, scope);
            ids.Add(row.Id);
        }

        return ids;
    }

    private async Task<IReadOnlySet<string>> ReadPublishedDefinitionIdsAsync(
        string scope,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var rows = await runtimeContext.WorkflowExecutableSourceReferences
            .AsNoTracking()
            .Where(row => row.ScopeKeyHash == EfRelationalIdentity.Hash(scope) &&
                          row.ScopeKey == EfRelationalIdentity.Encode(scope) &&
                          row.Scope == PublishedScope &&
                          !row.IsRetired &&
                          (row.ExpiresAtUtcTicks == null || row.ExpiresAtUtcTicks > asOf.UtcTicks))
            .OrderBy(row => row.SourceReferenceIdOrderKey)
            .ThenBy(row => row.Id)
            .Take(MaximumSourceRows + 1)
            .ToArrayAsync(cancellationToken);
        if (rows.Length > MaximumSourceRows)
            throw new InvalidDataException(
                $"The workflow portfolio read more than {MaximumSourceRows} published source references.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var reference = ReadSourceReference(row, scope);
            if (reference.Scope == WorkflowExecutableReferenceScope.Published &&
                reference.DeletedAt is null &&
                (reference.ExpiresAt is null || reference.ExpiresAt.Value.UtcTicks > asOf.UtcTicks))
                ids.Add(reference.DefinitionId);
        }

        return ids;
    }

    private async Task<IReadOnlySet<string>> ReadCurrentDraftDefinitionIdsAsync(
        string scope,
        IReadOnlySet<string> activeDefinitions,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? previousDefinitionId = null;
        foreach (var projected in await ReadCurrentDraftRowsAsync(scope, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = projected.Entity;
            ValidateDraftProjection(row, projected.ScopeKey, scope);
            if (StringComparer.Ordinal.Equals(previousDefinitionId, row.WorkflowDefinitionId))
                continue;

            previousDefinitionId = row.WorkflowDefinitionId;
            if (activeDefinitions.Contains(row.WorkflowDefinitionId))
                ids.Add(row.WorkflowDefinitionId);
        }

        return ids;
    }

    private async Task<IReadOnlyList<DraftProjection>> ReadCurrentDraftRowsAsync(
        string scope,
        CancellationToken cancellationToken)
    {
        // SQLite cannot translate DateTimeOffset ordering, while the design contract requires full
        // identity ordering. The candidate set is explicitly bounded in identity order before the provider
        // read, then the small bounded projection is sorted using the domain's ordinal rules on the client.
        var rows = await CurrentDraftQuery(scope)
            .Take(MaximumSourceRows + 1)
            .ToArrayAsync(cancellationToken);
        if (rows.Length > MaximumSourceRows)
            throw new InvalidDataException(
                $"The workflow portfolio read more than {MaximumSourceRows} workflow-definition drafts.");
        return rows
            .OrderBy(row => row.Entity.WorkflowDefinitionId, StringComparer.Ordinal)
            .ThenByDescending(row => row.Entity.LastModifiedAt)
            .ThenByDescending(row => row.Entity.CreatedAt)
            .ThenByDescending(row => row.Entity.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private IQueryable<DraftProjection> CurrentDraftQuery(string scope) =>
        designContext.Drafts
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "ScopeKey") == ScopeKey(scope) && row.TenantId == scope)
            .OrderBy(row => row.Id)
            .ThenBy(row => row.IdLookupHash)
            .Select(row => new DraftProjection(row, EF.Property<string>(row, "ScopeKey")));

    private WorkflowDefinitionDraft ReadDraftState(WorkflowDefinitionDraft row)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(row.StateSource))
                throw new InvalidDataException("The workflow definition draft state source is missing.");
            row.State = payloadSerializer.Deserialize<WorkflowDefinitionState>(row.StateSource);
            if (row.State is null)
                throw new InvalidDataException("The workflow definition draft state is empty.");
            return row;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            throw new InvalidDataException("The workflow definition draft state is not valid current data.", exception);
        }
    }

    private static void ValidateDefinitionProjection(WorkflowDefinition row, string? physicalScope, string scope)
    {
        try
        {
            WorkflowDefinitionLimits.ValidateIdentity(row.Id, nameof(row.Id));
            if (!StringComparer.Ordinal.Equals(row.TenantId, scope) ||
                !StringComparer.Ordinal.Equals(physicalScope, ScopeKey(scope)) ||
                !StringComparer.Ordinal.Equals(row.IdSearchKey, WorkflowDefinitionIdentity.Fold(row.Id)) ||
                !StringComparer.Ordinal.Equals(row.IdLookupHash, LookupHash(row.IdSearchKey)))
                throw new InvalidDataException("The workflow definition projection identity envelope is corrupt.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("The workflow definition projection identity envelope is corrupt.", exception);
        }
    }

    private static void ValidateDraftProjection(WorkflowDefinitionDraft row, string? physicalScope, string scope)
    {
        try
        {
            WorkflowDefinitionLimits.ValidateIdentity(row.Id, nameof(row.Id));
            WorkflowDefinitionLimits.ValidateIdentity(row.WorkflowDefinitionId, nameof(row.WorkflowDefinitionId));
            if (!StringComparer.Ordinal.Equals(row.TenantId, scope) ||
                !StringComparer.Ordinal.Equals(physicalScope, ScopeKey(scope)) ||
                !StringComparer.Ordinal.Equals(row.IdLookupHash, LookupHash(row.Id)) ||
                !StringComparer.Ordinal.Equals(row.WorkflowDefinitionIdLookupHash, LookupHash(WorkflowDefinitionIdentity.Fold(row.WorkflowDefinitionId))))
                throw new InvalidDataException("The workflow definition draft projection identity envelope is corrupt.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("The workflow definition draft projection identity envelope is corrupt.", exception);
        }
    }

    private static WorkflowExecutableSourceReference ReadSourceReference(
        WorkflowExecutableSourceReferenceEntity row,
        string scope)
    {
        try
        {
            var sourceReferenceId = Decode(row.SourceReferenceId);
            var artifactId = Decode(row.ArtifactId);
            var definitionVersionId = Decode(row.DefinitionVersionId);
            var definitionId = Decode(row.DefinitionId);
            if (EfSchemaVersion.NotReadable("RuntimeArtifact", row.SchemaVersion, RuntimeArtifactEfModule.SchemaVersion) ||
                row.Revision <= 0 || string.IsNullOrWhiteSpace(row.IncarnationId) ||
                !StringComparer.Ordinal.Equals(row.Id, EfRelationalIdentity.HashLengthFramed(scope, sourceReferenceId)) ||
                !StringComparer.Ordinal.Equals(row.ScopeKey, Encode(scope)) ||
                !StringComparer.Ordinal.Equals(row.ScopeKeyHash, EfRelationalIdentity.Hash(scope)) ||
                !StringComparer.Ordinal.Equals(row.ScopeKeyOrderKey, ScopeOrderKey(scope)) ||
                !StringComparer.Ordinal.Equals(row.SourceReferenceIdHash, EfRelationalIdentity.Hash(sourceReferenceId)) ||
                !StringComparer.Ordinal.Equals(row.SourceReferenceIdOrderKey, IdentityOrderKey(sourceReferenceId)) ||
                !StringComparer.Ordinal.Equals(row.ArtifactIdHash, EfRelationalIdentity.Hash(artifactId)) ||
                !StringComparer.Ordinal.Equals(row.DefinitionVersionIdHash, EfRelationalIdentity.Hash(definitionVersionId)) ||
                !StringComparer.Ordinal.Equals(row.DefinitionIdHash, EfRelationalIdentity.Hash(definitionId)))
                throw new InvalidDataException("The workflow executable source-reference projection identity envelope is corrupt.");

            using var document = JsonDocument.Parse(row.ContentJson);
            if (!document.RootElement.TryGetProperty("collection", out var collection) ||
                !StringComparer.Ordinal.Equals(collection.GetString(), "workflowExecutableSourceReference") ||
                !document.RootElement.TryGetProperty("artifactId", out var envelopeArtifactId) ||
                !StringComparer.Ordinal.Equals(envelopeArtifactId.GetString(), row.ArtifactId) ||
                !document.RootElement.TryGetProperty("reference", out var referenceJson))
                throw new InvalidDataException("The workflow executable source-reference envelope is corrupt.");

            var value = JsonSerializer.Deserialize<WorkflowExecutableSourceReference>(
                            referenceJson.GetRawText(),
                            RuntimeJsonOptions)
                        ?? throw new InvalidDataException("The workflow executable source-reference content is empty.");
            ValidateSourceReference(value);
            if (!StringComparer.Ordinal.Equals(value.SourceReferenceId, sourceReferenceId) ||
                !StringComparer.Ordinal.Equals(value.ArtifactId, artifactId) ||
                !StringComparer.Ordinal.Equals(value.DefinitionVersionId, definitionVersionId) ||
                !StringComparer.Ordinal.Equals(value.DefinitionId, definitionId) ||
                !StringComparer.Ordinal.Equals(value.Scope.ToString(), row.Scope) ||
                row.IsRetired != (value.DeletedAt is not null) ||
                row.ExpiresAtUtcTicks != value.ExpiresAt?.UtcTicks ||
                (value.TenantId is not null && !StringComparer.Ordinal.Equals(value.TenantId, scope)))
                throw new InvalidDataException("The workflow executable source-reference projection does not match its content.");
            return value;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            throw new InvalidDataException("The workflow executable source-reference content is not valid current data.", exception);
        }
    }

    private static void ValidateSourceReference(WorkflowExecutableSourceReference value)
    {
        if (!Enum.IsDefined(value.Scope) ||
            string.IsNullOrWhiteSpace(value.SourceReferenceId) ||
            string.IsNullOrWhiteSpace(value.ArtifactId) ||
            string.IsNullOrWhiteSpace(value.SourceKind) ||
            string.IsNullOrWhiteSpace(value.SourceId) ||
            value.SourceVersion is not null && string.IsNullOrWhiteSpace(value.SourceVersion) ||
            string.IsNullOrWhiteSpace(value.DefinitionId) ||
            string.IsNullOrWhiteSpace(value.DefinitionVersionId) ||
            string.IsNullOrWhiteSpace(value.ArtifactVersion) ||
            value.SourceReferenceId.Length > RuntimeArtifactEfModule.IdentityMaximumLength ||
            value.ArtifactId.Length > RuntimeArtifactEfModule.IdentityMaximumLength ||
            value.DefinitionId.Length > RuntimeArtifactEfModule.IdentityMaximumLength ||
            value.DefinitionVersionId.Length > RuntimeArtifactEfModule.IdentityMaximumLength)
            throw new InvalidDataException("The workflow executable source-reference content contains invalid identity values.");
    }

    private string RequireScope(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var current = accessContextAccessor.Current;
        var scope = current.RequireScope().Value;
        current.EnsureTenantScope(tenantId);
        return scope;
    }

    private static string ScopeKey(string scope) =>
        "1" + EfRelationalIdentity.Hash(scope);

    private sealed record DefinitionProjection(WorkflowDefinition Entity, string ScopeKey);

    private sealed record DraftProjection(WorkflowDefinitionDraft Entity, string ScopeKey);

    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Decode(string value) => EfRelationalIdentity.Decode(value);
    private static string IdentityOrderKey(string value) => Convert.ToHexString(
        EfRelationalIdentity.CreateOrderKey(value, RuntimeArtifactEfModule.IdentityMaximumLength));
    private static string ScopeOrderKey(string value) =>
        EfRelationalIdentity.CreateOrdinalTextOrderKey(value + "\0");

    private static string LookupHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static JsonSerializerOptions CreateRuntimeJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new LosslessUtf16StringConverter());
        return options;
    }

    private sealed class LosslessUtf16StringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Decode(reader.GetString() ?? throw new JsonException("A runtime artifact string was null."));

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Decode(reader.GetString() ?? throw new JsonException("A runtime artifact string was null."));

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Encode(value));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(Encode(value));
    }
}
