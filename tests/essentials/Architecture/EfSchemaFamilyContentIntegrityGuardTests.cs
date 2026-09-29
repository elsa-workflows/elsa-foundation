using Xunit;
using static Elsa.Architecture.Tests.EfSchemaFamilyTestFixtures;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Content and integrity declarations (spec 180): every content and integrity column is declared beside its family
/// (<c>[EfSchemaContent]</c>, <c>[EfSchemaIntegrity]</c>), naming a declared family, and an integrity column records
/// its reason (FR-008); <c>EfSchemaContentDeclarationTests</c> holds the declaration complete against every module's
/// model, and this guard holds it at least as complete as the call sites the restamp rule used to infer content from.
/// </summary>
public sealed class EfSchemaFamilyContentIntegrityGuardTests
{
    [Fact]
    public void Every_content_and_integrity_declaration_names_a_declared_family() =>
        AssertNone(Production.ColumnDeclarationViolations(), "A family's content and integrity columns are declared beside it, naming it by its " +
            "SchemaFamily constant, the entity with typeof and each column with nameof; an integrity column records why it is compared " +
            "as stored bytes (spec 180, FR-008 and FR-009):");

    [Fact]
    public void Every_column_a_store_upcasts_is_declared_content_by_its_family() =>
        AssertNone(Persistence.UpcastDeclarationViolations(), "A column a store reads through a family's chain is that family's content, so the " +
            "family declares it with [EfSchemaContent] and the read and restamp rules hold every other read and write of it " +
            "(spec 180, FR-009 and FR-014):");
}
