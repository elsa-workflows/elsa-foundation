using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Invokes the host's <c>UseSqlite</c>, <c>UseSqlServer</c>, <c>UseNpgsql</c>, or <c>UseMySQL</c> extension without
/// this policy package referencing a provider engine. The host (or test/tooling project) must
/// reference exactly one provider package so the extension type can be loaded.
/// </summary>
public static class EfRelationalProviderBinding
{
    /// <summary>
    /// One provider's reflection target: the extension type that carries the <c>Use*</c> overload, the overload's
    /// name, and the NuGet package a host adds to supply both. For all four default packs the engine assembly is
    /// named after its package, so <see cref="PackageId"/> also names the assembly to load. Binding and validation
    /// resolve through the same descriptor, so a validated provider is one that will configure.
    /// </summary>
    private sealed record ProviderEngine(string ExtensionTypeName, string MethodName, string PackageId)
    {
        public string AssemblyQualifiedTypeName => $"{ExtensionTypeName}, {PackageId}";
    }

    private static readonly ProviderEngine SqliteEngine = new(
        "Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions",
        "UseSqlite",
        "Microsoft.EntityFrameworkCore.Sqlite");

    private static readonly ProviderEngine SqlServerEngine = new(
        "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions",
        "UseSqlServer",
        "Microsoft.EntityFrameworkCore.SqlServer");

    private static readonly ProviderEngine PostgreSqlEngine = new(
        "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions",
        "UseNpgsql",
        "Npgsql.EntityFrameworkCore.PostgreSQL");

    private static readonly ProviderEngine MySqlEngine = new(
        "Microsoft.EntityFrameworkCore.MySQLDbContextOptionsExtensions",
        "UseMySQL",
        "MySql.EntityFrameworkCore");

    public static void UseSqlite(DbContextOptionsBuilder builder, string connectionString, string historyTableName, string? migrationsAssembly = null) =>
        Use(builder, SqliteEngine, connectionString, historyTableName, migrationsAssembly);

    public static void UseSqlServer(DbContextOptionsBuilder builder, string connectionString, string historyTableName, string? migrationsAssembly = null) =>
        Use(builder, SqlServerEngine, connectionString, historyTableName, migrationsAssembly);

    public static void UseNpgsql(DbContextOptionsBuilder builder, string connectionString, string historyTableName, string? migrationsAssembly = null) =>
        Use(builder, PostgreSqlEngine, connectionString, historyTableName, migrationsAssembly);

    public static void UseMySql(DbContextOptionsBuilder builder, string connectionString, string historyTableName, string? migrationsAssembly = null) =>
        Use(builder, MySqlEngine, connectionString, historyTableName, migrationsAssembly);

