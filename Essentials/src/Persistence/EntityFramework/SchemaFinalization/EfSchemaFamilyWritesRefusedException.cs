namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// Every write to a schema family is refused because this host found, after its module activated, that the family's
/// finalized version is one it cannot read (spec 181, FR-012): the host was partitioned or counted as expired while
/// the version finalized, or a cluster was misconfigured. It does not keep writing at its old version, since rows at
/// the finalized version may exist and it cannot read them.
/// </summary>
/// <remarks>
/// A write refusal (spec 180, FR-016a), so it is unassignable to the types store catch filters and API fault ladders
/// turn into corruption or a 400, and it carries the same stable code. <see cref="EfSchemaWriteRefusedException.WriteVersion"/>
/// is the version this host last wrote, <see cref="EfSchemaWriteRefusedException.RequiredVersion"/> the finalized
/// version it cannot read.
/// </remarks>
public sealed class EfSchemaFamilyWritesRefusedException : EfSchemaWriteRefusedException
{
    public EfSchemaFamilyWritesRefusedException(string family, string lastWriteVersion, string finalizedVersion, IReadOnlyList<string> readableVersions)
        : base(family, lastWriteVersion, finalizedVersion,
            $"Schema family '{family}' refused a write: it is finalized at '{finalizedVersion}', and this host reads only " +
            $"[{string.Join(", ", readableVersions)}], so every write to the family is refused rather than rewriting rows this " +
            $"host cannot read. It last wrote '{lastWriteVersion}'. Run a version that reads '{finalizedVersion}', or restore a " +
            $"database backup taken before '{finalizedVersion}' was finalized. Nothing was saved.")
    {
        ReadableVersions = readableVersions;
    }

    /// <summary>The versions of the family this host reads.</summary>
    public IReadOnlyList<string> ReadableVersions { get; }
}
