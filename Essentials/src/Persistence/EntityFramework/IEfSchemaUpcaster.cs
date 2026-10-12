namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Transforms one schema family's stored rows from one version to its immediate successor (spec 180, FR-003). The type
/// carries <see cref="EfSchemaUpcasterAttribute"/> naming the two versions, and the family's
/// <see cref="EfSchemaFamilyAttribute.Upcasters"/> lists it in chain order.
/// </summary>
/// <remarks>
/// <para>
/// A concrete type with a public parameterless constructor and no injected services, in the owning EF module's assembly
/// beside the family's store code: the <c>dotnet elsa persistence</c> worker has no shell container, which is why
/// <see cref="IEfPostMigrationAction"/> has the same constraint. One instance serves every read in the process, from
/// every thread.
/// </para>
/// <para>
/// It works on a whole row, not a column (owner decision of 2026-09-28 on #2093): <see cref="Upcast"/> receives every
/// content column the family declares for the row's table (<see cref="EfSchemaContentAttribute"/>), so one step can
/// move or split data across columns, and returns every one of them, so a row is never left half upgraded. It changes
/// the columns it has a change for, usually with <see cref="EfSchemaRowContent.With"/>, and returns the others exactly
/// as it received them; that includes a column a later version introduced, which it did not know when it shipped and
/// which is null on the rows it reads. <see cref="EfSchemaChain"/> refuses a result that adds a column, drops one or
/// names another table.
/// </para>
/// <para>
/// <see cref="Upcast"/> must be a pure, total function of its input over every valid row at the source version
/// (FR-019): no clock, randomness, environment, culture, configuration, I/O or service, so two hosts upcasting the same
/// stored row produce byte-identical output. It preserves every identity the row carries and every identity derived
/// from its content (FR-020). It sees decoded content only (FR-012); a null value is a column the row stores as null. A
/// throw, a null result, or a result that is not the same row's declared columns reports the row as corrupt (FR-009).
/// </para>
/// </remarks>
public interface IEfSchemaUpcaster
{
    /// <summary>Returns <paramref name="row"/> as the successor version stores it: the same table, the same columns.</summary>
    EfSchemaRowContent Upcast(EfSchemaRowContent row);
}
