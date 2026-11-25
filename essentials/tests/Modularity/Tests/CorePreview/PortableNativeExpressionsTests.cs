using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Expressions.Liquid;
using Elsa.Workbench.CorePreview;
using Kernel.Native;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests.CorePreview;

/// <summary>
/// Host-evaluated expressions on the kernel host: JavaScript and Liquid through the shell's portable evaluator, in one
/// service scope per session, with JavaScript seeing only the visible variables its source names.
/// </summary>
public sealed class PortableNativeExpressionsTests
{
    private static readonly IReadOnlyDictionary<string, JsonNode?> None = new Dictionary<string, JsonNode?>();

    [Fact]
    public async Task JavaScript_reads_its_parameters_as_args()
    {
        await using var session = Open(out _);
        var value = session.Evaluate(new HostExpression("JavaScript", "args.a + args.b", "Int32", null), new Dictionary<string, JsonNode?> { ["a"] = 2, ["b"] = 3 }, None);
        Assert.Equal("5", value?.ToJsonString());
    }

    [Fact]
    public async Task JavaScript_reads_the_visible_variables_it_names()
    {
        await using var session = Open(out _);
        var visible = new Dictionary<string, JsonNode?> { ["count"] = 5, ["order"] = new JsonObject { ["id"] = "o-7" } };
        Assert.Equal("6", session.Evaluate(new HostExpression("JavaScript", "variables.count + 1", "Int32", null), None, visible)?.ToJsonString());
        Assert.Equal("o-7", session.Evaluate(new HostExpression("JavaScript", "getVariable('order').id", "String", null), None, visible)?.GetValue<string>());
    }

    [Fact]
    public void Only_the_variables_a_source_names_are_passed_and_a_computed_access_passes_all()
    {
        var visible = new Dictionary<string, JsonNode?> { ["count"] = 5, ["keepGoing"] = true, ["unrelated"] = new string('x', 100_000) };
        Assert.Equal(["count", "keepGoing"], AmbientVariables.Referenced("variables.count > 0 && getKeepGoing()", visible)!.Keys.Order());
        Assert.Null(AmbientVariables.Referenced("1 + 1", visible));
        Assert.Equal(3, AmbientVariables.Referenced("variables[name]", visible)!.Count);
    }

    [Fact]
    public async Task Liquid_renders_its_parameters_and_normalizes_by_result_type()
    {
        await using var session = Open(out _);
        Assert.Equal("Hello Ada", session.Evaluate(new HostExpression("Liquid", "Hello {{ name }}", "String", null), new Dictionary<string, JsonNode?> { ["name"] = "Ada" }, None)?.GetValue<string>());
        Assert.True(session.Evaluate(new HostExpression("Liquid", "{{ approved }}", "Boolean", null), new Dictionary<string, JsonNode?> { ["approved"] = true }, None)?.GetValue<bool>());
    }

    [Fact]
    public async Task A_null_result_is_null_and_an_expression_without_a_result_type_is_refused()
    {
        await using var session = Open(out _);
        Assert.Null(session.Evaluate(new HostExpression("JavaScript", "null", "Object", null), None, None));
        Assert.Throws<InvalidOperationException>(() => session.Evaluate(new HostExpression("JavaScript", "1", null, null), None, None));
    }

    [Fact]
    public async Task An_unknown_language_or_a_throwing_expression_throws()
    {
        await using var session = Open(out _);
        Assert.ThrowsAny<Exception>(() => session.Evaluate(new HostExpression("Python", "1", "Object", null), None, None));
        Assert.ThrowsAny<Exception>(() => session.Evaluate(new HostExpression("JavaScript", "missing.x", "Object", null), None, None));
    }

    [Fact]
    public async Task A_session_disposes_its_scope()
    {
        var session = Open(out var lifetime);
        session.Evaluate(new HostExpression("JavaScript", "1", "Int32", null), None, None);
        Assert.False(lifetime.Disposed);
        await session.DisposeAsync();
        Assert.True(lifetime.Disposed);
    }

    [Fact]
    public async Task An_evaluation_that_does_not_complete_synchronously_throws()
    {
        var services = new ServiceCollection().AddSingleton<IPortableExpressionEvaluator>(new PendingEvaluator()).BuildServiceProvider();
        await using var session = new PortableNativeExpressions(() => (services, new Lifetime())).Open(CancellationToken.None);
        var refused = Assert.Throws<InvalidOperationException>(() => session.Evaluate(new HostExpression("JavaScript", "1", "Object", null), None, None));
        Assert.Contains("did not complete synchronously", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_gate_holds_the_service_until_it_opens_and_a_stop_before_then_starts_nothing()
    {
        var gate = new TaskCompletionSource();
        var inner = new Recording();
        var gated = new ShellGatedHostedService(inner, token => gate.Task.WaitAsync(token), _ => throw new InvalidOperationException("ended"));
        await gated.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(inner.Started);
        gate.SetResult();
        await WaitUntil(() => inner.Started);
        await gated.StopAsync(CancellationToken.None);
        Assert.True(inner.Stopped);

        var closed = new Recording();
        var never = new ShellGatedHostedService(closed, token => Task.Delay(Timeout.Infinite, token), _ => throw new InvalidOperationException("ended"));
        await never.StartAsync(CancellationToken.None);
        await never.StopAsync(CancellationToken.None);
        Assert.False(closed.Started);
        Assert.False(closed.Stopped);
    }

    [Fact]
    public async Task A_gate_that_fails_ends_the_process()
    {
        string? ended = null;
        var gated = new ShellGatedHostedService(new Recording(), _ => Task.FromException(new InvalidOperationException("no shell")), reason => ended = reason);
        await gated.StartAsync(CancellationToken.None);
        await WaitUntil(() => ended is not null);
        Assert.Contains("no shell", ended, StringComparison.Ordinal);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class Recording : Microsoft.Extensions.Hosting.IHostedService
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken) { Started = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { Stopped = true; return Task.CompletedTask; }
    }

    private static INativeExpressionSession Open(out Lifetime lifetime)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        new LiquidExpressionsFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        var tracked = lifetime = new Lifetime(scope);
        return new PortableNativeExpressions(() => (scope.ServiceProvider, tracked)).Open(CancellationToken.None);
    }

    private sealed class Lifetime(IAsyncDisposable? inner = null) : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            Disposed = true;
            if (inner is not null) await inner.DisposeAsync();
        }
    }

    private sealed class PendingEvaluator : IPortableExpressionEvaluator
    {
        public async ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        {
            await Task.Delay(50);
            return JsonSerializer.SerializeToElement(1);
        }
    }
}
