using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    private const string JwtBearerScheme = "Elsa.Identity.Oidc.Jwt";
    private const string NormalizedAuthenticationType = OidcBearerNormalizationEvents.NormalizedAuthenticationType;

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
        CandidateConfiguration candidateConfiguration;
        var startupStage = "build";

        try
        {
            candidateConfiguration = CandidateConfiguration.Load(input);
            app = CreateApp(input, mappingReads, candidateConfiguration.Configuration);
            app.MapShells();
            startupStage = "start";
            await app.StartAsync();
            address = GetListeningAddress(app);
            startupStage = "activate";
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(input.ShellId);
            startupStage = "describe";
            readyData = await DescribeAsync(app, shell, input, candidateConfiguration.FileHashes);
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
                    if (request.ResetEpochAfterOperationEntry && request.Command != "list-rules")
                    {
                        await WriteAsync(new { status = "error", code = "control-input-invalid" });
                        continue;
                    }

                    using var operation = mappingReads.BeginControlOperation(request.Command);
                    if (request.ResetEpochAfterOperationEntry)
                        mappingReads.Reset();
                    var result = await ExecuteControlAsync(activeApp, input, mappingReads, candidateConfiguration.FileHashes, request);
                    await WriteAsync(new
                    {
                        status = "ok",
                        operationId = operation.Tag.OperationId,
                        operationKind = operation.Tag.OperationKind,
                        operationCategory = operation.Tag.ContextCategory,
                        operationEpoch = operation.Tag.OperationEpoch,
                        data = result.Value
                    });
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

    private static WebApplication CreateApp(
        HostStartupInput input,
        MappingReadCounter mappingReads,
        IConfiguration candidateConfiguration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Configuration.AddConfiguration(candidateConfiguration);

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
                input.PersistenceScope,
                mappingReads));

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
            .WithConfigurationProvider(candidateConfiguration));

        var app = builder.Build();
        mappingReads.SetHttpContextAccessor(app.Services.GetRequiredService<IHttpContextAccessor>());
        app.Use(async (context, next) =>
        {
            using var operation = mappingReads.BeginHttpOperation(context);
            context.Response.Headers[MappingReadCounter.ResponseOperationIdHeader] = operation.Tag.OperationId;
            await next();
        });
        return app;
    }

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
        HostStartupInput input,
        IReadOnlyDictionary<string, string> candidateFileHashes)
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
            shell = input.ShellId,
            environment = input.Environment,
            candidateFileHashes,
            candidateAppsettingsOverlayLoaded = app.Configuration["WorkerProfileCandidate:Layer"] == "environment",
            candidateShellOverlayLoaded = app.Configuration[$"CShells:Shells:{input.ShellId}:Configuration:WorkerProfileCandidate:Layer"] == "environment",
            tenantId = oidc.TenantId,
            providerId = oidc.ProviderId,
            oidcAudienceSha256 = HashString(oidc.Audience ?? string.Empty),
            oidcAuthorityConfigured = !string.IsNullOrWhiteSpace(oidc.Authority),
            oidcClientIdConfigured = !string.IsNullOrWhiteSpace(oidc.ClientId),
            oidcRequireHttpsMetadata = oidc.RequireHttpsMetadata,
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
            runtimeUsesExpectedDatabase = SameDatabase(runtimeConnection,
                GetConnectionDataSource(app.Configuration, runtimeOptions.ConnectionName)),
            runtimeMigrationsApplied = runtimeAppliedMigrations.Any(),
            runtimeMigrationsPending = runtimePendingMigrations.Any(),
            iamProvider = iamOptions.Provider,
            iamConnectionName = iamOptions.ConnectionName,
            iamDatabaseProvider = iam.Database.ProviderName,
            iamUsesExpectedDatabase = SameDatabase(iamConnection,
                GetConnectionDataSource(app.Configuration, iamOptions.ConnectionName)),
            iamMigrationsApplied = iamAppliedMigrations.Any(),
            iamMigrationsPending = iamPendingMigrations.Any(),
            databasesAreDistinct = !string.Equals(Path.GetFullPath(runtimeConnection), Path.GetFullPath(iamConnection), StringComparison.Ordinal),
            mappingStoreType = scope.ServiceProvider.GetRequiredService<IClaimMappingStore>().GetType().FullName,
            normalizerType = scope.ServiceProvider.GetRequiredService<IClaimsNormalizer>().GetType().FullName,
            persistenceScope = access.Scope?.Value,
            persistenceAccessPolicy = access.AccessPolicy.ToString(),
            persistenceAcrossScopes = access.AcrossScopes,
            usesConfiguredTenant = string.Equals(access.Scope?.Value, oidc.TenantId, StringComparison.Ordinal)
        };
    }

    private static async Task<ControlResult> ExecuteControlAsync(
        WebApplication app,
        HostStartupInput input,
        MappingReadCounter mappingReads,
        IReadOnlyDictionary<string, string> candidateFileHashes,
        ControlRequest request)
    {
        var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(input.ShellId);
        switch (request.Command)
        {
            case "describe":
                return new(await DescribeAsync(app, shell, input, candidateFileHashes), Stop: false);
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
        var workflowExecutionStateRows = await runtime.WorkflowExecutionStates.CountAsync();
        var activityExecutionStateRows = await runtime.ActivityExecutionStates.CountAsync();
        var bookmarkRows = await runtime.Bookmarks.CountAsync();
        var userCount = await iam.Users.CountAsync();
        var externalIdentityCount = await iam.ExternalIdentities.CountAsync();
        var access = scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        var mappingReadSnapshot = mappingReads.Snapshot();

        return new
        {
            mappingReadCount = mappingReadSnapshot.MappingReadCount,
            lifetimeMappingReadCount = mappingReadSnapshot.LifetimeMappingReadCount,
            epochStartLifetimeMappingReadCount = mappingReadSnapshot.EpochStartLifetimeMappingReadCount,
            mappingReadEpoch = mappingReadSnapshot.MappingReadEpoch,
            observationRecordsTruncated = mappingReadSnapshot.ObservationRecordsTruncated,
            workflowExecutionStateRows,
            activityExecutionStateRows,
            bookmarkRows,
            workflowStatus = execution?.Status.ToString(),
            activityStatuses = activities.Select(activity => activity.Status.ToString()).ToArray(),
            bookmarks = bookmarks.Select(bookmark => new
            {
                bookmark.StimulusType,
                bookmark.StimulusHash
            }).ToArray(),
            userCount,
            externalIdentityCount,
            persistenceScope = access.Scope?.Value,
            persistenceAccessPolicy = access.AccessPolicy.ToString(),
            persistenceAcrossScopes = access.AcrossScopes,
            persistenceAccessObservations = mappingReadSnapshot.PersistenceAccessObservations,
            mappingReadObservations = mappingReadSnapshot.MappingReadObservations
        };
    }

    private static async Task WriteAsync(object receipt)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(receipt, JsonOptions));
        await Console.Out.FlushAsync();
    }

    private static string HashString(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool SameDatabase(string actual, string configured)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(configured))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(actual), Path.GetFullPath(configured), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string GetConnectionDataSource(IConfiguration configuration, string? connectionName)
    {
        var connectionString = string.IsNullOrWhiteSpace(connectionName)
            ? null
            : configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;
        var values = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return values.TryGetValue("Data Source", out var value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
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
        string CandidateDirectory,
        string ShellId,
        string Environment,
        string PersistenceScope)
    {
        public void Validate()
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(CandidateDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(ShellId);
            ArgumentException.ThrowIfNullOrWhiteSpace(Environment);
            ArgumentException.ThrowIfNullOrWhiteSpace(PersistenceScope);
            if (!IsSafeIdentity(ShellId) || !IsSafeIdentity(Environment))
                throw new JsonException();
        }

        private static bool IsSafeIdentity(string value) => value.Length <= 128 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private sealed record CandidateConfiguration(
        IConfigurationRoot Configuration,
        IReadOnlyDictionary<string, string> FileHashes)
    {
        public static CandidateConfiguration Load(HostStartupInput input)
        {
            var directory = Path.GetFullPath(input.CandidateDirectory);
            var names = new[]
            {
                "appsettings.json",
                $"appsettings.{input.Environment}.json",
                "shells.json",
                $"shells.{input.Environment}.json"
            };
            var bytes = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var path = Path.Join(directory, name);
                if (!File.Exists(path))
                    throw new FileNotFoundException("A selected Worker candidate file is missing.");
                bytes.Add(name, File.ReadAllBytes(path));
            }

            var hashes = bytes.ToDictionary(
                item => item.Key,
                item => Convert.ToHexString(SHA256.HashData(item.Value)),
                StringComparer.Ordinal);
            var builder = new ConfigurationBuilder();
            var streams = names.Select(name => new MemoryStream(bytes[name], writable: false)).ToArray();
            try
            {
                foreach (var stream in streams)
                    builder.AddJsonStream(stream);
                return new CandidateConfiguration(builder.Build(), hashes);
            }
            finally
            {
                foreach (var stream in streams)
                    stream.Dispose();
            }
        }
    }

    private sealed record ControlRequest(string Command, JsonElement Payload, bool ResetEpochAfterOperationEntry = false);
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
        string tenantId,
        MappingReadCounter observations) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current
        {
            get
            {
                var httpContext = httpContextAccessor.HttpContext;
                var category = observations.PersistenceAccessCategory(
                    httpContext?.Request.Headers[MappingReadCounter.PersistenceProbeHeader].ToString(),
                    httpContext is not null);
                observations.ObservePersistenceAccess(httpContext, category);
                if (httpContext is null && FixturePersistenceControl.Value is { } fixtureControl)
                    return fixtureControl;

                return category switch
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
        public const string ResponseOperationIdHeader = "X-Worker-Oidc-Fixture-Operation";
        public const string PersistenceProbeHeader = "X-Worker-Persistence-Probe";
        private const int MaxObservationRecords = 256;
        private static readonly object HttpOperationItemKey = new();
        private readonly object _gate = new();
        private readonly AsyncLocal<OperationTag?> _currentOperation = new();
        private readonly List<PersistenceAccessObservation> _persistenceAccessObservations = [];
        private readonly List<MappingReadObservation> _mappingReadObservations = [];
        private IHttpContextAccessor? _httpContextAccessor;
        private long _nextOperationId;
        private long _epoch;
        private long _lifetimeCount;
        private long _epochStartCount;
        private bool _observationRecordsTruncated;

        public void SetHttpContextAccessor(IHttpContextAccessor accessor) => _httpContextAccessor = accessor;

        public OperationScope BeginHttpOperation(HttpContext context)
        {
            var operation = CreateOperation(
                "http-request",
                PersistenceAccessCategory(context.Request.Headers[PersistenceProbeHeader].ToString(), hasRequest: true));
            context.Items[HttpOperationItemKey] = operation;
            return EnterOperation(operation);
        }

        public OperationScope BeginControlOperation(string command) =>
            EnterOperation(CreateOperation("fixture-control", ControlCategory(command)));

        public void ObservePersistenceAccess(HttpContext? httpContext, string persistenceAccessCategory)
        {
            var evidence = CaptureOperationEvidence(httpContext);
            lock (_gate)
            {
                AppendBounded(_persistenceAccessObservations, new PersistenceAccessObservation(
                    persistenceAccessCategory,
                    evidence.OperationId,
                    evidence.OperationKind,
                    evidence.ContextCategory,
                    evidence.OperationSource,
                    evidence.OperationEpoch,
                    _epoch,
                    evidence.DirectHttpContextPresent,
                    evidence.DirectHttpOperationId,
                    evidence.FlowedOperationId,
                    evidence.FlowedOperationKind,
                    evidence.FlowedContextCategory));
            }
        }

        public CounterSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new CounterSnapshot(
                    _epoch,
                    _lifetimeCount - _epochStartCount,
                    _lifetimeCount,
                    _epochStartCount,
                    _persistenceAccessObservations.ToArray(),
                    _mappingReadObservations.ToArray(),
                    _observationRecordsTruncated);
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _epoch++;
                _epochStartCount = _lifetimeCount;
                _persistenceAccessObservations.Clear();
                _mappingReadObservations.Clear();
                _observationRecordsTruncated = false;
            }
        }

        public string PersistenceAccessCategory(string? probe, bool hasRequest) =>
            !hasRequest ? "no-request" : ProbeCategory(probe);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CountIfMappingRead(command, eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CountIfMappingRead(command, eventData);
            return ValueTask.FromResult(result);
        }

        private void CountIfMappingRead(DbCommand command, CommandEventData eventData)
        {
            var sql = command.CommandText.TrimStart();
            if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                sql.Contains(IdentityIamEfModule.ClaimMappingTableName, StringComparison.OrdinalIgnoreCase))
            {
                var evidence = CaptureOperationEvidence(_httpContextAccessor?.HttpContext);
                var callerPath = CaptureCallerPath();
                lock (_gate)
                {
                    _lifetimeCount++;
                    AppendBounded(_mappingReadObservations, new MappingReadObservation(
                        evidence.OperationId,
                        evidence.OperationKind,
                        evidence.ContextCategory,
                        evidence.OperationSource,
                        evidence.OperationKind == "fixture-control" && evidence.ContextCategory == "list-rules"
                            ? _epoch : evidence.OperationEpoch,
                        _epoch,
                        _lifetimeCount - _epochStartCount,
                        _lifetimeCount,
                        evidence.DirectHttpContextPresent,
                        evidence.DirectHttpOperationId,
                        evidence.FlowedOperationId,
                        evidence.FlowedOperationKind,
                        evidence.FlowedContextCategory,
                        callerPath,
                        eventData.CommandId.ToString("N"),
                        eventData.Context?.ContextId.InstanceId.ToString("N"),
                        eventData.Context is null ? "none" : eventData.Context is IdentityIamDbContext ? "identity-iam" : "other"));
                }
            }
        }

        private static string CaptureCallerPath()
        {
            const int maxFrames = 64;
            const string claimMappingStore = "Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores.EfClaimMappingStore";
            const string schemaStampedTable = "Elsa.Persistence.EntityFramework.SchemaBackfill.EfSchemaStampedTable";

            var frames = new StackTrace(fNeedFileInfo: false).GetFrames();
            if (frames is null)
                return "unknown";

            foreach (var frame in frames.Take(maxFrames))
            {
                var method = frame.GetMethod();
                var frameType = method?.DeclaringType;
                if (frameType is null)
                    continue;

                if (string.Equals(frameType.FullName, claimMappingStore, StringComparison.Ordinal) ||
                    string.Equals(frameType.DeclaringType?.FullName, claimMappingStore, StringComparison.Ordinal))
                    return "claim-mapping-store";

                if (string.Equals(frameType.FullName, schemaStampedTable, StringComparison.Ordinal))
                {
                    if (method!.Name == "CountCoreAsync")
                        return "schema-stamped-count";
                    if (method.Name == "PageCoreAsync")
                        return "schema-stamped-page";
                }

                if (!string.Equals(frameType.DeclaringType?.FullName, schemaStampedTable, StringComparison.Ordinal) ||
                    !string.Equals(method!.Name, "MoveNext", StringComparison.Ordinal))
                    continue;

                if (frameType.Name.StartsWith("<CountCoreAsync>d__", StringComparison.Ordinal))
                    return "schema-stamped-count";
                if (frameType.Name.StartsWith("<PageCoreAsync>d__", StringComparison.Ordinal))
                    return "schema-stamped-page";
            }

            return "unknown";
        }

        private OperationTag CreateOperation(string operationKind, string contextCategory)
        {
            lock (_gate)
                return new OperationTag($"op-{++_nextOperationId}", operationKind, contextCategory, _epoch);
        }

        private void AppendBounded<T>(List<T> records, T observation)
        {
            if (records.Count == MaxObservationRecords)
            {
                records.RemoveAt(0);
                _observationRecordsTruncated = true;
            }

            records.Add(observation);
        }

        private static string ProbeCategory(string? probe) => probe switch
        {
            "mismatch" => "mismatch",
            "global" => "global",
            "privileged" => "privileged",
            "across" => "across",
            _ => "ordinary"
        };

        private static string ControlCategory(string command) => command switch
        {
            "describe" => "describe",
            "reset-mapping-reads" => "reset-mapping-reads",
            "save-rule" => "save-rule",
            "list-rules" => "list-rules",
            "seed-executable" => "seed-executable",
            "snapshot" => "snapshot",
            "shutdown" => "shutdown",
            _ => "other-control"
        };

        private OperationScope EnterOperation(OperationTag operation)
        {
            var previous = _currentOperation.Value;
            _currentOperation.Value = operation;
            return new OperationScope(this, operation, previous);
        }

        private OperationEvidence CaptureOperationEvidence(HttpContext? httpContext)
        {
            var directOperation = httpContext is not null &&
                                  httpContext.Items.TryGetValue(HttpOperationItemKey, out var value)
                ? value as OperationTag
                : null;
            var flowedOperation = _currentOperation.Value;
            var correlatedOperation = httpContext is null ? flowedOperation : directOperation;
            var operationSource = directOperation is not null
                ? "direct-http-context"
                : httpContext is not null
                    ? "direct-http-context-untracked"
                    : flowedOperation?.OperationKind switch
                    {
                        "http-request" => "flowed-http-operation",
                        "fixture-control" => "flowed-control-operation",
                        _ => "none"
                    };

            return new OperationEvidence(
                correlatedOperation?.OperationId ?? "none",
                correlatedOperation?.OperationKind ?? operationSource,
                correlatedOperation?.ContextCategory ?? operationSource,
                operationSource,
                correlatedOperation?.OperationEpoch,
                httpContext is not null,
                directOperation?.OperationId,
                flowedOperation?.OperationId,
                flowedOperation?.OperationKind,
                flowedOperation?.ContextCategory);
        }

        public sealed record OperationTag(
            string OperationId,
            string OperationKind,
            string ContextCategory,
            long OperationEpoch);

        public sealed record PersistenceAccessObservation(
            string PersistenceAccessCategory,
            string OperationId,
            string OperationKind,
            string ContextCategory,
            string OperationSource,
            long? OperationEpoch,
            long ObservationEpoch,
            bool DirectHttpContextPresent,
            string? DirectHttpOperationId,
            string? FlowedOperationId,
            string? FlowedOperationKind,
            string? FlowedContextCategory);

        public sealed record MappingReadObservation(
            string OperationId,
            string OperationKind,
            string ContextCategory,
            string OperationSource,
            long? OperationEpoch,
            long QueryEpoch,
            long MappingReadCount,
            long LifetimeMappingReadCount,
            bool DirectHttpContextPresent,
            string? DirectHttpOperationId,
            string? FlowedOperationId,
            string? FlowedOperationKind,
            string? FlowedContextCategory,
            string CallerPath,
            string CommandId,
            string? DbContextId,
            string DbContextKind);

        public sealed record CounterSnapshot(
            long MappingReadEpoch,
            long MappingReadCount,
            long LifetimeMappingReadCount,
            long EpochStartLifetimeMappingReadCount,
            PersistenceAccessObservation[] PersistenceAccessObservations,
            MappingReadObservation[] MappingReadObservations,
            bool ObservationRecordsTruncated);

        private sealed record OperationEvidence(
            string OperationId,
            string OperationKind,
            string ContextCategory,
            string OperationSource,
            long? OperationEpoch,
            bool DirectHttpContextPresent,
            string? DirectHttpOperationId,
            string? FlowedOperationId,
            string? FlowedOperationKind,
            string? FlowedContextCategory);

        public sealed class OperationScope(
            MappingReadCounter counter,
            OperationTag tag,
            OperationTag? previous) : IDisposable
        {
            public OperationTag Tag { get; } = tag;

            public void Dispose() => counter._currentOperation.Value = previous;
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
