using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Services;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class PortableExpressionEvaluatorTests
{
    [Fact]
    public async Task Dispatches_an_immutable_json_request_by_language()
    {
        var handler = new RecordingHandler("JavaScript", JsonSerializer.SerializeToElement(42));
        var evaluator = new PortableExpressionEvaluator([handler]);
        using var parameterDocument = JsonDocument.Parse("""{"value":21}""");
        var request = Request("JavaScript", new Dictionary<string, JsonElement>
        {
            ["input"] = parameterDocument.RootElement
        });
        parameterDocument.Dispose();

        var result = await evaluator.EvaluateAsync(request);

        Assert.Equal(42, result.GetInt32());
        Assert.Same(request, handler.Request);
        Assert.Equal(21, handler.Request!.ParameterValues["input"].GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Rejects_an_unregistered_language_without_service_location()
    {
        var evaluator = new PortableExpressionEvaluator([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await evaluator.EvaluateAsync(Request("Liquid", new Dictionary<string, JsonElement>())));

        Assert.Contains("Liquid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("service provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_duplicate_language_handlers()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new PortableExpressionEvaluator([
            new RecordingHandler("JavaScript", JsonSerializer.SerializeToElement(1)),
            new RecordingHandler("JavaScript", JsonSerializer.SerializeToElement(2))
        ]));

        Assert.Contains("JavaScript", exception.Message, StringComparison.Ordinal);
    }

    private static ExpressionEvaluationRequest Request(
        string language,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var bindings = values.ToDictionary(
            item => item.Key,
            item => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(item.Value),
            StringComparer.Ordinal);
        var definition = new ExpressionDefinition(
            language,
            "args.input * 2",
            new TypeReference("Int32"),
            bindings,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        return new ExpressionEvaluationRequest(definition, values, CancellationToken.None);
    }

    private sealed class RecordingHandler(string language, JsonElement result) : IPortableExpressionHandler
    {
        public string Language { get; } = language;
        public ExpressionEvaluationRequest? Request { get; private set; }

        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        {
            Request = request;
            return ValueTask.FromResult(result.Clone());
        }
    }
}
