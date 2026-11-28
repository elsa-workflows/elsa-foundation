using CShells.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Studio.Hosting;
using Elsa.Studio.Backends;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

public sealed class BackendProfilesTests : IAsyncLifetime
{
    private const string Route = "/_elsa/studio/backends/profiles";
    private WebApplication? _app;
    private readonly IdentityBackend _identity = new();

    private async Task<HttpClient> StartAsync(bool auth = true, bool personal = true, string? database = null, bool profilesOnly = false)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Studio:Auth:Enabled"] = auth.ToString(),
            ["Studio:BackendBaseUrl"] = "https://identity.example/",
            ["Studio:HostId"] = "studio-one",
            ["Studio:Backends:AllowPersonalProfiles"] = personal.ToString(),
            ["Studio:Backends:Profiles:0:Id"] = "managed",
            ["Studio:Backends:Profiles:0:Name"] = "Managed backend",
            ["Studio:Backends:Profiles:0:BaseUrl"] = "https://managed.example/tenant-a/",
            ["Studio:Backends:AuthenticationProfiles:corporate:Enabled"] = "true",
            ["Studio:Backends:AuthenticationProfiles:corporate:PrivateKey"] = "never-in-browser",
            ["Studio:BackendServerBaseUrl"] = "http://internal-only.example/",
            ["Studio:BackendModuleManagementApiKey"] = "private-management-key",
            ["CShells:Shells:Default:Features:StudioApi"] = "true",
            ["CShells:Shells:Default:Features:BackendsStudio"] = "true"
        });
        if (profilesOnly) builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
        { ["Studio:BackendBaseUrl"] = null, ["Studio:BackendServerBaseUrl"] = null, ["Studio:Backends:DefaultId"] = "managed" });
        if (database is not null)
        {
            builder.Services.AddDbContext<StudioPreferencesSqliteDbContext>(options => options.UseSqlite($"Data Source={database}"));
            builder.Services.AddScoped<StudioPreferencesDbContext>(services => services.GetRequiredService<StudioPreferencesSqliteDbContext>());
            builder.Services.AddScoped<IStudioPreferenceStore, EfStudioPreferenceStore>();
        }
        var studio = builder.AddElsaStudio(options => options.ShellsFile = null);
        builder.Services.AddStudioBackendsHostConfiguration(builder.Configuration);
        studio.ConfigureShells(shells => shells.WithAssemblies(typeof(BackendsStudioFeature).Assembly));
        builder.Services.RouteAllBackendClientsThrough(_identity);
        _app = builder.Build();
        if (database is not null)
        {
            using var scope = _app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<StudioPreferencesSqliteDbContext>().Database.EnsureCreatedAsync();
        }
        _app.UseElsaStudio();
        _app.MapElsaStudio();
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private static object Value(string id = "mine", string url = "https://personal.example/shell-b/") => new
    {
        profiles = new[] { new { id, name = "Personal backend", baseUrl = url, environment = "Test" } }
    };

    private static async Task<HttpResponseMessage> ReadAsync(HttpClient client, string bearer, string? forgedHost = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Route);
        request.Headers.Authorization = new("Bearer", bearer);
        if (forgedHost is not null) request.Headers.Add("X-Studio-Host-Id", forgedHost);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> WriteAsync(HttpClient client, string bearer, object value, string? revision = null, bool precondition = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Route) { Content = JsonContent.Create(new { schemaVersion = 1, value }) };
        request.Headers.Authorization = new("Bearer", bearer);
        if (precondition) request.Headers.TryAddWithoutValidation(revision is null ? "If-None-Match" : "If-Match", revision is null ? "*" : $"\"{revision}\"");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Saves_and_reads_only_verified_user_tenant_and_host_scope()
    {
        using var client = await StartAsync();
        using var saved = await WriteAsync(client, "alice-a", Value());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var own = await ReadAsync(client, "alice-a", "attacker-selected-host");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains("Personal backend", await own.Content.ReadAsStringAsync());
        using var tenant = await ReadAsync(client, "alice-b");
        using var subject = await ReadAsync(client, "bob-a");
        Assert.Equal(HttpStatusCode.NotFound, tenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, subject.StatusCode);
    }

    [Fact]
    public async Task Revision_preconditions_refuse_missing_stale_and_concurrent_overwrites()
    {
        using var client = await StartAsync();
        using var missing = await WriteAsync(client, "alice-a", Value(), precondition: false);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using var first = await WriteAsync(client, "alice-a", Value());
        var json = await first.Content.ReadFromJsonAsync<JsonElement>();
        var revision = json.GetProperty("revision").GetString()!;
        using var stale = await WriteAsync(client, "alice-a", Value("other"), "stale-revision");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var updates = await Task.WhenAll(WriteAsync(client, "alice-a", Value("one"), revision), WriteAsync(client, "alice-a", Value("two"), revision));
        try
        {
            Assert.Single(updates, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(updates, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in updates) response.Dispose(); }
    }

    [Theory]
    [InlineData("https://user:password@personal.example/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://personal.example/?token=secret")]
    public async Task Rejects_profile_urls_that_embed_credentials_or_are_not_backend_addresses(string url)
    {
        using var client = await StartAsync();
        using var saved = await WriteAsync(client, "alice-a", Value(url: url));
        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
    }

    [Fact]
    public async Task Rejects_unknown_metadata_and_configured_identity_overrides()
    {
        using var client = await StartAsync();
        using var configured = await WriteAsync(client, "alice-a", Value("managed"));
        Assert.Equal(HttpStatusCode.BadRequest, configured.StatusCode);
        using var secrets = await WriteAsync(client, "alice-a", new { profiles = new[] { new { id = "mine", name = "Secret", baseUrl = "https://personal.example/", environment = "Test", managementApiKey = "secret" } } });
        Assert.Equal(HttpStatusCode.BadRequest, secrets.StatusCode);
        using var excessive = await WriteAsync(client, "alice-a", new { profiles = Enumerable.Range(0, 33).Select(index => new { id = $"p{index}", name = "Many", baseUrl = "https://personal.example/", environment = "Test" }).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode);
    }

    [Fact]
    public async Task Preference_permissions_are_independent_of_administrator_permissions()
    {
        using var client = await StartAsync();
        using var admin = await WriteAsync(client, "admin-only", Value());
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        using var reader = await WriteAsync(client, "read-only", Value());
        Assert.Equal(HttpStatusCode.Forbidden, reader.StatusCode);
        using var owner = await WriteAsync(client, "alice-a", Value());
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Authentication_disabled_demo_does_not_create_a_shared_anonymous_preference_record()
    {
        using var client = await StartAsync(auth: false);
        using var saved = await WriteAsync(client, "alice-a", Value());
        Assert.Equal(HttpStatusCode.Unauthorized, saved.StatusCode);
    }

    [Fact]
    public async Task Host_policy_can_disable_personal_profile_writes()
    {
        using var client = await StartAsync(personal: false);
        using var saved = await WriteAsync(client, "alice-a", Value());
        Assert.Equal(HttpStatusCode.Forbidden, saved.StatusCode);
    }

    [Fact]
    public async Task Browser_configuration_projects_only_public_profile_and_authentication_metadata()
    {
        using var client = await StartAsync();
        var script = await client.GetStringAsync("/studio-runtime.js");
        Assert.Contains("Managed backend", script);
        Assert.Contains("tenant-a", script);
        Assert.Contains("authenticationProfiles", script);
        Assert.DoesNotContain("never-in-browser", script);
        Assert.DoesNotContain("private-management-key", script);
        Assert.DoesNotContain("internal-only", script);
    }

    [Fact]
    public async Task Missing_verified_tenant_cannot_write_shared_preferences()
    {
        using var client = await StartAsync();
        using var saved = await WriteAsync(client,"no-tenant",Value());
        Assert.Equal(HttpStatusCode.Unauthorized,saved.StatusCode);
    }

    [Fact]
    public async Task Profile_only_authority_preserves_the_complete_shell_for_bridge_introspection()
    {
        using var client = await StartAsync(profilesOnly:true);
        var runtime = await client.GetStringAsync("/studio-runtime.js");
        Assert.Contains("\"backendBaseUrl\":\"https://managed.example/tenant-a/\"",runtime);
        using var saved = await WriteAsync(client,"alice-a",Value());
        Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
        Assert.Contains("https://managed.example/tenant-a/_elsa/identity/session",_identity.Paths);
    }

    [Fact]
    public async Task Feature_disable_and_reenable_remove_api_and_preserve_profiles_within_host_lifetime()
    {
        using var client=await StartAsync();
        Assert.Equal(HttpStatusCode.OK,(await WriteAsync(client,"alice-a",Value())).StatusCode);
        var registry=_app!.Services.GetRequiredService<CShells.Lifecycle.IShellRegistry>();
        await registry.ReloadAsync("Default");
        Assert.Equal(HttpStatusCode.OK,(await ReadAsync(client,"alice-a")).StatusCode);
        _app.Configuration["CShells:Shells:Default:Features:BackendsStudio"]="false";
        await registry.ReloadAsync("Default");
        Assert.Equal(HttpStatusCode.NotFound,(await ReadAsync(client,"alice-a")).StatusCode);
        Assert.DoesNotContain("\"backends\"",await client.GetStringAsync("/studio-runtime.js"));
        _app.Configuration["CShells:Shells:Default:Features:BackendsStudio"]="true";
        await registry.ReloadAsync("Default");
        using var restored=await ReadAsync(client,"alice-a");
        Assert.Equal(HttpStatusCode.OK,restored.StatusCode);
        Assert.Contains("Personal backend",await restored.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Identical_host_and_identity_keys_do_not_share_default_store_between_host_instances()
    {
        using var first=await StartAsync();
        Assert.Equal(HttpStatusCode.OK,(await WriteAsync(first,"alice-a",Value())).StatusCode);
        var other=new BackendProfilesTests();
        try {
            using var second=await other.StartAsync();
            Assert.Equal(HttpStatusCode.NotFound,(await ReadAsync(second,"alice-a")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await ReadAsync(first,"alice-a")).StatusCode);
        } finally {await other.DisposeAsync();}
    }

    [Fact]
    public async Task External_durable_provider_preserves_profiles_across_host_restart()
    {
        var database = System.IO.Path.Combine(System.IO.Path.GetTempPath(),$"studio-backends-{Guid.NewGuid():N}.db");
        try
        {
            using (var first = await StartAsync(database:database))
            using (var saved = await WriteAsync(first,"alice-a",Value())) Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
            await _app!.DisposeAsync(); _app = null;
            using var second = await StartAsync(database:database);
            using var read = await ReadAsync(second,"alice-a");
            Assert.Equal(HttpStatusCode.OK,read.StatusCode);
            Assert.Contains("Personal backend",await read.Content.ReadAsStringAsync());
        }
        finally {if (_app is not null) {await _app.DisposeAsync();_app=null;} Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.File.Delete(database);}
    }

    [Fact]
    public async Task Chunked_oversized_bodies_are_bounded_before_parsing_or_persistence()
    {
        using var client = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, Route)
        {Content = new ChunkedJson(System.Text.Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x',40*1024) + "\"}"))};
        request.Headers.Authorization = new("Bearer","alice-a");
        request.Headers.TryAddWithoutValidation("If-None-Match","*");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,response.StatusCode);
        using var read = await ReadAsync(client,"alice-a");
        Assert.Equal(HttpStatusCode.NotFound,read.StatusCode);
    }

    [Fact]
    public async Task Unknown_wrapper_metadata_is_rejected_instead_of_silently_accepted()
    {
        using var client = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put,Route)
        {Content=JsonContent.Create(new {schemaVersion=1,value=Value(),token="not-profile-metadata"})};
        request.Headers.Authorization=new("Bearer","alice-a");
        request.Headers.TryAddWithoutValidation("If-None-Match","*");
        using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }

    private sealed class ChunkedJson(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) {length=0;return false;}
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() { if (_app is not null) await _app.DisposeAsync(); }

    private sealed class IdentityBackend : HttpMessageHandler
    {
        internal List<string> Paths {get;} = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsoluteUri);
            var token = request.Headers.Authorization?.Parameter ?? "";
            var permissions = token == "admin-only" ? new[] { "module-management:manage" }
                : token == "read-only" ? ["studio/preferences:read"] : ["studio/preferences:read", "studio/preferences:write"];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                status = "authenticated", subject = token.StartsWith("bob", StringComparison.Ordinal) ? "bob" : "alice",
                tenantId = token == "no-tenant" ? null : token.EndsWith("-b", StringComparison.Ordinal) ? "tenant-b" : "tenant-a", permissions
            }) });
        }
    }
}
