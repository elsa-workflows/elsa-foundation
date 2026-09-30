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
/// entry names none, which counts for every database. A database whose last gate has stopped is forgotten, below; every
/// other rule here can only delay finalization, never hasten it.
/// </para>
/// <para>
/// The observed finalized version is reported only while every database this host has read agrees on it, since the
/// report has one entry per family; otherwise, or before any record is read, none is reported.
/// </para>
/// <para>
/// <b>An entry says whether the family's module is active.</b> A module is active in a database from the moment a gate has
/// admitted it there, in any shell, until every gate that did has stopped, so a family whose module was disabled, or was
/// never enabled, is not active (spec 183, FR-019; the rationale is on <c>ReadabilityEntry.ModuleActive</c>). Activity
/// is tracked per database, not per family: an entry that names a database is active only while a gate for that database
/// is, so a tenant whose shell stopped does not stay active in the report because another tenant's, in another database,
/// still is. An activation between publishing its report and reading its record counts as active too, and hands over to
/// the gate's own activity without a moment in between, since it is about to write. An entry that names none, because the host serves the family in several databases whose records are read, or
/// an activation is pending, applies to every database and is active while a gate for any of them is: the conservative
/// direction. Each gate is one owner, so several shells activating one family, or one gate reporting twice, never leave it
/// active after the last of them stops.
/// </para>
/// <para>
/// <b>Stopping the last gate of a database forgets what this host read there.</b> The entry then names the databases
/// whose gates remain, or none when none does, which counts for every database. A module activated in that database again
/// reads its record afresh and refuses itself when the finalized version is not one it reads (spec 181, FR-013 to FR-015),
/// so forgetting cannot let it write what a count left it out of. A database read by no gate at all, since its activation
/// was refused before it began, is not forgotten. Not modelled: one entry per database, which would let an entry that
/// names none be active for exactly the databases that have a gate; today it is active for all of them while any does.
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
    /// Marks <paramref name="families"/> active in the database <paramref name="databaseIdentity"/> for
    /// <paramref name="owner"/>, the gate that admitted their module there, until <see cref="Deactivate"/>. Repeating it
    /// for one owner changes nothing.
    /// </summary>
    public void Activate(IEfSchemaModuleGate owner, string databaseIdentity, IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);
        ArgumentNullException.ThrowIfNull(families);
        lock (_gate)
        {
            foreach (var family in families)
                For(family).DatabaseOf(databaseIdentity).Owners.Add(owner);
        }
    }

    /// <summary>
    /// Ends what <see cref="Activate"/> began for <paramref name="owner"/>: a database stays active while another owner
    /// holds it, and is forgotten, with what was read there, when the last owner leaves.
    /// </summary>
    public void Deactivate(IEfSchemaModuleGate owner, IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(families);
        lock (_gate)
        {
            foreach (var family in families)
            {
                if (!_families.TryGetValue(family, out var observations))
                    continue;
                foreach (var (identity, database) in observations.Databases.Where(entry => entry.Value.Owners.Contains(owner)).ToArray())
                {
                    database.Owners.Remove(owner);
                    if (database.Owners.Count == 0)
                        observations.Databases.Remove(identity);
                }
            }
        }
    }

    /// <summary>Records that this host read <paramref name="family"/>'s record in the database <paramref name="databaseIdentity"/>.</summary>
    public void Observe(string family, string databaseIdentity, string finalizedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalizedVersion);
        lock (_gate)
            For(family).DatabaseOf(databaseIdentity).Version = finalizedVersion;
    }

    /// <summary>
    /// The database identity and observed finalized version <paramref name="family"/>'s report entry carries, each
    /// <see langword="null"/> when the rules above say it names none, and whether its module is active for what it names.
    /// </summary>
    public EfSchemaFamilyObservation Find(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        lock (_gate)
        {
            if (!_families.TryGetValue(family, out var observations))
                return EfSchemaFamilyObservation.None;

            // An activation in progress counts as active: it is about to write, and may write before its report says so.
            var anyActive = observations.PendingActivations > 0 || observations.Databases.Values.Any(database => database.Owners.Count > 0);
            var versions = observations.Databases.Values.Select(database => database.Version).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            if (versions.Length == 0)
                return EfSchemaFamilyObservation.None with { ModuleActive = anyActive };

            // The entry names the one database only while that is the whole truth, and then "any database" is that one;
            // otherwise it speaks for every database, and is active while a gate of any of them is.
            var identity = observations.PendingActivations == 0 && observations.Databases.Count == 1 ? observations.Databases.Keys.Single() : null;
            return new EfSchemaFamilyObservation(identity, versions.Length == 1 ? versions[0] : null, anyActive);
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

        public Dictionary<string, DatabaseObservations> Databases { get; } = new(StringComparer.Ordinal);

        public DatabaseObservations DatabaseOf(string identity)
        {
            if (!Databases.TryGetValue(identity, out var database))
                Databases[identity] = database = new DatabaseObservations();
            return database;
        }
    }

    /// <summary>What this host read of one family in one database, and the gates that are active there.</summary>
    private sealed class DatabaseObservations
    {
        public string? Version { get; set; }

        public HashSet<IEfSchemaModuleGate> Owners { get; } = new(ReferenceEqualityComparer.Instance);
    }
}

/// <summary>
/// What a family's report entry says about the records this host has read, each part null when it names none, and
/// whether the family's module is active for what it names. Every part is said, since a default for the last would be the
/// wrong one for some caller: <see cref="None"/> is the observation of a family no gate has admitted.
/// </summary>
public sealed record EfSchemaFamilyObservation(string? DatabaseIdentity, string? ObservedFinalizedVersion, bool ModuleActive)
{
    /// <summary>Nothing read and no module active: what a family whose declaration is loaded and whose module no gate admitted has.</summary>
    public static EfSchemaFamilyObservation None { get; } = new(null, null, false);
}
