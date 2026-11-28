namespace Elsa.Workflows.Runtime.Core.Models;

internal sealed record RuntimePreparedStartIdentity
{
    public RuntimePreparedStartIdentity(string tenant, string actorReference, string issuance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuance);
        if (tenant.Length > 64 || actorReference is not { Length: 64 } || !actorReference.All(char.IsAsciiHexDigitUpper) ||
            issuance.Length > 2_048)
            throw new ArgumentException("A prepared start requires a bounded tenant, actor reference and issuance.");
        Tenant = tenant;
        ActorReference = actorReference;
        Issuance = issuance;
    }

    public string Tenant { get; }
    public string ActorReference { get; }
    public string Issuance { get; }
}

internal sealed record RuntimePreparedStartAttempt
{
    public RuntimePreparedStartAttempt(long generation, RuntimePreparedStartIdentity identity, bool disarmed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        ArgumentNullException.ThrowIfNull(identity);
        Generation = generation;
        Identity = identity;
        Disarmed = disarmed;
    }

    public long Generation { get; }
    public RuntimePreparedStartIdentity Identity { get; }
    public bool Disarmed { get; }

    internal bool Follows(RuntimePreparedStartAttempt? previous)
    {
        if (previous is null) return Generation == 1 && !Disarmed;
        if (!previous.Disarmed) return Disarmed && Generation == previous.Generation && Identity == previous.Identity;
        return !Disarmed && Generation > previous.Generation && Generation - previous.Generation == 1 &&
            Identity.ActorReference != previous.Identity.ActorReference && Identity.Issuance != previous.Identity.Issuance;
    }
}
