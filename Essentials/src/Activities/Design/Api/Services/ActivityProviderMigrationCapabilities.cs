using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Matches a target provider schema's declarations against an exact source manifest identity.</summary>
internal static class ActivityProviderMigrationCapabilities
{
    /// <summary>
    /// Returns whether the target schema declares this exact source. Legacy schema-only declarations
    /// apply only when source and target provider keys are the same.
    /// </summary>
    internal static bool SupportsMigrationFrom(
        string targetProviderKey,
        ActivityProviderManifestSchemaCapabilities targetSchema,
        string? sourceProviderKey,
        string? sourceSchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(sourceProviderKey) || string.IsNullOrWhiteSpace(sourceSchemaVersion))
            return false;

        if (targetSchema.MigratableFrom?.Contains(new(sourceProviderKey, sourceSchemaVersion)) == true)
            return true;

        return StringComparer.Ordinal.Equals(targetProviderKey, sourceProviderKey) &&
               targetSchema.MigratableFromSchemaVersions.Contains(sourceSchemaVersion);
    }

    /// <summary>Returns whether an exact registered target schema is authorable and declares this source manifest.</summary>
    internal static bool SupportsAuthorableMigrationFrom(
        IActivityProvider targetProvider,
        string targetSchemaVersion,
        ActivityProviderManifest source)
    {
        var targetSchema = targetProvider.AuthoringCapabilities.ManifestSchemas
            .FirstOrDefault(schema => StringComparer.Ordinal.Equals(schema.SchemaVersion, targetSchemaVersion));
        return targetSchema is { IsAuthorable: true } && SupportsMigrationFrom(
            targetProvider.ProviderKey,
            targetSchema,
            source.ProviderKey,
            source.SchemaVersion);
    }
}
