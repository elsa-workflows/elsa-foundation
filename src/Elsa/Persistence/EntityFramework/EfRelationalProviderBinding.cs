using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Invokes the host's <c>UseSqlite</c> / <c>UseSqlServer</c> / <c>UseNpgsql</c> extension without
/// this policy package referencing a provider engine. The host (or test/tooling project) must
/// reference exactly one provider package so the extension type can be loaded.
/// </summary>
public static class EfRelationalProviderBinding
{
    public static void UseSqlite(DbContextOptionsBuilder builder, string connectionString, string historyTableName, string? migrationsAssembly = null) =>
        Use(
            builder,
            "Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions, Microsoft.EntityFrameworkCore.Sqlite",
            "UseSqlite",
            connectionString,
            historyTableName,
            migrationsAssembly);

    public static void UseSqlServer(DbContextOptionsBuilder builder, string connectionString, string historyTableName, string? migrationsAssembly = null) =>
        Use(
            builder,
            "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsBuilderExtensions, Microsoft.EntityFrameworkCore.SqlServer",
            "UseSqlServer",
            connectionString,
            historyTableName,
            migrationsAssembly);

    public static void UseNpgsql(DbContextOptionsBuilder builder, string connectionString, string historyTableName, string? migrationsAssembly = null) =>
        Use(
            builder,
            "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions, Npgsql.EntityFrameworkCore.PostgreSQL",
            "UseNpgsql",
            connectionString,
            historyTableName,
            migrationsAssembly);

    public static void Use(DbContextOptionsBuilder builder, string provider, string connectionString, string historyTableName, string? migrationsAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyTableName);

        switch (Normalize(provider))
        {
            case "sqlite":
                UseSqlite(builder, connectionString, historyTableName, migrationsAssembly);
                return;
            case "sqlserver":
                UseSqlServer(builder, connectionString, historyTableName, migrationsAssembly);
                return;
            case "postgresql":
            case "npgsql":
            case "postgres":
                UseNpgsql(builder, connectionString, historyTableName, migrationsAssembly);
                return;
            default:
                throw new ArgumentException(
                    $"Unknown EF relational provider '{provider}'. Expected Sqlite, SqlServer, or PostgreSql.",
                    nameof(provider));
        }
    }

    public static string Normalize(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return provider.Trim().ToLowerInvariant() switch
        {
            "sqlite" or "microsoft.entityframeworkcore.sqlite" => "sqlite",
            "sqlserver" or "sql server" or "mssql" or "microsoft.entityframeworkcore.sqlserver" => "sqlserver",
            "postgresql" or "postgres" or "npgsql" or "npgsql.entityframeworkcore.postgresql" => "postgresql",
            var value => value
        };
    }

    public static string ExpectedProviderName(string provider) =>
        Normalize(provider) switch
        {
            "sqlite" => EfProviderNames.Sqlite,
            "sqlserver" => EfProviderNames.SqlServer,
            "postgresql" => EfProviderNames.PostgreSql,
            _ => throw new ArgumentException($"Unknown EF relational provider '{provider}'.", nameof(provider))
        };

    private static void Use(
        DbContextOptionsBuilder builder,
        string extensionTypeName,
        string methodName,
        string connectionString,
        string historyTableName,
        string? migrationsAssembly)
    {
        var extensionType = Type.GetType(extensionTypeName, throwOnError: false)
                            ?? FindLoadedType(extensionTypeName)
                            ?? LoadType(extensionTypeName)
                            ?? throw ProviderMissing(extensionTypeName, methodName);

        var method = extensionType
                         .GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .FirstOrDefault(candidate =>
                             candidate.Name == methodName &&
                             candidate.GetParameters() is { Length: 3 } parameters &&
                             parameters[0].ParameterType == typeof(DbContextOptionsBuilder) &&
                             parameters[1].ParameterType == typeof(string) &&
                             parameters[2].ParameterType.IsGenericType &&
                             parameters[2].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
                     ?? throw ProviderMissing(extensionTypeName, methodName);

        var actionType = method.GetParameters()[2].ParameterType;
        var optionsBuilderType = actionType.GenericTypeArguments[0];
        var configure = BuildRelationalConfigure(actionType, optionsBuilderType, historyTableName, migrationsAssembly);
        method.Invoke(null, [builder, connectionString, configure]);
    }

    private static Delegate BuildRelationalConfigure(
        Type actionType,
        Type optionsBuilderType,
        string historyTableName,
        string? migrationsAssembly)
    {
        var parameter = Expression.Parameter(optionsBuilderType, "relational");
        Expression body = parameter;

        var history = optionsBuilderType
                          .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                          .FirstOrDefault(method =>
                              method.Name == "MigrationsHistoryTable" &&
                              method.GetParameters() is { Length: 2 } parameters &&
                              parameters[0].ParameterType == typeof(string) &&
                              parameters[1].ParameterType == typeof(string))
                      ?? throw new InvalidOperationException(
                          $"{optionsBuilderType.FullName} does not expose MigrationsHistoryTable(string, string).");
        body = Expression.Call(body, history, Expression.Constant(historyTableName), Expression.Constant(null, typeof(string)));

        if (!string.IsNullOrWhiteSpace(migrationsAssembly))
        {
            var assembly = optionsBuilderType
                               .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                               .FirstOrDefault(method =>
                                   method.Name == "MigrationsAssembly" &&
                                   method.GetParameters() is { Length: 1 } parameters &&
                                   parameters[0].ParameterType == typeof(string))
                           ?? throw new InvalidOperationException(
                               $"{optionsBuilderType.FullName} does not expose MigrationsAssembly(string).");
            body = Expression.Call(body, assembly, Expression.Constant(migrationsAssembly));
        }

        return Expression.Lambda(actionType, body, parameter).Compile();
    }

    private static Type? FindLoadedType(string assemblyQualifiedName)
    {
        if (!TrySplitAssemblyQualifiedName(assemblyQualifiedName, out var typeName, out var assemblyName))
            return null;
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal));
        return assembly?.GetType(typeName, throwOnError: false);
    }

    private static Type? LoadType(string assemblyQualifiedName)
    {
        if (!TrySplitAssemblyQualifiedName(assemblyQualifiedName, out var typeName, out var assemblyName))
            return null;
        try
        {
            return Assembly.Load(assemblyName).GetType(typeName, throwOnError: false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TrySplitAssemblyQualifiedName(string assemblyQualifiedName, out string typeName, out string assemblyName)
    {
        var comma = assemblyQualifiedName.LastIndexOf(',');
        if (comma < 0)
        {
            typeName = "";
            assemblyName = "";
            return false;
        }

        typeName = assemblyQualifiedName[..comma].Trim();
        assemblyName = assemblyQualifiedName[(comma + 1)..].Trim();
        return typeName.Length > 0 && assemblyName.Length > 0;
    }

    private static InvalidOperationException ProviderMissing(string extensionTypeName, string methodName) =>
        new(
            $"Cannot bind {methodName} because '{extensionTypeName}' is not loaded. " +
            "The host must PackageReference the matching EF provider engine (Sqlite, SqlServer, or Npgsql). " +
            "The module and policy packages stay provider-free.");
}
