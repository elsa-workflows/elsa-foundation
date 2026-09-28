namespace Elsa.Cluster.Core.Models;

/// <summary>
/// A requirement on what a member's workflow runtime can activate (spec 184, FR-006), answered from the member's
/// <see cref="RunnabilitySection"/>. The three kinds are data only, so every provider can translate them into its own
/// terms (ADR 0078, invariant 3).
/// </summary>
/// <remarks>
/// <para>
/// Runnability requirements are met jointly: a member meets the runnability requirements of a query only when one of
/// its entries that applies to each requirement's database identity satisfies all of them (FR-009). Two entries that
/// each satisfy half the requirements describe two shells, neither of which can run the work, so they do not add up.
/// </para>
/// <para>
/// A member whose report is unknown, or that publishes no runnability section, cannot say what it activates and meets
/// no runnability requirement, for either purpose.
/// </para>
/// </remarks>
public abstract record RunnabilityRequirement : MemberRequirement
{
    private protected RunnabilityRequirement(string? databaseIdentity)
    {
        if (databaseIdentity is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);
        DatabaseIdentity = databaseIdentity;
    }

    /// <summary>The database whose finalization record the entry must name, or name none; <see langword="null"/> asks
    /// about no particular database.</summary>
    public string? DatabaseIdentity { get; }

    /// <summary>Whether <paramref name="entry"/> speaks for this requirement's database and satisfies it.</summary>
    public bool IsMetBy(RunnabilityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.AppliesTo(DatabaseIdentity) && IsSatisfiedBy(entry);
    }

    private protected abstract bool IsSatisfiedBy(RunnabilityEntry entry);

    internal override bool IsMetBy(FleetMember member, MemberQueryPurpose purpose) => EntriesOf(member).Any(IsMetBy);

    /// <summary>
    /// The first requirement of <paramref name="requirements"/>, in order, that no entry of <paramref name="member"/>
    /// can meet together with every requirement before it; <see langword="null"/> when one entry meets them all.
    /// </summary>
    internal static RunnabilityRequirement? FirstUnmetJointly(FleetMember member, IEnumerable<RunnabilityRequirement> requirements)
    {
        IEnumerable<RunnabilityEntry> candidates = EntriesOf(member).ToArray();
        foreach (var requirement in requirements)
        {
            candidates = candidates.Where(requirement.IsMetBy).ToArray();
            if (!candidates.Any())
                return requirement;
        }

        return null;
    }

    private static IReadOnlyList<RunnabilityEntry> EntriesOf(FleetMember member) =>
        member.Report is { IsUnknown: false, Runnability: { } section } ? section.Entries : [];
}

/// <summary>"Activates runtime consumer <see cref="ConsumerKey"/> at schema version <see cref="SchemaVersion"/>"
/// (spec 184, FR-006).</summary>
public sealed record ActivatesRuntimeConsumer : RunnabilityRequirement
{
    public ActivatesRuntimeConsumer(string consumerKey, string schemaVersion, string? databaseIdentity = null) : base(databaseIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ConsumerKey = consumerKey;
        SchemaVersion = schemaVersion;
    }

    public string ConsumerKey { get; }

    public string SchemaVersion { get; }

    private protected override bool IsSatisfiedBy(RunnabilityEntry entry) => entry.Activates(ConsumerKey, SchemaVersion);

    public override string ToString() => $"activates runtime consumer {ConsumerKey} at schema version {SchemaVersion}";
}

/// <summary>"Has durable-value storage driver <see cref="DriverKey"/>" (spec 184, FR-006).</summary>
public sealed record HasStorageDriver : RunnabilityRequirement
{
    public HasStorageDriver(string driverKey, string? databaseIdentity = null) : base(databaseIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverKey);
        DriverKey = driverKey;
    }

    public string DriverKey { get; }

    private protected override bool IsSatisfiedBy(RunnabilityEntry entry) => entry.HasStorageDriver(DriverKey);

    public override string ToString() => $"has durable-value storage driver {DriverKey}";
}

/// <summary>"Resolves activity type alias <see cref="TypeAlias"/>" (spec 184, FR-006).</summary>
public sealed record ResolvesActivityType : RunnabilityRequirement
{
    public ResolvesActivityType(string typeAlias, string? databaseIdentity = null) : base(databaseIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeAlias);
        TypeAlias = typeAlias;
    }

    public string TypeAlias { get; }

    private protected override bool IsSatisfiedBy(RunnabilityEntry entry) => entry.Resolves(TypeAlias);

    public override string ToString() => $"resolves activity type {TypeAlias}";
}
