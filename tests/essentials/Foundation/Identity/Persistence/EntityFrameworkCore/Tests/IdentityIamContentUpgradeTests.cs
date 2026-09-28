using System.Reflection;
using System.Text.Json.Nodes;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 180, FR-009 and FR-014, for the Identity IAM rows the coordinators edit in place: a user's seven content columns
/// and a role's three. Every coordinator write of such a row upgrades it first, so its registries are read current and a
/// row it stamps is never partly in an older format.
/// </summary>
/// <remarks>
/// Identity IAM has only ever had one version, so its own chain cannot hold a row two versions behind. These tests drive
/// the upgrade the coordinators call over an Identity IAM chain 1, 2, 3 of their own, whose upcasters each append a
/// marker to every column they are given. The module keeps that overload internal and §2.23.3 allows no
/// InternalsVisibleTo, so it is reached by reflection. Which columns count as content comes from the model the module
/// maps, not from a list here, so a content column added to either entity without being added to the upgrade fails.
/// EfSchemaFamilyDeclarationGuardTests holds the other half: every Identity IAM content read goes through the chain, and
/// every in-place content write upgrades or stamps its row.
/// </remarks>
public sealed class IdentityIamContentUpgradeTests : IAsyncDisposable
{
    private static readonly EfSchemaChain TwoStepChain = EfSchemaChain.For(
        new EfSchemaFamilyDescriptor(IdentityIamEfModule.SchemaFamily, "Identity.Iam", "3", typeof(IdentityIamContentUpgradeTests).Assembly)
        {
            Upcasters = [new(typeof(MarkTwo), "1", "2"), new(typeof(MarkThree), "2", "3")]
        });

    private const string Id = "row-1";

    private readonly TemporarySqliteDatabase database = new("identity-upgrade");
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;

    public IdentityIamContentUpgradeTests()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore("tenant-a");
        services.AddIdentityIamEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database.ConnectionString });
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        scope = provider.CreateAsyncScope();
        Context.Database.EnsureCreated();
    }

    private IdentityIamDbContext Context => scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();

    public static TheoryData<Type> EditedInPlace() => [typeof(UserEntity), typeof(RoleEntity)];

    /// <summary>The columns the coordinators must carry forward, pinned so the model-derived set cannot silently shrink.</summary>
    [Fact]
    public void The_model_maps_every_registry_the_coordinators_edit_as_content()
    {
        Assert.Superset(
            new HashSet<string> { "RoleIdsJson", "DirectPermissionsJson", "ClaimIdsJson", "LoginIdsJson", "RoleLinkIdsJson", "TokenIdsJson", "TenantMembershipIdsJson" },
            ContentColumns(typeof(UserEntity)).ToHashSet());
        Assert.Superset(new HashSet<string> { "PermissionsJson", "ClaimIdsJson", "UserLinkIdsJson" }, ContentColumns(typeof(RoleEntity)).ToHashSet());
    }

    [Theory]
    [MemberData(nameof(EditedInPlace))]
    public async Task A_row_two_versions_behind_has_every_content_column_upcast_and_is_written_current(Type entityType)
    {
        var row = await InsertAsync(entityType, "1");

        Upgrade(row);

        // What a coordinator reads once it has upgraded the row.
        AssertColumns(row, entityType, column => [column, "v2", "v3"]);
        Assert.Equal("3", Stamp(row));

        // What its next write leaves in the database.
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        var stored = await Context.FindAsync(entityType, Id);
        AssertColumns(stored!, entityType, column => [column, "v2", "v3"]);
        Assert.Equal("3", Stamp(stored!));
    }

    /// <summary>FR-021: at the current version the upgrade runs no upcaster and leaves the row exactly as it was.</summary>
    [Theory]
    [MemberData(nameof(EditedInPlace))]
    public async Task A_row_at_the_current_version_is_left_exactly_as_it_was(Type entityType)
    {
        var row = await InsertAsync(entityType, "3");

        Upgrade(row);

        AssertColumns(row, entityType, column => [column]);
        Assert.Equal("3", Stamp(row));
        Assert.DoesNotContain(Context.ChangeTracker.Entries(), entry => entry.State != EntityState.Unchanged);
    }

    /// <summary>A row the chain cannot place is skew before any column is touched, so no write can save it half upgraded.</summary>
    [Theory]
    [MemberData(nameof(EditedInPlace))]
    public async Task A_row_the_chain_cannot_place_is_skew_and_is_left_untouched(Type entityType)
    {
        var row = await InsertAsync(entityType, "4");

        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => Upgrade(row));

        Assert.Equal(IdentityIamEfModule.SchemaFamily, skew.Family);
        AssertColumns(row, entityType, column => [column]);
        Assert.Equal("4", Stamp(row));
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
        await database.DisposeAsync();
    }

    private IEnumerable<string> ContentColumns(Type entityType) =>
        Context.Model.FindEntityType(entityType)!.GetProperties()
            .Where(property => property.ClrType == typeof(string) && property.Name.EndsWith("Json", StringComparison.Ordinal))
            .Select(property => property.Name);

    /// <summary>A tracked row stamped <paramref name="stamp"/> whose every content column holds a set naming the column.</summary>
    private async Task<object> InsertAsync(Type entityType, string stamp)
    {
        var row = Activator.CreateInstance(entityType)!;
        entityType.GetProperty("Id")!.SetValue(row, Id);
        entityType.GetProperty(nameof(UserEntity.SchemaVersion))!.SetValue(row, stamp);
        foreach (var column in ContentColumns(entityType))
            entityType.GetProperty(column)!.SetValue(row, new JsonArray(column).ToJsonString());
        Context.Add(row);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return (await Context.FindAsync(entityType, Id))!;
    }

    private void AssertColumns(object row, Type entityType, Func<string, string[]> expected)
    {
        var columns = ContentColumns(entityType).ToArray();
        Assert.NotEmpty(columns);
        foreach (var column in columns)
        {
            var values = JsonNode.Parse((string)entityType.GetProperty(column)!.GetValue(row)!)!.AsArray().Select(value => value!.GetValue<string>());
            Assert.True(expected(column).SequenceEqual(values), $"{entityType.Name}.{column} holds [{string.Join(", ", values)}].");
        }
    }

    private static string Stamp(object row) => (string)row.GetType().GetProperty(nameof(UserEntity.SchemaVersion))!.GetValue(row)!;

    /// <summary>The coordinators' upgrade, over <see cref="TwoStepChain"/>.</summary>
    private static void Upgrade(object row)
    {
        var support = typeof(IdentityIamDbContext).Assembly.GetType("Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores.EfIdentityStoreSupport", throwOnError: true)!;
        var upgrade = support.GetMethod("Upgrade", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, [row.GetType(), typeof(EfSchemaChain)])
                      ?? throw new MissingMethodException(support.FullName, $"Upgrade({row.GetType().Name}, EfSchemaChain)");
        upgrade.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [row, TwoStepChain], null);
    }

    private static string Mark(EfSchemaContent content, string marker)
    {
        var values = JsonNode.Parse(content.Value)!.AsArray();
        values.Add(marker);
        return values.ToJsonString();
    }

    private sealed class MarkTwo : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => Mark(content, "v2");
    }

    private sealed class MarkThree : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => Mark(content, "v3");
    }
}
