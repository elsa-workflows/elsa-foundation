using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one binary collation per relational provider, and the only supported way a module declares it.
/// <para>
/// Every module that compares or orders a string column the way .NET compares strings needs the database
/// to agree. A server whose default collation is linguistic (SQL Server's <c>*_CI_AS</c> family,
/// PostgreSQL's <c>en_US.UTF-8</c>) orders <c>'a'</c> before <c>'B'</c> and treats <c>'A'</c> and
/// <c>'a'</c> as equal, so an exact-value check stops being exact and a keyset page built from an ordinal
/// cursor can skip or repeat rows. A binary collation makes SQL ordering and equality agree with
/// <see cref="StringComparer.Ordinal"/>.
/// </para>
/// <para>
/// <b>Per column, never at model level.</b> <see cref="RelationalModelBuilderExtensions.UseCollation(ModelBuilder,string?)"/>
/// declares a collation for the whole database. Modules can share one database, so that is one module
/// imposing its comparison semantics on its neighbours' tables; and in this repository it did not even
/// reach the schema, because a model-level declaration produced migrations with no collation at all
/// (elsa-workflows/elsa-foundation#1837).
/// </para>
/// </summary>
public static class EfOrdinalCollation
{
    /// <summary>The modern binary collation. <c>Latin1_General_BIN2</c> orders identically; the 100 series is current.</summary>
    public const string SqlServer = "Latin1_General_100_BIN2";

    /// <summary>The C locale: byte order, which for UTF-8 is code-point order.</summary>
    public const string PostgreSql = "C";

    /// <summary>utf8mb4's binary collation, with NO PAD, so trailing spaces stay significant.</summary>
    public const string MySql = "utf8mb4_0900_bin";

    /// <summary>
    /// SQLite declares nothing. <c>BINARY</c> is its only byte-ordinal collation for TEXT and it is already
    /// the default for every column, so naming it would be four spellings of "leave it alone" — and it is
    /// why the SQLite legs cannot prove this fix. That proof is on the SQL Server and PostgreSQL legs.
    /// </summary>
    public const string? Sqlite = null;

    /// <summary>
    /// Oracle's MySQL provider reads its own annotation and ignores the relational column collation, so a
    /// MySQL column carries both. Setting it by its stable metadata name keeps modules provider-neutral.
    /// </summary>
    private const string MySqlCollationAnnotation = "MySQL:Collation";

    /// <summary>The binary collation for <paramref name="providerName"/>, or <c>null</c> where the default already is one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A provider nobody has decided a collation for. Failing here is deliberate: a silent <c>null</c>
    /// would give a new provider the server's linguistic default and look like a decision.
    /// </exception>
    public static string? ForProvider(string providerName) => providerName switch
    {
        EfProviderNames.SqlServer => SqlServer,
        EfProviderNames.PostgreSql => PostgreSql,
        EfProviderNames.MySql => MySql,
        EfProviderNames.Sqlite => Sqlite,
        _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName,
            "No ordinal collation is declared for this provider. Decide one in EfOrdinalCollation before binding a module to it.")
    };

    /// <summary>
    /// Applies <paramref name="providerName"/>'s binary collation to every string column the model compares
    /// or orders: the ones a key or an index is built on, plus the ones named in <paramref name="alsoCompared"/>.
    /// Payload, description and free-text columns are left alone, so nothing that searches them changes meaning.
    /// </summary>
    /// <param name="alsoCompared">
    /// Property names the module compares or orders in SQL without an index behind them — lookup projections,
    /// identity columns checked alongside their hash, cursor keys. A name no entity declares is ignored, so one
    /// list can serve a model whose entities do not all carry the same columns.
    /// </param>
    public static void Apply(ModelBuilder modelBuilder, string providerName, params string[] alsoCompared)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var collation = ForProvider(providerName);
        if (collation is null)
            return;
        foreach (var property in ComparedProperties(modelBuilder.Model, alsoCompared))
        {
            property.SetCollation(collation);
            if (providerName == EfProviderNames.MySql)
                property.SetAnnotation(MySqlCollationAnnotation, collation);
        }
    }

    /// <summary>
    /// The string properties <see cref="Apply"/> collates. Exposed so a test can ask the model what it declared
    /// and then check the generated migration carries exactly that.
    /// </summary>
    public static IReadOnlyList<IMutableProperty> ComparedProperties(IMutableModel model, params string[] alsoCompared)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(alsoCompared);
        var named = new HashSet<string>(alsoCompared, StringComparer.Ordinal);
        var compared = new List<IMutableProperty>();
        foreach (var entity in model.GetEntityTypes())
        {
            var keyed = new HashSet<string>(
                entity.GetKeys().SelectMany(key => key.Properties)
                    .Concat(entity.GetIndexes().SelectMany(index => index.Properties))
                    .Select(property => property.Name),
                StringComparer.Ordinal);
            compared.AddRange(entity.GetProperties()
                .Where(property => property.ClrType == typeof(string))
                .Where(property => keyed.Contains(property.Name) || named.Contains(property.Name)));
        }

        return compared;
    }
}
