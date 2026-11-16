namespace Elsa.Connections.Core.Models;

/// <summary>Metadata storage characteristics in the current repository scope; credentials retain their own store contract.</summary>
/// <param name="Provider">The configured backend identifier, without its connection string.</param>
/// <param name="IsDurable">Whether the selected backend retains connection metadata across a host restart.</param>
public sealed record ConnectionRepositoryCapabilities(string Provider, bool IsDurable);
