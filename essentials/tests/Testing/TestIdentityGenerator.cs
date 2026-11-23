using Elsa.Primitives.Contracts;

namespace Elsa.Testing;

/// <summary>
/// An <see cref="IIdentityGenerator"/> that needs no clock: every id is a new random GUID as 32 lowercase hex characters.
/// </summary>
public sealed class TestIdentityGenerator : IIdentityGenerator
{
    public string Generate() => Guid.NewGuid().ToString("N");
}
