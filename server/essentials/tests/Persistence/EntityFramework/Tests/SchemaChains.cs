using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Synthetic schema-family declarations for tests: every family ships at version 1.0.0 today, so no first-party chain
/// has more than one version. Each entry is read from its type's metadata the way <see cref="EfSchemaFamilyCatalog"/>
/// reads a declaration's chain, so a test chain is refused or accepted exactly as a declared one would be. Unless a test
/// says otherwise, a family declares one table, <see cref="Row"/>, with two content columns, so a step can move data
/// between them.
/// </summary>
internal static class SchemaChains
{
    public const string Content = "Content";
    public const string Notes = "Notes";

    /// <summary>A family that has only ever had <paramref name="version"/>.</summary>
    public static EfSchemaChain Single(string family, string version) => EfSchemaChain.For(Descriptor(family, version));

    /// <summary>A family at <paramref name="current"/> whose chain lists <paramref name="upcasters"/>, oldest first.</summary>
    public static EfSchemaChain Declare(string family, string current, params EfSchemaUpcasterDescriptor[] upcasters) =>
        EfSchemaChain.For(Descriptor(family, current, upcasters));

    public static EfSchemaFamilyDescriptor Descriptor(string family, string current, params EfSchemaUpcasterDescriptor[] upcasters) =>
        new(family, null, current, typeof(SchemaChains).Assembly)
        {
            Upcasters = upcasters,
            ContentColumns = [new EfSchemaColumn(typeof(Row), Content), new EfSchemaColumn(typeof(Row), Notes)]
        };

    public static EfSchemaUpcasterDescriptor Step<TUpcaster>() => EfSchemaFamilyCatalog.DescribeUpcaster(typeof(TUpcaster));

    /// <summary>A row of <see cref="Row"/>'s table holding exactly its two declared content columns.</summary>
    public static EfSchemaRowContent RowOf(string? content, string? notes = null) => new(typeof(Row), (Content, content), (Notes, notes));

    /// <summary>The table every family here declares, unless a test declares its own.</summary>
    public sealed class Row;
}
