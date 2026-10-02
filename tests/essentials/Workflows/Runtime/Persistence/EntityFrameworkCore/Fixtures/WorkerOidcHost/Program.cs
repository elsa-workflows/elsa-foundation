using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Sequence;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Locking.FileSystem;
using Elsa.Mediator;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

[assembly: EfToolingShellDefaults(typeof(WorkerOidcToolingShellDefaults))]

internal static class Program
{
    private const string ShellName = "worker-oidc-runtime";
    private const string OidcFeatureName = "FoundationIdentityOidc";
    private const string IamFeatureName = "IdentityIamEntityFrameworkCore";
    private const string RuntimeFeatureName = "WorkflowsRuntimeEntityFrameworkCore";
    private const string LockingFeatureName = "FileSystemDistributedLocking";
    private const string RuntimeResourceName = "WorkerRuntime";
    private const string IamConnectionName = "Iam";
    private const string JwtBearerScheme = "Elsa.Identity.Oidc.Jwt";
    private const string NormalizedAuthenticationType = OidcBearerNormalizationEvents.NormalizedAuthenticationType;
    private const string RecoverySigningKey = "worker-oidc-runtime-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "worker-oidc-runtime-hierarchy-signing-key-32-bytes";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly AsyncLocal<PersistenceAccessContext?> FixturePersistenceControl = new();

    public static async Task<int> Main()
    {
        var startupLine = await Console.In.ReadLineAsync();
        if (startupLine is null || startupLine.Length > 64 * 1024)
        {
            await WriteAsync(new { status = "error", code = "startup-input-invalid" });
            return 2;
        }

        HostStartupInput? input;
        try
        {
            input = JsonSerializer.Deserialize<HostStartupInput>(startupLine, JsonOptions);
            if (input is null)
                throw new JsonException();
            input.Validate();
        }
        catch
        {
            await WriteAsync(new { status = "error", code = "startup-input-invalid" });
            return 2;
        }

        WebApplication? app = null;
        MappingReadCounter mappingReads = new();
        object? readyData;
        string? address;
        string artifactSha256;
        var startupStage = "build";

        try
        {
            app = CreateApp(input, mappingReads);
            app.MapShells();
            startupStage = "start";
            await app.StartAsync();
            address = GetListeningAddress(app);
            startupStage = "activate";
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            startupStage = "describe";
            readyData = await DescribeAsync(app, shell, input);
            artifactSha256 = await HashAssemblyAsync();
        }
        catch (Exception exception)
        {
            if (app is not null)
            {
                try
                {
                    await app.DisposeAsync();
                }
                catch
                {
                    // Preserve only the original failure's bounded type and stage in the receipt.
                }
            }
            await WriteAsync(new
            {
                status = "error",
                code = "host-start-failed",
                stage = startupStage,
                exceptionType = SafeExceptionTypeName(exception)
            });
            return 2;
        }

        var activeApp = app ?? throw new InvalidOperationException("The Worker OIDC host was not created.");
        await WriteAsync(new
        {
            status = "ready",
            processId = Environment.ProcessId,
            processStartUtcTicks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            artifactSha256,
            address,
            data = readyData
        });

        try
        {
            while (true)
            {
                var line = await Console.In.ReadLineAsync();
                if (line is null)
                    break;
                if (line.Length > 64 * 1024)
                {
                    await WriteAsync(new { status = "error", code = "control-input-invalid" });
                    continue;
                }

                ControlRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<ControlRequest>(line, JsonOptions);
                    if (request is null || string.IsNullOrWhiteSpace(request.Command))
                        throw new JsonException();
                }
                catch
                {
                    await WriteAsync(new { status = "error", code = "control-input-invalid" });
                    continue;
                }

                try
                {
                    var result = await ExecuteControlAsync(activeApp, input, mappingReads, request);
                    await WriteAsync(new { status = "ok", data = result.Value });
                    if (result.Stop)
                        break;
                }
                catch
                {
                    // Control failures deliberately omit exception text, paths, values and claims.
                    await WriteAsync(new { status = "error", code = "control-operation-failed" });
                }
            }
        }
        finally
        {
            await activeApp.DisposeAsync();
        }

