using Elsa.Expressions.JavaScript;
using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// The JavaScript value adapter through both evaluators (spec 1228): input expressions only read (US1 acceptance 4–6,
/// FR-006a), lossy writes meet the precision policy (US2 acceptance 1–6, FR-013 to FR-016), and no CLR surface is
/// reachable (FR-008).
/// </summary>
public sealed class ScriptValueAdapterTests
{
    private const string Order = """{"id":9007199254740993,"price":10.50,"lines":[1,2]}""";

    [Theory]
    [InlineData("(() => { const o = variables.order; o.price = 1; return 1; })()", "variables.order.price")]
    [InlineData("(() => { const k = 'li' + 'nes'; variables.order[k].push(3); return 1; })()", "variables.order.lines")]
    [InlineData("(() => { getVariable('order').extra = true; return 1; })()", "variables.order.extra")]
    public async Task An_input_expression_that_changes_a_read_value_fails_with_the_path(string source, string path)
    {
        var exception = await Assert.ThrowsAsync<InputExpressionWriteException>(() => ExpressionAsync(source));

        Assert.Equal("order", exception.Variable);
        Assert.Equal(path, exception.Path);
        Assert.Contains("Input expressions only read", exception.Message);
        Assert.Contains("Run JavaScript", exception.Message);
    }

    [Theory]
    [InlineData("(() => { const copy = { ...variables.order }; copy.price = 1; return copy.price; })()", "1")]
    [InlineData("(() => { variables.order.price = 3; variables.order.price = 10.5; return 2; })()", "2")]
    [InlineData("(() => { args.n = 5; return args.n; })()", "5")]
    public async Task A_local_copy_a_change_changed_back_and_args_are_not_writes(string source, string expected)
    {
        var result = await ExpressionAsync(source);

        Assert.Equal(expected, result.GetRawText());
    }

    [Fact]
    public async Task A_lossy_input_expression_result_is_refused()
    {
        var exception = await Assert.ThrowsAsync<JavaScriptPrecisionException>(() => ExpressionAsync("variables.order.id"));

        Assert.Equal(JavaScriptValueDiagnostics.PrecisionLossRefused, exception.Code);
        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("result", diagnostic.Path);
        Assert.Contains("9007199254740993", exception.Message);
        Assert.Contains("9007199254740992", exception.Message);
        Assert.Contains("9007199254740991", exception.Message);
    }

    [Fact]
    public async Task An_unchanged_value_returned_by_an_input_expression_is_its_original_JSON()
    {
        var result = await ExpressionAsync("variables.order");

        Assert.Equal(Order, result.GetRawText());
    }

    [Fact]
    public async Task Computing_with_a_lossy_value_warns_once_without_its_value_and_is_not_refused()
    {
        var logs = new List<string>();
        var sink = new ExecutionWarningCollector();

        var result = await ExpressionAsync("variables.order.price * 2", logs, sink: sink);

        Assert.Equal("21", result.GetRawText());
        Assert.Contains(JavaScriptValueDiagnostics.PrecisionLossRead, Assert.Single(logs));
        var warning = Assert.Single(sink.Warnings);
        Assert.Equal(JavaScriptValueDiagnostics.PrecisionLossRead, warning.Code);
        Assert.Equal(ExecutionWarningSeverity.Warning, warning.Severity);
        Assert.Equal("variables.order.id", warning.Details!["paths"]);
        Assert.Contains("integers beyond 9007199254740991", warning.Message);
        // Spec 1229 FR-006: a warning names paths, never a value.
        Assert.DoesNotContain("9007199254740993", warning.Message);
        Assert.DoesNotContain("9007199254740992", warning.Message);
    }

    [Fact]
    public async Task Passing_a_value_with_a_lossy_number_through_unchanged_does_not_warn()
    {
        var sink = new ExecutionWarningCollector();

        await ExpressionAsync("({ wrapped: variables.order })", sink: sink);

        Assert.Empty(sink.Warnings);
    }

    [Fact]
    public async Task A_script_reports_its_warning_to_the_step()
    {
        var sink = new ExecutionWarningCollector();
        await using var provider = Build(null);
        await using var scope = provider.CreateAsyncScope();
        var arguments = JsonSerializer.Deserialize<JsonElement>($$"""{"order":{{Order}}}""");

        await scope.ScriptEvaluator().EvaluateAsync(new JavaScriptScriptEvaluationRequest("return args.order.price;", arguments, CancellationToken.None) { Warnings = sink });

        Assert.Equal("args.order.id", Assert.Single(sink.Warnings).Details!["paths"]);
    }

    [Fact]
    public async Task A_script_returning_a_lossy_id_is_refused_and_a_string_id_passes()
    {
        await Assert.ThrowsAsync<JavaScriptPrecisionException>(() => ScriptAsync("return { id: args.order.id };", Order));

        var result = await ScriptAsync("return { id: args.order.id };", """{"id":"9007199254740993"}""");
        Assert.Equal("""{"id":"9007199254740993"}""", result!.Value.GetRawText());
    }

    [Fact]
    public async Task A_script_returning_a_changed_array_with_a_lossy_element_lists_the_element()
    {
        var exception = await Assert.ThrowsAsync<JavaScriptPrecisionException>(() => ScriptAsync("args.order.ids.push(1); return args.order.ids;", """{"ids":[9007199254740993,2]}"""));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("result[0]", diagnostic.Path);
        Assert.Contains("written as a whole", diagnostic.Reason);
        Assert.Contains("do not change the array", diagnostic.Remedy);
    }

