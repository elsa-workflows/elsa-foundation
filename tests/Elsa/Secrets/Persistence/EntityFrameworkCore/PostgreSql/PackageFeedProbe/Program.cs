using System.Reflection;
using System.Runtime.Loader;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Features;
using Elsa.Secrets.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe;

internal static class Program
{
    private const string ShellName = "secrets-package-feed";
    private const string TenantId = "tenant-a";
    private const string SecretName = "payments.package-feed";
    private const string SecretValue = "package-feed-postgresql-value";
    private const string ModuleAssemblyName = "Elsa.Secrets.Persistence.EntityFrameworkCore";
    private const string ContextTypeName = ModuleAssemblyName + ".SecretsDbContext";
    private const string ExpectedProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string InitialMigration = "20260910210216_Initial";
    private const string WidenLookupKeysMigration = "20260911011058_WidenLookupKeys";

    public static async Task<int> Main()
    {
        var modulePath = Environment.GetEnvironmentVariable("ELSA_SECRETS_PACKAGE_MODULE_PATH");
        var connectionString = Environment.GetEnvironmentVariable("ELSA_SECRETS_PACKAGE_CONNECTION_STRING");
        var policy = Environment.GetEnvironmentVariable("ELSA_SECRETS_PACKAGE_MIGRATE_POLICY");
        try
        {
            if (string.IsNullOrWhiteSpace(modulePath))
                throw new InvalidOperationException("The extracted Secrets EF module path was not supplied.");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The PostgreSQL connection string was not supplied.");
            if (!Enum.TryParse<EfMigratePolicy>(policy, ignoreCase: true, out var migratePolicy))
                throw new InvalidOperationException("The Secrets EF migration policy was not supplied.");

            await RunAsync(Path.GetFullPath(modulePath), connectionString, migratePolicy);
            Console.WriteLine($"{{\"status\":\"ok\",\"mode\":\"{migratePolicy}\",\"provider\":\"{ExpectedProvider}\",\"processId\":{Environment.ProcessId}}}");
            return 0;
        }
        catch (Exception exception)
        {
            // Keep connection credentials out of child diagnostics while retaining the exception category
            // the parent uses to distinguish load/provider/identity failures.
            var message = RedactConnectionSecrets(exception.ToString(), connectionString);
            Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "error",
                exceptionType = exception.GetType().Name,
                message
            }));
            return 1;
        }
    }

    private static string RedactConnectionSecrets(string message, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return message;

        var redacted = message.Replace(connectionString, "[REDACTED CONNECTION STRING]", StringComparison.Ordinal);
        try
        {
            var password = new NpgsqlConnectionStringBuilder(connectionString).Password;
            if (!string.IsNullOrEmpty(password))
                redacted = redacted.Replace(password, "[REDACTED PASSWORD]", StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // The main validation path will report malformed connection strings without echoing the full value.
        }

        return redacted;
    }

    private static async Task RunAsync(string modulePath, string connectionString, EfMigratePolicy migratePolicy)
    {
        if (!File.Exists(modulePath))
            throw new FileNotFoundException("The extracted Secrets EF module assembly was not found.", modulePath);

        var moduleAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(modulePath);
        if (!string.Equals(moduleAssembly.GetName().Name, ModuleAssemblyName, StringComparison.Ordinal))
            throw new InvalidOperationException($"The extracted module assembly was '{moduleAssembly.GetName().Name}'.");
        if (!string.Equals(Path.GetFullPath(moduleAssembly.Location), modulePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The loaded module assembly location did not remain the extracted package path.");
        if (AssemblyLoadContext.GetLoadContext(moduleAssembly) != AssemblyLoadContext.Default)
            throw new InvalidOperationException("The module assembly was not loaded into AssemblyLoadContext.Default.");
        if (AssemblyLoadContext.Default.Assemblies.Count(assembly =>
                string.Equals(assembly.GetName().Name, ModuleAssemblyName, StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException("Duplicate EF module assembly identity was loaded.");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["CShells:Shells:" + ShellName + ":Name"] = ShellName,
                ["CShells:Shells:" + ShellName + ":Features:Secrets"] = "true",
                ["CShells:Shells:" + ShellName + ":Features:PackageFeedProbeEncryption:EncryptionKey"] = "package-feed-probe-key",
                ["CShells:Shells:" + ShellName + ":Features:SecretsEntityFrameworkCore:Provider"] = "PostgreSql",
                ["CShells:Shells:" + ShellName + ":Features:SecretsEntityFrameworkCore:ConnectionString"] = connectionString,
                ["CShells:Shells:" + ShellName + ":Features:SecretsEntityFrameworkCore:MigratePolicy"] = migratePolicy.ToString()
            })
            .Build();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithHostAssemblies()
            .WithAssemblies(typeof(SecretsFeature).Assembly, moduleAssembly, typeof(PackageFeedProbeEncryptionFeature).Assembly)
            .WithConfigurationProvider(configuration));

        await using var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        try
        {
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = ResolveContext(services, moduleAssembly);

            var providerName = context.Database.ProviderName
                               ?? throw new InvalidOperationException("The Secrets DbContext did not report an EF provider.");
            var contextAssemblyName = context.GetType().Assembly.GetName().Name
                                      ?? throw new InvalidOperationException("The Secrets DbContext assembly had no name.");
            Assert.Equal(ExpectedProvider, providerName);
            Assert.Equal(ModuleAssemblyName, contextAssemblyName);
            Assert.Same(moduleAssembly, context.GetType().Assembly);
            Assert.Same(moduleAssembly, context.GetService<IMigrationsAssembly>().Assembly);
            Assert.Equal(modulePath, Path.GetFullPath(context.GetService<IMigrationsAssembly>().Assembly.Location), StringComparer.OrdinalIgnoreCase);
            AssertNpgsqlOnly();

            var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
            if (pending.Length != 0)
                throw new InvalidOperationException($"Secrets EF had pending migrations: {string.Join(",", pending)}.");

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToHashSet(StringComparer.Ordinal);
            if (!applied.Contains(InitialMigration) || !applied.Contains(WidenLookupKeysMigration))
                throw new InvalidOperationException("The expected PostgreSQL Secrets EF migrations were not applied.");

            var repository = services.GetRequiredService<ISecretRepository>();
            if (!string.Equals(repository.GetType().FullName, ModuleAssemblyName + ".Stores.EfSecretRepository", StringComparison.Ordinal) ||
                !ReferenceEquals(repository.GetType().Assembly, moduleAssembly))
                throw new InvalidOperationException("The selected repository was not the extracted Secrets EF implementation.");

            if (migratePolicy == EfMigratePolicy.AutoMigrate)
                await AssertFirstRunAsync(services, repository);
            else
                await AssertValidationRunAsync(services, repository);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static DbContext ResolveContext(IServiceProvider services, Assembly moduleAssembly)
    {
        var contextType = moduleAssembly.GetType(ContextTypeName, throwOnError: true)
                           ?? throw new InvalidOperationException($"The module did not contain {ContextTypeName}.");
        return (DbContext)services.GetRequiredService(contextType);
    }

    private static async Task AssertFirstRunAsync(IServiceProvider services, ISecretRepository repository)
    {
        var manager = services.GetRequiredService<ISecretManager>();
        await manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = SecretName,
            DisplayName = "Package feed PostgreSQL secret",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = SecretValue
        });

        var resolved = await services.GetRequiredService<ISecretValueResolver>()
            .ResolveAsync(TenantId, new SecretReference(SecretName));
        Assert.True(resolved.Succeeded);
        Assert.Equal(SecretValue, resolved.Value);

        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        var current = await revisions.FindWithRevisionAsync(TenantId, SecretName)
                      ?? throw new InvalidOperationException("The created secret was not found for the OCC proof.");
        current.Secret.Description = "OCC winner";
        Assert.Equal(SecretRevisionSaveStatus.Saved,
            (await revisions.SaveWithRevisionAsync(current.Secret, current.Revision)).Status);
        current.Secret.Description = "stale overwrite";
        Assert.Equal(SecretRevisionSaveStatus.Conflict,
            (await revisions.SaveWithRevisionAsync(current.Secret, current.Revision)).Status);

        var identity = LongNonAsciiIdentity();
        await repository.SaveAsync(new Secret
        {
            TenantId = TenantId,
            Name = "identity.package-feed",
            DisplayName = "Package feed identity",
            TypeName = identity.TypeName,
            StoreName = identity.StoreName,
            Scope = identity.Scope,
            Versions =
            [
                new SecretVersion
                {
                    Version = 1,
                    Status = SecretStatus.Active,
                    Payload = SecretPayload.FromValue("identity-value")
                }
            ]
        });

        var page = await repository.ListPageAsync(TenantId, IdentityQuery(identity));
        Assert.Equal("identity.package-feed", Assert.Single(page.Items).Name);
    }

    private static async Task AssertValidationRunAsync(IServiceProvider services, ISecretRepository repository)
    {
        var found = await services.GetRequiredService<ISecretManager>().FindAsync(TenantId, SecretName);
        if (found is null)
            throw new InvalidOperationException("The created secret was not found after validation restart.");
        Assert.Equal("OCC winner", found.Description ?? "");

        var resolved = await services.GetRequiredService<ISecretValueResolver>()
            .ResolveAsync(TenantId, new SecretReference(SecretName));
        Assert.True(resolved.Succeeded);
        Assert.Equal(SecretValue, resolved.Value);

        var identity = LongNonAsciiIdentity();
        var page = await repository.ListPageAsync(TenantId, IdentityQuery(identity));
        var persisted = Assert.Single(page.Items);
        Assert.Equal("identity.package-feed", persisted.Name);
        Assert.Equal(identity.TypeName, persisted.TypeName);
        Assert.Equal(identity.StoreName, persisted.StoreName);
        Assert.Equal(identity.Scope, persisted.Scope);
    }

    private static SecretRepositoryListRequest IdentityQuery((string TypeName, string StoreName, string Scope) identity) =>
        new(typeName: identity.TypeName.ToLowerInvariant(), storeName: identity.StoreName.ToLowerInvariant(), scope: identity.Scope.ToLowerInvariant());

    private static (string TypeName, string StoreName, string Scope) LongNonAsciiIdentity() =>
        ($"Typé-{new string('Ä', 96)}", $"Storé-{new string('Ö', 96)}", $"Scopé-{new string('Ü', 96)}");

    private static void AssertNpgsqlOnly()
    {
        var names = AssemblyLoadContext.Default.Assemblies
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(ExpectedProvider))
            throw new InvalidOperationException("The Npgsql EF provider was not loaded.");
        if (names.Contains("Microsoft.EntityFrameworkCore.Sqlite") || names.Contains("Microsoft.EntityFrameworkCore.SqlServer"))
            throw new InvalidOperationException("A second relational EF provider was loaded.");
    }

    private static class Assert
    {
        public static void True(bool condition)
        {
            if (!condition)
                throw new InvalidOperationException("An expected probe assertion was false.");
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected '{expected}' but found '{actual}'.");
        }

        public static void Equal(string expected, string actual, StringComparer comparer)
        {
            if (!comparer.Equals(expected, actual))
                throw new InvalidOperationException($"Expected '{expected}' but found '{actual}'.");
        }

        public static void Same(object expected, object actual)
        {
            if (!ReferenceEquals(expected, actual))
                throw new InvalidOperationException("Expected the same object instance.");
        }

        public static T IsAssignableFrom<T>(object? value)
        {
            if (value is not T typed)
                throw new InvalidOperationException($"Expected an instance assignable to {typeof(T).FullName}.");
            return typed;
        }

        public static T Single<T>(IReadOnlyCollection<T> values)
        {
            if (values.Count != 1)
                throw new InvalidOperationException($"Expected one value but found {values.Count}.");
            return values.Single();
        }

        public static void NotNull(object? value)
        {
            if (value is null)
                throw new InvalidOperationException("Expected a non-null value.");
        }
    }
}

[ShellFeature(name: "PackageFeedProbeEncryption")]
public sealed class PackageFeedProbeEncryptionFeature : IShellFeature
{
    public string EncryptionKey { get; set; } = "package-feed-probe-key";

    public void ConfigureServices(IServiceCollection services) =>
        services.PostConfigure<SecretsOptions>(options => options.EncryptionKey = EncryptionKey);
}
