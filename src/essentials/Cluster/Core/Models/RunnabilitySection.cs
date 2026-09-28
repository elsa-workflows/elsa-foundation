namespace Elsa.Cluster.Core.Models;

/// <summary>
/// The member report section placement adds (spec 184, FR-008): what this host's workflow runtime can activate, one
/// entry per shell in which the distributed runtime is active. It says "runnability" where older text says
/// "capability", because the root glossary retires "Capability".
/// </summary>
/// <remarks>
/// A placement-purpose member query with runnability requirements matches a member when one of its entries that applies
/// to the queried database satisfies every requirement (FR-009). A member's own claims never read this section: the
/// claimant checks its registries directly (FR-011), so a section that lags can mislead a diagnostic but never places
/// work.
/// </remarks>
public sealed record RunnabilitySection : MemberReportSection
{
    public RunnabilitySection(IEnumerable<RunnabilityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToArray();
        if (list.Any(entry => entry is null))
            throw new ArgumentException("A runnability entry must not be null.", nameof(entries));
        Entries = list;
    }

    public IReadOnlyList<RunnabilityEntry> Entries { get; }

    public bool Equals(RunnabilitySection? other) => other is not null && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode() => Entries.Count;
}

/// <summary>
/// What one shell's workflow runtime can activate (spec 184, FR-008): the runtime consumer keys with the schema versions
/// each supports, the durable-value storage-driver keys, and the activity type aliases its type registry resolves, plus
/// the per-database identity of the finalization record the shell's runtime last read, or none yet. It is data only
/// and carries no secret or configuration text.
/// </summary>
/// <remarks>
/// Every list is normalized to ordinal order without duplicates, so a shell whose registries have not changed publishes
/// an equal entry and a provider republishes only when something changed (FR-030). An entry that names no database
/// identity applies to every database, the conservative direction for a routing answer.
/// </remarks>
public sealed record RunnabilityEntry
{
    private readonly Dictionary<string, HashSet<string>> _versionsByConsumer;
    private readonly HashSet<string> _storageDrivers;
    private readonly HashSet<string> _activityTypes;

    public RunnabilityEntry(
        IEnumerable<RunnableConsumer> consumers,
        IEnumerable<string> storageDrivers,
        IEnumerable<string> activityTypes,
        string? databaseIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(consumers);
        ArgumentNullException.ThrowIfNull(storageDrivers);
        ArgumentNullException.ThrowIfNull(activityTypes);
        if (databaseIdentity is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);

        var consumerList = consumers.ToArray();
        if (consumerList.Any(consumer => consumer is null))
            throw new ArgumentException("A runnable consumer must not be null.", nameof(consumers));

        Consumers = consumerList
            .GroupBy(consumer => consumer.ConsumerKey, StringComparer.Ordinal)
            .Select(group => new RunnableConsumer(group.Key, group.SelectMany(consumer => consumer.SchemaVersions)))
            .OrderBy(consumer => consumer.ConsumerKey, StringComparer.Ordinal)
            .ToArray();
        StorageDrivers = Normalize(storageDrivers, nameof(storageDrivers));
        ActivityTypes = Normalize(activityTypes, nameof(activityTypes));
        DatabaseIdentity = databaseIdentity;

        _versionsByConsumer = Consumers.ToDictionary(
            consumer => consumer.ConsumerKey,
            consumer => consumer.SchemaVersions.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        _storageDrivers = StorageDrivers.ToHashSet(StringComparer.Ordinal);
        _activityTypes = ActivityTypes.ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<RunnableConsumer> Consumers { get; }

    public IReadOnlyList<string> StorageDrivers { get; }

    public IReadOnlyList<string> ActivityTypes { get; }

    /// <summary>The finalization record's per-database identity the shell's runtime last read (spec 181, FR-001), or
    /// <see langword="null"/> before it has read one.</summary>
    public string? DatabaseIdentity { get; }

    /// <summary>Whether this entry speaks for <paramref name="databaseIdentity"/>: it names it, it names none, or no
    /// database was asked about.</summary>
    public bool AppliesTo(string? databaseIdentity) =>
        databaseIdentity is null || DatabaseIdentity is null || string.Equals(DatabaseIdentity, databaseIdentity, StringComparison.Ordinal);

    public bool Activates(string consumerKey, string schemaVersion) =>
        _versionsByConsumer.TryGetValue(consumerKey, out var versions) && versions.Contains(schemaVersion);

    public bool HasStorageDriver(string driverKey) => _storageDrivers.Contains(driverKey);

    public bool Resolves(string activityTypeAlias) => _activityTypes.Contains(activityTypeAlias);

    public bool Equals(RunnabilityEntry? other) =>
        other is not null &&
        string.Equals(DatabaseIdentity, other.DatabaseIdentity, StringComparison.Ordinal) &&
        Consumers.SequenceEqual(other.Consumers) &&
        StorageDrivers.SequenceEqual(other.StorageDrivers, StringComparer.Ordinal) &&
        ActivityTypes.SequenceEqual(other.ActivityTypes, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(DatabaseIdentity, Consumers.Count, StorageDrivers.Count, ActivityTypes.Count);

    private static string[] Normalize(IEnumerable<string> values, string parameterName)
    {
        var list = values.ToArray();
        if (list.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A runnability key must not be blank.", parameterName);
        return list.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>A runtime consumer key and the schema versions the shell's runtime activates it at (spec 184, FR-008).</summary>
public sealed record RunnableConsumer
{
    public RunnableConsumer(string consumerKey, IEnumerable<string> schemaVersions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        ArgumentNullException.ThrowIfNull(schemaVersions);
        var versions = schemaVersions.ToArray();
        if (versions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A schema version must not be blank.", nameof(schemaVersions));

        ConsumerKey = consumerKey;
        SchemaVersions = versions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public string ConsumerKey { get; }

    public IReadOnlyList<string> SchemaVersions { get; }

    public bool Equals(RunnableConsumer? other) =>
        other is not null &&
        string.Equals(ConsumerKey, other.ConsumerKey, StringComparison.Ordinal) &&
        SchemaVersions.SequenceEqual(other.SchemaVersions, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(ConsumerKey, SchemaVersions.Count);
}
