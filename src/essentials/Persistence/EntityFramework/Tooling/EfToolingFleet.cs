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
/// process's clock with the skew allowance the tool names, or the provider's own default when it names none, since the
/// tool reads no more of the host's configuration than that.
/// </remarks>
public interface IEfToolingFleetSource
{
    /// <summary>The <c>[EfModule]</c> name of the module that holds the members.</summary>
    string ModuleName { get; }

    /// <param name="context">A context of <see cref="ModuleName"/>.</param>
    /// <param name="skewAllowance">The skew allowance to judge liveness with, or <see langword="null"/> for the provider's default.</param>
    Task<EfToolingFleet> ReadAsync(DbContext context, TimeSpan? skewAllowance = null, CancellationToken cancellationToken = default);
}

/// <summary>A schema family and the database whose finalization record it is judged for, as <c>status</c> lists it.</summary>
public readonly record struct EfToolingFamilyDatabase(string Family, string? DatabaseIdentity);

/// <summary>
/// The fleet as one read shows it, and the answer to the question the finalization gate asks of it: which counted members
/// cannot read a version of a family in a database (spec 181, FR-022; MR-007). The answer comes from the provider, which
/// owns what "counted" and "can read" mean, so the tool repeats neither, and neither does whatever prints it.
/// </summary>
public sealed class EfToolingFleet(
    DateTimeOffset judgedAt,
    TimeSpan skewAllowance,
    Func<IReadOnlyList<EfToolingFamilyDatabase>, IReadOnlyList<EfToolingClusterMember>> members,
    Func<string, string?, string, IReadOnlyList<EfToolingWaitingOn>> blockers)
{
    public DateTimeOffset JudgedAt { get; } = judgedAt;

    public TimeSpan SkewAllowance { get; } = skewAllowance;

    /// <summary>Every member of the table, each with what it reads of <paramref name="families"/>: the entries that speak for the database of each family, and only those.</summary>
    public IReadOnlyList<EfToolingClusterMember> MembersFor(IReadOnlyList<EfToolingFamilyDatabase> families) => members(families);

    /// <summary>The counted members that cannot read <paramref name="version"/> of <paramref name="family"/> in the database with <paramref name="databaseIdentity"/>.</summary>
    public IReadOnlyList<EfToolingWaitingOn> Blockers(string family, string? databaseIdentity, string version) =>
        blockers(family, databaseIdentity, version);
}
