using System.Text.Json;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Pins <see cref="SandboxSurfaceCatalog"/> — the declared sandbox surface published into this assembly's
/// contract fragment (spec 149 / RFC #1191) — to the surface <c>IsolatedJintEngine</c> actually installs.
/// Every catalog entry is verified against a live engine, and every entry kind must be one this test
/// understands: adding a catalog entry without extending the verification here fails the build.
/// </summary>
public sealed class SandboxSurfaceCatalogTests
{
    [Fact]
    public async Task Every_declared_entry_matches_the_live_engine_surface()
    {
        // Ambient evaluation with one variable ("count") exercises the full installed surface.
        var probes = new Dictionary<string, string>
        {
            ["args"] = "typeof args",
            ["variables"] = "typeof variables",
            ["getVariable"] = "typeof getVariable",
            ["get{VariableName}"] = "typeof getCount",
            ["Date"] = "typeof globalThis.Date",
            ["Temporal"] = "typeof globalThis.Temporal",
            ["Intl"] = "typeof globalThis.Intl",
            ["Math.random"] = "typeof Math.random"
        };

        foreach (var entry in SandboxSurfaceCatalog.Globals)
        {
            Assert.True(probes.ContainsKey(entry.Name),
                $"Catalog entry '{entry.Name}' has no live-engine probe; extend SandboxSurfaceCatalogTests alongside the catalog.");

            var observed = (await EvaluateWithAmbientVariable(probes[entry.Name])).GetString();
            var expected = entry.Kind switch
            {
                "function" or "perVariableAccessor" => "function",
                "frozenObject" => "object",
                "disabledIntrinsic" => "undefined",
                _ => throw new Xunit.Sdk.XunitException(
                    $"Catalog entry '{entry.Name}' declares unknown kind '{entry.Kind}'; extend SandboxSurfaceCatalogTests alongside the catalog.")
            };

            Assert.True(expected == observed,
                $"Catalog entry '{entry.Name}' ({entry.Kind}) expected typeof '{expected}' but the live engine reports '{observed}'.");
        }
    }

    [Fact]
    public async Task Variable_surface_is_absent_without_ambient_variables()
    {
        // The catalog's availability notes claim variables/getVariable exist only when the workflow
        // exposes visible variables — the binding-pure path must stay surface-free.
        var result = await Evaluate(
            "[typeof variables, typeof getVariable, typeof getCount].join(':')",
            ambient: new Dictionary<string, JsonElement>());

        Assert.Equal("undefined:undefined:undefined", result.GetString());
    }

    private static Task<JsonElement> EvaluateWithAmbientVariable(string source) =>
        Evaluate(source, new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement(5) });

    private static async Task<JsonElement> Evaluate(string source, IReadOnlyDictionary<string, JsonElement> ambient)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var definition = new ExpressionDefinition(
            "JavaScript",
            source,
            new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding> { ["probe"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(1)) },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var request = new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement> { ["probe"] = JsonSerializer.SerializeToElement(1) },
            ambient,
            CancellationToken.None);
        return await evaluator.EvaluateAsync(request);
    }
}
