using System.Collections.Concurrent;
using System.Reflection;
using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A schema family's one chain (spec 180, FR-010), resolved from its <see cref="EfSchemaFamilyAttribute"/> declaration:
/// the versions every reader of the family accepts, the upcasters that carry a row from its stamp to the current
/// version, and the version a write stamps. Every store, and every reader outside the owning module, checks and
/// upcasts through the same instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant.</b> A read accepts a row only when its stamp is in <see cref="ReadableVersions"/>, and that set is
/// computed by the same function over the same declaration as the set a host's readability report credits
/// (<see cref="EfSchemaFamilyDescriptor.ReadableVersions"/>), so a host never reports a version its reads refuse or
/// reads one it does not report. Every version in the set has an upcaster for each step up to the current version, so a
/// read never returns data for a version it cannot upcast; anything outside the set, a row below a gap included, is
/// <see cref="EfSchemaVersionSkewException"/> and never bridged.
/// </para>
/// <para>
/// <b>Resolved once per process</b> (FR-025): <see cref="Of"/> caches one instance per declaring assembly and family, and
/// constructs each upcaster once. A row at the current version costs one membership test and one comparison of its
/// content columns with the declared ones, and runs no upcaster (FR-021); an older row costs one in-memory transform of
/// the whole row per step, on every read until it is next written, which stamps the current version and so upgrades it
/// (FR-014).
/// </para>
/// <para>
/// A fault in the chain (FR-005) does not stop a read: it only shrinks the readable set to what the chain still reaches.
/// <see cref="EnsureSound"/> refuses it where the family is registered, at startup, and the build refuses it before
/// that.
/// </para>
/// </remarks>
public sealed class EfSchemaChain
{
    private static readonly ConcurrentDictionary<(Assembly Assembly, string Family), Lazy<EfSchemaChain>> Chains = new();

    private readonly string[] _readable;
    private readonly IEfSchemaUpcaster[] _upcasters;
    private readonly Dictionary<Type, HashSet<string>> _content;

    private EfSchemaChain(EfSchemaFamilyDescriptor declaration)
    {
        Family = declaration.Name;
        Module = declaration.Module;
        CurrentVersion = declaration.CurrentVersion;
        _readable = [.. declaration.ReadableVersions];
        ReadableVersions = Array.AsReadOnly(_readable);
        Defects = declaration.Defects;
        ContentColumns = declaration.ContentColumns;
        _content = declaration.ContentColumns
            .GroupBy(column => column.Entity)
            .ToDictionary(table => table.Key, table => table.Select(column => column.Name).ToHashSet(StringComparer.Ordinal));
        // The readable set is the tail of the declared chain, so its steps are the last ReadableVersions.Count - 1 entries.
        _upcasters = declaration.Upcasters
            .Skip(declaration.Upcasters.Count - (_readable.Length - 1))
            .Select(upcaster => Construct(declaration, upcaster))
            .ToArray();
    }

    /// <summary>The family's name, as its declaration and every skew report name it.</summary>
    public string Family { get; }

    /// <summary>The owning EF module, or <see langword="null"/> for a family shared by no single module.</summary>
    public string? Module { get; }

    /// <summary>The version this build writes: every write stamps it, and it is the last of <see cref="ReadableVersions"/>.</summary>
    public string CurrentVersion { get; }

    /// <summary>The versions a read accepts, oldest first, ending at <see cref="CurrentVersion"/> (FR-004).</summary>
    public IReadOnlyList<string> ReadableVersions { get; }

    /// <summary>The faults in the declared chain (FR-005); empty when it is sound.</summary>
    public IReadOnlyList<string> Defects { get; }

    /// <summary>
    /// The content columns the family declares (<see cref="EfSchemaContentAttribute"/>), by table: exactly the columns
    /// <see cref="Upcast(string?, EfSchemaRowContent)"/> accepts and returns for a row of each table.
    /// </summary>
    public IReadOnlyList<EfSchemaColumn> ContentColumns { get; }

