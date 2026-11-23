namespace Elsa.Integrations.Connectors.Microsoft365.Models;

public sealed record GraphUser
{
    public string? Id { get; init; }

    public string? DisplayName { get; init; }

    public string? Mail { get; init; }

    public string? UserPrincipalName { get; init; }
}

public sealed record GraphUserLookupResult
{
    public bool Found { get; init; }

    public GraphUser? User { get; init; }
}
