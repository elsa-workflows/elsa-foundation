using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// A script body's variables through the script evaluator (spec 1222 FR-033, spec 1228 FR-003 to FR-007): what the
/// evaluator hands the step's session to write, the ADR 0090 C2 warning (T027), and that a refusal or a value with no
/// JSON form writes nothing.
/// </summary>
public sealed class ScriptBodyVariableEvaluationTests
{
    private const string Order = """{"id":9007199254740993,"price":10.50}""";

    [Fact]
    public async Task A_member_change_is_written_with_the_stored_text_and_warned()
    {
        var (session, warnings) = await RunAsync("variables.order.price = 20; return 1;");

        var (name, value) = Assert.Single(session.Writes);
        Assert.Equal("order", name);
        Assert.Equal("""{"id":9007199254740993,"price":20}""", value!.Value.GetRawText());
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal(JavaScriptValueDiagnostics.ChangeWithoutAssignment, warning.Code);
        Assert.Equal("variables.order.price", warning.Details!["paths"]);
        Assert.Equal("variables.order", warning.ProtectedForm!.Details!["paths"]);
    }

    [Fact]
    public async Task A_whole_assignment_is_written_without_a_warning()
    {
        var (session, warnings) = await RunAsync("variables.order = { total: 1 }; return 1;");

        Assert.Equal("""{"total":1}""", Assert.Single(session.Writes).Value!.Value.GetRawText());
        Assert.Empty(warnings.Warnings);
    }

    [Fact]
    public async Task A_read_only_body_writes_nothing()
    {
        var (session, warnings) = await RunAsync("return variables.order.price * 2;");

        Assert.Empty(session.Writes);
        // The id was read lossily and not written.
        Assert.Equal(JavaScriptValueDiagnostics.PrecisionLossRead, Assert.Single(warnings.Warnings).Code);
    }

    [Fact]
    public async Task A_refused_write_writes_nothing_of_the_step()
    {
        var session = new FakeSession(Order);

        await Assert.ThrowsAsync<JavaScriptPrecisionException>(() =>
            EvaluateAsync("variables.order.price = 1; variables.order = { id: variables.order.id }; return 1;", session, new ExecutionWarningCollector()));

        Assert.Empty(session.Writes);
    }

    [Fact]
    public async Task A_value_made_unserializable_fails_the_evaluation_and_writes_nothing()
    {
        var session = new FakeSession(Order);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync("variables.order.self = variables.order; return 1;", session, new ExecutionWarningCollector()));

        Assert.Empty(session.Writes);
    }

    [Fact]
    public async Task Warnings_keep_their_protected_form_when_a_protected_value_was_read()
    {
        var (_, warnings) = await RunAsync("variables.order.price = 20; return 1;", protectedRead: true);

        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal("variables.order", warning.Details!["paths"]);
    }

    private static async Task<(FakeSession Session, ExecutionWarningCollector Warnings)> RunAsync(string source, bool protectedRead = false)
    {
        var session = new FakeSession(Order) { HasReadProtectedValue = protectedRead };
        var warnings = new ExecutionWarningCollector();
        await EvaluateAsync(source, session, warnings);
        return (session, warnings);
    }

    private static async Task EvaluateAsync(string source, FakeSession session, IExecutionWarningSink warnings)
    {
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>();
        await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(source, JsonSerializer.SerializeToElement(new { }), CancellationToken.None)
        {
            Warnings = warnings,
            Variables = session
        });
    }

    private sealed class FakeSession(string order) : IScriptVariableSession
    {
        public List<(string Name, JsonElement? Value)> Writes { get; } = [];
        public IReadOnlyList<string> CandidateNames { get; } = ["order"];
        public bool HasReadProtectedValue { get; init; }

        public ExpressionVariableRead Read(string name) =>
            name == "order" ? ExpressionVariableRead.Present(JsonSerializer.Deserialize<JsonElement>(order)) : ExpressionVariableRead.Missing;

        public void Write(string name, JsonElement? value) => Writes.Add((name, value?.Clone()));
    }
}
