using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// DS-10: the engine options (the configurator pass + sandbox constraints) are assembled once per
/// factory scope and reused; only the Engine is fresh per Create, so per-evaluation state stays
/// isolated. The configurators-run-once guard pins the caching contract so a future context-sensitive
/// configurator trips a loud test instead of silently reading stale first-call options.
/// </summary>
public class JintEngineOptionsCacheTests
{
    private sealed class CountingConfigurator : IJintEngineOptionsConfigurator
    {
        public int ConfigureCount;
        public void Configure(JintOptions options, IExpressionEvaluatorOptions? evaluatorOptions)
            => Interlocked.Increment(ref ConfigureCount);
    }

    [Fact]
    public async Task ConfiguratorsRunOncePerScopeAcrossManyCreates()
    {
        await using var provider = JintTestHost.Build(configureServices: s =>
            s.AddScoped<IJintEngineOptionsConfigurator, CountingConfigurator>());
        using var scope = provider.CreateScope();
        var factory = scope.Factory();

        for (var i = 0; i < 10; i++)
            await factory.Create(null);

        var configurator = scope.ServiceProvider
            .GetServices<IJintEngineOptionsConfigurator>()
            .OfType<CountingConfigurator>()
            .Single();

        Assert.Equal(1, configurator.ConfigureCount); // options assembled once, reused for every Create
    }

    [Fact]
    public async Task EngineStateIsIsolatedBetweenEvaluations()
    {
        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        var factory = scope.Factory();

        var first = await factory.Create(null);
        first.Evaluate("var leaked = 42;");

        var second = await factory.Create(null);
        var undefined = second.Evaluate("typeof leaked").ToString();

        Assert.Equal("undefined", undefined); // fresh engine — no state carried over from the first
    }

    [Fact]
    public async Task SeparateScopesDoNotShareOptions()
    {
        await using var provider = JintTestHost.Build(configureServices: s =>
            s.AddScoped<IJintEngineOptionsConfigurator, CountingConfigurator>());

        using (var scopeA = provider.CreateScope())
        {
            var factoryA = scopeA.Factory();
            await factoryA.Create(null);
            await factoryA.Create(null);
            var a = scopeA.ServiceProvider.GetServices<IJintEngineOptionsConfigurator>().OfType<CountingConfigurator>().Single();
            Assert.Equal(1, a.ConfigureCount); // scope A assembled its options once
        }

        using (var scopeB = provider.CreateScope())
        {
            var factoryB = scopeB.Factory();
            await factoryB.Create(null);
            var b = scopeB.ServiceProvider.GetServices<IJintEngineOptionsConfigurator>().OfType<CountingConfigurator>().Single();
            Assert.Equal(1, b.ConfigureCount); // scope B is independent — its own fresh configurator + options
        }
    }
}
