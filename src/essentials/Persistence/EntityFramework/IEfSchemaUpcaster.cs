namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Transforms one schema family's stored content from one version to its immediate successor (spec 180, FR-003). The
/// type carries <see cref="EfSchemaUpcasterAttribute"/> naming the two versions, and the family's
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
/// <see cref="Upcast"/> must be a pure, total function of its input over every valid document at the source version
/// (FR-019): no clock, randomness, environment, culture, configuration, I/O or service, so two hosts upcasting the same
/// stored content produce byte-identical output. It preserves every identity the row carries and every identity derived
/// from its content (FR-020). It sees decoded content only (FR-012) and is never called for a null column. It receives
/// every content column of the family, so it returns content it has no change for exactly as it received it. A throw,
/// or a null result, reports the row as corrupt (FR-009).
/// </para>
/// </remarks>
public interface IEfSchemaUpcaster
{
    /// <summary>Returns <paramref name="content"/> as the successor version stores it.</summary>
    string Upcast(EfSchemaContent content);
}

/// <summary>
/// One decoded content column of a schema family's row, as <see cref="IEfSchemaUpcaster.Upcast"/> receives it.
/// </summary>
/// <param name="Table">
/// The table's name, as the owning module's constants spell it; for a table every EF module maps under a name of its
/// own, such as the finalization record, the shared prefix of that name.
/// </param>
/// <param name="Column">The name of the entity property that maps the column.</param>
/// <param name="Value">The column's decoded content at the upcaster's source version.</param>
public readonly record struct EfSchemaContent(string Table, string Column, string Value);
