using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Models;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed class JintCallDepthTests
{
    [Theory]
    [InlineData("script", false, false)]
    [InlineData("binding", false, false)]
    [InlineData("script", true, false)]
    [InlineData("binding", true, false)]
    [InlineData("script", false, true)]
    [InlineData("binding", false, true)]
    [InlineData("script", true, true)]
    [InlineData("binding", true, true)]
    public void A_small_user_call_limit_does_not_charge_trusted_setup(string surface, bool variables, bool noOtherLimits)
    {
        using var provider = JintTestHost.Build(feature =>
        {
            feature.MaxRecursionDepth = 1;
            if (noOtherLimits)
            {
                feature.ExecutionTimeout = null;
                feature.MaxStatements = null;
                feature.MaxMemoryBytes = null;
            }
        });
        using var scope = provider.CreateScope();
        Assert.Equal(42, Evaluate(scope, surface, "return 42;", variables).GetInt32());
        Assert.Throws<RecursionDepthOverflowException>(() => Evaluate(scope, surface,
            "function f() { return g(); } function g() { return 42; } return f();", variables));
    }

    private static JsonElement Evaluate(IServiceScope scope, string surface, string source, bool variables)
    {
        if (!variables)
            return JintCrashProbe.Evaluate(scope, surface, source);

        if (surface == "script")
            return scope.ScriptEvaluator().EvaluateAsync(new JavaScriptScriptEvaluationRequest(
                source, JsonSerializer.SerializeToElement(new { }), CancellationToken.None)
            {
                Variables = new UnusedSession()
            }).AsTask().GetAwaiter().GetResult().Value!.Value;

        var key = ExpressionParameterSurfaces.VariablesKey("unused");
        var definition = new ExpressionDefinition("JavaScript", source, new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding>
            {
                [key] = new VariableExpressionParameterBinding("workflow", "unused", ExpressionParameterSurfaces.Variables)
            }, JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
        var request = new ExpressionEvaluationRequest(definition,
            new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(7) }, CancellationToken.None);
        return scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>()
            .EvaluateAsync(request).AsTask().GetAwaiter().GetResult();
    }

    private sealed class UnusedSession : IScriptVariableSession
    {
        public IReadOnlyList<string> CandidateNames => ["unused"];
        public bool HasReadProtectedValue => false;
        public bool IsReadOnly(string name) => false;
        public ExpressionVariableRead Read(string name) => throw new InvalidOperationException("The unused variable must not be read.");
        public void Write(string name, JsonElement? value) => throw new InvalidOperationException("The unused variable must not be written.");
    }
}
