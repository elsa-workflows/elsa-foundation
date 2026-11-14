using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Weaver.Api;
using Elsa.Weaver.Api.Constants;
using Elsa.Weaver.Api.Extensions;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class HostSupportContextTests
{
    [Theory]
    [InlineData("plain")]
    [InlineData("unicode")]
    [InlineData("html")]
    public async Task Actual_mapped_prompt_admits_typed_client_claim_and_native_session_policy_separately(string encoding)
    {
        var support = new Support();
        using var root = Services(support);
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        var sessions = services.GetRequiredService<IWeaverSessionService>();
        var policy = WeaverPolicy.Default with { Id = "owned-session-policy", DeniedCapabilityIds = ["not-supported"] };
        var session = await sessions.CreateAsync(new("tenant", "actor", "conversation", "test", "explain", null, policy, new Dictionary<string, string>()));
        var content = Content();
        var displayName = new string(encoding == "unicode" ? 'é' : '<', 256);
        if (encoding != "plain")
        {
            content["modules"] = new JsonArray(Enumerable.Range(0, 24).Select(index => (JsonNode)new JsonObject
                { ["id"] = "module-" + index, ["displayName"] = displayName, ["status"] = "available" }).ToArray());
            content["loader"] = new JsonArray(Enumerable.Range(0, 24).Select(index => (JsonNode)new JsonObject
                { ["moduleId"] = "module-" + index, ["status"] = "loaded" }).ToArray());
            content["registrations"] = new JsonArray();
        }
        content["notProviderContent"] = Guid.NewGuid().ToString("N");
        var candidate = Caller(content);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { message = "Explain available support", contextAttachments = new[]
        {
            new { candidate.Id, candidate.Source, candidate.SourceId, candidate.Label, candidate.ModuleId, candidate.ContentType,
                candidate.SchemaVersion, Content = content, candidate.References, candidate.Target, candidate.Client,
                sensitivity = "internal", origin = "authoritativeRead", weaverScopes = new[] { "forged" } }
        }, requestId = "support-admission" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // Model the actual browser's JSON.stringify encoding, without changing product encoding.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Assert.True(System.Text.Encoding.UTF8.GetString(body).Length < 24576);
        var context = Context(services);
        context.Request.RouteValues["sessionId"] = session.Id;
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        var app = new TestEndpointRouteBuilder(services);
        WeaverApi.MapWeaverApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
            item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaWeaverApiEndpointsPostMessage");

        await endpoint.RequestDelegate!(context);

        Assert.Equal(200, context.Response.StatusCode);
        var message = Assert.Single(await sessions.ListMessagesAsync(session.Id));
        Assert.Equal(2, message.Context.Count);
        var browser = Assert.Single(message.Context, item => item.Source == "studio");
        var backend = Assert.Single(message.Context, item => item.Source == "backend");
        Assert.Equal(WeaverContextOrigin.ClientSnapshot, browser.Origin);
        Assert.Equal(WeaverContextOrigin.AuthoritativeRead, backend.Origin);
        Assert.All(message.Context, item =>
        {
            Assert.Equal("Elsa.Weaver", item.ModuleId);
            Assert.Empty(item.WeaverScopes);
            Assert.Empty(item.References);
            Assert.Null(item.Target);
            Assert.Null(item.Client);
            Assert.Equal("weaver.host-support", item.ContributorId);
        });
        Assert.Equal("session", Element(backend).GetProperty("policyScope").GetString());
        Assert.Equal(session.PolicyRevision, Element(backend).GetProperty("policyRevision").GetInt64());
        Assert.Equal(policy.Id, Assert.Single(support.Policies).Id);
        Assert.Equal(policy.DeniedCapabilityIds, support.Policies[0].DeniedCapabilityIds);
        Assert.DoesNotContain("notProviderContent", Element(browser).GetRawText());
        Assert.DoesNotContain("forged", JsonSerializer.Serialize(message.Context));
        Assert.True(JsonSerializer.Serialize(browser.Content).Length <= 24576);
        if (encoding != "plain")
        {
            Assert.Equal("incomplete", Element(browser).GetProperty("completeness").GetString());
            Assert.Equal("incomplete", Element(browser).GetProperty("registrationCompleteness").GetString());
            Assert.InRange(Element(browser).GetProperty("modules").GetArrayLength(), 1, 23);
            Assert.Equal(displayName, Element(browser).GetProperty("modules")[0].GetProperty("displayName").GetString());
            var original = JsonSerializer.Serialize(message.Context);
            var retained = await services.GetRequiredService<IWeaverContextCollector>().CollectAsync(session.Policy,
                new(session.Id, "", new Dictionary<string, string>())
                { Disclosure = new(session.Id, message.Context, [], false) { IsHistorical = true } });
            Assert.True(retained.Succeeded, retained.Error?.Message);
            Assert.Equal("backend", Assert.Single(retained.Value!.Attachments).Source);
            Assert.Equal(original, JsonSerializer.Serialize(message.Context));
        }
    }

    [Fact]
    public async Task Context_descriptor_is_discoverable_without_a_constructor_cycle_or_resource_collection()
    {
        using var root = Services();
        using var scope = root.CreateScope();
        Context(scope.ServiceProvider);
        var source = scope.ServiceProvider.GetRequiredService<IWeaverSupportSource>();
        var observation = await source.ReadAsync("actor", "tenant", WeaverPolicy.Default);
        var descriptor = Assert.Single(observation.Contexts, item => item.Id == "weaver.host-support");
        Assert.Equal("Elsa.Weaver", descriptor.ModuleId);
        Assert.Equal(["weaver.host-support"], descriptor.ContextKinds);
        Assert.Equal(["1"], descriptor.SchemaVersions);
        Assert.Equal(WeaverSupportStatus.Unchecked, descriptor.Authority);
    }

    [Theory]
    [InlineData("unlisted")]
    [InlineData("path")]
    [InlineData("schema")]
    [InlineData("reason")]
    public async Task Unlisted_root_and_nested_fields_never_survive_allowlisted_admission(string field)
    {
        var canary = Guid.NewGuid().ToString("N");
        var content = Content();
        content[field] = canary;
        content["modules"]![0]![field] = canary;
        content["registrations"]![0]![field] = canary;
        var support = new Support();
        using var fixture = new Fixture(support);
        var result = await fixture.Collect(content);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result.Value));
        Assert.Equal(1, support.Calls);
    }

    [Theory]
    [InlineData("side")]
    [InlineData("clientAsserted")]
    [InlineData("missingArray")]
    [InlineData("duplicateModule")]
    [InlineData("duplicateRegistration")]
    [InlineData("moduleStatus")]
    [InlineData("loaderStatus")]
    [InlineData("registrationStatus")]
    [InlineData("oversize")]
    [InlineData("tooManyResources")]
    [InlineData("moduleUrl")]
    [InlineData("absolutePath")]
    [InlineData("ambiguousSegments")]
    public async Task Malformed_or_oversized_claim_is_refused_before_native_support_read(string invalid)
    {
        var content = Content();
        switch (invalid)
        {
            case "ambiguousSegments": content["modules"]![0]!["id"] = "a" + new string('-', 150) + "!"; break;
            case "side": content["side"] = "backend"; break;
            case "clientAsserted": content["clientAsserted"] = false; break;
            case "missingArray": content.Remove("loader"); break;
            case "duplicateModule": content["modules"]!.AsArray().Add(content["modules"]![0]!.DeepClone()); break;
            case "duplicateRegistration": content["registrations"]!.AsArray().Add(content["registrations"]![0]!.DeepClone()); break;
            case "moduleStatus": content["modules"]![0]!["status"] = "executed"; break;
            case "loaderStatus": content["loader"]![0]!["status"] = "available"; break;
            case "registrationStatus": content["registrations"]![0]!["status"] = "authorized"; break;
            case "oversize": content["unlisted"] = new string('x', 32769); break;
            case "tooManyResources": content["registrations"]![0]!["resourceModuleIds"] = new JsonArray(Enumerable.Range(0, 9).Select(index => (JsonNode?)JsonValue.Create("module" + index)).ToArray()); break;
            case "moduleUrl": content["modules"]![0]!["id"] = "https://private.invalid/module"; break;
            case "absolutePath": content["registrations"]![0]!["originModuleId"] = "/private/path"; break;
        }
        var support = new Support();
        using var fixture = new Fixture(support);
        var result = await fixture.Collect(content);
        Assert.False(result.Succeeded);
        Assert.Empty(Assert.IsType<WeaverContextContribution>(result.Value).Attachments);
        Assert.Equal(0, support.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("denied")]
    [InlineData("anonymous")]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("foreign")]
    public async Task Current_native_permission_and_plain_owner_precede_hydration_and_support_read(string invalid)
    {
        var support = new Support();
        using var fixture = new Fixture(support, invalid);
        var result = await fixture.Collect(Content());
        Assert.False(result.Succeeded);
        Assert.Empty(Assert.IsType<WeaverContextContribution>(result.Value).Attachments);
        Assert.Equal(0, fixture.Sessions.HydrationCalls);
        Assert.Equal(invalid == "foreign" ? 1 : 0, fixture.Sessions.OwnerCalls);
        Assert.Equal(0, support.Calls);
    }

    [Fact]
    public async Task Retained_browser_claim_stays_historical_and_current_permission_is_rechecked()
    {
        var support = new Support();
        using var fixture = new Fixture(support);
        var first = await fixture.Collect(Content());
        var originals = Assert.IsType<WeaverContextContribution>(first.Value).Attachments;
        var before = JsonSerializer.Serialize(originals);
        var disclosure = new WeaverContextDisclosureRequest(fixture.Session.Id, originals, [], false) { IsHistorical = true };
        var result = await fixture.Collector.CollectAsync(fixture.Session.Policy,
            new(fixture.Session.Id, "", new Dictionary<string, string>()) { Disclosure = disclosure });
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal("backend", Assert.Single(Assert.IsType<WeaverContextContribution>(result.Value).Attachments).Source);
        Assert.Equal(before, JsonSerializer.Serialize(originals));
        fixture.Permission.Allowed = false;
        var calls = support.Calls;
        var denied = await fixture.Collector.CollectAsync(fixture.Session.Policy,
            new(fixture.Session.Id, "", new Dictionary<string, string>()) { Disclosure = disclosure });
        Assert.False(denied.Succeeded);
        Assert.Equal(calls, support.Calls);
    }

    [Fact]
    public async Task Retained_metadata_with_an_unlisted_field_is_not_authorized_by_resanitizing_it()
    {
        var support = new Support();
        using var fixture = new Fixture(support);
        var first = await fixture.Collect(Content());
        var browser = Assert.Single(first.Value!.Attachments, item => item.Source == "studio");
        var forged = JsonNode.Parse(Element(browser).GetRawText())!.AsObject();
        forged["unlisted"] = Guid.NewGuid().ToString("N");
        var result = await fixture.Collector.CollectAsync(fixture.Session.Policy, new(fixture.Session.Id, "", new Dictionary<string, string>())
        { Disclosure = new(fixture.Session.Id, [browser with { Content = JsonSerializer.SerializeToElement(forged) }], [], false) });
        Assert.False(result.Succeeded);
        Assert.Empty(result.Value!.Attachments);
    }

    [Fact]
    public async Task A_caller_secret_classification_is_preserved_for_the_normal_redaction_pipeline()
    {
        var support = new Support();
        using var fixture = new Fixture(support);
        var canary = Guid.NewGuid().ToString("N");
        var content = Content();
        content["modules"]![0]!["displayName"] = canary;
        var result = await fixture.Collector.CollectAsync(fixture.Session.Policy,
            new(fixture.Session.Id, "/unknown", new Dictionary<string, string>())
            { CallerAttachments = [Caller(content) with { Sensitivity = WeaverContextSensitivity.Secret }] });
        Assert.True(result.Succeeded, result.Error?.Message);
        var redacted = Assert.Single(result.Value!.Attachments, item => item.Source == "studio");
        Assert.Equal(WeaverContextSensitivity.SecretRedacted, redacted.Sensitivity);
        Assert.Null(redacted.Content);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result.Value));
        var retained = await fixture.Collector.CollectAsync(fixture.Session.Policy,
            new(fixture.Session.Id, "", new Dictionary<string, string>())
            { Disclosure = new(fixture.Session.Id, result.Value.Attachments, [], false) });
        Assert.True(retained.Succeeded, retained.Error?.Message);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("policy")]
    [InlineData("principal")]
    public async Task A_change_while_native_support_is_awaited_prevents_accepting_the_observation(string changed)
    {
        var support = new Support();
        using var fixture = new Fixture(support);
        support.Produce = _ =>
        {
            if (changed == "permission") fixture.Permission.Allowed = false;
            if (changed == "policy") fixture.Sessions.Session = fixture.Session with { PolicyRevision = 4 };
            if (changed == "principal") fixture.Context.User = new(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "foreign"), new Claim("elsa.identity.tenant_id", "tenant")], "test"));
            return Task.FromResult(support.View);
        };
        var result = await fixture.Collect(Content());
        Assert.False(result.Succeeded);
        Assert.Empty(result.Value!.Attachments);
        Assert.Equal(1, support.Calls);
    }

    [Theory]
    [InlineData(1, "owner", "principal")]
    [InlineData(1, "hydrate", "principal")]
    [InlineData(1, "owner", "context")]
    [InlineData(1, "hydrate", "context")]
    [InlineData(2, "owner", "principal")]
    [InlineData(2, "hydrate", "principal")]
    [InlineData(2, "owner", "context")]
    [InlineData(2, "hydrate", "context")]
    public async Task A_replaced_same_owner_request_cannot_reuse_permission_across_session_reads(int pass, string boundary, string changed)
    {
        var support = new Support();
        using var fixture = new Fixture(support);
        var accessor = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        var originalPrincipal = fixture.Context.User;
        fixture.Permission.Decide = _ => Task.FromResult(ReferenceEquals(accessor.HttpContext?.User, originalPrincipal)
            ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied("withdrawn permission"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Pause(int calls)
        {
            if (calls != pass) return;
            entered.TrySetResult();
            await release.Task;
        }
        if (boundary == "owner") fixture.Sessions.BeforeOwner = Pause;
        else fixture.Sessions.BeforeHydration = Pause;
        var pending = fixture.Collect(Content());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var replacement = Principal(); // The same actor/tenant, with a different permission-bearing principal.
            if (changed == "principal") fixture.Context.User = replacement;
            else accessor.HttpContext = new DefaultHttpContext { RequestServices = fixture.Services, User = replacement };
            release.SetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(result.Succeeded);
            Assert.Empty(result.Value!.Attachments);
            Assert.Equal(pass - 1, support.Calls);
            Assert.Equal(pass, fixture.Sessions.OwnerCalls);
            Assert.Equal(boundary == "owner" ? pass - 1 : pass, fixture.Sessions.HydrationCalls);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task A_late_permission_allow_after_stop_cannot_start_session_reads()
    {
        var support = new Support();
        using var fixture = new Fixture(support);
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<PermissionEvaluationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Permission.Decide = _ => { entered.SetResult(); return release.Task; };
        var source = Assert.Single(fixture.Services.GetServices<IWeaverContextProvider>(), item => item.Descriptor.Id == "weaver.host-support");
        var pending = source.CollectAsync(new(fixture.Session.Id, "/unknown", new Dictionary<string, string>())
            { CallerAttachments = [Caller(Content())] }, stop.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop.Cancel();
            release.SetResult(PermissionEvaluationResult.Success);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(0, fixture.Sessions.OwnerCalls);
            Assert.Equal(0, fixture.Sessions.HydrationCalls);
            Assert.Equal(0, support.Calls);
        }
        finally { release.TrySetResult(PermissionEvaluationResult.Success); }
    }

    [Fact]
    public async Task Invalid_native_replacement_data_is_unknown_rather_than_disclosed_or_called_complete()
    {
        var support = new Support { View = new(WeaverSupportCompleteness.Complete,
            [new("https://private.invalid/tool", "module", "1", WeaverExecutionLocation.Server, WeaverSupportStatus.AvailableHint, WeaverSupportStatus.AvailableHint)], []) };
        using var fixture = new Fixture(support);
        var result = await fixture.Collect(Content());
        Assert.True(result.Succeeded, result.Error?.Message);
        var backend = Assert.Single(result.Value!.Attachments, item => item.Source == "backend");
        Assert.Equal("unavailable", Element(backend).GetProperty("support").GetProperty("completeness").GetString());
        Assert.DoesNotContain("private.invalid", Element(backend).GetRawText());
    }

    [Fact]
    public async Task Old_prompts_without_the_new_kind_do_not_resolve_permissions_sessions_or_support()
    {
        var support = new Support();
        using var fixture = new Fixture(support, "missing");
        var result = await fixture.Collector.CollectAsync(fixture.Session.Policy, new(fixture.Session.Id, "/unknown", new Dictionary<string, string>()));
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Empty(result.Value!.Attachments);
        Assert.Equal(0, fixture.Sessions.OwnerCalls);
        Assert.Equal(0, fixture.Sessions.HydrationCalls);
        Assert.Equal(0, support.Calls);
    }

    private static JsonObject Content() => JsonNode.Parse("""
        {"side":"studio","clientAsserted":true,"completeness":"complete",
         "modules":[{"id":"@custom/resource-owner","displayName":"Resource owner","status":"available"}],
         "registrationCompleteness":"complete","loader":[{"moduleId":"custom.satellite","status":"loaded"}],
         "registrations":[{"kind":"contextProviders","id":"custom.context","originModuleId":"custom.satellite",
             "declaredModuleId":"@custom/resource-owner","resourceModuleIds":["@custom/resource-owner"],"status":"available"}]}
        """)!.AsObject();

    private static WeaverContextAttachment Caller(JsonObject content) => new("browser-input", "forged", "forged", "forged", "weaver.host-support",
        WeaverContextSensitivity.Internal, "forged", "forged", JsonSerializer.SerializeToElement(content), new Dictionary<string, string> { ["forged"] = "forged" })
    {
        ModuleId = "Elsa.Weaver", SchemaVersion = "1", Origin = WeaverContextOrigin.AuthoritativeRead,
        WeaverScopes = ["forged"], Target = new("forged", "forged", "forged", "forged"), Client = new("forged", "forged")
    };

    private static JsonElement Element(WeaverContextAttachment item) => Assert.IsType<JsonElement>(item.Content);
    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim("elsa.identity.tenant_id", "tenant")], "test"));
    private static DefaultHttpContext Context(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services, User = Principal() };
        context.Response.Body = new MemoryStream();
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return context;
    }

    private static ServiceProvider Services(Support? support = null, Action<IServiceCollection>? configure = null)
        => WeaverTestServices.Create(services =>
        {
            services.AddWeaverApi();
            services.AddRouting().AddElsaEndpoints();
            services.AddSingleton<IPermissionAuthorizationService>(new Permission());
            if (support is not null) services.Replace(ServiceDescriptor.Scoped<IWeaverSupportSource>(_ => support));
            configure?.Invoke(services);
        });

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        public Permission Permission { get; } = new();
        public SessionSource Sessions { get; }
        public WeaverSession Session => Sessions.Session;
        public IServiceProvider Services => scope.ServiceProvider;
        public DefaultHttpContext Context { get; }
        public IWeaverContextCollector Collector => scope.ServiceProvider.GetRequiredService<IWeaverContextCollector>();
        public Fixture(Support support, string? invalid = null)
        {
            Sessions = new();
            root = HostSupportContextTests.Services(support, services =>
            {
                services.RemoveAll<IPermissionAuthorizationService>();
                if (invalid != "missing") services.AddSingleton<IPermissionAuthorizationService>(Permission);
                if (invalid == "ambiguous") services.AddSingleton<IPermissionAuthorizationService>(new Permission());
                services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(Sessions));
            });
            scope = root.CreateScope();
            var context = HostSupportContextTests.Context(scope.ServiceProvider);
            Context = context;
            if (invalid == "anonymous") context.User = new(new ClaimsIdentity());
            if (invalid is "actor" or "tenant")
                context.User = new(new ClaimsIdentity(Principal().Claims.Where(claim => claim.Type !=
                    (invalid == "actor" ? ClaimTypes.NameIdentifier : "elsa.identity.tenant_id")), "test"));
            if (invalid == "foreign") Sessions.Owner = new("foreign", "tenant");
            if (invalid == "denied") Permission.Allowed = false;
        }
        public Task<WeaverResult<WeaverContextContribution>> Collect(JsonObject content) => Collector.CollectAsync(Session.Policy,
            new(Session.Id, "/unknown", new Dictionary<string, string>()) { CallerAttachments = [Caller(content)] });
        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class Permission : IPermissionAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public Func<CancellationToken, Task<PermissionEvaluationResult>>? Decide { get; set; }
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            Assert.Equal(WeaverPermissionKeys.Use, context.Permission);
            Assert.Equal("tenant", context.TenantId);
            return Decide is null ? ValueTask.FromResult(Allowed ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied("not forwarded"))
                : new(Decide(cancellationToken));
        }
    }

    private sealed class Support : IWeaverSupportSource
    {
        public WeaverSupportView View { get; init; } = new(WeaverSupportCompleteness.Complete, [], []);
        public List<WeaverPolicy> Policies { get; } = [];
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<WeaverSupportView>>? Produce { get; set; }
        public Task<WeaverSupportView> ReadAsync(string actorId, string tenantId, WeaverPolicy policy, CancellationToken cancellationToken = default)
        {
            Assert.Equal("actor", actorId);
            Assert.Equal("tenant", tenantId);
            Calls++; Policies.Add(policy);
            return Produce?.Invoke(cancellationToken) ?? Task.FromResult(View);
        }
    }

    private sealed class SessionSource : IWeaverSessionService
    {
        public WeaverSession Session { get; set; } = new("session", null, "tenant", "actor", "conversation", "test", "explain",
            WeaverPolicy.Default with { Id = "owned-session-policy" }, WeaverSessionStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, new Dictionary<string, string>()) { PolicyRevision = 3 };
        public WeaverSessionOwner Owner { get; set; } = new("actor", "tenant");
        public int OwnerCalls { get; private set; }
        public int HydrationCalls { get; private set; }
        public Func<int, Task>? BeforeOwner { get; set; }
        public Func<int, Task>? BeforeHydration { get; set; }
        public async Task<WeaverSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default)
        { Assert.Equal(Session.Id, sessionId); OwnerCalls++; if (BeforeOwner is not null) await BeforeOwner(OwnerCalls); return Owner; }
        public async Task<WeaverSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
        { Assert.Equal(Session.Id, sessionId); HydrationCalls++; if (BeforeHydration is not null) await BeforeHydration(HydrationCalls); return Session; }
        public Task<WeaverSession> CreateAsync(WeaverSessionCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeaverMessage> AddMessageAsync(string sessionId, WeaverMessageCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<WeaverMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeaverMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeaverMessage?> FindLatestMessageAsync(string sessionId, WeaverRole? role = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetMessageStatusAsync(string sessionId, string messageId, WeaverMessageStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<WeaverContextAttachment> attachments, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<WeaverContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
