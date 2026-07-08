using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Services;
using Elsa.Expressions.Liquid.Handlers;
using Elsa.Expressions.Liquid.Notifications;
using Elsa.Expressions.Liquid.Options;
using Fluid;
using Microsoft.Extensions.DependencyInjection;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// ADR 0036: a schemaless <c>Any</c> value (e.g. an HTTP JSON payload) declared on a workflow variable is
/// read identically from every expression engine. This pins JavaScript (Jint) and Liquid (Fluid) read parity
/// for both canonical runtime forms of such a value: the freshly parsed <see cref="JsonNode"/> and the
/// <see cref="JsonElement"/> it round-trips back as through the durable store on resume.
/// </summary>
public sealed class AnyValueReadParityTests
{
    private const string PayloadJson = """{"customer":{"name":"Alice","age":42},"tags":["red","green"]}""";

    // The two runtime forms an Any value takes: fresh (HTTP parse) and durable round-trip (post-resume).
    public static TheoryData<string, string> Fields() => new()
    {
        { "customer.name", "Alice" },
        { "customer.age", "42" },
        { "tags[0]", "red" },
    };

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task JsonNodeForm_ReadsIdenticallyFromJsAndLiquid(string path, string expected)
    {
        await AssertParity(() => JsonNode.Parse(PayloadJson)!, path, expected);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task JsonElementForm_ReadsIdenticallyFromJsAndLiquid(string path, string expected)
    {
        // A resumed Any value arrives as a read-only JsonElement (the durable store's opaque form).
        await AssertParity(() => JsonSerializer.Deserialize<JsonElement>(PayloadJson), path, expected);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task ExpandoObjectForm_ReadsIdenticallyFromJsAndLiquid(string path, string expected)
    {
        // A legacy dynamic value still produced by the polymorphic converter arrives as an ExpandoObject;
        // it must read identically across engines (materialized to JsonNode at the seam) until that converter
        // is deleted (ADR 0035 D5).
        await AssertParity(BuildExpandoPayload, path, expected);
    }

    private static object BuildExpandoPayload()
    {
        IDictionary<string, object?> customer = new ExpandoObject();
        customer["name"] = "Alice";
        customer["age"] = 42;

        IDictionary<string, object?> root = new ExpandoObject();
        root["customer"] = customer;
        root["tags"] = new List<object> { "red", "green" };
        return root;
    }

    private static async Task AssertParity(Func<object> valueFactory, string path, string expected)
    {
        var js = (await ReadWithJavaScript(valueFactory(), path))?.ToString();
        var liquid = await ReadWithLiquid(valueFactory(), path);

        Assert.Equal(expected, js);
        Assert.Equal(expected, liquid);
        Assert.Equal(js, liquid);
    }

    private static async Task<object?> ReadWithJavaScript(object anyValue, string path)
    {
        await using var provider = JintTestHost.Build(configureServices: s => s.AddMemoryCache());
        using var scope = provider.CreateScope();
        var context = await scope.CreateExecutionContextAsync();

        // Mirror the runtime pre-processors: expose the Any value through the `variables` container.
        var container = new Dictionary<string, object?> { ["payload"] = anyValue };
        context.SetValue("variables", context.NormalizeValue(container));

        return context.Evaluate($"variables.payload.{path}");
    }

    private static async Task<string?> ReadWithLiquid(object anyValue, string path)
    {
        var executionContext = new SingleVariableExecutionContext("payload", anyValue);
        var templateContext = new TemplateContext(executionContext, new TemplateOptions());

        // Configuration is only consulted when configuration access is allowed, which it is not here.
        var configure = new ConfigureLiquidEngine(null!, new ConfigureLiquidEngineOptions([], AllowConfigurationAccess: false));
        await configure.Handle(new RenderingLiquidTemplate(templateContext, executionContext), CancellationToken.None);

        var template = new FluidParser().Parse("{{ Variables.payload." + path + " }}");
        return await template.RenderAsync(templateContext);
    }

    /// <summary>
    /// Minimal <see cref="IExpressionExecutionContext"/> exposing a single named variable — enough for the
    /// Liquid <c>Variables.*</c> accessor, which reads only <see cref="GetVariableValueOrDefault"/>.
    /// </summary>
    private sealed class SingleVariableExecutionContext(string name, object? value) : IExpressionExecutionContext
    {
        public IMemoryRegister Memory => null!;
        public IExpressionExecutionContext? ParentContext { get => null; set { } }
        public CancellationToken CancellationToken => CancellationToken.None;

        public object? GetVariableValueOrDefault(string variableName) =>
            string.Equals(variableName, name, StringComparison.Ordinal) ? value : null;

        public bool IsContainedWithinCompositeActivity() => false;
        public bool TryGetActivityInput(string key, out object? result) { result = null; return false; }
        public bool TryGetWorkflowInput(string key, out object? result) { result = null; return false; }
        public string GetCorrelationId() => string.Empty;
        public string GetWorkflowDefinitionId() => string.Empty;
        public string GetWorkflowDefinitionVersionId() => string.Empty;
        public int GetWorkflowDefinitionVersion() => 0;
        public string GetWorkflowInstanceId() => string.Empty;
        public object? GetRequiredService(Type type) => throw new NotSupportedException();

        public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => null!;
        public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) { block = null!; return false; }
        public T? Get<T>(IMemoryBlockReference blockReference) => default;
        public void Set(IMemoryBlockReference blockReference, object? blockValue, Action<IMemoryBlock>? configure = null) { }
        public IVariable? GetVariable(string variableName, bool localScopeOnly = false) => null;
        public IVariable SetVariable<T>(string variableName, T? variableValue, Action<IMemoryBlock>? configure = null) => null!;
        public IEnumerable<IVariable> EnumerateVariablesInScope() => [];
    }
}
