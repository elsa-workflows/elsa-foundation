using Elsa.Primitives.Contracts;

namespace Elsa.Persistence.EFCore.Services;

public sealed class EFCoreIdentityGenerator(ISystemClock systemClock) : IIdentityGenerator
{
    public string Generate() => $"{Ulid.NewUlid(systemClock.UtcNow)}";
}
