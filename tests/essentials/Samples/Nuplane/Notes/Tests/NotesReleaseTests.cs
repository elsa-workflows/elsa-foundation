using System.Reflection;
using System.Runtime.Loader;
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
    private static readonly string Release1Path = Path.Join(AppContext.BaseDirectory, "notes-v1", "Elsa.Samples.Nuplane.Notes.dll");

    private readonly AssemblyLoadContext release1Context = new("notes-release-1", isCollectible: true);
    private readonly Assembly release1;

    public NotesReleaseTests() => release1 = release1Context.LoadFromAssemblyPath(Release1Path);

    public void Dispose() => release1Context.Unload();

    private static Assembly Release2 => typeof(NotesModule).Assembly;

    [Fact]
    public void Release_1_0_0_is_a_single_version_family_with_the_initial_migration_only()
    {
        Assert.Equal(new Version(1, 0, 0, 0), release1.GetName().Version);
        var family = Family(release1);
        Assert.Equal("1.0.0", family.CurrentVersion);
        Assert.Empty(family.Upcasters);
        Assert.Empty(family.ContentColumns);
        Assert.Equal(["Initial", "Initial"], MigrationNames(release1));
        Assert.Null(release1.GetType("Elsa.Samples.Nuplane.Notes.NotesWithTagsFeature"));
        Assert.DoesNotContain(release1.GetType("Elsa.Samples.Nuplane.Notes.NoteRecord")!.GetProperties(), property => property.Name == "TagsJson");
    }

    [Fact]
    public void Release_1_1_0_is_family_version_2_0_0_reading_1_0_0_through_one_upcaster_and_adds_the_tags_migration()
    {
        Assert.Equal(new Version(1, 1, 0, 0), Release2.GetName().Version);
        var family = Family(Release2);
        Assert.Equal("2.0.0", family.CurrentVersion);
        Assert.Equal(["1.0.0", "2.0.0"], family.ReadableVersions);
        Assert.Equal(typeof(NotesOneToTwo), Assert.Single(family.Upcasters).Type);
        Assert.Equal([nameof(NoteRecord.TagsJson)], family.ContentColumns.Select(column => column.Name));
        Assert.Equal(["AddTags", "AddTags", "Initial", "Initial"], MigrationNames(Release2).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_tags_feature_needs_the_version_release_1_1_0_adds()
    {
        var requirement = Assert.Single(SchemaVersionRequirement.DeclaredBy(typeof(NotesWithTagsFeature)));

        Assert.Equal((NotesModule.Family, NotesModule.TagsVersion, false), (requirement.Family, requirement.Version, requirement.RequiresCompleteness));
    }

    private static EfSchemaFamilyDescriptor Family(Assembly assembly) =>
        Assert.Single(EfSchemaFamilyCatalog.Discover([assembly]), family => family.Name == NotesModule.Family);

    private static string[] MigrationNames(Assembly assembly) =>
        [.. assembly.GetTypes().Where(type => type.GetCustomAttribute<MigrationAttribute>() is not null && type.GetCustomAttribute<DbContextAttribute>() is not null).Select(type => type.Name)];
}
