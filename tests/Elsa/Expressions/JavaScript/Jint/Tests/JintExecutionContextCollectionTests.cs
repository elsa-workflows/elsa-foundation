using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// #407: <c>ConvertToJsValue</c> cast every <c>IsCollectionType()</c> hit to the non-generic
/// <see cref="System.Collections.ICollection"/>, but types such as <see cref="HashSet{T}"/> implement
/// only <c>ICollection&lt;T&gt;</c> — the cast threw <see cref="InvalidCastException"/> instead of
/// producing a JS array. Collections must be enumerated generically.
/// </summary>
public sealed class JintExecutionContextCollectionTests
{
    // PreparedScriptFactory requires IMemoryCache, which the hosting shell normally provides.
    private static ServiceProvider BuildProvider() => JintTestHost.Build(configureServices: s => s.AddMemoryCache());

    private static async Task<JintExecutionContext> CreateContextAsync(IServiceScope scope)
    {
        var engine = await scope.Factory().Create(null);
        var scriptFactory = scope.ServiceProvider.GetRequiredService<IPreparedScriptFactory>();
        return new(scriptFactory, engine);
    }

    [Fact]
    public async Task NormalizeValue_ContainerWithGenericOnlyCollection_ConvertsToJsArray()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var context = await CreateContextAsync(scope);

        var container = new Dictionary<string, object?> { ["tags"] = new HashSet<string> { "alpha", "beta" } };

        var normalized = context.NormalizeValue(container);
        context.SetValue("ctx", normalized);

        Assert.Equal(2d, context.Evaluate("ctx.tags.length"));
        Assert.Equal("alpha,beta", context.Evaluate("ctx.tags.slice().sort().join(',')"));
    }

    [Fact]
    public async Task NormalizeValue_ContainerWithList_StillConvertsToJsArray()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var context = await CreateContextAsync(scope);

        var container = new Dictionary<string, object?> { ["numbers"] = new List<int> { 3, 1, 2 } };

        var normalized = context.NormalizeValue(container);
        context.SetValue("ctx", normalized);

        // Order must be preserved for ordered collections.
        Assert.Equal("3,1,2", context.Evaluate("ctx.numbers.join(',')"));
    }
}
