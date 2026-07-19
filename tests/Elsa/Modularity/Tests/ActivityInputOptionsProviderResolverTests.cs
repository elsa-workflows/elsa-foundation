using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ActivityInputOptionsProviderResolverTests
{
    [Fact]
    public async Task Resolver_selects_provider_by_case_sensitive_key_and_preserves_option_order()
    {
        var expected = new[]
        {
            new ActivityInputOption("First", "first"),
            new ActivityInputOption("Second", 2)
        };
        var resolver = new ActivityInputOptionsProviderResolver([new StubProvider("catalog.fields", expected)]);

        var provider = resolver.Find("catalog.fields");
        var options = await provider!.GetOptionsAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(["First", "Second"], options.Select(x => x.Label));
        Assert.Equal(JsonValueKind.String, options[0].Value.ValueKind);
        Assert.Equal(JsonValueKind.Number, options[1].Value.ValueKind);
        Assert.Null(resolver.Find("CATALOG.FIELDS"));
    }

    [Fact]
    public void Resolver_rejects_duplicate_provider_keys()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ActivityInputOptionsProviderResolver(
        [
            new StubProvider("catalog.fields", []),
            new StubProvider("catalog.fields", [])
        ]));

        Assert.Contains("catalog.fields", exception.Message, StringComparison.Ordinal);
    }

    private static ActivityInputOptionsContext CreateContext()
    {
        var node = new ActivityNode("node-1", "activity-v1", [], []);
        var input = new InputDefinition("Field", "Field", new TypeReference("String"), null, "Field", null);
        var state = new WorkflowDefinitionState([], node, [], [], null);
        return new ActivityInputOptionsContext(state, node, input);
    }

    private sealed class StubProvider(string key, IReadOnlyList<ActivityInputOption> options) : IActivityInputOptionsProvider
    {
        public string Key { get; } = key;

        public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(
            ActivityInputOptionsContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(options);
    }
}
