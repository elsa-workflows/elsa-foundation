namespace Elsa.Integrations.Sdk.Design;

/// <summary>
/// Design-time description of one authentication scheme (plan D3). Runtime execution never loads
/// this type (#2726 rule 8).
/// </summary>
public sealed class AuthSchemeDescriptor
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public IReadOnlyList<ConnectionFieldDescriptor> Fields { get; init; } = [];

    public bool HasInteractiveAuthorization { get; init; }

    public bool HasRefreshableState { get; init; }

    public bool HasRevocation { get; init; }
}
