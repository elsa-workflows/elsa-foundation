using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scripting.Tests;

/// <summary>
/// How a Run JavaScript failure is recorded when the step read or received a protected value (spec 1229 FR-006,
/// spec 1222 FR-017a): a read the session refused is the cause, a script's own error text is withheld, and nothing the
/// script threw is attached.
/// </summary>
public sealed class RunJavaScriptProtectedFailureTests
{
    [Fact]
    public async Task A_caught_refusal_is_the_cause_and_the_error_thrown_after_it_is_not_attached()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(
            "const p = variables.order.price; try { variables.cache; } catch (e) { } throw new Error('p ' + p);", new FakeSession()));

        Assert.Equal(FakeSession.Refusal, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task A_script_error_after_a_protected_read_is_withheld()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(
            "throw new Error('p ' + variables.order.price);", new FakeSession()));

        Assert.Contains("withheld", exception.Message);
        Assert.DoesNotContain("10.5", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task A_parse_error_is_withheld_when_an_input_is_protected()
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync("return (;", new FakeSession(), protectedInputs: true));

        Assert.Contains("withheld", exception.Message);
    }

    private static async Task<ActivityTransition> ExecuteAsync(string script, FakeSession session, bool protectedInputs = false)
    {
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>()) { Script = script };
        var context = new SimpleActivityExecutionContext(activity, CancellationToken.None, invocationId: "script-1", executableNodeId: "script-node")
            .ToActivityExecutionContext()
            .WithVariables(session)
            .WithProtectedInputs(protectedInputs);
        return await ((IActivity)activity).ExecuteAsync(context);
    }

    private sealed class FakeSession : IStepVariableSession
    {
        public const string Refusal = "Variable 'cache' is transient: a script cannot read it.";
        private static readonly StepVariableTarget Order = new("workflow", "var-order", "order");
        private static readonly StepVariableTarget Cache = new("workflow", "var-cache", "cache");

        public IReadOnlyDictionary<string, StepVariableTarget> PinnedReads { get; } =
            new Dictionary<string, StepVariableTarget> { ["order"] = Order, ["cache"] = Cache };
        public bool ReadsComputedNames => false;
        public IReadOnlyList<string> VisibleNames => ["order", "cache"];
        public bool HasReadProtectedValue { get; private set; }
        public bool IsReadOnly(StepVariableTarget target) => false;
        public bool TryResolve(string name, out StepVariableTarget target)
        {
            target = null!;
            return false;
        }

        public JsonNode? ReadJson(StepVariableTarget target)
        {
            if (target == Cache)
                throw new InvalidOperationException(Refusal);
            HasReadProtectedValue = true;
            return JsonNode.Parse("""{"price":10.5}""");
        }

        public void WriteJson(StepVariableTarget target, JsonNode? value) { }
        public T Get<T>(VariableReference<T> reference) => throw new NotSupportedException();
        public T Get<T>(ReadOnlyVariableReference<T> reference) => throw new NotSupportedException();
        public void Set<T>(VariableReference<T> reference, T value) => throw new NotSupportedException();
        [Obsolete("Not used.")]
        public void Set<T>(ReadOnlyVariableReference<T> reference, T value) => throw new NotSupportedException();
        public T Get<T>(string name) => throw new NotSupportedException();
        public void Set<T>(string name, T value) => throw new NotSupportedException();
    }
}
