using System.Security.Claims;
using System.Text.Json;
using Elsa.Weaver.Api;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>
/// The authenticated audit route reads only through the owning reader's owner-bounded query (#3440 T022):
/// no global read, no per-event session lookup and no fallback when the installed reader lacks the query.
/// </summary>
public sealed class WeaverAuditApiTests
{
    private const string Actor = "actor-1";
    private const string Tenant = "tenant-1";

    [Fact]
    public async Task The_callers_newest_record_is_returned_before_the_limit_despite_newer_foreign_records()
    {
        using var fixture = new Fixture();
        var older = await fixture.CreateAsync(Actor, Tenant);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var newer = await fixture.CreateAsync(Actor, Tenant);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.CreateAsync("actor-2", Tenant);
        await fixture.CreateAsync(Actor, "tenant-2");

        var (status, body) = await fixture.AuditAsync("?take=1");
        var (_, all) = await fixture.AuditAsync(string.Empty);

        Assert.Equal(200, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("error").ValueKind);
        var item = Assert.Single(body.GetProperty("data").EnumerateArray());
        Assert.Equal(newer.Id, item.GetProperty("sessionId").GetString());
        Assert.Equal("sessionCreated", item.GetProperty("kind").GetString());
        Assert.Equal(Actor, item.GetProperty("actorId").GetString());
        Assert.Equal(JsonValueKind.Object, item.GetProperty("metadata").ValueKind);
        Assert.Equal([newer.Id, older.Id], all.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Records_without_an_ownership_association_are_excluded_even_for_an_authorized_explicit_session()
    {
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(sp =>
            new InMemoryWeaverSessionService(sp.GetRequiredService<IWeaverAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
        var legacy = await fixture.CreateAsync(Actor, Tenant);
        await fixture.Root.GetRequiredService<IWeaverAuditSink>().EmitAsync(new("legacy-actor", WeaverAuditEventKind.ToolInvoked, legacy.Id, Actor,
            "A record naming the caller in an unassociated session.", fixture.Clock.GetUtcNow(), new Dictionary<string, string>()));

        var (explicitStatus, explicitBody) = await fixture.AuditAsync($"?sessionId={legacy.Id}");
        var (listStatus, listBody) = await fixture.AuditAsync("?take=1");

        Assert.Equal(200, explicitStatus);
        Assert.Empty(explicitBody.GetProperty("data").EnumerateArray());
        Assert.Equal(200, listStatus);
        Assert.Empty(listBody.GetProperty("data").EnumerateArray());
        Assert.Equal(2, (await fixture.Root.GetRequiredService<IWeaverAuditReader>().ListAsync(legacy.Id)).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_foreign_or_unknown_explicit_session_refuses_with_the_shared_session_result_without_reading(bool foreign)
    {
        var reader = new RecordingReader();
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverAuditReader>(reader)));
        var other = await fixture.CreateAsync("actor-2", Tenant);

        var (status, body) = await fixture.AuditAsync($"?sessionId={(foreign ? other.Id : "missing")}&take=1");

        Assert.Equal(foreign ? 403 : 404, status);
        Assert.Equal(foreign ? "weaver.session.forbidden" : "weaver.session.not_found", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Empty(reader.Queries);
        Assert.Equal(0, reader.GlobalReads);
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("?take=1", 1)]
    [InlineData("?take=0", 1)]
    [InlineData("?take=5000", WeaverAuditOwnerQuery.MaxTake)]
    public async Task A_current_replacement_reader_owns_the_query_with_the_principals_actor_and_tenant(string query, int take)
    {
        var reader = new RecordingReader
        {
            Result = new(WeaverAuditQueryOutcome.Succeeded,
                [new("replacement-owned", WeaverAuditEventKind.ToolInvoked, "replacement-session", null, "Owned by the replacement.", DateTimeOffset.UnixEpoch, new Dictionary<string, string>())])
        };
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverAuditReader>(reader)));
        // The principal resolves through the shared actor/tenant resolver's fallback claims.
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Actor), new Claim("tenant_id", Tenant)], "test"));

        var (status, body) = await fixture.AuditAsync(query, principal);

        Assert.Equal(200, status);
        Assert.Equal(new WeaverAuditOwnerQuery(Actor, Tenant, null, take), Assert.Single(reader.Queries));
        // The replacement's ownership decision is returned as-is: no per-event session lookup re-filters it.
        Assert.Equal("replacement-owned", Assert.Single(body.GetProperty("data").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(0, reader.GlobalReads);
    }

    [Fact]
    public async Task An_owned_explicit_session_is_passed_to_the_owning_query()
    {
        var reader = new RecordingReader();
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverAuditReader>(reader)));
        var session = await fixture.CreateAsync(Actor, Tenant);

