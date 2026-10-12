namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>
/// A catalog row as seen from publishing, including stable provider and Runtime consumer identities.
/// </summary>
public sealed record ConstructableActivityView(
    string ActivityVersionId,
    string DefinitionId,
    string Version,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ConsumerKey,
    string ConsumerSchemaVersion
);
