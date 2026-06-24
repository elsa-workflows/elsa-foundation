using Elsa.Primitives.Contracts;

namespace Elsa.Persistence.Groundwork.Services;

public sealed class GroundworkIdentityGenerator : IIdentityGenerator
{
    public string Generate() => Guid.NewGuid().ToString("N");
}