        var (status, _) = await fixture.AuditAsync($"?sessionId={session.Id}&take=1");

        Assert.Equal(200, status);
        Assert.Equal(new WeaverAuditOwnerQuery(Actor, Tenant, session.Id, 1), Assert.Single(reader.Queries));
    }

    [Fact]
    public async Task A_reader_without_the_owned_query_refuses_explicitly_without_a_global_read()
    {
        var reader = new TrustedOnlyReader();
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverAuditReader>(reader)));
        await fixture.CreateAsync(Actor, Tenant);

        var (status, body) = await fixture.AuditAsync("?take=1");

        Assert.Equal(409, status);
        Assert.Equal("weaver.audit.query_unsupported", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal(0, reader.GlobalReads);
    }

    [Theory]
    [InlineData(null, Tenant)]
    [InlineData(Actor, null)]
    [InlineData(" ", Tenant)]
    public async Task An_unresolved_principal_is_refused_before_any_read(string? actor, string? tenant)
    {
        var reader = new RecordingReader();
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Singleton<IWeaverAuditReader>(reader)));
        var claims = new List<Claim>();
        if (actor is not null) claims.Add(new(ClaimTypes.NameIdentifier, actor));
        if (tenant is not null) claims.Add(new("elsa.identity.tenant_id", tenant));

        var (status, body) = await fixture.AuditAsync("?take=1", new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

        Assert.Equal(403, status);
        Assert.Equal("weaver.actor.unresolved", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(reader.Queries);
        Assert.Equal(0, reader.GlobalReads);
    }

    private sealed class RecordingReader : IWeaverAuditReader
    {
        public List<WeaverAuditOwnerQuery> Queries { get; } = [];
        public int GlobalReads { get; private set; }
        public WeaverAuditQueryResult Result { get; init; } = new(WeaverAuditQueryOutcome.Succeeded, []);

        public Task<IReadOnlyCollection<WeaverAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default)
        {
            GlobalReads++;
            return Task.FromResult<IReadOnlyCollection<WeaverAuditEvent>>([]);
        }

        public Task<WeaverAuditQueryResult> ListOwnedAsync(WeaverAuditOwnerQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(Result);
        }
    }

    /// <summary>A replacement that predates the owner query and implements only the trusted read.</summary>
    private sealed class TrustedOnlyReader : IWeaverAuditReader
    {
        public int GlobalReads { get; private set; }

        public Task<IReadOnlyCollection<WeaverAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default)
        {
            GlobalReads++;
            return Task.FromResult<IReadOnlyCollection<WeaverAuditEvent>>([]);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Endpoint _endpoint;

        public Fixture(Action<IServiceCollection>? configure = null)
        {
            Root = WeaverTestServices.Create(services =>
            {
                services.AddRouting().AddElsaEndpoints();
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                configure?.Invoke(services);
            });
            using var scope = Root.CreateScope();
            var app = new TestEndpointRouteBuilder(scope.ServiceProvider);
            WeaverApi.MapWeaverApi(app);
            _endpoint = Assert.Single(app.DataSources.SelectMany(x => x.Endpoints),
                x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaWeaverApiEndpointsAudit");
        }

        public ProposalTestClock Clock { get; } = new();
        public ServiceProvider Root { get; }

        public Task<WeaverSession> CreateAsync(string actor, string tenant) => Root.GetRequiredService<IWeaverSessionService>().CreateAsync(new(
            tenant, actor, "conversation-1", "test-provider", "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));

        public async Task<(int Status, JsonElement Body)> AuditAsync(string query, ClaimsPrincipal? user = null)
        {
            using var request = Root.CreateScope();
            var context = new DefaultHttpContext
            {
                RequestServices = request.ServiceProvider,
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Actor), new Claim("elsa.identity.tenant_id", Tenant)], "test"))
            };
            context.Request.Method = "GET";
            context.Request.QueryString = new(query.Length == 0 ? null : query);
            var response = new MemoryStream();
            context.Response.Body = response;

            await _endpoint.RequestDelegate!(context);

            return (context.Response.StatusCode, JsonDocument.Parse(response.ToArray()).RootElement.Clone());
        }

        public void Dispose() => Root.Dispose();
    }
}
