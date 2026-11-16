using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The payload columns a module stores documents in, and the only supported way it declares them.
/// <para>
/// A payload column holds a module's lossless JSON document; every attribute a query needs is lifted out into its
/// own sibling projection column. That separation is what makes an encoding possible at all, and this type is where
/// it is enforced: a declared payload column may not be keyed, indexed, or length-bounded, because each of those
/// would make the database depend on the column's stored bytes.
/// </para>
/// </summary>
public static class EfPayloadColumns
{
    /// <summary>
    /// Property names that may never be encoded, whatever entity declares them.
    /// <para>
    /// The <c>ContentAuthority</c> family is read by <b>database-side SQL in all four dialects</b>: the
    /// <c>ContentAuthorityIsValid</c> computed column, and SQL Server's digest comparison against
    /// <c>ContentAuthorityIntegrityHash</c> (<c>ActivityAuthorityValiditySql</c>). A client-side encoding makes every
    /// row evaluate invalid, and because the Activities Design stores filter on <c>ContentAuthorityIsValid</c> before
    /// paging, the failure mode is silent omission rather than an error. Lifting this means removing those computed
    /// columns first, which elsa-workflows/elsa-foundation#1836 declined deliberately: the column exists so the
    /// database re-derives validity from the stored bytes rather than trusting the writer.
    /// </para>
    /// <para>
    /// Matched by bare name rather than by entity: the risk is a module listing one of these names in its payload
    /// declaration, and the name is distinctive enough that any entity carrying it means the same thing.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.Ordinal)
    {
        "ContentAuthorityJson",
        "ContentAuthorityCanonicalJson",
        "ContentAuthorityAuthorityKeyJson",
        "ContentAuthoritySourceIdJson"
    };

    /// <summary>
    /// Columns excluded on one entity only, as <c>&lt;entity&gt;.&lt;property&gt;</c>, because the name alone is too
    /// common to refuse everywhere.
    /// <para>
    /// <c>SecretRecord.Payload</c> is secret material. Compressing before encrypting leaks length structure, and that
    /// is not a persistence decision to take here.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ExcludedColumns = new(StringComparer.Ordinal)
    {
        "SecretRecord.Payload"
    };

    /// <summary>
    /// Encodes every string property named in <paramref name="payloadProperties"/> with the codec
    /// <paramref name="context"/> was bound to. A name no entity declares is ignored, so one list can serve a model
    /// whose entities do not all carry the same columns.
    /// <para>
    /// With no encoding configured this still installs the converter, so the context <b>reads</b> a frame written by
    /// any other host. That is the point of calling it unconditionally: the decoder has to be deployed before any
    /// encoder is enabled, or a rollback cannot read what the newer build wrote.
    /// </para>
    /// </summary>
    public static ModelBuilder UseElsaPayloadColumns(
        this ModelBuilder modelBuilder,
        DbContext context,
        params string[] payloadProperties)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);
        var options = EfPayloadCompressionOptionsExtension.Find(context);
        var codec = options.Codec;
        var minimumLength = options.MinimumLength;
        var converter = new EfPayloadValueConverter(codec, minimumLength);
        foreach (var property in PayloadProperties(modelBuilder.Model, payloadProperties))
            property.SetValueConverter(converter);
        return modelBuilder;
    }

    /// <summary>
    /// The string properties <see cref="UseElsaPayloadColumns"/> encodes, after refusing every declaration that would
    /// make the database depend on a payload column's stored bytes. Exposed so a test can ask a model what it
    /// declared.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A declared column is excluded, keyed, indexed, or length-bounded.
    /// </exception>
    public static IReadOnlyList<IMutableProperty> PayloadProperties(IMutableModel model, params string[] payloadProperties)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(payloadProperties);
        var named = new HashSet<string>(payloadProperties, StringComparer.Ordinal);
        var payloads = new List<IMutableProperty>();
        foreach (var entity in model.GetEntityTypes())
        {
            var constrained = new HashSet<string>(
                entity.GetKeys().SelectMany(key => key.Properties)
                    .Concat(entity.GetIndexes().SelectMany(index => index.Properties))
                    .Select(property => property.Name),
                StringComparer.Ordinal);
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType != typeof(string) || !named.Contains(property.Name))
                    continue;
                Refuse(entity, property, constrained);
                payloads.Add(property);
            }
        }

        return payloads;
    }

    /// <summary>True when <paramref name="property"/> is encoded by this codec rather than by some other converter.</summary>
    /// <remarks>
    /// Identified by the converter's type rather than by a model annotation on purpose: a custom annotation is
    /// written into the migrations model snapshot, which would make every committed snapshot stale and turn a
    /// read-side change into a migration for all four providers.
    /// </remarks>
    public static bool IsPayloadColumn(IReadOnlyProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.GetValueConverter() is EfPayloadValueConverter;
    }

    private static void Refuse(IMutableEntityType entity, IMutableProperty property, HashSet<string> constrained)
    {
        var qualified = $"{entity.ClrType.Name}.{property.Name}";
        if (ExcludedNames.Contains(property.Name) || ExcludedColumns.Contains(qualified))
        {
            throw new InvalidOperationException(
                $"{qualified} cannot be a payload column. See EfPayloadColumns for why this one is excluded " +
                "permanently rather than deferred.");
        }

        if (constrained.Contains(property.Name))
        {
            throw new InvalidOperationException(
                $"{qualified} is part of a key or an index, so it cannot be a payload column: the database would " +
                "compare its stored bytes. Project what the query needs into its own column instead.");
        }

        if (property.GetMaxLength() is { } maximumLength)
        {
            throw new InvalidOperationException(
                $"{qualified} has a maximum length of {maximumLength}, so it cannot be a payload column: an encoded " +
                "value has a different length from its plaintext and would be refused or truncated by the provider.");
        }
    }
}

/// <summary>
/// The converter <see cref="EfPayloadColumns.UseElsaPayloadColumns"/> installs. A named type rather than an inline
/// <see cref="ValueConverter{TModel,TProvider}"/> so a test can tell a payload column from a column some other part
/// of the model converts for its own reasons.
/// </summary>
public sealed class EfPayloadValueConverter(EfPayloadCompression codec, int minimumLength)
    : ValueConverter<string, string>(
        plaintext => EfPayloadCodec.Encode(plaintext, codec, minimumLength)!,
        stored => EfPayloadCodec.Decode(stored)!);
