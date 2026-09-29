using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Every schema family's content declaration (<c>[EfSchemaContent]</c>, <c>[EfSchemaIntegrity]</c>; spec 180, FR-009
/// and FR-008) against the model its module's context actually builds, on every provider: a declared column is a column
/// of a stamped table in that model, and every document column of a stamped table is declared, content or integrity, by
/// one family of the module that maps it. Nothing falls through, so the guards that read the declaration
/// (<c>EfSchemaFamilyContentReadGuardTests</c>, <c>EfSchemaFamilyRestampGuardTests</c>) see every column that needs a
/// chain on read and a stamp on write.
/// </summary>
/// <remarks>
/// A document column is one a store keeps a document in: a string column whose name ends in <c>Json</c> or is
/// <c>Content</c> or <c>Payload</c>, a payload column (<see cref="EfPayloadColumns"/>), or a domain value a converter
/// stores as a string, which EF deserializes as it materializes the row.
/// </remarks>
public sealed class EfSchemaContentDeclarationTests
{
    private static readonly Assembly[] Declaring = [.. ModuleContextCatalog.Modules, typeof(EfSchemaFinalization).Assembly];
    private static readonly IReadOnlyList<EfSchemaFamilyDescriptor> Families = EfSchemaFamilyCatalog.Discover(Declaring);

    public static TheoryData<string> Providers() => [.. ModuleContextCatalog.Providers];

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_document_column_of_a_stamped_table_is_declared_and_every_declared_column_is_in_the_model(string provider)
    {
        var violations = new List<string>();
        var found = new HashSet<(Type, string)>();
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            violations.AddRange(ContentDeclarationRules.Violations(context.Model, Owned(type.Assembly), type.Name));
            found.UnionWith(ContentDeclarationRules.Mapped(context.Model, Families));
        }

        violations.AddRange(ContentDeclarationRules.Unmapped(Families, found, $"{provider} module context"));

        Assert.True(violations.Count == 0,
            "Every document column of a stamped table is declared content ([EfSchemaContent]) or integrity ([EfSchemaIntegrity]) by one " +
            "family of the module that maps it, and every declared column is a column of a stamped table (spec 180, FR-009 and FR-008):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct().Order(StringComparer.Ordinal)));
    }

    /// <summary>The rule passes vacuously if the catalog stops reading declarations, so pin floors on what it reads.</summary>
    [Fact]
    public void Every_first_party_family_with_a_document_column_declares_it()
    {
        var content = Families.SelectMany(family => family.ContentColumns).ToArray();
        Assert.True(content.Length >= 100, $"Expected the tree's content columns to be declared; found {content.Length}.");
        Assert.Contains(content, column => column.Entity.Name == "BookmarkStateEntity" && column.Name == "PayloadJson");
        Assert.Contains(content, column => column.Entity.Name == "RuntimeCheckpointCommitEntity" && column.Name == "PendingPostCommitWorkIdsJson");
        Assert.Contains(content, column => column.Entity.Name == "DesignOperationEntity" && column.Name == "ResultJson");
        Assert.Contains(content, column => column.Entity.Name == "ActivityUpgradePlanRecord" && column.Name == "PlanJson");
        Assert.Contains(Families.SelectMany(family => family.IntegrityColumns), column => column.Name == "ContentAuthorityCanonicalJson");
    }

