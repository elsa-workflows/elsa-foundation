using System.Reflection;
using System.Xml.Linq;
using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Elsa.Persistence.EntityFramework.BindingDriftTests;

/// <summary>
/// ADR 0073 keeps the shared EF policy package engine-free, so <see cref="EfRelationalProviderBinding"/> reaches each
/// provider's <c>Use*</c> extension by type and method name. No compile then notices when a provider upgrade renames,
/// moves, or reshapes that extension: the binding would first fail in a host, when a context is configured. This
/// project references all four engines at the versions <c>Directory.Packages.props</c> pins and binds against them,
/// so such an upgrade fails CI instead.
/// </summary>
public sealed class EfRelationalProviderBindingDriftTests
{
    private const string HistoryTable = "__EFMigrationsHistory_Drift";
    private const string MigrationsAssembly = "Elsa.Drift.Migrations";

    private sealed record Engine(string Provider, string Package, string ProviderName, string ConnectionString);

    /// <summary>Connection strings are parsed but never opened; binding a provider does not reach a server.</summary>
    private static readonly Engine[] Engines =
    [
        new("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite", EfProviderNames.Sqlite, "Data Source=drift.db"),
        new("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer", EfProviderNames.SqlServer, "Server=localhost;Database=drift;TrustServerCertificate=True"),
        new("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL", EfProviderNames.PostgreSql, "Host=localhost;Database=drift;Username=drift;Password=drift"),
        new("MySql", "MySql.EntityFrameworkCore", EfProviderNames.MySql, "Server=localhost;Database=drift;User Id=drift;Password=drift")
    ];

    public static TheoryData<string> Providers => Data(engine => engine.Provider);

    public static TheoryData<string> Packages => Data(engine => engine.Package);

    [Theory]
    [MemberData(nameof(Providers))]
    public void The_binding_resolves_against_the_pinned_provider_package(string provider)
    {
        var engine = Engines.Single(candidate => candidate.Provider == provider);

        Assert.Equal(engine.Package, EfRelationalProviderBinding.ProviderPackageId(provider));
        Assert.Null(EfRelationalProviderBinding.DescribeBindingFailure(provider));

        var relational = Bind(engine, builder => EfRelationalProviderBinding.Use(builder, provider, engine.ConnectionString, HistoryTable, MigrationsAssembly));
        Assert.Equal(MigrationsAssembly, relational.MigrationsAssembly);
    }

    /// <summary>
    /// The path every module binding takes (spec 183, FR-021, amended 2026-09-29): the options builder's
    /// <c>MigrationsAssembly(Assembly)</c> overload, which a provider upgrade could drop or reshape exactly as it could the
    /// name overload.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void The_binding_by_assembly_resolves_against_the_pinned_provider_package(string provider)
    {
        var engine = Engines.Single(candidate => candidate.Provider == provider);
        var migrations = typeof(EfRelationalProviderBindingDriftTests).Assembly;

        var relational = Bind(engine, builder => EfRelationalProviderBinding.UseMigrationsFrom(builder, provider, engine.ConnectionString, HistoryTable, migrations));
        MigrationsAssemblyAssert.BoundByTheAssemblyItself(migrations, relational);
    }

    /// <summary>
    /// #2209: the SQLite binding opens its connections one at a time per connection string, because Microsoft.Data.Sqlite's
    /// pool can lend one connection to two opens that check out at once. The race is that library's, so no other engine's
    /// opens wait at the gate.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void Only_the_SQLite_binding_gates_its_connection_opens(string provider)
    {
        var engine = Engines.Single(candidate => candidate.Provider == provider);
        var builder = new DbContextOptionsBuilder();

        EfRelationalProviderBinding.Use(builder, provider, engine.ConnectionString, HistoryTable);

        var gates = builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?.OfType<IDbConnectionInterceptor>().Count() ?? 0;
        Assert.Equal(engine.ProviderName == EfProviderNames.Sqlite ? 1 : 0, gates);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void The_bound_engine_is_the_version_Directory_Packages_props_pins(string package)
    {
        var informational = Assembly.Load(package).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? throw new InvalidOperationException($"{package} carries no informational version.");

        Assert.Equal(NormalizeVersion(PinnedVersions()[package]), NormalizeVersion(informational));
    }

    /// <summary>Binds <paramref name="engine"/> with <paramref name="bind"/>, and checks what every binding must configure.</summary>
    private static RelationalOptionsExtension Bind(Engine engine, Action<DbContextOptionsBuilder> bind)
    {
        var builder = new DbContextOptionsBuilder();
        bind(builder);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
        Assert.Equal(engine.ConnectionString, relational.ConnectionString);
        Assert.Equal(HistoryTable, relational.MigrationsHistoryTableName);

        using var context = new DbContext(builder.Options);
        Assert.Equal(engine.ProviderName, context.Database.ProviderName);
        Assert.Equal(engine.ProviderName, EfRelationalProviderBinding.ExpectedProviderName(engine.Provider));
        return relational;
    }

    private static TheoryData<string> Data(Func<Engine, string> select)
    {
        var data = new TheoryData<string>();
        foreach (var engine in Engines)
            data.Add(select(engine));
        return data;
    }

    /// <summary>
    /// Puts NuGet's three-part package version and the assembly's four-part informational version on the same
    /// footing: drop build metadata, drop trailing zero parts past the third, and keep any prerelease tag.
    /// </summary>
    private static string NormalizeVersion(string version)
    {
        var release = version.Split('+')[0];
        var tagStart = release.IndexOf('-');
        var core = tagStart < 0 ? release : release[..tagStart];
        var tag = tagStart < 0 ? "" : release[tagStart..];
        var parts = core.Split('.').ToList();
        while (parts.Count > 3 && parts[^1] == "0")
            parts.RemoveAt(parts.Count - 1);
        return string.Join('.', parts) + tag;
    }

    private static Dictionary<string, string> PinnedVersions() =>
        XDocument.Load(Path.Join(RepoRoot(), "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Where(element => element.Attribute("Include") is not null && element.Attribute("Version") is not null)
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.OrdinalIgnoreCase);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Directory.Packages.props was not found above the test output directory.");
    }
}
