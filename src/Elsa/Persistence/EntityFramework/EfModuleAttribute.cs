namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The single, discoverable declaration of an EF module — first-party or third-party alike: its
/// canonical <see cref="Name"/>, its base derived-context <see cref="ContextType"/>, the per-provider
/// derived contexts a host selects by provider name, its frozen <see cref="HistoryModule"/> name, and
/// what it depends on and runs after migrating. <see cref="EfModuleCatalog.Discover"/> is the one place
/// that reads it.
/// </summary>
/// <remarks>
/// Assembly-level and constant-argument-only by design (ADR 0076 D2): readable as metadata
/// (<see cref="System.Reflection.CustomAttributeData"/>) without running any module code, which is what
/// a package loader needs to decide whether a module may activate before it loads. <see cref="AllowMultiple"/>
/// covers the two assemblies that carry two contexts today (Identity, Runtime.Distributed). A <c>null</c>
/// provider property means that provider is unsupported for this module — see
/// <see cref="EfModuleDescriptor.RequireProviderContext"/> — not a binding to chase down.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EfModuleAttribute(string name, Type contextType) : Attribute
{
    /// <summary>The canonical <c>--modules</c> name: matched case-insensitively, unique across a host's closure.</summary>
    public string Name { get; } = name;

    /// <summary>The module's base context; every provider-derived context below derives from it.</summary>
    public Type ContextType { get; } = contextType;

    /// <summary>
    /// The frozen migrations-history module name — the same string the module's own
    /// <c>&lt;Module&gt;EfModule.HistoryModuleName</c> constant carries. A guard test fails when the two disagree.
    /// </summary>
    public string? HistoryModule { get; init; }

    /// <summary>The SQLite-derived context, or <c>null</c> when this module does not support SQLite.</summary>
    public Type? Sqlite { get; init; }

    /// <summary>The SQL Server-derived context, or <c>null</c> when this module does not support SQL Server.</summary>
    public Type? SqlServer { get; init; }

    /// <summary>The PostgreSQL-derived context, or <c>null</c> when this module does not support PostgreSQL.</summary>
    public Type? PostgreSql { get; init; }

    /// <summary>The MySQL-derived context, or <c>null</c> when this module does not support MySQL.</summary>
    public Type? MySql { get; init; }

    /// <summary>Other canonical module names this module's migrations must apply after. Empty for every first-party module today.</summary>
    public string[] DependsOn { get; init; } = [];

    /// <summary>
    /// Named, audited, never-auto-run actions this module declares for after its migrations apply. Plain
    /// <see cref="Type"/>s for now: <c>IEfPostMigrationAction</c> does not exist yet, and no first-party
    /// module sets this until it does.
    /// </summary>
    public Type[] PostMigration { get; init; } = [];

    /// <summary>An operator-facing name, when the canonical <see cref="Name"/> alone would not read clearly.</summary>
    public string? DisplayName { get; init; }
}
