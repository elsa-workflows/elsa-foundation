namespace Elsa.Cluster.Fixtures.MigratingModule;

/// <summary>The names the fixture's module, feature and migrations are declared and composed by.</summary>
public static class MigratingModule
{
    /// <summary>The EF module, as its <c>[EfModule]</c> names it.</summary>
    public const string Name = "MigratingModuleFixture";

    /// <summary>The module's migrations-history name.</summary>
    public const string HistoryModuleName = "ElsaMigratingModuleFixture";

    /// <summary>The feature that binds the module's context and has its migrator apply or validate its migrations.</summary>
    public const string Feature = "MigratingModuleFixture";

    /// <summary>Where the feature answers with the version of the package its shell runs, and that shell's generation, as <c>1.0.0 generation 1</c>.</summary>
    public const string VersionPath = "/migrating-module-fixture/version";

    /// <summary>The table generation 1 creates.</summary>
    public const string Table = "MigratingModuleWidgets";

    /// <summary>The column generation 2 adds to <see cref="Table"/>.</summary>
    public const string AddedColumn = "Color";

    /// <summary>The migration generation 1 carries.</summary>
    public const string CreateWidgets = "20260930000001_CreateWidgets";

    /// <summary>The migration generation 2 adds.</summary>
    public const string AddColor = "20260930000002_AddColor";
}
