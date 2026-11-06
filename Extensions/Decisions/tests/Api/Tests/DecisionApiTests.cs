using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Decisions.Api;
using Elsa.Decisions.Core;
using Elsa.Decisions.Design;
using Elsa.Decisions.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Decisions.Api.Tests;

public sealed class DecisionApiTests
{
    [Fact]
    public async Task Bom_xml_import_keeps_original_signature_through_publish_export_and_evaluation()
    {
        await using var fixture = await ApiFixture.Create(); var xml = "\uFEFF" + ApiFixture.Xml;
        var expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml)));
        using var save = await fixture.Send(HttpMethod.Post, "/decisions", new SaveDecisionRequest("bom", "BOM import", xml));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var asset = (await save.Content.ReadFromJsonAsync<DecisionAssetView>())!;
        Assert.Equal(xml, asset.Xml); Assert.Equal(expectedHash, asset.SourceHash);
        using var publish = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/publish", new PublishDecisionRequest("publish", asset.Revision));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var version = (await publish.Content.ReadFromJsonAsync<DecisionVersionView>())!; Assert.Equal(expectedHash, version.SourceHash);
        using var export = await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}/export?versionId={version.Id}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode); Assert.Equal(System.Text.Encoding.UTF8.GetBytes(xml), await export.Content.ReadAsByteArrayAsync());
        using var evaluate = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/evaluate", new EvaluateDecisionRequest(version.Id, ApiFixture.Input("950")));
        Assert.Equal(HttpStatusCode.OK, evaluate.StatusCode);
        using var body = JsonDocument.Parse(await evaluate.Content.ReadAsStringAsync());
        Assert.Equal("Success", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Automatic", body.RootElement.GetProperty("result").GetProperty("value").GetString());
        using var doubled = await fixture.Send(HttpMethod.Post, "/decisions", new SaveDecisionRequest("double-bom", "Malformed", "\uFEFF" + xml));
        Assert.Equal(HttpStatusCode.BadRequest, doubled.StatusCode);
    }

    [Fact]
    public async Task Normal_endpoints_authenticate_authorize_and_isolate_two_tenants()
    {
        await using var fixture = await ApiFixture.Create();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync("/decisions")).StatusCode);
        var asset = await fixture.CreateAsset();
        Assert.NotNull(asset.Xml);
        var denied = await fixture.Send(HttpMethod.Get, "/decisions/" + asset.Id, grants: "decisions:evaluate");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var foreign = await fixture.Send(HttpMethod.Get, "/decisions/" + asset.Id, tenant: "tenant-b");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var inventory = await fixture.Send(HttpMethod.Get, "/decisions", tenant: "tenant-b");
        var body = await inventory.Content.ReadFromJsonAsync<DecisionAssetsResponse>();
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task Exact_version_http_evaluation_matches_direct_library_and_needs_no_source_grant()
    {
        await using var fixture = await ApiFixture.Create(); var asset = await fixture.CreateAsset();
        var publish = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/publish", new PublishDecisionRequest("publish", asset.Revision));
        var version = await publish.Content.ReadFromJsonAsync<DecisionVersionView>(); Assert.NotNull(version);
        var response = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/evaluate", new EvaluateDecisionRequest(version.Id, ApiFixture.Input("950")), grants: "decisions:evaluate");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var engine = new DecisionEngine(); var direct = engine.Evaluate(engine.Compile(ApiFixture.Xml).Artifact!, ApiFixture.Input("950"));
        Assert.Equal(direct.Status, result.RootElement.GetProperty("status").GetString());
        Assert.Equal(direct.ContentHash, result.RootElement.GetProperty("contentHash").GetString());
        Assert.True(JsonElement.DeepEquals(direct.Result, result.RootElement.GetProperty("result")));
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}/export", grants: "decisions:evaluate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}/runs", grants: "decisions:evaluate")).StatusCode);
    }

    [Fact]
    public async Task Protected_source_case_output_and_history_are_independent_of_ordinary_grants()
    {
        await using var fixture = await ApiFixture.Create(); var asset = await fixture.CreateAsset(true);
        var publish = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/publish", new PublishDecisionRequest("publish", asset.Revision));
        var version = await publish.Content.ReadFromJsonAsync<DecisionVersionView>();
        var source = await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}", grants: "decisions:read,decisions:source");
        var hidden = await source.Content.ReadFromJsonAsync<DecisionAssetView>(); Assert.Null(hidden!.Xml);
        var evaluate = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/evaluate", new EvaluateDecisionRequest(version!.Id, ApiFixture.Input("1")), grants: "decisions:evaluate");
        Assert.Equal(HttpStatusCode.Forbidden, evaluate.StatusCode);
        Assert.DoesNotContain("Automatic", await evaluate.Content.ReadAsStringAsync());
        var saved = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/cases", new SaveDecisionCaseRequest("case", "case", ApiFixture.Input("950"), "Success", ApiFixture.Expected("Automatic")));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var cases = await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}/cases", grants: "decisions:read,decisions:test");
        var caseViews = await cases.Content.ReadFromJsonAsync<DecisionCasesResponse>();
        Assert.Null(caseViews!.Items[0].Inputs); Assert.Null(caseViews.Items[0].ExpectedResult);
        var item = (await saved.Content.ReadFromJsonAsync<DecisionCaseView>())!;
        var test = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/test", new TestDecisionRequest("run", asset.SourceHash, [new(item.Id, item.Revision)], VersionId: version.Id), grants: "decisions:read,decisions:test");
        Assert.Equal(HttpStatusCode.Forbidden, test.StatusCode);
    }

    [Fact]
    public async Task Exact_concurrency_conflict_keeps_server_content_and_safe_error()
    {
        await using var fixture = await ApiFixture.Create(); var asset = await fixture.CreateAsset();
        var first = await fixture.Send(HttpMethod.Put, $"/decisions/{asset.Id}", new SaveDecisionRequest("first", "First", ApiFixture.Xml, ExpectedRevision: asset.Revision));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stale = await fixture.Send(HttpMethod.Put, $"/decisions/{asset.Id}", new SaveDecisionRequest("stale", "Stale", ApiFixture.Xml.Replace("Automatic", "SECRET-BUSINESS-VALUE"), ExpectedRevision: asset.Revision));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.DoesNotContain("SECRET-BUSINESS-VALUE", await stale.Content.ReadAsStringAsync());
        var current = await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}");
        Assert.Equal("First", (await current.Content.ReadFromJsonAsync<DecisionAssetView>())!.Name);
    }

    [Fact]
    public async Task Revoked_grants_take_effect_on_next_operation_without_trusting_cached_ui()
    {
        await using var fixture = await ApiFixture.Create(); var asset = await fixture.CreateAsset();
        var validation = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/validate", new ValidateDecisionRequest(asset.Revision));
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        var revoked = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/publish", new PublishDecisionRequest("publish", asset.Revision), grants: "decisions:manage,decisions:read");
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        var versions = await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}/versions");
        Assert.Empty((await versions.Content.ReadFromJsonAsync<DecisionVersionsResponse>())!.Items);
    }

    [Fact]
    public async Task Clone_to_draft_is_revision_checked_and_requires_source_access()
    {
        await using var fixture = await ApiFixture.Create(); var asset = await fixture.CreateAsset();
        var published = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/publish", new PublishDecisionRequest("publish", asset.Revision));
        var version = (await published.Content.ReadFromJsonAsync<DecisionVersionView>())!;
        var current = (await (await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}")).Content.ReadFromJsonAsync<DecisionAssetView>())!;
        var noSource = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/clone-to-draft", new CloneDecisionRequest("clone", current.Revision, version.Id), grants: "decisions:read,decisions:manage");
        Assert.Equal(HttpStatusCode.Forbidden, noSource.StatusCode);
        var cloned = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/clone-to-draft", new CloneDecisionRequest("clone", current.Revision, version.Id));
        Assert.Equal(HttpStatusCode.OK, cloned.StatusCode);
        var stale = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/clone-to-draft", new CloneDecisionRequest("stale", current.Revision, version.Id));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Theory]
    [InlineData("duplicate-tenant")]
    [InlineData("duplicate-user")]
    [InlineData("blank-tenant")]
    [InlineData("blank-user")]
    [InlineData("alternate-tenant-only")]
    [InlineData("raw-user-only")]
    public async Task Ambiguous_or_noncanonical_tenant_user_claims_cannot_choose_persistence_scope(string mode)
    {
        await using var fixture = await ApiFixture.Create();
        var response = await fixture.Send(HttpMethod.Post, "/decisions", new SaveDecisionRequest("create", "Spoofed", ApiFixture.Xml), mode: mode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var inventory = await fixture.Send(HttpMethod.Get, "/decisions");
        Assert.Empty((await inventory.Content.ReadFromJsonAsync<DecisionAssetsResponse>())!.Items);
    }

    [Fact]
    public async Task Canonical_persisted_user_id_owns_operation_replay_not_external_subject_alias()
    {
        await using var fixture = await ApiFixture.Create(); var asset = await fixture.CreateAsset();
        var response = await fixture.Send(HttpMethod.Post, "/decisions", new SaveDecisionRequest("create", "Approval", ApiFixture.Xml), mode: "external-subject-alias");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(asset.Id, (await response.Content.ReadFromJsonAsync<DecisionAssetView>())!.Id);
    }

    [Fact]
    public async Task Malformed_or_unsafe_input_has_safe_failure_without_storing_values()
    {
        await using var fixture = await ApiFixture.Create();
        var response = await fixture.Send(HttpMethod.Post, "/decisions", new SaveDecisionRequest("unsafe", "Unsafe", "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///private/secret'>]><x>&e;</x>"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("file:///", await response.Content.ReadAsStringAsync());
        var list = await fixture.Send(HttpMethod.Get, "/decisions"); Assert.Empty((await list.Content.ReadFromJsonAsync<DecisionAssetsResponse>())!.Items);
    }
}

internal sealed class ApiFixture : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly string directory;
    public HttpClient Client { get; }
    private ApiFixture(WebApplication app, string directory) { this.app = app; this.directory = directory; Client = app.GetTestClient(); }
    public static async Task<ApiFixture> Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-decisions-api-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var factory = new ApiContextFactory(Path.Combine(directory, "decisions.db"));
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(HeaderAuthentication.SchemeName).AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(HeaderAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { HeaderAuthentication.SchemeName });
        builder.Services.AddSingleton<IDbContextFactory<ApiContext>>(factory); builder.Services.AddDecisionsEntityFrameworkCore<ApiContext>();
        var engine = new DecisionEngine(); builder.Services.AddSingleton<IDecisionCompiler>(engine); builder.Services.AddSingleton<IDecisionEvaluator>(engine);
        new DecisionsApiFeature().ConfigureServices(builder.Services);
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); new DecisionsApiFeature().MapEndpoints(app, app.Environment);
        await app.StartAsync(); return new(app, directory);
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string grants = "*", string tenant = "tenant-a", string? mode = null)
    {
        var request = new HttpRequestMessage(method, path); request.Headers.Add("X-Test-Subject", "user-a"); request.Headers.Add("X-Test-Tenant", tenant); request.Headers.Add("X-Test-Grants", grants);
        if (mode is not null) request.Headers.Add("X-Test-Claim-Mode", mode);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }
    public async Task<DecisionAssetView> CreateAsset(bool sensitive = false)
    {
        var response = await Send(HttpMethod.Post, "/decisions", new SaveDecisionRequest("create", "Approval", Xml, IsSensitive: sensitive));
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, text);
        return JsonSerializer.Deserialize<DecisionAssetView>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); Directory.Delete(directory, true); }
    public static IReadOnlyDictionary<string, DecisionValue> Input(string value) => new Dictionary<string, DecisionValue> { ["amount"] = new("number", value) };
    public static JsonElement Expected(string value) => JsonSerializer.SerializeToElement(new DecisionValue("string", value), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public const string Xml = """
      <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="definitions" name="Approval" namespace="urn:elsa:decisions:test">
        <decision id="approval" name="Approval"><variable id="decision-variable" name="Approval" typeRef="string" />
          <decisionTable id="table" hitPolicy="UNIQUE">
            <input id="amount-column"><inputExpression id="amount-expression" typeRef="number"><text>amount</text></inputExpression></input>
            <output id="approval-column" name="approval" typeRef="string" />
            <rule id="automatic"><inputEntry id="automatic-input"><text>&lt;= 1000</text></inputEntry><outputEntry id="automatic-output"><text>"Automatic"</text></outputEntry></rule>
            <rule id="manager"><inputEntry id="manager-input"><text>&gt; 1000</text></inputEntry><outputEntry id="manager-output"><text>"Manager"</text></outputEntry></rule>
          </decisionTable>
        </decision>
      </definitions>
      """;
}
internal sealed class ApiContextFactory(string file) : IDbContextFactory<ApiContext>
{ public ApiContext CreateDbContext() => new(new DbContextOptionsBuilder<ApiContext>().UseSqlite("Data Source=" + file + ";Pooling=False").Options); }
internal sealed class ApiContext(DbContextOptions<ApiContext> options) : DbContext(options)
{ protected override void OnModelCreating(ModelBuilder builder) => builder.AddDecisionsModel(Database.ProviderName!); }
internal sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "decisions-test";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Subject", out var subject)) return Task.FromResult(AuthenticateResult.NoResult());
        var grants = Request.Headers["X-Test-Grants"].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var claims = grants.Select(x => new Claim(IdentityClaimTypes.Permission, x)).Append(new Claim(ClaimTypes.NameIdentifier, subject.ToString()))
            .Append(new Claim(IdentityClaimTypes.UserId, subject.ToString()))
            .Append(new Claim(IdentityClaimTypes.Normalized, "v1")).Append(new Claim(IdentityClaimTypes.Provider, SchemeName))
            .Append(new Claim(IdentityClaimTypes.TenantId, Request.Headers["X-Test-Tenant"].ToString())).ToList();
        var mode = Request.Headers["X-Test-Claim-Mode"].ToString();
        if (mode == "duplicate-tenant") claims.Add(new(IdentityClaimTypes.TenantId, "tenant-b"));
        if (mode == "duplicate-user") claims.Add(new(IdentityClaimTypes.UserId, "another-user"));
        if (mode == "blank-tenant") { claims.RemoveAll(x => x.Type == IdentityClaimTypes.TenantId); claims.Add(new(IdentityClaimTypes.TenantId, "")); }
        if (mode == "blank-user") { claims.RemoveAll(x => x.Type == IdentityClaimTypes.UserId); claims.Add(new(IdentityClaimTypes.UserId, "")); }
        if (mode == "alternate-tenant-only") { claims.RemoveAll(x => x.Type == IdentityClaimTypes.TenantId); claims.Add(new("tenant_id", "tenant-b")); }
        if (mode == "raw-user-only") claims.RemoveAll(x => x.Type == IdentityClaimTypes.UserId);
        if (mode == "external-subject-alias") { claims.RemoveAll(x => x.Type == ClaimTypes.NameIdentifier); claims.Add(new(ClaimTypes.NameIdentifier, "external-provider-subject")); claims.Add(new("sub", "external-provider-subject")); }
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) { Response.StatusCode = 401; return Task.CompletedTask; }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) { Response.StatusCode = 403; return Task.CompletedTask; }
}
