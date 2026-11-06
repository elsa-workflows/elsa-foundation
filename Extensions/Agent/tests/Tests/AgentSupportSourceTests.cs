using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Api.Extensions;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Elsa.Testing;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentSupportSourceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Installed_support_and_targetless_authority_are_distinct_and_no_resource_callback_runs(bool ownerAllows)
    {
        var tool = new Tool { Allows = ownerAllows };
        var source = new Source();
        using var root = Services(source, tool);
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportCompleteness.Complete, result.Completeness);
        var installed = Assert.Single(result.Tools);
        Assert.Equal(tool.Descriptor.Name, installed.Name);
        Assert.Equal(tool.Descriptor.ModuleId, installed.ModuleId);
        Assert.Equal(AgentSupportStatus.AvailableHint, installed.Admission);
        Assert.Equal(ownerAllows ? AgentSupportStatus.AvailableHint : AgentSupportStatus.Withheld, installed.Authority);
        Assert.Equal(AgentSupportStatus.Unchecked, Assert.Single(result.Contexts).Authority);
        Assert.Equal(1, tool.AuthorityCalls);
        Assert.Equal(0, tool.PrepareCalls);
        Assert.Equal(0, tool.ExecuteCalls);
        Assert.Equal(0, source.CollectCalls);
        Assert.Equal(0, source.DisclosureCalls);
    }

    [Fact]
    public async Task Explicit_policy_withholds_tools_without_pretending_the_registration_is_absent()
    {
        var tool = new Tool();
        using var root = Services(new(), tool);
        using var scope = root.CreateScope();
        var denied = await Read(scope, AgentPolicy.Default with { DeniedCapabilityIds = [tool.Descriptor.Name] });
        var installed = Assert.Single(denied.Tools);
        Assert.Equal(AgentSupportStatus.Withheld, installed.Admission);
        Assert.Equal(AgentSupportStatus.Unchecked, installed.Authority);
        Assert.Equal(0, tool.AuthorityCalls);
        Assert.Equal(AgentSupportStatus.AvailableHint, Assert.Single((await Read(scope)).Tools).Admission);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_contract_registration_remains_visible_when_its_selected_effects_feature_is_missing(bool sourceDeclaresCodes)
    {
        var tool = new Tool();
        tool.Descriptor = tool.Descriptor with { ModuleCodes = [] };
        var source = new Source();
        if (sourceDeclaresCodes) source.Descriptor = source.Descriptor with { ModuleCodes = [] };
        using var root = Services(source, tool, effects: false);
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportCompleteness.Complete, result.Completeness);
        Assert.Equal(AgentSupportStatus.Withheld, Assert.Single(result.Tools).Admission);
        Assert.Equal(sourceDeclaresCodes ? AgentSupportStatus.Withheld : AgentSupportStatus.AvailableHint,
            Assert.Single(result.Contexts).Admission);
        Assert.Equal(0, tool.AuthorityCalls);
        Assert.Equal(0, tool.PrepareCalls);
        Assert.Equal(0, source.CollectCalls);
    }

    [Fact]
    public async Task Host_privilege_is_reread_after_an_owner_await()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [AgentPrivilegePolicyOptions.SectionName + ":Administrative"] = "true" }).Build();
        var tool = new Tool();
        tool.Descriptor = tool.Descriptor with { Risk = AgentRisk.Admin };
        tool.Authorize = async _ =>
        {
            await Task.Yield();
            configuration[AgentPrivilegePolicyOptions.SectionName + ":Administrative"] = "false";
            configuration.Reload();
            return new(true, []);
        };
        using var root = Services(new(), tool, configure: services => services.AddSingleton<IConfiguration>(configuration));
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportStatus.Withheld, Assert.Single(result.Tools).Admission);
        Assert.Equal(1, tool.AuthorityCalls);
        Assert.Equal(0, tool.PrepareCalls);
    }

    [Fact]
    public async Task Shared_schema_admission_is_reread_after_an_owner_await()
    {
        var observation = new Observation { Available = true };
        var tool = new Tool();
        tool.Descriptor = tool.Descriptor with { ModuleCodes = [] };
        tool.Authorize = async _ => { await Task.Yield(); observation.Available = false; return new(true, []); };
        using var root = Services(new(), tool, effects: false, configure: services =>
        {
            new AgentEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddSingleton<ISchemaDormancyCheck>(observation);
        });
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportStatus.Withheld, Assert.Single(result.Tools).Admission);
        Assert.Equal(1, tool.AuthorityCalls);
        Assert.Equal(0, tool.PrepareCalls);
    }

    [Fact]
    public async Task Partial_or_failed_discovery_is_never_complete_empty_success_and_never_exposes_raw_failure()
    {
        var canary = Guid.NewGuid().ToString("N");
        using var root = Services(new(), new(), configure: services =>
            services.Replace(ServiceDescriptor.Scoped<IAgentToolRegistry>(_ => new Registry(_ => throw new IOException(canary)))));
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportCompleteness.Incomplete, result.Completeness);
        Assert.Empty(result.Tools);
        Assert.Single(result.Contexts);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Complete_empty_discovery_is_distinct_from_failed_discovery()
    {
        using var root = Services(new(), new(), configure: services =>
        {
            services.RemoveAll<IAgentTool>();
            services.RemoveAll<IAgentContextProvider>();
        });
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportCompleteness.Complete, result.Completeness);
        Assert.Empty(result.Tools);
        Assert.Empty(result.Contexts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_failures_or_refusal_messages_are_observed_without_forwarding_arbitrary_text(bool throws)
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new Tool { Authorize = _ => throws
            ? throw new IOException(canary)
            : Task.FromResult(new AgentPolicyDecision(false, [new(canary, canary)])) };
        using var root = Services(new(), tool);
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(throws ? AgentSupportCompleteness.Incomplete : AgentSupportCompleteness.Complete, result.Completeness);
        Assert.Equal(throws ? AgentSupportStatus.Unknown : AgentSupportStatus.Withheld, Assert.Single(result.Tools).Authority);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("ambiguous")]
    [InlineData("allowed")]
    public async Task Http_host_authority_uses_actual_permission_service_and_never_grants_from_registration(string authority)
    {
        var tool = new Tool();
        using var root = Services(new(), tool, configure: services =>
        {
            services.AddFoundationAgentApi();
            if (authority != "missing") services.AddSingleton<IPermissionAuthorizationService>(new Permissions(authority == "allowed"));
            if (authority == "ambiguous") services.AddSingleton<IPermissionAuthorizationService>(new Permissions(true));
        });
        using var scope = root.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = Principal("actor", "tenant") };
        try
        {
            var result = await Read(scope);
            Assert.Equal(authority == "allowed" ? AgentSupportStatus.AvailableHint : AgentSupportStatus.Withheld,
                Assert.Single(result.Tools).Authority);
            Assert.Equal(authority == "allowed" ? 1 : 0, tool.AuthorityCalls);
            Assert.Equal(0, tool.PrepareCalls);
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task Source_only_duplicate_integrity_is_checked_without_collecting_context()
    {
        var source = new Source();
        using var root = Services(source, new(), configure: services => services.AddSingleton<IAgentContextProvider>(new Source()));
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.NotEqual(AgentSupportCompleteness.Complete, result.Completeness);
        Assert.Empty(result.Contexts);
        Assert.Equal(0, source.CollectCalls);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("url")]
    [InlineData("values")]
    public async Task Oversized_or_unsafe_metadata_is_omitted_with_explicit_incompleteness(string boundary)
    {
        var source = new Source();
        source.Descriptor = boundary switch
        {
            "identity" => source.Descriptor with { Id = new string('a', 129) },
            "url" => source.Descriptor with { ModuleId = "https://private.invalid/module" },
            _ => source.Descriptor with { ContextKinds = Enumerable.Range(0, 9).Select(index => "kind" + index).ToArray() }
        };
        using var root = Services(source, new(), configure: services =>
            services.Replace(ServiceDescriptor.Scoped<IAgentToolRegistry>(_ => new Registry(_ => Task.FromResult<IReadOnlyCollection<IAgentTool>>([])))));
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportCompleteness.Incomplete, result.Completeness);
        Assert.Empty(result.Contexts);
        Assert.DoesNotContain("private.invalid", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Snapshot_collections_are_copied_before_an_awaited_authority_callback()
    {
        var kinds = new[] { "sample.context" };
        var source = new Source();
        source.Descriptor = source.Descriptor with { ContextKinds = kinds };
        var tool = new Tool { Authorize = async _ => { await Task.Yield(); kinds[0] = "changed.context"; return new(true, []); } };
        using var root = Services(source, tool);
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal("sample.context", Assert.Single(Assert.Single(result.Contexts).ContextKinds));
        Assert.Equal("changed.context", kinds[0]);
    }

    [Fact]
    public async Task The_complete_serialized_projection_is_bounded_with_explicit_truncation()
    {
        using var root = Services(new(), new(), configure: services =>
        {
            services.RemoveAll<IAgentTool>();
            services.RemoveAll<IAgentContextProvider>();
            for (var index = 0; index < 64; index++)
                services.AddSingleton<IAgentContextProvider>(new Source
                {
                    Descriptor = new("source_" + index, "module_" + new string('m', 120), "*",
                        Enumerable.Range(0, 8).Select(item => "kind_" + item + new string('k', 120)).ToArray(),
                        Enumerable.Range(0, 8).Select(item => "schema_" + item + new string('s', 120)).ToArray(),
                        Enumerable.Range(0, 8).Select(item => "type_" + item + new string('t', 120)).ToArray())
                });
        });
        using var scope = root.CreateScope();
        var result = await Read(scope);
        Assert.Equal(AgentSupportCompleteness.Incomplete, result.Completeness);
        Assert.NotEmpty(result.Contexts);
        Assert.True(result.Contexts.Count < 64);
        Assert.True(JsonSerializer.Serialize(result).Length <= 24 * 1024);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncooperative_owner_is_bounded_by_stop_or_deadline_and_late_results_do_not_rejoin(bool callerStop)
    {
        var clock = new AgentContextCollectionBoundsTests.TriggeredTimeProvider();
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentPolicyDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new Tool { Authorize = _ => { entered.SetResult(); return release.Task; } };
        using var root = Services(new(), tool, configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        using var scope = root.CreateScope();
        var pending = scope.ServiceProvider.GetRequiredService<IAgentSupportSource>().ReadAsync("actor", "tenant", AgentPolicy.Default, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (callerStop)
            {
                stop.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            else
            {
                clock.FireNext();
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(AgentSupportCompleteness.Incomplete, result.Completeness);
                Assert.Equal(AgentSupportStatus.Unknown, Assert.Single(result.Tools).Authority);
                release.SetResult(new(true, []));
                await release.Task;
                Assert.Equal(AgentSupportStatus.Unknown, Assert.Single(result.Tools).Authority);
            }
            Assert.Equal(0, tool.PrepareCalls);
        }
        finally { release.TrySetResult(new(true, [])); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Support_replacement_rejects_duplicates_and_non_scoped_lifetimes(bool duplicate)
    {
        var failure = Assert.ThrowsAny<Exception>(() =>
        {
            using var root = Services(new(), new(), configure: services =>
            {
                if (duplicate) services.AddScoped<IAgentSupportSource, StubSupport>();
                else services.Replace(ServiceDescriptor.Singleton<IAgentSupportSource, StubSupport>());
            });
            using var scope = root.CreateScope();
            scope.ServiceProvider.GetRequiredService<AgentRegistrationValidator>().Validate();
        });
        Assert.Contains("IAgentSupportSource", failure.ToString());
    }

    [Fact]
    public async Task Sessionless_endpoint_uses_actual_principal_default_policy_and_generated_JSON_without_provider_connectivity()
    {
        var authorizer = new Authority();
        using var root = Services(new(), new(), configure: services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authorizer));
        });
        using var scope = root.CreateScope();
        var response = await GetSupport(scope.ServiceProvider, Principal("actual-actor", "actual-tenant"),
            "?actorId=spoofed&tenantId=spoofed&policy=unrestricted");
        Assert.Equal(200, response.Status);
        var data = response.Body.GetProperty("data");
        Assert.Equal("hostDefault", data.GetProperty("policyScope").GetString());
        var support = data.GetProperty("support");
        Assert.Equal("complete", support.GetProperty("completeness").GetString());
        var tool = Assert.Single(support.GetProperty("tools").EnumerateArray());
        Assert.Equal("availableHint", tool.GetProperty("authority").GetString());
        Assert.Equal("actual-actor", authorizer.ActorId);
        Assert.Equal("actual-tenant", authorizer.TenantId);
        Assert.DoesNotContain("actual-actor", response.Body.GetRawText());
        Assert.DoesNotContain("requiredPermissions", response.Body.GetRawText());
        Assert.DoesNotContain("jsonSchema", response.Body.GetRawText());
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Support_endpoint_does_not_replace_missing_authenticated_identity_with_caller_query(bool authenticated, bool actor, bool tenant)
    {
        using var root = Services(new(), new(), configure: services => services.AddRouting().AddElsaEndpoints());
        using var scope = root.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            actor ? new Claim(ClaimTypes.NameIdentifier, "actor") : null,
            tenant ? new Claim("elsa.identity.tenant_id", "tenant") : null
        }.OfType<Claim>(), authenticated ? "test" : null));
        var response = await GetSupport(scope.ServiceProvider, principal, "?actorId=actor&tenantId=tenant");
        Assert.Equal(403, response.Status);
        Assert.Equal("agent.actor.unresolved", response.Body.GetProperty("error").GetProperty("code").GetString());
    }

    private static ServiceProvider Services(Source source, Tool tool, bool effects = true, Action<IServiceCollection>? configure = null)
        => AgentTestServices.Create(services =>
        {
            services.RemoveAll<IAgentContextProvider>();
            services.AddSingleton<IAgentContextProvider>(source);
            services.AddSingleton<IAgentTool>(tool);
            if (effects) new FoundationAgentEffectsFeature().ConfigureServices(services);
            configure?.Invoke(services);
        });

    private static Task<AgentSupportView> Read(IServiceScope scope, AgentPolicy? policy = null)
        => scope.ServiceProvider.GetRequiredService<IAgentSupportSource>().ReadAsync("actor", "tenant", policy ?? AgentPolicy.Default);

    private static ClaimsPrincipal Principal(string actor, string tenant) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, actor), new Claim("elsa.identity.tenant_id", tenant)], "test"));

    private static async Task<(int Status, JsonElement Body)> GetSupport(IServiceProvider services, ClaimsPrincipal principal, string? query)
    {
        var app = new TestEndpointRouteBuilder(services);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), endpoint =>
            endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsSupport");
        var context = new DefaultHttpContext { RequestServices = services, User = principal };
        context.Request.Method = "GET";
        context.Request.QueryString = new(query);
        await using var response = new MemoryStream();
        context.Response.Body = response;
        await endpoint.RequestDelegate!(context);
        response.Position = 0;
        using var document = await JsonDocument.ParseAsync(response);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private sealed class Source : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; set; } = new("sample.context", "sample.module", "*", ["sample.context"], ["1"], ["sample.resource"]);
        public int CollectCalls { get; private set; }
        public int DisclosureCalls { get; private set; }
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        { CollectCalls++; throw new InvalidOperationException("Inventory must not collect resource context."); }
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        { DisclosureCalls++; throw new InvalidOperationException("Inventory must not admit resource disclosure."); }
    }

    private sealed class Tool : IAgentTool, IAgentToolAuthorization
    {
        public AgentToolDescriptor Descriptor { get; set; } = new("sample_read", "Sample", "Read the sample resource.", AgentToolMutability.ReadOnly,
            AgentRisk.ReadOnly, "{\"type\":\"object\"}", ["sample:read"], ["sample"])
            { ModuleId = "sample.module", DisclosureSourceId = "sample.context" };
        public bool Allows { get; set; } = true;
        public Func<CancellationToken, Task<AgentPolicyDecision>>? Authorize { get; set; }
        public int AuthorityCalls { get; private set; }
        public int PrepareCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public async ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
        {
            AuthorityCalls++;
            Assert.Null(target);
            return Authorize is null ? new(Allows, []) : await Authorize(cancellationToken);
        }
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        { PrepareCalls++; throw new InvalidOperationException("Inventory must not prepare."); }
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        { ExecuteCalls++; throw new InvalidOperationException("Inventory must not execute."); }
    }

    private sealed class Registry(Func<CancellationToken, Task<IReadOnlyCollection<IAgentTool>>> list) : IAgentToolRegistry
    {
        public Task<IReadOnlyCollection<IAgentTool>> ListAsync(CancellationToken cancellationToken = default) => list(cancellationToken);
        public Task<IAgentTool?> FindAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Authority : IAgentToolAuthorizer
    {
        public string? ActorId { get; private set; }
        public string? TenantId { get; private set; }
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor descriptor, string actorId, string tenantId, AgentResourceTarget? target,
            CancellationToken cancellationToken = default)
        { ActorId = actorId; TenantId = tenantId; return Task.FromResult(new AgentPolicyDecision(true, [])); }
    }

    private sealed class Permissions(bool allowed) : IPermissionAuthorizationService
    {
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(allowed ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied("withheld"));
    }

    private sealed class StubSupport : IAgentSupportSource
    {
        public StubSupport() { }
        public Task<AgentSupportView> ReadAsync(string actorId, string tenantId, AgentPolicy policy, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentSupportView(AgentSupportCompleteness.Unavailable, [], []));
    }

    private sealed class Observation : ISchemaDormancyCheck
    {
        public bool Available { get; set; }
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaDormancyRule.Evaluate(requirements,
            family => new(family, null, ["0.0.0", "1.0.0"], Available ? "1.0.0" : "0.0.0", Available ? "1.0.0" : "0.0.0", false, null, [], null, DateTimeOffset.UnixEpoch));
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Evaluate(requirements));
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe());
    }
}
