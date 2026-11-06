using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Jobs.Api.Tests;

internal sealed class JobsApiHost(WebApplication app, HttpClient client, string directory, bool ownsDirectory, MutableJobAuthorization authorization) : IAsyncDisposable
{
    internal const string Partition = "api-owned-partition";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    internal static readonly JobHandlerSupport Handler = new(new("api.report", "1"), new("api.report.input", 1), new("api.report.result", 1), "api.report.implementation.v1");
    internal HttpClient Client => client;
    internal MutableJobAuthorization Authorization => authorization;
    internal IReadOnlyList<RouteEndpoint> Endpoints => ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>().ToArray();

    internal static async Task<JobsApiHost> StartAsync(string? storageDirectory = null, bool installJobsProducer = true,
        Action<IServiceCollection>? configure = null, Action<JobsEntityFrameworkCoreOptions>? persistence = null)
    {
        var ownsDirectory = storageDirectory is null;
        var directory = storageDirectory ?? Path.Combine(Path.GetTempPath(), "elsa-jobs-api-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().SetApplicationName("Elsa.Jobs.Api.Tests").PersistKeysToFileSystem(new(Path.Combine(directory, "keys")));
        builder.Services.AddAuthentication(JobsApiTestAuthentication.SchemeId).AddScheme<AuthenticationSchemeOptions, JobsApiTestAuthentication>(JobsApiTestAuthentication.SchemeId, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string> { JobsApiTestAuthentication.SchemeId });
        var authorization = new MutableJobAuthorization(); builder.Services.AddSingleton(authorization);
        builder.Services.ReplaceCurrentSubjectPermissionAuthorizer<MutableJobAuthorization>();
        builder.Services.Replace(ServiceDescriptor.Singleton<ICurrentSubjectPermissionAuthorizer>(authorization));
        builder.Services.AddJobsEntityFrameworkCore(options =>
        {
            options.ConnectionString = "Data Source=" + Path.Combine(directory, "jobs.db") + ";Pooling=False";
            persistence?.Invoke(options);
        });
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
        if (installJobsProducer) builder.Services.AddElsaJobs();
        builder.Services.AddJobsApi(options => options.PersistencePartition = Partition);
        configure?.Invoke(builder.Services);
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); JobsApi.MapJobsApi(app);
        try { await app.StartAsync(); return new(app, app.GetTestClient(), directory, ownsDirectory, authorization); }
        catch { await app.DisposeAsync(); if (ownsDirectory) Directory.Delete(directory, recursive: true); throw; }
    }

    internal void SignIn(string subject = "alice", string tenant = "tenant-a", string? variant = null)
    {
        foreach (var header in new[] { "X-User", "X-Tenant", "X-Variant" }) client.DefaultRequestHeaders.Remove(header);
        client.DefaultRequestHeaders.Add("X-User", subject); client.DefaultRequestHeaders.Add("X-Tenant", tenant);
        if (variant is not null) client.DefaultRequestHeaders.Add("X-Variant", variant);
    }
    internal async Task<T> AuthorityAsync<T>(Func<IJobCommands, IJobQueries, Task<T>> action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IJobCommands>(), scope.ServiceProvider.GetRequiredService<IJobQueries>());
    }
    internal static JobRequest Request(object? input = null, JobAssociation? association = null) => new(Handler.Handler,
        new(Handler.InputSchema, JsonSerializer.SerializeToElement(input ?? new { Order = 42 })), Handler.ResultSchema, "reports",
        new(new(3, TimeSpan.Zero, TimeSpan.Zero, 1, 0, true), TimeSpan.FromMinutes(1), TimeSpan.FromDays(30)), null, association);
    internal async Task<JobAcceptance> SeedAsync(string jobId = "job", string tenant = "tenant-a", object? input = null, JobAssociation? association = null,
        string queue = "reports")
    {
        var request = Request(input, association) with { Queue = queue };
        return await AuthorityAsync(async (commands, _) => (await commands.AdmitAsync(new(new(Partition, tenant), jobId,
            "seed-" + jobId, JobRequestDigest.Compute(request), request, null))).Acceptance!);
    }
    internal Task<JobSnapshot?> ReadAsync(string jobId = "job", string tenant = "tenant-a") => AuthorityAsync(async (_, queries) => await queries.FindAsync(new(Partition, tenant), jobId));
    internal Task<HttpResponseMessage> PostAsync(string path, object value) => client.PostAsJsonAsync(path, value, Json);
    internal Task<HttpResponseMessage> PutAsync(string path, object value) => client.PutAsJsonAsync(path, value, Json);
    public async ValueTask DisposeAsync()
    {
        client.Dispose(); await app.DisposeAsync(); if (ownsDirectory && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

internal sealed class MutableJobAuthorization : ICurrentSubjectPermissionAuthorizer
{
    internal readonly HashSet<string> Granted = new(JobsPermissions.All, StringComparer.Ordinal);
    internal readonly List<CurrentSubjectPermissionRequest> Requests = [];
    internal readonly HashSet<(string Kind, string Id)> DeniedResources = [];
    internal bool DenyResources;
    internal bool Throw;
    public ValueTask<CurrentSubjectPermissionResult> AuthorizeAsync(CurrentSubjectPermissionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request);
        if (Throw) throw new InvalidOperationException("AUTHORIZATION_FAILURE_CANARY");
        if (request.Resource is JobApiResource resource && resource.TenantId != request.TenantId)
            throw new InvalidOperationException("A tenant mismatch reached the identity provider.");
        return ValueTask.FromResult(Granted.Contains(request.Permission)
            && !(request.Resource is JobApiResource candidate && (DenyResources || DeniedResources.Contains((candidate.Kind, candidate.Id))))
            ? CurrentSubjectPermissionResult.Granted : CurrentSubjectPermissionResult.Denied("Current decision denied."));
    }
}

internal sealed class JobsApiTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeId = "JobsApiTests";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var variant = Request.Headers["X-Variant"].ToString();
        var claims = new List<Claim> { new(IdentityClaimTypes.Normalized, "v1"), new(IdentityClaimTypes.TenantId, Request.Headers["X-Tenant"].ToString()), new(IdentityClaimTypes.Permission, "*") };
        if (variant != "missing-user") claims.Add(new(IdentityClaimTypes.UserId, user.ToString()));
        if (variant == "duplicate-user") claims.Add(new(IdentityClaimTypes.UserId, "forged"));
        if (variant == "duplicate-tenant") claims.Add(new(IdentityClaimTypes.TenantId, "forged-tenant"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, variant == "untrusted" ? "Untrusted" : SchemeId));
        return Task.FromResult(AuthenticateResult.Success(new(principal, SchemeId)));
    }
}
