using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Cluster.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Samples.Nuplane.Notes.Tests;

/// <summary>
/// The sample is one project that builds as two releases (<c>-p:DemoVersion=1</c> and <c>2</c>). This project is compiled
/// against release 1.1.0 and builds release 1.0.0 beside it, so both builds are exercised on every run: each declares
/// the schema family at its own version, carries the migrations it is meant to, and the second adds exactly what the
/// demo says it adds.
/// </summary>
public sealed class NotesReleaseTests : IDisposable
{
    private readonly NotesRelease release1 = NotesRelease.One();
    private readonly NotesRelease release2 = NotesRelease.Two();

    public void Dispose() => release1.Dispose();

    [Fact]
    public void Release_1_0_0_is_a_single_version_family_with_the_initial_migration_only()
    {
        var assembly = release1.Assembly;

        Assert.Equal(new Version(1, 0, 0, 0), assembly.GetName().Version);
        var family = Family(assembly);
        Assert.Equal("1.0.0", family.CurrentVersion);
        Assert.Empty(family.Upcasters);
        Assert.Empty(family.ContentColumns);
        Assert.Null(family.Rewriter);
        Assert.Equal(["Initial", "Initial"], MigrationNames(assembly));
        Assert.Null(assembly.GetType("Elsa.Samples.Nuplane.Notes.NotesWithTagsFeature"));
        Assert.DoesNotContain(assembly.GetType("Elsa.Samples.Nuplane.Notes.NoteRecord")!.GetProperties(), property => property.Name == "TagsJson");
    }

    [Fact]
    public void Release_1_1_0_is_family_version_2_0_0_reading_1_0_0_through_one_upcaster_and_adds_the_tags_migration()
    {
        var assembly = release2.Assembly;

        Assert.Equal(new Version(1, 1, 0, 0), assembly.GetName().Version);
        var family = Family(assembly);
        Assert.Equal("2.0.0", family.CurrentVersion);
        Assert.Equal(["1.0.0", "2.0.0"], family.ReadableVersions);
        Assert.Equal(typeof(NotesOneToTwo), Assert.Single(family.Upcasters).Type);
        Assert.Equal([nameof(NoteRecord.TagsJson)], family.ContentColumns.Select(column => column.Name));
        Assert.Equal(typeof(NotesRewriter), family.Rewriter);
        Assert.Equal(["AddTags", "AddTags", "Initial", "Initial"], MigrationNames(assembly));
    }

    [Fact]
    public void The_tags_feature_needs_the_version_release_1_1_0_adds()
    {
        var requirement = Assert.Single(SchemaVersionRequirement.DeclaredBy(typeof(NotesWithTagsFeature)));

        Assert.Equal((NotesModule.Family, NotesModule.TagsVersion, false), (requirement.Family, requirement.Version, requirement.RequiresCompleteness));
    }

    private static EfSchemaFamilyDescriptor Family(Assembly assembly) =>
        Assert.Single(EfSchemaFamilyCatalog.Discover([assembly]), family => family.Name == NotesModule.Family);

    /// <summary>The names of the migrations an assembly carries (one per provider), sorted, so a comparison does not depend on reflection order.</summary>
    private static string[] MigrationNames(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<MigrationAttribute>() is not null && type.GetCustomAttribute<DbContextAttribute>() is not null)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)];
}
