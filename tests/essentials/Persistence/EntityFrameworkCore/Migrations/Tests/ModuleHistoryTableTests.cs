using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Guards the migrations-history table names a deployed host actually uses. The rest of this suite derives
/// its own name per context so modules can share one test database; these read what each module declares,
/// which is what `EfModuleBinding` passes to the provider and what `Validate` reads on startup.
/// </summary>
public sealed class ModuleHistoryTableTests
{
    /// <summary>
    /// Coverage, not just non-emptiness: reflection that silently found two modules would pass every other
    /// assertion here. Each module assembly must contribute at least one declared history table.
    /// </summary>
    [Fact]
    public void Every_module_assembly_declares_a_history_table()
    {
        var declared = ModuleContextCatalog.DeclaredHistoryTables();
        Assert.All(declared, row => Assert.False(string.IsNullOrWhiteSpace(row.Table), $"{row.Module} declares an empty history table."));

        var silent = ModuleContextCatalog.Modules
            .Where(assembly => declared.All(row => row.Assembly != assembly))
            .Select(assembly => assembly.GetName().Name)
            .ToArray();

        Assert.True(silent.Length == 0, $"Module assemblies declaring no history table: {string.Join(", ", silent)}.");
    }

    /// <summary>
    /// Modules are designed to share one database, so two modules on the same history table would each read
    /// the other's applied migrations as its own and skip work that was never done. Nothing else checks this.
    /// </summary>
    [Fact]
    public void No_two_modules_share_a_history_table()
    {
        var collisions = ModuleContextCatalog.DeclaredHistoryTables()
            .GroupBy(row => row.Table, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} <- {string.Join(", ", group.Select(row => row.Module))}")
            .ToArray();

        Assert.True(collisions.Length == 0, $"Modules sharing a migrations-history table: {string.Join("; ", collisions)}.");
    }

    /// <summary>
    /// Every module builds its table through <see cref="EfMigrationsHistory.TableName"/> from an
    /// <c>Elsa…</c> module name. A hand-written literal skips that validation and drifts in style; two
    /// modules did exactly that before this guard existed.
    /// </summary>
    [Fact]
    public void Every_history_table_is_built_from_an_elsa_module_name()
    {
        Assert.All(ModuleContextCatalog.DeclaredHistoryTables(), row =>
        {
            Assert.StartsWith(EfMigrationsHistory.TablePrefix, row.Table, StringComparison.Ordinal);
            var module = row.Table[EfMigrationsHistory.TablePrefix.Length..];
            Assert.Equal(row.Table, EfMigrationsHistory.TableName(module));
            Assert.StartsWith("Elsa", module, StringComparison.Ordinal);
        });
    }
}