    [Fact]
    public async Task A_decimal_returned_in_another_notation_is_no_loss()
    {
        var result = await ScriptAsync("return args.order.price;", Order);

        Assert.Equal("10.5", result!.Value.GetRawText());
    }

    [Fact]
    public async Task A_host_policy_decides_and_the_default_does_not_run()
    {
        var policy = new AllowingPolicy();

        var result = await ScriptAsync("return args.order.id;", Order, services => services.Replace(ServiceDescriptor.Singleton<IJavaScriptPrecisionPolicy>(policy)));

        // FR-016: the number stays a number; Elsa never turns it into a string by itself.
        Assert.Equal(JsonValueKind.Number, result!.Value.ValueKind);
        Assert.Equal("9007199254740992", result.Value.GetRawText());
        Assert.Equal("result", Assert.Single(policy.Seen).Target);
    }

    [Fact]
    public async Task The_default_bridge_and_policy_are_registered_once_and_a_host_replacement_wins()
    {
        // Framework §2.23.1: the JavaScript features register conditionally, so their wiring is tested.
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(Elsa.Expressions.JavaScript.Jint.Contracts.IScriptValueBridge));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IJavaScriptPrecisionPolicy));

        var policy = new AllowingPolicy();
        var replaced = new ServiceCollection();
        replaced.AddSingleton<IJavaScriptPrecisionPolicy>(policy);
        new JintFeature().ConfigureServices(replaced);
        await using var provider = replaced.BuildServiceProvider();
        Assert.Same(policy, provider.GetRequiredService<IJavaScriptPrecisionPolicy>());
    }

    [Fact]
    public async Task A_second_policy_registration_fails_shell_activation_naming_both()
    {
        // Framework §2.6.2: a replacement contract never resolves by registration order.
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        services.AddSingleton<IJavaScriptPrecisionPolicy, AllowingPolicy>();
        await using var provider = services.BuildServiceProvider();
        var validator = new Elsa.Expressions.JavaScript.Jint.Services.JavaScriptValueCompositionValidator(
            provider.GetRequiredService<Elsa.Expressions.JavaScript.Jint.Services.JavaScriptValueComposition>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.InitializeAsync());

        Assert.Contains("RefusingPrecisionPolicy", exception.Message);
        Assert.Contains("AllowingPolicy", exception.Message);
    }

    [Theory]
    [InlineData("typeof importNamespace + typeof System + typeof args.order.GetType + typeof args.order.ToString + (Object.getPrototypeOf(args.order) === Object.prototype)")]
    public async Task No_clr_object_type_or_member_is_reachable(string expression)
    {
        // FR-008, D-16: from a script body and from an input expression alike.
        const string expected = "undefinedundefinedundefinedundefinedtrue";
        Assert.Equal(expected, (await ScriptAsync($"return {expression};", Order))!.Value.GetString());
        Assert.Equal(expected, (await ExpressionAsync(expression, args: true)).GetString());
    }

    private static async Task<JsonElement> ExpressionAsync(string source, List<string>? logs = null, bool args = false, ExecutionWarningCollector? sink = null)
    {
        await using var provider = Build(logs);
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>();
        var order = JsonSerializer.Deserialize<JsonElement>(Order);
        var parameters = new Dictionary<string, ExpressionParameterBinding>
        {
            [ExpressionParameterSurfaces.VariablesKey("order")] = new VariableExpressionParameterBinding("workflow", "order", ExpressionParameterSurfaces.Variables),
            ["n"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(1))
        };
        var values = new Dictionary<string, JsonElement>
        {
            [ExpressionParameterSurfaces.VariablesKey("order")] = order,
            ["n"] = JsonSerializer.SerializeToElement(1)
        };
        if (args)
        {
            parameters["order"] = new LiteralExpressionParameterBinding(order);
            values["order"] = order;
        }

        var definition = new ExpressionDefinition("JavaScript", source, new TypeReference("Any"), parameters, JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
        return await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(definition, values, CancellationToken.None) { Warnings = sink });
    }

    private static async Task<JsonElement?> ScriptAsync(string source, string order, Action<IServiceCollection>? configure = null)
    {
        await using var provider = Build(null, configure);
        await using var scope = provider.CreateAsyncScope();
        var arguments = JsonSerializer.Deserialize<JsonElement>($$"""{"order":{{order}}}""");
        var result = await scope.ScriptEvaluator().EvaluateAsync(new JavaScriptScriptEvaluationRequest(source, arguments, CancellationToken.None));
        return result.Value;
    }

    private static ServiceProvider Build(List<string>? logs, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        if (logs is not null)
            services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(logs)).AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class AllowingPolicy : IJavaScriptPrecisionPolicy
    {
        public List<JavaScriptPrecisionContext> Seen { get; } = [];

        public JavaScriptPrecisionDecision Decide(JavaScriptPrecisionContext context)
        {
            Seen.Add(context);
            return JavaScriptPrecisionDecision.Allow();
        }
    }

    private sealed class CapturingLoggerFactory(List<string> logs) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    private sealed class CapturingLogger(List<string> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                logs.Add(formatter(state, exception));
        }
    }
}
