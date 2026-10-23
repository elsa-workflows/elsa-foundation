using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.Capabilities;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Api;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Api;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Etl.Tests;

/// <summary>
/// Shared real TestServer around the ETL Design or runtime API, SQLite persistence, migrations, and Foundation
/// Identity permission pipeline. The test authentication and persistence headers stand in for host-selected
/// identity and partition context; neither value is read from an authored ETL request body.
/// </summary>
internal sealed class EtlDesignApiTestHost : IAsyncDisposable
{
    private const string AuthenticationScheme = "EtlDesignApiTest";
    private const string IdentityHeader = "X-Etl-Test-Identity";
    private const string TenantHeader = "X-Etl-Test-Tenant";
    private const string PartitionHeader = "X-Etl-Test-Partition";
    private const string PermissionsHeader = "X-Etl-Test-Permissions";
    private const string NormalizedHeader = "X-Etl-Test-Normalized";
    private const string DuplicateTenantHeader = "X-Etl-Test-Duplicate-Tenant";
    private const string PersistenceContextHeader = "X-Etl-Test-Persistence-Context";
    private const string CursorSigningKey = "etl-design-api-test-cursor-signing-key-v1";
    private readonly string _directory;

    private EtlDesignApiTestHost(IHost host, string directory)
    {
        Host = host;
        Client = host.GetTestClient();
        _directory = directory;
    }

    public HttpClient Client { get; }

