using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Elsa.Persistence.EntityFramework.BindingDriftTests;

public sealed partial class EfRelationalProviderBindingDriftTests
{
    [Fact]
    public void Provider_types_in_separate_load_contexts_are_isolated_and_collectible()
    {
        var contexts = BindCollectibleProviders();
        for (var i = 0; i < 10 && contexts.Any(reference => reference.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.All(contexts, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] BindCollectibleProviders()
    {
        var contexts = new[] { new ProviderLoadContext(), new ProviderLoadContext() };
        try
        {
            var configured = new List<RelationalOptionsExtension>();
            for (var i = 0; i < contexts.Length; i++)
            {
                using var reflection = contexts[i].EnterContextualReflection();
                var builder = new DbContextOptionsBuilder();
                EfRelationalProviderBinding.UseSqlite(builder, $"Data Source=provider-{i}.db", $"history_{i}", $"Migrations{i}");
                var options = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
                Assert.Same(contexts[i], AssemblyLoadContext.GetLoadContext(options.GetType().Assembly));
                configured.Add(options);
            }
            Assert.NotSame(configured[0].GetType(), configured[1].GetType());
            for (var i = 0; i < configured.Count; i++)
            {
                Assert.Equal($"history_{i}", configured[i].MigrationsHistoryTableName);
                Assert.Equal($"Migrations{i}", configured[i].MigrationsAssembly);
            }
            return contexts.Select(context => new WeakReference(context)).ToArray();
        }
        finally
        {
            foreach (var context in contexts) context.Unload();
        }
    }

    private sealed class ProviderLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) =>
            name.Name == "Microsoft.EntityFrameworkCore.Sqlite"
                ? LoadFromAssemblyPath(typeof(SqliteDbContextOptionsBuilderExtensions).Assembly.Location)
                : Default.LoadFromAssemblyName(name);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Repeated_concurrent_binding_keeps_each_configuration_and_assembly_identity(string provider)
    {
        var engine = Engines.Single(x => x.Provider == provider);
        var assemblies = Enumerable.Range(0, 2).Select(_ => AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("SameMigrationName"), AssemblyBuilderAccess.RunAndCollect)).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() =>
        {
            var builder = new DbContextOptionsBuilder();
            var table = $"history_{i}";
            var schema = $"tenant_{i}";
            var connection = engine.ConnectionString.Replace("drift", $"tenant{i}", StringComparison.Ordinal);
            if (i % 3 == 0)
                EfRelationalProviderBinding.Use(builder, provider, connection, table, schema: schema);
            else if (i % 3 == 1)
                EfRelationalProviderBinding.Use(builder, provider, connection, table, $"Migrations{i}", schema);
            else
                EfRelationalProviderBinding.UseMigrationsFrom(builder, provider, connection, table, assemblies[i % 2], schema);

            var options = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
            Assert.Equal(connection, options.ConnectionString);
            Assert.Equal(table, options.MigrationsHistoryTableName);
            Assert.Equal(provider == "Sqlite" ? null : schema, options.MigrationsHistoryTableSchema);
            if (i % 3 == 2)
                Assert.Same(assemblies[i % 2], options.MigrationsAssemblyObject);
            else
            {
                Assert.Null(options.MigrationsAssemblyObject);
                Assert.Equal(i % 3 == 1 ? $"Migrations{i}" : null, options.MigrationsAssembly);
            }
        })));
    }

    [Fact]
    public void Warm_provider_binding_does_not_pay_expression_compilation_allocations_each_time()
    {
        static void BindOne() => EfRelationalProviderBinding.UseSqlite(
            new DbContextOptionsBuilder(), "Data Source=allocation.db", "history", "Migrations");
        BindOne();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) BindOne();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Includes EF option/extension construction; the baseline is 2.31 MB for these 100 calls.
        // This budget excludes repeated compilation while allowing the remaining provider allocations.
        Assert.InRange(allocated, 0, 2 * 1024 * 1024);
    }

    [Fact]
    public void Reusable_provider_preparation_does_not_retain_a_collectible_migrations_assembly()
    {
        var assembly = BindCollectibleAssembly();
        for (var i = 0; i < 10 && assembly.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(assembly.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BindCollectibleAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CollectibleMigrations"), AssemblyBuilderAccess.RunAndCollect);
        EfRelationalProviderBinding.UseMigrationsFrom(new DbContextOptionsBuilder(), "Sqlite", "Data Source=collectible.db", "history", assembly);
        return new WeakReference(assembly);
    }
}
