using Elsa.Primitives.Exceptions;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The write refusal of spec 180 (FR-016a): a write whose value needs a schema version later than the version this host
/// may write for the family. Nothing is saved, and nothing is dropped to make the value fit: a value carrying data in a
/// member its write version cannot hold is refused whole (FR-016).
/// </summary>
/// <remarks>
/// <para>
/// Unassignable to <see cref="InvalidOperationException"/>, <see cref="ArgumentException"/>, <see cref="FormatException"/>,
/// <see cref="NotSupportedException"/>, <c>JsonException</c> and <see cref="InvalidDataException"/>, the types store catch
/// filters and API fault ladders turn into corruption or a 400, for the same reason <see cref="EfSchemaVersionSkewException"/>
/// is. Every domain API that can raise it maps it to HTTP 409 in its own problem envelope, carrying
/// <see cref="SchemaWriteRefusedException.Code"/>: an API sees no EF Core, so it answers the
/// <see cref="SchemaWriteRefusedException"/> this derives from, through <c>Elsa.Api.AspNetCore</c>'s
/// <c>SchemaWriteRefusalProblem</c>.
/// </para>
/// <para>
/// A store raises it with the family and the two versions alone. Spec 182 reuses it for a write that needs a dormant
/// feature's data, adding a feature id and an operator-facing reason in a derived type, which is why it is not sealed.
/// Nothing raises it while a host's write version is its current version, which is the case until spec 181's gate lets a
/// host write an older finalized version.
/// </para>
/// </remarks>
public class EfSchemaWriteRefusedException : SchemaWriteRefusedException
{
    public EfSchemaWriteRefusedException(string family, string writeVersion, string requiredVersion)
        : this(family, writeVersion, requiredVersion,
            $"Schema family '{family}' refused a write: the value needs schema version '{requiredVersion}', but this host may write " +
            $"only '{writeVersion}' until '{requiredVersion}' is finalized for the family. Nothing was saved.")
    {
    }

    /// <summary>For a derived refusal that states its own reason, such as spec 182's dormant-feature refusal.</summary>
    protected EfSchemaWriteRefusedException(string family, string writeVersion, string requiredVersion, string message)
        : base(family, writeVersion, requiredVersion, message)
    {
    }
}