    public IHost Host { get; }

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static async Task<EtlDesignApiTestHost> StartAsync(
        bool registerBindingCatalog = true,
        IReadOnlyList<HostFileBinding>? bindings = null,
        bool registerDesignPersistence = true,
        Action<IServiceCollection>? configureServices = null,
        bool runtimeOnly = false,
        bool registerRuntimePersistence = true,
        int maximumPageSize = 100)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Design.Api.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "etl-design.db")};Pooling=False";

        var builder = new HostBuilder();
        builder.ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.UseSetting(WebHostDefaults.ApplicationKey, "elsa-etl-design-api-tests");
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddPersistenceCore();
                services.AddAuthentication(AuthenticationScheme)
                    .AddScheme<AuthenticationSchemeOptions, EtlDesignApiTestAuthenticationHandler>(AuthenticationScheme, _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options =>
                    options.NormalizedAuthenticationTypes = new HashSet<string>([AuthenticationScheme], StringComparer.Ordinal));

                new ApiCapabilitiesFeature().ConfigureServices(services);
                if (!runtimeOnly)
                {
                    new EtlDesignFeature().ConfigureServices(services);
                    if (registerDesignPersistence)
                    {
                        new EtlDesignEntityFrameworkCoreFeature
                        {
                            Provider = "Sqlite",
                            ConnectionString = connectionString
                        }.ConfigureServices(services);
                        services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
                    }
                    new EtlDesignApiFeature { CursorSigningKey = CursorSigningKey }.ConfigureServices(services);
                }
                else
                {
                    if (registerRuntimePersistence)
                    {
                        new EtlRuntimeEntityFrameworkCoreFeature
                        {
                            Provider = "Sqlite",
                            ConnectionString = $"Data Source={Path.Combine(directory, "etl-runtime.db")};Pooling=False",
                            MaximumPageSize = maximumPageSize
                        }.ConfigureServices(services);
                        services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
                    }
                    new EtlRuntimeApiFeature { CursorSigningKey = CursorSigningKey }.ConfigureServices(services);
                }

                if (registerBindingCatalog && !runtimeOnly)
                {
                    var configuredBindings = bindings ?? DefaultBindings(directory);
                    services.AddScoped(_ => new HostFileBindings(configuredBindings));
                    services.AddScoped<IEtlBindingCatalog>(provider => provider.GetRequiredService<HostFileBindings>());
                }

                configureServices?.Invoke(services);
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                // This fixture middleware models the host selecting one persistence partition. The HTTP API
                // does not receive partition authority from request DTOs or query parameters.
                app.Use(async (context, next) =>
                {
                    var partition = context.Request.Headers[PartitionHeader].ToString();
                    if (string.IsNullOrWhiteSpace(partition))
                        partition = "partition-a";
                    var accessContext = context.Request.Headers[PersistenceContextHeader].ToString() switch
                    {
                        "global" => PersistenceAccessContext.Global,
                        "privileged-global" => PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("etl-design-api-test")),
                        _ => PersistenceAccessContext.Scoped(new PersistenceScope(partition))
                    };
                    context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>().Bind(accessContext);
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    if (runtimeOnly)
                        EtlRuntimeApi.MapEtlRuntimeApi(endpoints);
                    else
                        EtlDesignApi.MapEtlDesignApi(endpoints);
                });
            });
        });

        var host = builder.Build();
        try
        {
            await host.StartAsync();
            return new EtlDesignApiTestHost(host, directory);
        }
        catch
        {
            host.Dispose();
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public static Task<EtlDesignApiTestHost> StartRuntimeAsync(
        bool registerRuntimePersistence = true,
        int maximumPageSize = 100,
        Action<IServiceCollection>? configureServices = null) =>
        StartAsync(registerBindingCatalog: false, registerDesignPersistence: false, configureServices: configureServices,
            runtimeOnly: true, registerRuntimePersistence: registerRuntimePersistence, maximumPageSize: maximumPageSize);

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? tenant = "tenant-a",
        string partition = "partition-a",
        IReadOnlyCollection<string>? permissions = null,
        bool authenticated = true,
        bool normalized = true,
        bool duplicateTenant = false,
        string persistenceContext = "scoped")
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        AddTestContext(request, tenant, partition, permissions, authenticated, normalized, duplicateTenant, persistenceContext);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        string content,
        string? tenant = "tenant-a",
        string partition = "partition-a",
        IReadOnlyCollection<string>? permissions = null,
        bool authenticated = true,
        bool chunked = false,
        bool normalized = true,
        bool duplicateTenant = false,
        string persistenceContext = "scoped")
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = chunked
                ? new ChunkedJsonContent(content)
                : new StringContent(content, Encoding.UTF8, "application/json")
        };
        request.Content.Headers.ContentType = new("application/json");
        if (chunked)
            request.Headers.TransferEncodingChunked = true;
        AddTestContext(request, tenant, partition, permissions, authenticated, normalized, duplicateTenant, persistenceContext);
        return await Client.SendAsync(request);
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return StopAndDeleteAsync();
    }

    private async ValueTask StopAndDeleteAsync()
    {
        try
        {
            await Host.StopAsync();
        }
        finally
        {
            Host.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private static void AddTestContext(
        HttpRequestMessage request,
        string? tenant,
        string partition,
        IReadOnlyCollection<string>? permissions,
        bool authenticated,
        bool normalized,
        bool duplicateTenant,
        string persistenceContext)
    {
        request.Headers.TryAddWithoutValidation(PartitionHeader, partition);
        request.Headers.TryAddWithoutValidation(PersistenceContextHeader, persistenceContext);
        if (!authenticated)
            return;

        request.Headers.TryAddWithoutValidation(IdentityHeader, "authenticated");
        request.Headers.TryAddWithoutValidation(NormalizedHeader, normalized ? "true" : "false");
        if (duplicateTenant)
            request.Headers.TryAddWithoutValidation(DuplicateTenantHeader, "true");
        if (tenant is not null)
            request.Headers.TryAddWithoutValidation(TenantHeader, tenant);
        if (permissions is { Count: > 0 })
            request.Headers.TryAddWithoutValidation(PermissionsHeader, string.Join(",", permissions));
    }

    private static IReadOnlyList<HostFileBinding> DefaultBindings(string directory)
    {
        var scope = new EtlScope("tenant-a", "partition-a");
        var sourceHash = new string('a', 64);
        var lookupHash = new string('b', 64);
        string PathFor(string name) => Path.GetFullPath(Path.Combine(directory, name));

        return
        [
            new(scope, "source", EtlFileRole.Source, "source-v1", PathFor("source.csv"), true, sourceHash, ImmutableInput: true),
            new(scope, "lookup", EtlFileRole.Lookup, "lookup-v1", PathFor("lookup.csv"), true, lookupHash, ImmutableInput: true),
            new(scope, "output", EtlFileRole.MainOutput, "output-v1", PathFor("main"), true, AtomicPublication: true),
            new(scope, "errors", EtlFileRole.ErrorOutput, "errors-v1", PathFor("errors"), true, AtomicPublication: true),
            new(scope, "source-extra", EtlFileRole.Source, "source-extra-v1", PathFor("source-extra.csv"), true, new string('c', 64), ImmutableInput: true),
            new(new EtlScope("tenant-b", "partition-a"), "foreign-tenant", EtlFileRole.Source,
                "foreign-v1", PathFor("foreign-tenant.csv"), true, new string('d', 64), ImmutableInput: true),
            new(new EtlScope("tenant-a", "partition-b"), "foreign-partition", EtlFileRole.Source,
                "foreign-v1", PathFor("foreign-partition.csv"), true, new string('e', 64), ImmutableInput: true)
        ];
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private sealed class ChunkedJsonContent(string content) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(content);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes, 0, _bytes.Length);
    }

    private sealed class EtlDesignApiTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(IdentityHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            var tenant = Request.Headers[TenantHeader].ToString();
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Provider, "etl-design-test-provider"),
                new(ClaimTypes.NameIdentifier, "etl-design-api-test-actor")
            };
            if (!string.Equals(Request.Headers[NormalizedHeader].ToString(), "false", StringComparison.OrdinalIgnoreCase))
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
            if (!string.IsNullOrWhiteSpace(tenant))
                claims.Add(new Claim(IdentityClaimTypes.TenantId, tenant));
            if (string.Equals(Request.Headers[DuplicateTenantHeader].ToString(), "true", StringComparison.OrdinalIgnoreCase))
                claims.Add(new Claim(IdentityClaimTypes.TenantId, "tenant-b"));
            claims.AddRange(Request.Headers[PermissionsHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
