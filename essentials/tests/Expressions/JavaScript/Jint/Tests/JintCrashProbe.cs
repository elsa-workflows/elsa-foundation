using System.Diagnostics;
using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Models;
using Jint.Runtime;
using Elsa.Expressions.JavaScript.Jint.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

internal static class JintCrashProbe
{
    internal const string ExpressionLimit = "function f(n, a = n >= 5300 ? new Array(200000) : f(n + 1)) { } " +
        "function g(m) { {{{{ if (m >= 40) return f(0); return g(m + 1); }}}} } return g(0);";

    public static int Main(string[] args)
    {
        if (args is not ["stack-probe", var surface, var scenario])
            return 2;
        try
        {
            using var provider = JintTestHost.Build(f =>
            {
                if (scenario == "without-recursion-limit")
                    f.MaxRecursionDepth = null;
                if (scenario == "high-recursion-limit")
                    f.MaxRecursionDepth = int.MaxValue;
                if (scenario == "minimum-budget")
                    f.EvaluationStackBudgetBytes = 64 * 1024;
                if (scenario == "maximum-stack")
                    f.EvaluationThreadStackBytes = 64 * 1024 * 1024;
            });
            using var scope = provider.CreateScope();
            var failure = Record.Exception(() => Evaluate(scope, surface, ExpressionLimit));
            if (failure is not (RecursionDepthOverflowException or JavaScriptException))
                throw new InvalidOperationException("Expected a stack limit error", failure);
            if (failure is JavaScriptException js)
                Assert.Equal(JintEvaluationThreads.StackBudgetExceededMessage, js.Message);

            // A fresh evaluation must still work in the same host after the failed evaluation.
            Assert.Equal(42, Evaluate(scope, surface, "return 42;").GetInt32());
            Assert.IsType<MemoryLimitExceededException>(Record.Exception(() =>
                Evaluate(scope, surface, "return new Array(200000);")));
            Console.WriteLine($"survived: {surface}/{scenario}: {failure.GetType().Name}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    internal static JsonElement Evaluate(IServiceScope scope, string surface, string source)
    {
        if (surface == "script")
            return scope.ScriptEvaluator().EvaluateAsync(new JavaScriptScriptEvaluationRequest(
                source, JsonSerializer.SerializeToElement(new { }), CancellationToken.None)).AsTask().GetAwaiter().GetResult().Value!.Value;

        var definition = new ExpressionDefinition("JavaScript", source, new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding>(), JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingReadonlyV1);
        var request = new ExpressionEvaluationRequest(definition, new Dictionary<string, JsonElement>(), CancellationToken.None);
        return scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>().EvaluateAsync(request).AsTask().GetAwaiter().GetResult();
    }
}

public sealed class JintCrashIsolationTests
{
    [Theory]
    [InlineData("script", "default")]
    [InlineData("binding", "default")]
    [InlineData("script", "without-recursion-limit")]
    [InlineData("binding", "without-recursion-limit")]
    [InlineData("script", "high-recursion-limit")]
    [InlineData("binding", "high-recursion-limit")]
    [InlineData("script", "minimum-budget")]
    [InlineData("binding", "minimum-budget")]
    [InlineData("script", "maximum-stack")]
    [InlineData("binding", "maximum-stack")]
    public async Task An_expression_limit_after_statement_free_recursion_survives(string surface, string scenario)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(JintCrashProbe).Assembly.Location);
        start.ArgumentList.Add("stack-probe");
        start.ArgumentList.Add(surface);
        start.ArgumentList.Add(scenario);
        using var process = Process.Start(start)!;
        // Drain both pipes concurrently: a crash can write a stack trace larger than a pipe buffer.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The isolated Jint stack probe did not finish.");
        }
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, $"Probe exit {process.ExitCode}: {error[..Math.Min(error.Length, 6000)]}\n{output}");
        Assert.Contains($"survived: {surface}/{scenario}", output, StringComparison.Ordinal);
    }
}
