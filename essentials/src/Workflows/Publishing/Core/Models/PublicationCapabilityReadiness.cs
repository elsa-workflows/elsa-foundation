namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>One selected runtime/storage capability used to bind publication readiness.</summary>
public sealed record PublicationCapabilityReadiness(string Kind, string Key, string? SchemaVersion, string Status, IReadOnlyList<string> SupportedSchemaVersions);
