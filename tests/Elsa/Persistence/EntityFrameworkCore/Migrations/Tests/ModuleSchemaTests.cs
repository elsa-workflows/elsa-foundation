using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// What a configured schema does to every module context, without a database. The provider legs that prove the
/// same migrations then apply into that schema live in the ProviderTests project beside this one.
/// </summary>
public sealed class ModuleSchemaTests : IDisposable
{
    private const string Schema = "elsa_alt";
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"elsa-ef-schema-{Guid.NewGuid():N}.db");

    /// <summary>
    /// The providers that have a schema inside a database. SQLite has no schemas, and MySQL calls a database one,
    /// so both are settled by <c>EfSchemaTests</c> instead.
    /// </summary>
    public static TheoryData<string> SchemaProviders() =>
        [.. ModuleContextCatalog.Providers.Where(provider => provider is not ("Sqlite" or "MySql"))];

    [Theory]
    [MemberData(nameof(SchemaProviders))]
    public void Every_module_context_puts_its_model_and_its_history_table_in_a_configured_schema(string provider)
    {
        var contexts = ModuleContextCatalog.Contexts(provider);
        Assert.NotEmpty(contexts);
        foreach (var type in contexts)
        {
            using var context = Create(type, provider, Schema);
            Assert.Equal(Schema, context.Model.GetDefaultSchema());
            Assert.Equal(Schema, Relational(context).MigrationsHistoryTableSchema);
            Assert.All(context.Model.GetEntityTypes(), entity => Assert.Equal(Schema, entity.GetSchema()));
        }
    }

    [Fact]
    public void Sqlite_ignores_a_configured_schema_instead_of_refusing_to_start()
    {
        foreach (var type in ModuleContextCatalog.Contexts("Sqlite"))
        {
            using var context = Create(type, "Sqlite", Schema);
            Assert.Null(context.Model.GetDefaultSchema());
            Assert.Null(Relational(context).MigrationsHistoryTableSchema);
        }
    }

    [Fact]
    public void MySql_module_contexts_are_unchanged_because_a_schema_never_reaches_them()
    {
        foreach (var type in ModuleContextCatalog.Contexts("MySql"))
        {
            using var context = Create(type, "MySql", schema: null);
            Assert.Null(context.Model.GetDefaultSchema());
            Assert.Null(Relational(context).MigrationsHistoryTableSchema);
        }
    }

    [Theory]
    [MemberData(nameof(SchemaProviders))]
    public void Configuring_no_schema_leaves_every_module_context_exactly_as_it_was(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = Create(type, provider, schema: null);
            Assert.Null(context.Model.GetDefaultSchema());
            Assert.Null(Relational(context).MigrationsHistoryTableSchema);
            Assert.IsNotType<EfSchemaMigrationsAssembly>(context.GetService<IMigrationsAssembly>());
        }
    }

    /// <summary>
    /// The claim the whole option rests on: migrations scaffolded with no schema carry one at apply time. An
    /// operation shape nobody qualified would create a table in the provider's default schema while the model
    /// reads the configured one.
    /// </summary>
    [Theory]
    [MemberData(nameof(SchemaProviders))]
    public void Every_scaffolded_migration_operation_is_qualified_with_the_configured_schema(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = Create(type, provider, Schema);
            var migrations = context.GetService<IMigrationsAssembly>();
            Assert.IsType<EfSchemaMigrationsAssembly>(migrations);
            Assert.NotEmpty(migrations.Migrations);
            foreach (var (id, migrationClass) in migrations.Migrations)
            {
                var migration = migrations.CreateMigration(migrationClass, EfRelationalProviderBinding.ExpectedProviderName(provider));
                foreach (var unqualified in Unqualified(migration.UpOperations).Concat(Unqualified(migration.DownOperations)))
                    Assert.Fail($"{type.Name} migration {id} leaves {unqualified} outside schema {Schema}.");
            }
        }
    }

    /// <summary>
    /// EF caches one model per context type. Two contexts of one type bound to different schemas therefore have to
    /// end up with different internal service providers, or the second silently writes to the first one's schema.
    /// </summary>
    [Fact]
    public void One_context_type_bound_to_two_schemas_keeps_two_models()
    {
        var type = ModuleContextCatalog.Contexts("PostgreSql").First();
        using var first = Create(type, "PostgreSql", "elsa_one");
        using var second = Create(type, "PostgreSql", "elsa_two");
        using var none = Create(type, "PostgreSql", schema: null);

        Assert.Equal("elsa_one", first.Model.GetDefaultSchema());
        Assert.Equal("elsa_two", second.Model.GetDefaultSchema());
        Assert.Null(none.Model.GetDefaultSchema());
    }

    /// <summary>
    /// What makes pooling safe to offer on every module: a pooled context is reused across scopes, so anything it
    /// captured at construction would leak from one request into the next. Options are all any of them takes.
    /// </summary>
    [Fact]
    public void Every_module_context_is_constructed_from_its_options_alone()
    {
        foreach (var type in ModuleContextCatalog.AllContexts())
        {
            var constructor = Assert.Single(type.GetConstructors());
            var parameter = Assert.Single(constructor.GetParameters());
            Assert.Equal(typeof(DbContextOptions<>).MakeGenericType(type), parameter.ParameterType);
            Assert.Empty(type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
        }
    }

    /// <summary>
    /// Pooling is only worth an option if it actually reuses instances, and only safe because a module context
    /// carries nothing across that reuse. Two sequential scopes are what a pool hands the same instance to.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pooling_decides_whether_two_scopes_share_one_context_instance(bool pooling)
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={databasePath};Pooling=False",
            Pooling = pooling
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        ActivitiesDesignDbContext first;
        await using (var scope = provider.CreateAsyncScope())
        {
            first = scope.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>();
            await EfDatabaseMigrator.ApplyAsync(first, EfProviderNames.Sqlite, EfMigratePolicy.AutoMigrate);
        }

        await using var second = provider.CreateAsyncScope();
        var reused = second.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>();
        Assert.Equal(pooling, ReferenceEquals(first, reused));
        Assert.Equal(0, await reused.ActivityDefinitions.CountAsync());
    }

    public void Dispose()
    {
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }

    private static DbContext Create(Type context, string provider, string? schema) =>
        ModuleContextCatalog.Create(context, ModuleContextCatalog.PlaceholderConnection(provider), schema: schema);

    private static RelationalOptionsExtension Relational(DbContext context) =>
        context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single();

    private static IEnumerable<string> Unqualified(IEnumerable<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations) =>
        operations
            .SelectMany(operation => operation.GetType()
                .GetProperties()
                .Where(property => property.PropertyType == typeof(string) && property.Name.EndsWith("Schema", StringComparison.Ordinal))
                .Where(property => property.GetValue(operation) is null)
                .Select(property => $"{operation.GetType().Name}.{property.Name}"));
}
