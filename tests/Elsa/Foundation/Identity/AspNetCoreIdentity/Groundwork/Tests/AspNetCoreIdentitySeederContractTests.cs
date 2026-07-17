using System.Collections.Concurrent;
using System.Reflection;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ElsaRoleStore = Elsa.Foundation.Identity.Abstractions.Iam.IRoleStore;
using ElsaUserStore = Elsa.Foundation.Identity.Abstractions.Iam.IUserStore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentitySeederContractTests
{
    private const string CoreAssemblyName = "Elsa.Foundation.Identity.AspNetCoreIdentity";
    private const string GroundworkAssemblyName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork";
    private const string SeedOptionsTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding.IdentitySeedOptions";
    private const string SeedCoordinatorTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding.IdentitySeedCoordinator";
    private const string GroundworkSeederTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Seeding.GroundworkIdentitySeeder";
    private const string GroundworkRegistrationTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection.AspNetCoreIdentityGroundworkRegistration";
    private const string EfSeedOptionsTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding.IdentitySeedOptions";

    [Fact]
    public void Seed_options_are_provider_neutral_and_no_longer_owned_by_the_ef_namespace()
    {
        Assert.NotNull(Type.GetType(typeof(AspNetCoreIdentityUser).AssemblyQualifiedName!, throwOnError: false));

        var providerNeutralOptions = RequiredType(SeedOptionsTypeName, CoreAssemblyName);
        var efOwnedOptions = Type.GetType($"{EfSeedOptionsTypeName}, Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore", throwOnError: false);

        Assert.True(providerNeutralOptions.IsPublic);
        Assert.Null(efOwnedOptions);
        AssertPublicProperty(providerNeutralOptions, "UserName", typeof(string));
        AssertPublicProperty(providerNeutralOptions, "Password", typeof(string));
        AssertPublicProperty(providerNeutralOptions, "Email", typeof(string));
        AssertPublicProperty(providerNeutralOptions, "RoleName", typeof(string));
        AssertPublicProperty(providerNeutralOptions, "IsDevelopmentSeed", typeof(bool));
    }

    [Fact]
    public void Seed_coordinator_declares_partial_configuration_and_password_policy_failure_contracts()
    {
        var coordinator = RequiredType(SeedCoordinatorTypeName, CoreAssemblyName);

        AssertPublicAsyncMethod(coordinator, "ValidateSeedOptionsAsync");
        AssertPublicAsyncMethod(coordinator, "EnsureSeededAsync");
        AssertPublicNestedResult(coordinator, "MissingUserName");
        AssertPublicNestedResult(coordinator, "MissingPassword");
        AssertPublicNestedResult(coordinator, "PasswordPolicyRejected");
    }

    [Fact]
    public void Seed_coordinator_declares_convergent_two_instance_idempotency_contract()
    {
        var coordinator = RequiredType(SeedCoordinatorTypeName, CoreAssemblyName);

        AssertPublicAsyncMethod(coordinator, "EnsureSeededAsync");
        AssertPublicNestedResult(coordinator, "Created");
        AssertPublicNestedResult(coordinator, "AlreadyConverged");
        AssertPublicNestedResult(coordinator, "ConvergedAfterRace");
    }

    [Fact]
    public void Seed_coordinator_declares_wildcard_and_catalog_permission_grant_contract()
    {
        var coordinator = RequiredType(SeedCoordinatorTypeName, CoreAssemblyName);
        var allAccess = coordinator.GetField("AllAccessPermission", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(allAccess);
        Assert.Equal("*", allAccess!.GetRawConstantValue());
        AssertPublicAsyncMethod(coordinator, "EnsureAdminRoleAsync");
    }

    [Fact]
    public void Groundwork_seeder_exposes_dual_lifecycle_and_secret_safe_logging_contract()
    {
        var seeder = RequiredType(GroundworkSeederTypeName, GroundworkAssemblyName);

        Assert.Contains(
            seeder.GetInterfaces(),
            candidate => candidate.FullName == "Microsoft.Extensions.Hosting.IHostedService");
        Assert.Contains(
            seeder.GetInterfaces(),
            candidate => candidate.FullName == "CShells.Lifecycle.IShellInitializer");
        AssertPublicAsyncMethod(seeder, "StartAsync");
        AssertPublicAsyncMethod(seeder, "InitializeAsync");
        AssertPublicNestedResult(seeder, "ProductionPasswordRedacted");
        AssertPublicNestedResult(seeder, "DevelopmentPasswordLoggedWithCaveat");
    }

    [Fact]
    public void Groundwork_registration_accepts_optional_seed_options_and_registers_one_dual_lifecycle_instance()
    {
        var seedOptions = RequiredType(SeedOptionsTypeName, CoreAssemblyName);
        var registration = RequiredType(GroundworkRegistrationTypeName, GroundworkAssemblyName);
        var method = registration.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(candidate =>
            {
                if (candidate.Name != "AddFoundationAspNetCoreIdentityGroundwork")
                    return false;

                var parameters = candidate.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(IServiceCollection) &&
                       parameters[1].ParameterType == seedOptions;
            });

        Assert.NotNull(method);
    }

    [Fact]
    public async Task Two_instance_groundwork_seeding_converges_for_100_races_without_logging_the_password()
    {
        const string userName = "admin";
        var password = CreateFixtureCredential();
        const string roleName = "administrator";

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
            var logs = new CapturingLoggerProvider();
            await using var first = BuildSeedProvider(documents, logs, userName, password, roleName);
            await using var second = BuildSeedProvider(documents, logs, userName, password, roleName);

            var firstSeeder = first.GetRequiredService<GroundworkIdentitySeeder>();
            var secondSeeder = second.GetRequiredService<GroundworkIdentitySeeder>();

            await Task.WhenAll(
                firstSeeder.StartAsync(CancellationToken.None),
                secondSeeder.StartAsync(CancellationToken.None));

            await using var scope = first.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var userManager = sp.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
            var user = await userManager.FindByNameAsync(userName);
            Assert.NotNull(user);

            var roles = await sp.GetRequiredService<ElsaRoleStore>().ListAsync(user!.TenantId);
            var adminRole = Assert.Single(roles.Where(role => string.Equals(role.Name, roleName, StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(IdentitySeedCoordinator.AllAccessPermission, adminRole.Permissions);
            Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersRead, adminRole.Permissions);

            var userRecord = await sp.GetRequiredService<ElsaUserStore>().FindAsync(user.TenantId, user.Id);
            Assert.NotNull(userRecord);
            Assert.Contains(adminRole.Id, userRecord!.RoleIds);

            var membership = await sp.GetRequiredService<ITenantMembershipStore>().FindAsync(user.TenantId, user.Id);
            Assert.NotNull(membership);
            Assert.Equal(TenantMembershipStatus.Active, membership!.Status);
            Assert.Contains(adminRole.Id, membership.RoleIds);

            Assert.Single(documents.Snapshot(IdentityStorageManifest.IdentityUserDocumentKind));
            Assert.Single(documents.Snapshot(IdentityStorageManifest.IdentityRoleDocumentKind));
            Assert.Single(documents.Snapshot(IdentityStorageManifest.IdentityTenantMembershipDocumentKind));
            Assert.DoesNotContain(logs.Messages, message => message.Contains(password, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Development_seed_fails_before_writes_outside_development_and_never_logs_the_secret(string environmentName)
    {
        var password = CreateFixtureCredential();
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildSeedProvider(
            documents,
            logs,
            "admin",
            password,
            "administrator",
            isDevelopmentSeed: true,
            environmentName: environmentName);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<GroundworkIdentitySeeder>().StartAsync(CancellationToken.None));

        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
        AssertSeedAuthorityIsEmpty(documents);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_seed_fails_closed_when_host_environment_is_unavailable()
    {
        var password = CreateFixtureCredential();
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildSeedProvider(
            documents,
            logs,
            "admin",
            password,
            "administrator",
            isDevelopmentSeed: true,
            registerEnvironment: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<GroundworkIdentitySeeder>().StartAsync(CancellationToken.None));

        AssertSeedAuthorityIsEmpty(documents);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_seed_logs_credentials_only_in_a_verified_development_host()
    {
        const string userName = "admin";
        var password = CreateFixtureCredential();
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildSeedProvider(
            documents,
            logs,
            userName,
            password,
            "administrator",
            isDevelopmentSeed: true,
            environmentName: Environments.Development);

        await provider.GetRequiredService<GroundworkIdentitySeeder>().StartAsync(CancellationToken.None);

        Assert.Contains(logs.Messages, message =>
            message.Contains(userName, StringComparison.Ordinal) &&
            message.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_password_policy_fails_before_role_user_or_membership_writes()
    {
        const string password = "short";
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildSeedProvider(documents, logs, "admin", password, "administrator");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<GroundworkIdentitySeeder>().StartAsync(CancellationToken.None));

        Assert.Contains("Password", exception.Message, StringComparison.Ordinal);
        AssertSeedAuthorityIsEmpty(documents);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Production_seed_logs_neither_username_nor_password()
    {
        var secret = string.Concat("Fixture", "Value", "1");
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildSeedProvider(
            documents,
            logs,
            secret,
            secret,
            "administrator",
            configureIdentity: options => options.Password.RequireNonAlphanumeric = false);

        await provider.GetRequiredService<GroundworkIdentitySeeder>().StartAsync(CancellationToken.None);

        Assert.DoesNotContain(logs.Messages, message => message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Password_validator_diagnostics_cannot_echo_the_configured_secret()
    {
        var password = CreateFixtureCredential();
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildSeedProvider(
            documents,
            logs,
            "admin",
            password,
            "administrator",
            passwordValidator: new EchoingPasswordValidator());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<GroundworkIdentitySeeder>().StartAsync(CancellationToken.None));

        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
        AssertSeedAuthorityIsEmpty(documents);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(password, StringComparison.Ordinal));
    }

    private static Type RequiredType(string typeName, string assemblyName)
    {
        var type = Type.GetType($"{typeName}, {assemblyName}", throwOnError: false);
        Assert.NotNull(type);
        return type!;
    }

    private static string CreateFixtureCredential() => string.Concat("Fixture", "-Value", "-A1!");

    private static void AssertPublicProperty(Type type, string name, Type propertyType)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        Assert.Equal(propertyType, property!.PropertyType);
    }

    private static void AssertPublicAsyncMethod(Type type, string name)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(
            method!.ReturnType == typeof(Task) ||
            (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)) ||
            method.ReturnType == typeof(ValueTask) ||
            (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)),
            $"{type.FullName}.{name} must be asynchronous.");
    }

    private static void AssertPublicNestedResult(Type type, string name)
    {
        var nested = type.GetNestedType(name, BindingFlags.Public);
        Assert.NotNull(nested);
    }

    private static ServiceProvider BuildSeedProvider(
        InMemoryDocumentStore documents,
        ILoggerProvider loggerProvider,
        string userName,
        string password,
        string roleName,
        bool isDevelopmentSeed = false,
        string environmentName = "Production",
        bool registerEnvironment = true,
        Action<IdentityOptions>? configureIdentity = null,
        IPasswordValidator<AspNetCoreIdentityUser>? passwordValidator = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        if (registerEnvironment)
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddSingleton<IDocumentStore>(documents);
        services.AddSingleton<IBoundedDocumentStore>(documents);
        services.AddFoundationAspNetCoreIdentityGroundwork(new IdentitySeedOptions
        {
            UserName = userName,
            Password = password,
            Email = "admin@example.test",
            RoleName = roleName,
            IsDevelopmentSeed = isDevelopmentSeed
        });
        if (configureIdentity is not null)
            services.Configure(configureIdentity);
        if (passwordValidator is not null)
            services.AddSingleton(passwordValidator);
        return services.BuildServiceProvider();
    }

    private static void AssertSeedAuthorityIsEmpty(InMemoryDocumentStore documents)
    {
        Assert.Empty(documents.Snapshot(IdentityStorageManifest.IdentityRoleDocumentKind));
        Assert.Empty(documents.Snapshot(IdentityStorageManifest.IdentityUserDocumentKind));
        Assert.Empty(documents.Snapshot(IdentityStorageManifest.IdentityTenantMembershipDocumentKind));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Elsa.Foundation.Identity.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class EchoingPasswordValidator : IPasswordValidator<AspNetCoreIdentityUser>
    {
        public Task<IdentityResult> ValidateAsync(
            UserManager<AspNetCoreIdentityUser> manager,
            AspNetCoreIdentityUser user,
            string? password) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = $"Rejected:{password}",
                Description = $"Rejected configured password '{password}'."
            }));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