    [Theory]
    [MemberData(nameof(ViolatingDeclarations))]
    public void Detector_flags_an_undeclared_document_column_and_a_declared_column_the_model_lacks(string name, string[] content, string expected)
    {
        var family = Family(content);
        var model = SyntheticModel();

        var violations = ContentDeclarationRules.Violations(model, [family], "SyntheticDbContext")
            .Concat(ContentDeclarationRules.Unmapped([family], ContentDeclarationRules.Mapped(model, [family]).ToHashSet(), "module context"))
            .ToArray();

        Assert.True(violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The detector missed '{name}'. It reported: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Detector_accepts_a_model_whose_every_document_column_is_declared() =>
        Assert.Empty(ContentDeclarationRules.Violations(
            SyntheticModel(),
            [Family([nameof(SyntheticRow.ContentJson), nameof(SyntheticRow.Payload), nameof(SyntheticRow.Settings)])],
            "SyntheticDbContext"));

    public static TheoryData<string, string[], string> ViolatingDeclarations() => new()
    {
        { "an undeclared Json-suffixed column", [nameof(SyntheticRow.Payload), nameof(SyntheticRow.Settings)], "'SyntheticRow.ContentJson' of stamped table 'synthetic_rows' is a document column no family declares" },
        { "an undeclared column named Payload", [nameof(SyntheticRow.ContentJson), nameof(SyntheticRow.Settings)], "'SyntheticRow.Payload'" },
        { "an undeclared converted domain value", [nameof(SyntheticRow.ContentJson), nameof(SyntheticRow.Payload)], "'SyntheticRow.Settings'" },
        { "a declared column the model does not map", [nameof(SyntheticRow.ContentJson), nameof(SyntheticRow.Payload), nameof(SyntheticRow.Settings), "Missing"], "declares 'SyntheticRow.Missing'" }
    };

    /// <summary>The families a module's context may declare its columns under: its own and the shared ones.</summary>
    private static IReadOnlyList<EfSchemaFamilyDescriptor> Owned(Assembly module) =>
        Families.Where(family => family.Assembly == module || family.Module is null).ToArray();

    private static EfSchemaFamilyDescriptor Family(string[] content) =>
        new("Synthetic", "Synthetic", "1", typeof(SyntheticRow).Assembly)
        {
            ContentColumns = content.Select(column => new EfSchemaColumn(typeof(SyntheticRow), column)).ToArray()
        };

    private static IReadOnlyModel SyntheticModel()
    {
        var builder = new ModelBuilder(SqliteConventionSetBuilder.Build());
        builder.Entity<SyntheticRow>(entity =>
        {
            entity.ToTable("synthetic_rows");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Settings).HasConversion(settings => settings.Name, name => new SyntheticSettings(name));
        });
        builder.Entity<UnstampedRow>(entity =>
        {
            entity.ToTable("unstamped_rows");
            entity.HasKey(row => row.Id);
        });
        return builder.FinalizeModel();
    }

    private sealed class SyntheticRow
    {
        public string Id { get; set; } = "";
        public string SchemaVersion { get; set; } = "";
        public string Name { get; set; } = "";
        public string ContentJson { get; set; } = "";
        public string Payload { get; set; } = "";
        public SyntheticSettings Settings { get; set; } = new("");
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>No stamp, so its document column belongs to no family and is not judged.</summary>
    private sealed class UnstampedRow
    {
        public string Id { get; set; } = "";
        public string OtherJson { get; set; } = "";
    }

    private sealed record SyntheticSettings(string Name);
}

/// <summary>The content declaration rules over one model, so the detector can be pinned on a synthetic model.</summary>
internal static class ContentDeclarationRules
{
    private static readonly HashSet<Type> Scalars =
    [
        typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid),
        typeof(DateOnly), typeof(TimeOnly), typeof(byte[])
    ];

    /// <summary>
    /// Every document column of a stamped table in <paramref name="model"/> that no family of <paramref name="families"/>
    /// declares, and every column declared twice across them.
    /// </summary>
    public static IEnumerable<string> Violations(IReadOnlyModel model, IReadOnlyList<EfSchemaFamilyDescriptor> families, string context)
    {
        var declared = families
            .SelectMany(family => Declared(family).Select(column => (Family: family.Name, Column: column)))
            .ToLookup(item => item.Column, item => item.Family);
        foreach (var entity in model.GetEntityTypes().Where(IsStamped))
        {
            foreach (var property in entity.GetProperties().Where(IsDocument))
            {
                var owners = declared[(entity.ClrType, property.Name)].Distinct(StringComparer.Ordinal).ToArray();
                if (owners.Length == 0)
                    yield return $"{context}: '{entity.ClrType.Name}.{property.Name}' of stamped table '{entity.GetTableName()}' is a document column no family " +
                                 "declares; declare it [EfSchemaContent], or [EfSchemaIntegrity] with the reason it is compared as stored bytes.";
                else if (owners.Length > 1)
                    yield return $"{context}: '{entity.ClrType.Name}.{property.Name}' is declared by {owners.Length} families ({string.Join(", ", owners)}); a table belongs to one.";
            }
        }
    }

    /// <summary>The declared columns <paramref name="model"/> maps as columns of stamped tables.</summary>
    public static IEnumerable<(Type Entity, string Name)> Mapped(IReadOnlyModel model, IEnumerable<EfSchemaFamilyDescriptor> families) =>
        families.SelectMany(Declared)
            .Where(column => model.FindEntityType(column.Entity) is { } entity && IsStamped(entity) && entity.FindProperty(column.Name) is not null);

    /// <summary>Every column <paramref name="families"/> declare that <paramref name="mapped"/> does not hold.</summary>
    public static IEnumerable<string> Unmapped(IEnumerable<EfSchemaFamilyDescriptor> families, IReadOnlySet<(Type Entity, string Name)> mapped, string where) =>
        families.SelectMany(family => Declared(family)
            .Where(column => !mapped.Contains(column))
            .Select(column => $"'{family.Name}' declares '{column.Entity.Name}.{column.Name}', which no {where} maps."));

    public static IEnumerable<(Type Entity, string Name)> Declared(EfSchemaFamilyDescriptor family) =>
        family.ContentColumns.Select(column => (column.Entity, column.Name))
            .Concat(family.IntegrityColumns.Select(column => (column.Entity, column.Name)));

    private static bool IsStamped(IReadOnlyEntityType entity) =>
        entity.GetTableName() is not null && entity.FindProperty(EfSchemaVersion.ColumnName) is not null;

    private static bool IsDocument(IReadOnlyProperty property)
    {
        var converter = property.GetValueConverter();
        if ((converter?.ProviderClrType ?? property.ClrType) != typeof(string))
            return false;
        return property.Name.EndsWith("Json", StringComparison.Ordinal) ||
               property.Name is "Content" or "Payload" ||
               EfPayloadColumns.IsPayloadColumn(property) ||
               (converter is not null && !IsScalar(property.ClrType));
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || Scalars.Contains(underlying);
    }
}
