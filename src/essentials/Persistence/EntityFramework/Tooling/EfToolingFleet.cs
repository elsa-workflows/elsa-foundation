using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// How <c>persistence status</c> reads the cluster's members without this package knowing what membership is. A
/// provider of cluster membership that keeps its members in an EF module declares one public, parameterless class
/// implementing this in the assembly that declares the module, and the tool finds it in the host's closure, beside the
/// module, exactly as it finds the module.
/// </summary>
/// <remarks>
/// The tool hands it a context of that module, already checked to have no pending migration, on the connection the
/// command carries. It never writes: the members are read as any reader of the fleet reads them, and judged live on this
/// process's clock with the provider's default settings, since the tool reads no host configuration.
/// </remarks>
public interface IEfToolingFleetSource
{
    /// <summary>The <c>[EfModule]</c> name of the module that holds the members.</summary>
    string ModuleName { get; }

    Task<EfToolingFleet> ReadAsync(DbContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// The fleet as one read shows it, and the answer to the question the finalization gate asks of it: which counted members
/// cannot read a version of a family in a database (spec 181, FR-022; MR-007). The answer comes from the provider, which
/// owns what "counted" and "can read" mean, so the tool repeats neither.
/// </summary>
public sealed class EfToolingFleet(
    DateTimeOffset judgedAt,
    TimeSpan skewAllowance,
    IReadOnlyList<EfToolingClusterMember> members,
    Func<string, string?, string, IReadOnlyList<EfToolingWaitingOn>> blockers)
{
    public DateTimeOffset JudgedAt { get; } = judgedAt;

    public TimeSpan SkewAllowance { get; } = skewAllowance;

    public IReadOnlyList<EfToolingClusterMember> Members { get; } = members;

    /// <summary>The counted members that cannot read <paramref name="version"/> of <paramref name="family"/> in the database with <paramref name="databaseIdentity"/>.</summary>
    public IReadOnlyList<EfToolingWaitingOn> Blockers(string family, string? databaseIdentity, string version) =>
        blockers(family, databaseIdentity, version);
}
