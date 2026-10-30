using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentContextContributionTests
{
    [Fact]
    public async Task Healthy_sources_compose_around_an_optional_typed_limitation()
    {
        var first = Healthy("first", "module.first");
        var optional = new Source(Descriptor("optional", "module.optional"), (_, _) =>
            ValueTask.FromResult(new AgentContextContribution([], [new("optional", "sample.unavailable", "Optional supporting context is unavailable.", false)])));
        var last = Healthy("last", "module.last");
        using var root = Create(first, optional, last);
        using var scope = root.CreateScope();

        var result = await Collector(scope).CollectAsync(AgentPolicy.Default, Request());

        Assert.True(result.Succeeded, result.Error?.Message);
        var contribution = Assert.IsType<AgentContextContribution>(result.Value);
        Assert.Equal(["first-context", "last-context"], contribution.Attachments.Select(item => item.Id));
        var limitation = Assert.Single(contribution.Diagnostics);
        Assert.Equal("sample.unavailable", limitation.Code);
        Assert.False(limitation.Required);
        Assert.Equal([1, 1, 1], new[] { first.Calls, optional.Calls, last.Calls });
    }

    [Fact]
    public async Task Required_typed_limitation_denies_collection_and_preserves_healthy_context_for_diagnostics()
    {
        var healthy = Healthy("healthy", "module.healthy");
        var required = new Source(Descriptor("required", "module.required"), (_, _) =>
            ValueTask.FromResult(new AgentContextContribution([], [new("required", "sample.required_missing", "The selected resource is unavailable.", true)])));
        using var root = Create(healthy, required);
        using var scope = root.CreateScope();

        var result = await Collector(scope).CollectAsync(AgentPolicy.Default, Request());

        Assert.False(result.Succeeded);
        Assert.Equal("agent.context.incomplete", result.Error?.Code);
        Assert.Equal(400, result.Error?.StatusCode);
        var contribution = Assert.IsType<AgentContextContribution>(result.Value);
        Assert.Equal("healthy-context", Assert.Single(contribution.Attachments).Id);
        var limitation = Assert.Single(contribution.Diagnostics);
        Assert.Equal("sample.required_missing", limitation.Code);
        Assert.True(limitation.Required);
        Assert.Equal(1, required.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_faults_and_cancellation_propagate_and_stop_later_sources(bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("The source failed unexpectedly.");
        var first = Healthy("first", "module.first");
        var interrupted = new Source(Descriptor("interrupted", "module.interrupted"), (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            if (cancelled)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw failure;
        });
        var later = Healthy("later", "module.later");
        using var root = Create(first, interrupted, later);
        using var scope = root.CreateScope();
        var collector = Collector(scope);

        if (cancelled)
        {
            var propagated = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                collector.CollectAsync(AgentPolicy.Default, Request(), cancellation.Token));
            Assert.Equal(cancellation.Token, propagated.CancellationToken);
        }
        else
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
                collector.CollectAsync(AgentPolicy.Default, Request(), cancellation.Token)));

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, interrupted.Calls);
        Assert.Equal(0, later.Calls);
    }

    [Theory]
    [InlineData("owner", "agent.context.kind_unsupported")]
    [InlineData("kind", "agent.context.kind_unsupported")]
    [InlineData("version", "agent.context.version_unsupported")]
    public async Task Unknown_caller_owner_kind_or_version_is_required_failure_and_never_forwarded(string invalid, string code)
    {
        var source = new Source(Descriptor("installed", "module.installed", "bound"), (request, _) =>
            ValueTask.FromResult(new AgentContextContribution(request.CallerAttachments, [])));
        var caller = Attachment("caller-context", "module.installed") with
        {
            ModuleId = invalid == "owner" ? "module.not-installed" : "module.installed",
            ContentType = invalid == "kind" ? "unknown.context" : "sample.context",
            SchemaVersion = invalid == "version" ? "2" : "1"
        };
        using var root = Create(source);
        using var scope = root.CreateScope();

        var result = await Collector(scope).CollectAsync(AgentPolicy.Default, Request() with { CallerAttachments = [caller] });

        Assert.False(result.Succeeded);
        var contribution = Assert.IsType<AgentContextContribution>(result.Value);
        Assert.Empty(contribution.Attachments);
        var limitation = Assert.Single(contribution.Diagnostics);
        Assert.Equal(code, limitation.Code);
        Assert.True(limitation.Required);
        Assert.All(source.Requests, request => Assert.Empty(request.CallerAttachments));
        Assert.Equal(invalid == "version" ? 1 : 0, source.Calls);
    }

    [Fact]
    public async Task Duplicate_installed_source_identity_fails_before_any_source_read()
    {
        var first = Healthy("duplicate", "module.first");
        var second = Healthy("duplicate", "module.second");
        using var root = Create(first, second);
        using var scope = root.CreateScope();

        var failure = await Assert.ThrowsAsync<AgentContributionException>(() =>
            Collector(scope).CollectAsync(AgentPolicy.Default, Request()));

        Assert.Contains("Duplicate agent context source", failure.Message);
        Assert.Equal(0, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task Installed_source_cannot_contribute_context_owned_by_another_module()
    {
        var invalid = new Source(Descriptor("invalid", "module.installed"), (_, _) =>
            ValueTask.FromResult(new AgentContextContribution([Attachment("foreign-context", "module.other")], [])));
        var later = Healthy("later", "module.later");
        using var root = Create(invalid, later);
        using var scope = root.CreateScope();

        var failure = await Assert.ThrowsAsync<AgentContributionException>(() =>
            Collector(scope).CollectAsync(AgentPolicy.Default, Request()));

        Assert.Contains("another module's context", failure.Message);
        Assert.Equal(1, invalid.Calls);
        Assert.Equal(0, later.Calls);
    }

    private static Source Healthy(string id, string owner) => new(Descriptor(id, owner), async (_, _) =>
    {
        await Task.Yield();
        return new([Attachment(id + "-context", owner)], []);
    });

    private static AgentContextProviderDescriptor Descriptor(string id, string owner, string scope = "*") =>
        new(id, owner, scope, ["sample.context"], ["1"], ["sample.resource"]);

    private static AgentContextAttachment Attachment(string id, string owner) => new(id, owner, null, "Sample context", "sample.context",
        AgentContextSensitivity.Internal, "sample", "Module-owned supporting context.", JsonSerializer.SerializeToElement(new { value = "sample" }), new Dictionary<string, string>())
    { ModuleId = owner, SchemaVersion = "1", Origin = AgentContextOrigin.ResourceData };

    private static AgentContextRequest Request() => new("context-test-session", "unbound", new Dictionary<string, string>());

    private static ServiceProvider Create(params Source[] sources) => AgentTestServices.Create(services =>
    {
        foreach (var source in sources)
            services.AddScoped<IAgentContextProvider>(_ => source);
    });

    private static IAgentContextCollector Collector(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IAgentContextCollector>();

    private sealed class Source(AgentContextProviderDescriptor descriptor,
        Func<AgentContextRequest, CancellationToken, ValueTask<AgentContextContribution>> collect) : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; } = descriptor;
        public int Calls { get; private set; }
        public List<AgentContextRequest> Requests { get; } = [];
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            return collect(request, cancellationToken);
        }
    }
}
