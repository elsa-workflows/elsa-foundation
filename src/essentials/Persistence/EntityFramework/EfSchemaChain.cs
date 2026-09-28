using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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
/// constructs each upcaster once. A row at the current version costs one membership test and runs no upcaster (FR-021);
/// an older row costs one in-memory transform per step, on every read until it is next written, which stamps the
/// current version and so upgrades it (FR-014).
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

    private EfSchemaChain(EfSchemaFamilyDescriptor declaration)
    {
        Family = declaration.Name;
        Module = declaration.Module;
        CurrentVersion = declaration.CurrentVersion;
        _readable = [.. declaration.ReadableVersions];
        ReadableVersions = Array.AsReadOnly(_readable);
        Defects = declaration.Defects;
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
    /// Returns <paramref name="content"/>, a decoded content column of a row stamped <paramref name="stamp"/>, as the
    /// current version stores it: upcast one step at a time from the stamp to <see cref="CurrentVersion"/> (FR-009).
    /// Returns it untouched, running no upcaster, when the row is at the current version or the column is null. Call it
    /// after the row's integrity clauses, which describe the stamped version, and before deserializing.
    /// </summary>
    /// <exception cref="EfSchemaVersionSkewException"><paramref name="stamp"/> is not readable.</exception>
    /// <exception cref="InvalidDataException">An upcaster failed, or returned nothing, on a readable row: the row is corrupt.</exception>
    [return: NotNullIfNotNull(nameof(content))]
    public string? Upcast(string? stamp, string table, string column, string? content)
    {
        var position = Position(stamp);
        if (content is null)
            return null;

        var value = content;
        for (var step = position; step < _upcasters.Length; step++)
        {
            var from = _readable[step];
            var to = _readable[step + 1];
            try
            {
                value = _upcasters[step].Upcast(new EfSchemaContent(table, column, value)) ??
                        throw new InvalidDataException("The upcaster returned no content.");
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Schema family '{Family}' could not upcast {table}.{column} from '{from}' to '{to}' on a row stamped " +
                    $"'{stamp}', a version this build reads, so the row is corrupt.",
                    exception);
            }
        }

        return value;
    }

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
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Schema family '{declaration.Name}' cannot construct its upcaster '{upcaster.Type.FullName}' from " +
                $"'{upcaster.From}' to '{upcaster.To}'.",
                exception);
        }
    }
}