    /// <summary>
    /// The chain <paramref name="assembly"/> declares for <paramref name="family"/>, resolved on first use and shared by
    /// every caller after. A family's module class holds it in a static field, so the family's identity is one declared
    /// value every call site references (FR-002).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="assembly"/> declares no such family, or one of the chain's upcasters cannot be constructed: both are
    /// faults in the build, not in any row.
    /// </exception>
    public static EfSchemaChain Of(Assembly assembly, string family)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return Chains.GetOrAdd((assembly, family), key => new Lazy<EfSchemaChain>(() => Resolve(key.Assembly, key.Family))).Value;
    }

    /// <summary>A chain for <paramref name="declaration"/>, uncached: for a declaration no assembly carries, as in tests.</summary>
    public static EfSchemaChain For(EfSchemaFamilyDescriptor declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return new EfSchemaChain(declaration);
    }

    /// <summary>
    /// Refuses the registration of every family <paramref name="module"/>'s assembly declares for it, and of every shared
    /// family this package declares, which every module's context maps, when its chain has a fault (FR-005). Called by
    /// <c>AddEfModuleMigrations</c>, the one place every EF module passes through while a host wires itself up.
    /// </summary>
    public static void EnsureSound(Assembly assembly, string module)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var families = EfSchemaFamilyCatalog.Discover([assembly, typeof(EfSchemaChain).Assembly])
            .Where(family => family.Module is null || StringComparer.OrdinalIgnoreCase.Equals(family.Module, module));
        foreach (var family in families)
            Of(family.Assembly, family.Name).EnsureSound();
    }

    /// <summary>Throws when the declared chain has a fault, naming the family and every fault.</summary>
    public void EnsureSound()
    {
        if (Defects.Count > 0)
            throw new InvalidOperationException(
                $"Schema family '{Family}' declares an upcaster chain this build refuses to register (spec 180, FR-005): " +
                string.Join(" ", Defects));
    }

    /// <summary>True when <paramref name="stamp"/> is one of <see cref="ReadableVersions"/>, compared ordinally.</summary>
    public bool IsReadable(string? stamp) => IndexOf(stamp) >= 0;

    /// <summary>Throws <see cref="EfSchemaVersionSkewException"/> when <paramref name="stamp"/> is not readable.</summary>
    public void EnsureReadable(string? stamp) => _ = Position(stamp);

    /// <summary>
    /// True when a row stamped <paramref name="stamp"/> was written at <paramref name="version"/> or later, in chain
    /// order. An integrity clause for a projection column introduced at <paramref name="version"/> applies only to such
    /// rows (FR-008), since an older writer left the column unset.
    /// </summary>
    /// <exception cref="EfSchemaVersionSkewException"><paramref name="stamp"/> is not readable.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not one of <see cref="ReadableVersions"/>.</exception>
    public bool IsAtOrAfter(string? stamp, string version)
    {
        var introduced = IndexOf(version);
        if (introduced < 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, $"'{version}' is not a version schema family '{Family}' reads.");
        return Position(stamp) >= introduced;
    }

    /// <summary>
    /// Returns <paramref name="row"/>, the decoded content of a row stamped <paramref name="stamp"/>, as the current
    /// version stores it: every declared content column upcast together, one step at a time from the stamp to
    /// <see cref="CurrentVersion"/> (FR-009; #2144). Returns the very same instance, running no upcaster, when the row is
    /// at the current version (FR-021). Call it after the row's integrity clauses, which describe the stamped version, and
    /// before deserializing.
    /// </summary>
    /// <remarks>
    /// The invariant it keeps: a row is either at its stamped version or wholly upcast to the current one. Its columns must
    /// be exactly the ones the family declares for its table, whatever its version, and every step must return exactly
    /// those again, so no content column is read past the chain beside upcast ones, and no step adds or drops one.
    /// </remarks>
    /// <exception cref="EfSchemaVersionSkewException"><paramref name="stamp"/> is not readable.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="row"/> is not exactly the declared content columns of its table: a fault in the reading code, not in
    /// the row, so it is raised for a row at any readable version, the current one included.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// An upcaster failed on a readable row, returned nothing, or returned another table or other columns: the row cannot
    /// be trusted, so it is corrupt.
    /// </exception>
    public EfSchemaRowContent Upcast(string? stamp, EfSchemaRowContent row)
    {
        var position = Position(stamp);
        ArgumentNullException.ThrowIfNull(row);
        return Upcast(position, stamp, row);
    }

    /// <summary>
    /// <see cref="Upcast(string?, EfSchemaRowContent)"/> over a row of <typeparamref name="TEntity"/>'s table holding
    /// <paramref name="columns"/>: the form a store reads a row with, each content column named beside the value it reads,
    /// so the build can see that every read of one goes through the chain together with the rest of its row.
    /// </summary>
    public EfSchemaRowContent Upcast<TEntity>(string? stamp, params ReadOnlySpan<(string Column, string? Value)> columns)
    {
        var position = Position(stamp);
        return Upcast(position, stamp, new EfSchemaRowContent(typeof(TEntity), columns));
    }

    private EfSchemaRowContent Upcast(int position, string? stamp, EfSchemaRowContent row)
    {
        var declared = _content.GetValueOrDefault(row.Entity);
        if (declared is null || !SameColumns(row, declared))
            throw new InvalidOperationException(
                $"A read of schema family '{Family}' passes content columns {EfSchemaRowContent.Names(row.Columns.Keys)} of {row.Entity.Name}, " +
                $"but the family declares {(declared is null ? "no content columns for that table" : EfSchemaRowContent.Names(declared))}. " +
                "A read upcasts every declared content column of a row together, and no other column, so a row is never upcast in part " +
                "(spec 180, FR-009; #2144).");

        var value = row;
        for (var step = position; step < _upcasters.Length; step++)
        {
            var from = _readable[step];
            var to = _readable[step + 1];
            EfSchemaRowContent? next;
            try
            {
                next = _upcasters[step].Upcast(value);
            }
            // An upcaster is arbitrary module code (FR-009): whatever it throws means this row cannot be trusted, so it
            // becomes corruption rather than propagating. Only OutOfMemoryException is let through, since it is not the
            // upcaster's data that is unsound.
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw Corrupt(row, stamp, from, to, "failed", exception);
            }

            if (next is null)
                throw Corrupt(row, stamp, from, to, "returned no row", null);
            if (next.Entity != row.Entity)
                throw Corrupt(row, stamp, from, to, $"returned a row of {next.Entity.Name}", null);
            if (!SameColumns(next, declared))
                throw Corrupt(row, stamp, from, to,
                    $"returned columns {EfSchemaRowContent.Names(next.Columns.Keys)} where the table declares {EfSchemaRowContent.Names(declared)}, " +
                    "and a step adds and drops none", null);
            value = next;
        }

        return value;
    }

    private static bool SameColumns(EfSchemaRowContent row, HashSet<string> declared) =>
        row.Columns.Count == declared.Count && row.Columns.Keys.All(declared.Contains);

    private InvalidDataException Corrupt(EfSchemaRowContent row, string? stamp, string from, string to, string fault, Exception? inner) =>
        new($"Schema family '{Family}' could not upcast a row of {row.Entity.Name} from '{from}' to '{to}': its upcaster {fault}, " +
            $"on a row stamped '{stamp}', a version this build reads, so the row is corrupt.",
            inner);

    /// <summary>The version's position in the chain, the current version tried first since nearly every row is at it.</summary>
    private int IndexOf(string? version)
    {
        if (version is null)
            return -1;
        for (var index = _readable.Length - 1; index >= 0; index--)
        {
            if (StringComparer.Ordinal.Equals(_readable[index], version))
                return index;
        }

        return -1;
    }

    private int Position(string? stamp)
    {
        var position = IndexOf(stamp);
        return position >= 0 ? position : throw new EfSchemaVersionSkewException(Family, stamp, CurrentVersion, ReadableVersions);
    }

    private static EfSchemaChain Resolve(Assembly assembly, string family)
    {
        var declaration = EfSchemaFamilyCatalog.Discover([assembly])
                              .SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, family))
                          ?? throw new InvalidOperationException(
                              $"{assembly.GetName().Name} declares no [EfSchemaFamily(\"{family}\")], so no read of that family can " +
                              "be checked against a readable set.");
        return new EfSchemaChain(declaration);
    }

    private static IEfSchemaUpcaster Construct(EfSchemaFamilyDescriptor declaration, EfSchemaUpcasterDescriptor upcaster)
    {
        try
        {
            return (IEfSchemaUpcaster)Activator.CreateInstance(upcaster.Type)!;
        }
        catch (Exception exception) when (exception is TargetInvocationException or MissingMethodException
                                               or MemberAccessException or ArgumentException or NotSupportedException
                                               or TypeLoadException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Schema family '{declaration.Name}' cannot construct its upcaster '{upcaster.Type.FullName}' from " +
                $"'{upcaster.From}' to '{upcaster.To}'.",
                exception);
        }
    }
}
