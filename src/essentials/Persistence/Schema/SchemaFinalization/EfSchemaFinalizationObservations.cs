namespace Elsa.Persistence.Schema.SchemaFinalization;

/// <summary>
/// What this host has read from finalization records, per schema family, across every shell and database it serves:
/// the source of the database identity and observed finalized version its readability report carries (spec 183,
/// FR-019; spec 181, FR-001 and FR-010). One instance is shared by the whole host, so it is registered by instance on
/// the host container.
/// </summary>
/// <remarks>
/// <para>
/// <b>An entry names a database only while that is the whole truth.</b> A report entry that names a database identity
/// is counted only by evaluators of that database (spec 183, FR-023), so naming one while this host also serves the
/// family in another database would let the other database finalize a version this host cannot read. So
/// <see cref="Find"/> names an identity only when every record of the family this host has read carries that one
/// identity, and no activation of the family is between publishing its report and reading its record (FR-013): an
/// activation about to read a database it has not read before cannot yet name that database, so until it has, the
/// entry names none, which counts for every database. Identities are never forgotten, so a family whose module was
/// disabled keeps being counted where it was. Each of these can only delay finalization, never hasten it.
/// </para>
/// <para>
/// The observed finalized version is reported only while every database this host has read agrees on it, since the
/// report has one entry per family; otherwise, or before any record is read, none is reported.
/// </para>
/// <para>
/// <b>An entry says whether the family's module is active.</b> A module is active from the moment a gate has admitted
/// it, in any shell, until that gate stops, so a family whose module was disabled, or was never enabled, keeps the
/// versions it observed but is not active. That is what lets the backfill's settle condition (spec 186, FR-012) leave a
/// host that only loads the family's declaration out of what it waits for, while every readability count still counts it.
/// Each gate is one owner, so several shells activating one family, or one gate reporting twice, never leave it active
/// after the last of them stops.
/// </para>
/// </remarks>
public sealed class EfSchemaFinalizationObservations
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FamilyObservations> _families = new(StringComparer.Ordinal);

    /// <summary>
    /// Marks an activation of <paramref name="families"/> that has not yet read their records, so the report names no
    /// database for them until <see cref="EndActivation"/>.
    /// </summary>
    public void BeginActivation(IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        lock (_gate)
        {
            foreach (var family in families)
                For(family).PendingActivations++;
        }
    }

    /// <summary>Ends an activation <see cref="BeginActivation"/> began, whether or not it read the records.</summary>
    public void EndActivation(IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        lock (_gate)
        {
            foreach (var observations in families.Select(For))
                observations.PendingActivations = Math.Max(0, observations.PendingActivations - 1);
        }
    }

    /// <summary>
    /// Marks <paramref name="families"/> active in this host for <paramref name="owner"/>, the gate that admitted their
    /// module, until <see cref="Deactivate"/>. Repeating it for one owner changes nothing.
    /// </summary>
    public void Activate(object owner, IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(families);
        lock (_gate)
        {
            foreach (var family in families)
                For(family).ActiveOwners.Add(owner);
        }
    }

    /// <summary>Ends what <see cref="Activate"/> began for <paramref name="owner"/>: the family stays active while another owner holds it.</summary>
    public void Deactivate(object owner, IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(families);
        lock (_gate)
        {
            foreach (var observations in families.Select(For))
                observations.ActiveOwners.Remove(owner);
        }
    }

    /// <summary>Records that this host read <paramref name="family"/>'s record in the database <paramref name="databaseIdentity"/>.</summary>
    public void Observe(string family, string databaseIdentity, string finalizedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalizedVersion);
        lock (_gate)
            For(family).Versions[databaseIdentity] = finalizedVersion;
    }

    /// <summary>
    /// The database identity and observed finalized version <paramref name="family"/>'s report entry carries, each
    /// <see langword="null"/> when the rules above say it names none, and whether its module is active.
    /// </summary>
    public EfSchemaFamilyObservation Find(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        lock (_gate)
        {
            if (!_families.TryGetValue(family, out var observations))
                return EfSchemaFamilyObservation.None;

            var active = observations.ActiveOwners.Count > 0;
            if (observations.Versions.Count == 0)
                return EfSchemaFamilyObservation.None with { ModuleActive = active };

            var identity = observations.PendingActivations == 0 && observations.Versions.Count == 1
                ? observations.Versions.Keys.Single()
                : null;
            var versions = observations.Versions.Values.Distinct(StringComparer.Ordinal).ToArray();
            return new EfSchemaFamilyObservation(identity, versions.Length == 1 ? versions[0] : null, active);
        }
    }

    private FamilyObservations For(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (!_families.TryGetValue(family, out var observations))
            _families[family] = observations = new FamilyObservations();
        return observations;
    }

    private sealed class FamilyObservations
    {
        public int PendingActivations { get; set; }

        public HashSet<object> ActiveOwners { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<string, string> Versions { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>
/// What a family's report entry says about the records this host has read, each part null when it names none, and
/// whether the family's module is active in this host.
/// </summary>
public sealed record EfSchemaFamilyObservation(string? DatabaseIdentity, string? ObservedFinalizedVersion, bool ModuleActive = false)
{
    public static EfSchemaFamilyObservation None { get; } = new(null, null);
}
