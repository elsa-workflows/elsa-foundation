using System.Text.Json;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;

namespace Elsa.Studio.Preferences.Core.Services;

public sealed class StudioPreferenceService(
    IStudioPreferenceNamespaceRegistry namespaces,
    IStudioPreferenceStore store,
    TimeProvider timeProvider) : IStudioPreferenceService
{
    public const int SystemMaxBytes = 256 * 1024;

    public async ValueTask<StudioPreferenceDocument?> FindAsync(StudioPreferenceKey key, CancellationToken cancellationToken = default)
    {
        ValidateScope(key);
        var definition = RequireNamespace(key.Namespace);
        return await store.FindAsync(key with { Namespace = definition.Name }, cancellationToken);
    }

    public async ValueTask<StudioPreferenceDocument> WriteAsync(
        StudioPreferenceKey key,
        StudioPreferenceWrite write,
        StudioPreferenceWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(key);
        ArgumentNullException.ThrowIfNull(condition);
        var definition = RequireNamespace(key.Namespace);

        if (write.SchemaVersion <= 0 || write.SchemaVersion > definition.CurrentSchemaVersion)
            throw new StudioPreferenceValidationException(
                $"Schema version {write.SchemaVersion} is not supported by namespace '{definition.Name}'.");

        var value = write.SchemaVersion == definition.CurrentSchemaVersion
            ? write.Value.Clone()
            : definition.Migrate(write.Value, write.SchemaVersion)
              ?? throw new StudioPreferenceValidationException(
                  $"Namespace '{definition.Name}' cannot migrate schema version {write.SchemaVersion}.");

        if (!definition.Validate(value))
            throw new StudioPreferenceValidationException($"Namespace '{definition.Name}' rejected the preference value.");

        var actualBytes = JsonSerializer.SerializeToUtf8Bytes(value).Length;
        var maximumBytes = Math.Min(definition.MaxBytes, SystemMaxBytes);
        if (actualBytes > maximumBytes)
            throw new StudioPreferenceQuotaExceededException(definition.Name, actualBytes, maximumBytes);

        var result = await store.WriteAsync(
            key with { Namespace = definition.Name },
            new StudioPreferenceWrite(definition.CurrentSchemaVersion, value),
            condition,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return result.Status switch
        {
            StudioPreferenceStoreWriteStatus.Saved when result.Document is not null => result.Document,
            StudioPreferenceStoreWriteStatus.NotFound => throw new StudioPreferenceConflictException("The preference document no longer exists."),
            _ => throw new StudioPreferenceConflictException("The preference document revision is stale.")
        };
    }

    private IStudioPreferenceNamespace RequireNamespace(string name) =>
        namespaces.Find(name) ?? throw new StudioPreferenceNamespaceNotFoundException(name);

    private static void ValidateScope(StudioPreferenceKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key.SubjectId))
            throw new StudioPreferenceScopeException("An authenticated subject is required.");
        if (string.IsNullOrWhiteSpace(key.TenantId))
            throw new StudioPreferenceScopeException("An authenticated tenant is required.");
        if (string.IsNullOrWhiteSpace(key.StudioHostId) || key.StudioHostId.Length > 128)
            throw new StudioPreferenceScopeException("A Studio host ID of at most 128 characters is required.");
        if (string.IsNullOrWhiteSpace(key.Namespace))
            throw new StudioPreferenceScopeException("A preference namespace is required.");
    }
}