        return 0;
    }

    private static WebApplication CreateApp(HostStartupInput input, MappingReadCounter mappingReads)
    {
        Directory.CreateDirectory(input.LocksDirectory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var configuration = BuildConfiguration(input);
        builder.Configuration.AddConfiguration(configuration);

        // This establishes the ordinary nondefault tenant context before CShells copies root services into a shell.
        builder.Services.AddPersistenceCore(input.PersistenceScope);
        builder.Services.AddHttpContextAccessor();

        // The fixture's adversarial header selects a hostile context for one request. Without that header the exact
        // AddPersistenceCore accessor remains authoritative; this adapter never binds or mutates its context.
        var originalAccessor = builder.Services.Single(descriptor =>
            descriptor.ServiceType == typeof(IPersistenceAccessContextAccessor));
        var originalAccessorFactory = originalAccessor.ImplementationFactory ??
            throw new InvalidOperationException("The static persistence accessor registration is not factory-backed.");
        builder.Services.Remove(originalAccessor);
        builder.Services.AddScoped<IPersistenceAccessContextAccessor>(services =>
            new RequestControlledAccessContextAccessor(
                (IPersistenceAccessContextAccessor)originalAccessorFactory(services),
                services.GetRequiredService<IHttpContextAccessor>(),
                input.PersistenceScope));

        // This observes real EF commands without replacing the IAM mapping store used by the OIDC adapter.
        builder.Services.ConfigureDbContext<IdentityIamSqliteDbContext>(options => options.AddInterceptors(mappingReads));
        builder.Services.AddEfPersistenceResources(builder.Configuration, typeof(Program).Assembly);

        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(FoundationIdentityAbstractionsFeature).Assembly,
                typeof(OidcAuthenticationFeature).Assembly,
                typeof(IdentityIamEntityFrameworkCoreFeature).Assembly,
                typeof(PrimitivesFeature).Assembly,
                typeof(SerializationFeature).Assembly,
                typeof(MediatorFeature).Assembly,
                typeof(EventsFeature).Assembly,
                typeof(ExpressionsFeature).Assembly,
                typeof(ActivitiesRuntimeFeature).Assembly,
                typeof(ActivitiesPrimitivesFeature).Assembly,
                typeof(ActivitiesControlFlowFeature).Assembly,
                typeof(ActivitiesSequenceFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly,
                typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(WorkflowsRuntimeTriggersFeature).Assembly,
                typeof(FileSystemLockingFeature).Assembly,
                typeof(TasksFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration));

        return builder.Build();
    }

    private static IConfiguration BuildConfiguration(HostStartupInput input)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = RuntimeResourceName,
            [$"ConnectionStrings:{RuntimeResourceName}"] = $"Data Source={input.RuntimeDatabasePath};Pooling=False",
            [$"ConnectionStrings:{IamConnectionName}"] = $"Data Source={input.IamDatabasePath};Pooling=False",
            [$"CShells:Shells:{ShellName}:Configuration:WebRouting:Path"] = "",
            [$"CShells:Shells:{ShellName}:Features:{OidcFeatureName}:Authority"] = input.Authority,
            [$"CShells:Shells:{ShellName}:Features:{OidcFeatureName}:Audience"] = input.Audience,
            [$"CShells:Shells:{ShellName}:Features:{OidcFeatureName}:ProviderId"] = input.ProviderId,
            [$"CShells:Shells:{ShellName}:Features:{OidcFeatureName}:TenantId"] = input.TenantId,
            [$"CShells:Shells:{ShellName}:Features:{OidcFeatureName}:NormalizeBearerClaims"] = "true",
            [$"CShells:Shells:{ShellName}:Features:{OidcFeatureName}:RequireHttpsMetadata"] = "false",
            [$"CShells:Shells:{ShellName}:Features:{OidcFeatureName}:IsDefault"] = "true",
            [$"CShells:Shells:{ShellName}:Features:{IamFeatureName}:Provider"] = "Sqlite",
            [$"CShells:Shells:{ShellName}:Features:{IamFeatureName}:ConnectionName"] = IamConnectionName,
            [$"CShells:Shells:{ShellName}:Features:{RuntimeFeatureName}:RecoveryContinuationSigningKey"] = RecoverySigningKey,
            [$"CShells:Shells:{ShellName}:Features:{RuntimeFeatureName}:HierarchyCursorSigningKey"] = HierarchySigningKey,
            [$"CShells:Shells:{ShellName}:Features:{LockingFeatureName}:LocksFolderPath"] = input.LocksDirectory
        };

        foreach (var feature in SelectedFeatures)
            values[$"CShells:Shells:{ShellName}:Features:{feature}"] = null;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static readonly string[] SelectedFeatures =
    [
        "FoundationIdentityAbstractions",
        "FoundationIdentityOidc",
        "IdentityIamEntityFrameworkCore",
        "Primitives",
        "Serialization",
        "Mediator",
        "Events",
        "Expressions",
        "ActivitiesRuntime",
        "ActivitiesPrimitives",
        "ActivitiesControlFlow",
        "ActivitiesSequence",
        "ApiCapabilities",
        "WorkflowsRuntimeApi",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers",
        "FileSystemDistributedLocking"
    ];

    private static string GetListeningAddress(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses;
        return addresses?.Single(address => address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)) ??
            throw new InvalidOperationException("Kestrel did not expose its loopback address.");
    }

    private static async Task<object> DescribeAsync(
        WebApplication app,
        IShell shell,
        HostStartupInput input)
    {
        var schemes = shell.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultAuthenticate = await schemes.GetDefaultAuthenticateSchemeAsync();
        var defaultChallenge = await schemes.GetDefaultChallengeSchemeAsync();
        var bearer = await schemes.GetSchemeAsync(JwtBearerScheme);
        var bearerOptions = shell.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerScheme);
        var oidc = shell.ServiceProvider.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;
        var identity = shell.ServiceProvider.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value;
        var enabledFeatures = shell.ServiceProvider.GetRequiredService<ShellSettings>().EnabledFeatures
            .OrderBy(feature => feature, StringComparer.Ordinal)
            .ToArray();
        var routes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(route => route is not null)
            .Select(route => route!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var runtimeOptions = scope.ServiceProvider.GetRequiredService<RuntimeWorkflowExecutionEntityFrameworkCoreOptions>();
        var iamOptions = scope.ServiceProvider.GetRequiredService<IdentityIamEntityFrameworkCoreOptions>();
        var runtime = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        var iam = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        var runtimeConnection = runtime.Database.GetDbConnection().DataSource;
        var iamConnection = iam.Database.GetDbConnection().DataSource;
        var runtimeAppliedMigrations = await runtime.Database.GetAppliedMigrationsAsync();
        var runtimePendingMigrations = await runtime.Database.GetPendingMigrationsAsync();
        var iamAppliedMigrations = await iam.Database.GetAppliedMigrationsAsync();
        var iamPendingMigrations = await iam.Database.GetPendingMigrationsAsync();

        return new
        {
            shell = ShellName,
            tenantId = oidc.TenantId,
            providerId = oidc.ProviderId,
            normalizationEnabled = oidc.NormalizeBearerClaims,
            audienceConfigured = !string.IsNullOrWhiteSpace(oidc.Audience),
            jwtBearerScheme = oidc.JwtBearerScheme,
            normalizedAuthenticationType = NormalizedAuthenticationType,
            normalizedTypeEnrolled = identity.NormalizedAuthenticationTypes.Contains(NormalizedAuthenticationType),
            rawSchemeEnrolled = identity.NormalizedAuthenticationTypes.Contains(oidc.JwtBearerScheme),
            defaultAuthenticateScheme = defaultAuthenticate?.Name,
            defaultChallengeScheme = defaultChallenge?.Name,
            bearerHandlerType = bearer?.HandlerType?.FullName,
            bearerEventsType = bearerOptions.EventsType?.FullName,
            interactiveOidcSchemePresent = (await schemes.GetSchemeAsync(oidc.AuthenticationScheme)) is not null,
            enabledFeatures,
            routes,
            runtimeProvider = runtimeOptions.Provider,
            runtimeConnectionName = runtimeOptions.ConnectionName,
            runtimeDatabaseProvider = runtime.Database.ProviderName,
            runtimeUsesExpectedDatabase = string.Equals(Path.GetFullPath(runtimeConnection), Path.GetFullPath(input.RuntimeDatabasePath), StringComparison.Ordinal),
            runtimeMigrationsApplied = runtimeAppliedMigrations.Any(),
            runtimeMigrationsPending = runtimePendingMigrations.Any(),
            iamProvider = iamOptions.Provider,
            iamConnectionName = iamOptions.ConnectionName,
            iamDatabaseProvider = iam.Database.ProviderName,
            iamUsesExpectedDatabase = string.Equals(Path.GetFullPath(iamConnection), Path.GetFullPath(input.IamDatabasePath), StringComparison.Ordinal),
            iamMigrationsApplied = iamAppliedMigrations.Any(),
            iamMigrationsPending = iamPendingMigrations.Any(),
            databasesAreDistinct = !string.Equals(Path.GetFullPath(runtimeConnection), Path.GetFullPath(iamConnection), StringComparison.Ordinal),
            mappingStoreType = scope.ServiceProvider.GetRequiredService<IClaimMappingStore>().GetType().FullName,
            normalizerType = scope.ServiceProvider.GetRequiredService<IClaimsNormalizer>().GetType().FullName,
            persistenceScope = access.Scope?.Value,
            persistenceAccessPolicy = access.AccessPolicy.ToString(),
            persistenceAcrossScopes = access.AcrossScopes,
            usesConfiguredTenant = string.Equals(access.Scope?.Value, input.TenantId, StringComparison.Ordinal)
        };
    }

    private static async Task<ControlResult> ExecuteControlAsync(
        WebApplication app,
        HostStartupInput input,
        MappingReadCounter mappingReads,
        ControlRequest request)
    {
        var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        switch (request.Command)
        {
            case "describe":
                return new(await DescribeAsync(app, shell, input), Stop: false);
            case "reset-mapping-reads":
                mappingReads.Reset();
                return new(new { reset = true }, Stop: false);
            case "save-rule":
            {
                var command = request.Payload.Deserialize<SaveRuleCommand>(JsonOptions) ?? throw new JsonException();
                var rule = new ClaimMappingRule(
                    command.Id,
                    command.TenantId,
                    command.Provider,
                    command.MatchClaimType,
                    command.MatchValue,
                    new HashSet<string>(command.GrantRoles, StringComparer.Ordinal),
                    new HashSet<string>(command.GrantPermissions, StringComparer.Ordinal),
                    command.Order,
                    command.StopOnMatch);
                var previousControl = FixturePersistenceControl.Value;
                FixturePersistenceControl.Value = PersistenceAccessContext.Scoped(new PersistenceScope(command.TenantId));
                try
                {
                    await using var scope = shell.ServiceProvider.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IClaimMappingStore>().SaveAsync(rule);
                }
                finally
                {
                    FixturePersistenceControl.Value = previousControl;
                }
                return new(new { saved = true, id = command.Id }, Stop: false);
            }
            case "list-rules":
            {
                var command = request.Payload.Deserialize<ListRulesCommand>(JsonOptions) ?? throw new JsonException();
                await using var scope = shell.ServiceProvider.CreateAsyncScope();
                var rules = await scope.ServiceProvider.GetRequiredService<IClaimMappingStore>()
                    .ListForProviderAsync(command.TenantId, command.Provider);
                return new(new
                {
                    rules = rules.OrderBy(rule => rule.Id, StringComparer.Ordinal).Select(rule => new
                    {
                        rule.Id,
                        rule.TenantId,
                        rule.Provider,
                        grantRoles = rule.GrantRoles.OrderBy(value => value, StringComparer.Ordinal),
                        grantPermissions = rule.GrantPermissions.OrderBy(value => value, StringComparer.Ordinal)
                    }).ToArray()
                }, Stop: false);
            }
            case "seed-executable":
            {
                var command = request.Payload.Deserialize<SeedExecutableCommand>(JsonOptions) ?? throw new JsonException();
                var executable = RuntimeEventExecutableTestFixture.Create(command.Prefix);
                await using var scope = shell.ServiceProvider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
                    new WorkflowExecutableSourceReference(
                        $"{command.Prefix}-published-reference",
                        executable.Identity.ArtifactId,
                        "WorkflowDefinitionVersion",
                        executable.Identity.DefinitionId,
                        executable.Identity.ArtifactVersion,
                        executable.Identity.DefinitionId,
                        executable.Identity.DefinitionVersionId,
                        executable.Identity.ArtifactVersion,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        WorkflowExecutableReferenceScope.Published));
                return new(new
                {
                    artifactId = executable.Identity.ArtifactId,
                    definitionId = executable.Identity.DefinitionId,
                    artifactVersion = executable.Identity.ArtifactVersion
                }, Stop: false);
            }
            case "snapshot":
            {
                var command = request.Payload.Deserialize<SnapshotCommand>(JsonOptions) ?? new SnapshotCommand(null);
                return new(await SnapshotAsync(shell, mappingReads, command.ExecutionId), Stop: false);
            }
            case "shutdown":
                return new(new { stopped = true }, Stop: true);
            default:
                throw new InvalidOperationException("Unknown fixture control.");
        }
    }

    private static async Task<object> SnapshotAsync(IShell shell, MappingReadCounter mappingReads, string? executionId)
    {
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        var iam = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
        var executionStore = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        var activityStore = scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var bookmarkStore = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
        var execution = executionId is null ? null : await executionStore.FindAsync(executionId);
        IReadOnlyList<ActivityExecutionState> activities = executionId is null
            ? Array.Empty<ActivityExecutionState>()
            : await activityStore.ListAllAsync(executionId);
        IReadOnlyCollection<BookmarkState> bookmarks = executionId is null
            ? Array.Empty<BookmarkState>()
            : await bookmarkStore.ListAllBookmarkStatesAsync(executionId);
        var access = scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;

        return new
        {
            mappingReadCount = mappingReads.Count,
            workflowExecutionStateRows = await runtime.WorkflowExecutionStates.CountAsync(),
            activityExecutionStateRows = await runtime.ActivityExecutionStates.CountAsync(),
            bookmarkRows = await runtime.Bookmarks.CountAsync(),
            workflowStatus = execution?.Status.ToString(),
            activityStatuses = activities.Select(activity => activity.Status.ToString()).ToArray(),
            bookmarks = bookmarks.Select(bookmark => new
            {
                bookmark.StimulusType,
                bookmark.StimulusHash
            }).ToArray(),
            userCount = await iam.Users.CountAsync(),
            externalIdentityCount = await iam.ExternalIdentities.CountAsync(),
            persistenceScope = access.Scope?.Value,
            persistenceAccessPolicy = access.AccessPolicy.ToString(),
            persistenceAcrossScopes = access.AcrossScopes
        };
    }

    private static async Task WriteAsync(object receipt)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(receipt, JsonOptions));
        await Console.Out.FlushAsync();
    }

    private static async Task<string> HashAssemblyAsync()
    {
        await using var assembly = File.OpenRead(typeof(Program).Assembly.Location);
        return Convert.ToHexString(await SHA256.HashDataAsync(assembly));
    }

    private static string SafeExceptionTypeName(Exception exception)
    {
        var name = exception.GetType().FullName ?? exception.GetType().Name;
        return name.Length <= 160 ? name : name[..160];
    }

    private sealed record HostStartupInput(
        string Authority,
        string Audience,
        string ProviderId,
        string TenantId,
        string PersistenceScope,
        string IamDatabasePath,
        string RuntimeDatabasePath,
        string LocksDirectory)
    {
        public void Validate()
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Authority);
            ArgumentException.ThrowIfNullOrWhiteSpace(Audience);
            ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
            ArgumentException.ThrowIfNullOrWhiteSpace(TenantId);
            ArgumentException.ThrowIfNullOrWhiteSpace(PersistenceScope);
            ArgumentException.ThrowIfNullOrWhiteSpace(IamDatabasePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(RuntimeDatabasePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(LocksDirectory);
        }
    }

    private sealed record ControlRequest(string Command, JsonElement Payload);
    private sealed record ControlResult(object Value, bool Stop);
    private sealed record SaveRuleCommand(
        string Id,
        string TenantId,
        string Provider,
        string MatchClaimType,
        string MatchValue,
        string[] GrantRoles,
        string[] GrantPermissions,
        int Order,
        bool StopOnMatch);
    private sealed record ListRulesCommand(string TenantId, string Provider);
    private sealed record SeedExecutableCommand(string Prefix);
    private sealed record SnapshotCommand(string? ExecutionId);

    private sealed class RequestControlledAccessContextAccessor(
        IPersistenceAccessContextAccessor initialized,
        IHttpContextAccessor httpContextAccessor,
        string tenantId) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current
        {
            get
            {
                var httpContext = httpContextAccessor.HttpContext;
                if (httpContext is null && FixturePersistenceControl.Value is { } fixtureControl)
                    return fixtureControl;

                var probe = httpContext?.Request.Headers["X-Worker-Persistence-Probe"].ToString();
                return probe switch
                {
                    "mismatch" => PersistenceAccessContext.Scoped(new PersistenceScope($"{tenantId}-other")),
                    "global" => PersistenceAccessContext.Global,
                    "privileged" => PersistenceAccessContext.PrivilegedScoped(
                        new PersistenceScope(tenantId), new PersistenceAccessPurpose("worker-oidc-fixture-control")),
                    "across" => PersistenceAccessContext.PrivilegedAcrossScopes(
                        new PersistenceAccessPurpose("worker-oidc-fixture-control")),
                    _ => initialized.Current
                };
            }
        }
    }

    private sealed class MappingReadCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CountIfMappingRead(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CountIfMappingRead(command);
            return ValueTask.FromResult(result);
        }

        private void CountIfMappingRead(DbCommand command)
        {
            var sql = command.CommandText.TrimStart();
            if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                sql.Contains(IdentityIamEfModule.ClaimMappingTableName, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _count);
        }
    }
}

public sealed class WorkerOidcToolingShellDefaults : IEfToolingShellDefaults
{
    public void Configure(CShells.Configuration.ShellBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
    }
}
