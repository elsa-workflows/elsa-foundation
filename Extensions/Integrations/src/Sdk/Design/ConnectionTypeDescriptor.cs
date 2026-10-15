namespace Elsa.Integrations.Sdk.Design;

/// <summary>
/// Design-time descriptor of a service-connection kind: settings schema, supported schemes, and
/// egress allowlist. Runtime execution never loads this type to execute (#2726 rule 8). Persistence
/// of connection instances is <c>Elsa.Connections</c> (plan D6), not this SDK.
/// </summary>
public sealed class ConnectionTypeDescriptor
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public IReadOnlyList<AuthSchemeDescriptor> Schemes { get; init; } = [];

    public IReadOnlyList<ConnectionFieldDescriptor> Settings { get; init; } = [];

    public IReadOnlyList<string> EgressAllowlist { get; init; } = [];
}