    public static void Use(DbContextOptionsBuilder builder, string provider, string connectionString, string historyTableName, string? migrationsAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyTableName);
        Use(builder, EngineFor(provider), connectionString, historyTableName, migrationsAssembly);
    }

    /// <summary>
    /// Resolves what <see cref="Use(DbContextOptionsBuilder,string,string,string,string?)"/> would reflect over for
    /// <paramref name="provider"/> — the engine assembly, its <c>Use*</c> overload, and the two relational options
    /// methods the binding calls — without configuring a context or opening a connection. Returns <c>null</c> when
    /// the provider binds, and otherwise a message naming the missing assembly or method and the package to add.
    /// </summary>
    public static string? DescribeBindingFailure(string provider)
    {
        try
        {
            var method = ResolveExtensionMethod(EngineFor(provider));
            var optionsBuilderType = method.GetParameters()[2].ParameterType.GenericTypeArguments[0];
            ResolveMigrationsHistoryTable(optionsBuilderType);
            ResolveMigrationsAssembly(optionsBuilderType);
            return null;
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
        {
            return failure.Message;
        }
    }

    /// <summary>
    /// Picks what a module registers for the provider a host named, typically the registration of its derived context
    /// for that dialect. Every module matches provider names here, so they all accept the same aliases and refuse an
    /// unknown provider with the same message.
    /// </summary>
    public static T Select<T>(string provider, string owner, T sqlite, T sqlServer, T postgreSql, T mySql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return Normalize(provider) switch
        {
            "sqlite" => sqlite,
            "sqlserver" => sqlServer,
            "postgresql" => postgreSql,
            "mysql" => mySql,
            _ => throw new ArgumentException(
                $"Unknown {owner} EF provider '{provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.",
                nameof(provider))
        };
    }

    public static string Normalize(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return provider.Trim().ToLowerInvariant() switch
        {
            "sqlite" or "microsoft.entityframeworkcore.sqlite" => "sqlite",
            "sqlserver" or "sql server" or "mssql" or "microsoft.entityframeworkcore.sqlserver" => "sqlserver",
            "postgresql" or "postgres" or "npgsql" or "npgsql.entityframeworkcore.postgresql" => "postgresql",
            "mysql" or "my sql" or "mysql.entityframeworkcore" => "mysql",
            var value => value
        };
    }

    public static string ExpectedProviderName(string provider) =>
        Select(provider, "relational", EfProviderNames.Sqlite, EfProviderNames.SqlServer, EfProviderNames.PostgreSql, EfProviderNames.MySql);

    /// <summary>The NuGet package a host must reference for <paramref name="provider"/> to bind.</summary>
    public static string ProviderPackageId(string provider) => EngineFor(provider).PackageId;

    private static ProviderEngine EngineFor(string provider) =>
        Select(provider, "relational", SqliteEngine, SqlServerEngine, PostgreSqlEngine, MySqlEngine);

    private static void Use(
        DbContextOptionsBuilder builder,
        ProviderEngine engine,
        string connectionString,
        string historyTableName,
        string? migrationsAssembly)
    {
        var method = ResolveExtensionMethod(engine);
        var actionType = method.GetParameters()[2].ParameterType;
        var optionsBuilderType = actionType.GenericTypeArguments[0];
        var configure = BuildRelationalConfigure(actionType, optionsBuilderType, historyTableName, migrationsAssembly);
        method.Invoke(null, [builder, connectionString, configure]);
    }

    private static MethodInfo ResolveExtensionMethod(ProviderEngine engine)
    {
        var extensionType = Type.GetType(engine.AssemblyQualifiedTypeName, throwOnError: false)
                            ?? FindLoadedType(engine)
                            ?? LoadType(engine)
                            ?? throw EngineMissing(engine);

        return extensionType
                   .GetMethods(BindingFlags.Public | BindingFlags.Static)
                   .FirstOrDefault(candidate =>
                       candidate.Name == engine.MethodName &&
                       candidate.GetParameters() is { Length: 3 } parameters &&
                       parameters[0].ParameterType == typeof(DbContextOptionsBuilder) &&
                       parameters[1].ParameterType == typeof(string) &&
                       parameters[2].ParameterType.IsGenericType &&
                       parameters[2].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
               ?? throw ExtensionMethodMissing(engine, extensionType);
    }

    private static Delegate BuildRelationalConfigure(
        Type actionType,
        Type optionsBuilderType,
        string historyTableName,
        string? migrationsAssembly)
    {
        var parameter = Expression.Parameter(optionsBuilderType, "relational");
        Expression body = Expression.Call(
            parameter,
            ResolveMigrationsHistoryTable(optionsBuilderType),
            Expression.Constant(historyTableName),
            Expression.Constant(null, typeof(string)));

        if (!string.IsNullOrWhiteSpace(migrationsAssembly))
            body = Expression.Call(body, ResolveMigrationsAssembly(optionsBuilderType), Expression.Constant(migrationsAssembly));

        return Expression.Lambda(actionType, body, parameter).Compile();
    }

    private static MethodInfo ResolveMigrationsHistoryTable(Type optionsBuilderType) =>
        optionsBuilderType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                method.Name == "MigrationsHistoryTable" &&
                method.GetParameters() is { Length: 2 } parameters &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(string))
        ?? throw new InvalidOperationException(
            $"{optionsBuilderType.FullName} does not expose MigrationsHistoryTable(string, string).");

    private static MethodInfo ResolveMigrationsAssembly(Type optionsBuilderType) =>
        optionsBuilderType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                method.Name == "MigrationsAssembly" &&
                method.GetParameters() is { Length: 1 } parameters &&
                parameters[0].ParameterType == typeof(string))
        ?? throw new InvalidOperationException(
            $"{optionsBuilderType.FullName} does not expose MigrationsAssembly(string).");

    private static Type? FindLoadedType(ProviderEngine engine)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, engine.PackageId, StringComparison.Ordinal));
        return assembly?.GetType(engine.ExtensionTypeName, throwOnError: false);
    }

    private static Type? LoadType(ProviderEngine engine)
    {
        try
        {
            return Assembly.Load(engine.PackageId).GetType(engine.ExtensionTypeName, throwOnError: false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (TypeLoadException)
        {
            return null;
        }
        catch (ReflectionTypeLoadException)
        {
            return null;
        }
    }

    private static InvalidOperationException EngineMissing(ProviderEngine engine) =>
        new($"assembly '{engine.PackageId}' is not loaded, so '{engine.ExtensionTypeName}.{engine.MethodName}' cannot be bound. " +
            $"Add <PackageReference Include=\"{engine.PackageId}\" /> to the host project. " +
            "The module and policy packages stay provider-free.");

    private static InvalidOperationException ExtensionMethodMissing(ProviderEngine engine, Type extensionType) =>
        new($"'{extensionType.FullName}' loaded from '{extensionType.Assembly.GetName().Name} {extensionType.Assembly.GetName().Version}' exposes no " +
            $"{engine.MethodName}(DbContextOptionsBuilder, string, Action<T>) overload. " +
            $"The referenced {engine.PackageId} version is not the one this Elsa build binds against.");
}
