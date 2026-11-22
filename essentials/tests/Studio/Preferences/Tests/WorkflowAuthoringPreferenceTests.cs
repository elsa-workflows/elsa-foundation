using System.Text.Json;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class WorkflowAuthoringPreferenceTests
{
    [Fact]
    public void Feature_registers_governed_namespace_without_changing_existing_namespaces()
    {
        using var provider = CreateProvider();
        var registry = provider.GetRequiredService<IStudioPreferenceNamespaceRegistry>();
        var definition = Assert.IsAssignableFrom<IStudioPreferenceNamespace>(registry.Find("workflow-authoring"));
        Assert.Equal(1, definition.CurrentSchemaVersion);
        Assert.Equal(1024, definition.MaxBytes);
        Assert.All(new[] { "dashboard", "attention", "home-layout" }, name => Assert.NotNull(registry.Find(name)));
        using var maximum = JsonDocument.Parse($"{{\"editorId\":\"{new string('a', 128)}\"}}");
        Assert.True(definition.Validate(maximum.RootElement));
    }

    [Theory]
    [InlineData("{\"editorId\":\"bpmn-js_2.0\"}", true)]
    [InlineData("{}", false)]
    [InlineData("[]", false)]
    [InlineData("{\"editorId\":null}", false)]
    [InlineData("{\"editorId\":1}", false)]
    [InlineData("{\"editorId\":\"\"}", false)]
    [InlineData("{\"editorId\":\"a/b\"}", false)]
    [InlineData("{\"editorId\":\"with space\"}", false)]
    [InlineData("{\"editorId\":\"é\"}", false)]
    [InlineData("{\"EditorId\":\"example\"}", false)]
    [InlineData("{\"editorId\":\"one\",\"editorId\":\"two\"}", false)]
    [InlineData("{\"editorId\":\"one\",\"extra\":true}", false)]
    public void Namespace_accepts_only_one_valid_editor_id(string json, bool expected)
    {
        using var provider = CreateProvider();
        using var document = JsonDocument.Parse(json);
        var definition = provider.GetRequiredService<IStudioPreferenceNamespaceRegistry>().Find("workflow-authoring")!;
        Assert.Equal(expected, definition.Validate(document.RootElement));
    }

    [Fact]
    public void Namespace_rejects_an_overlength_id()
    {
        using var provider = CreateProvider();
        using var document = JsonDocument.Parse($"{{\"editorId\":\"{new string('a', 129)}\"}}");
        Assert.False(provider.GetRequiredService<IStudioPreferenceNamespaceRegistry>().Find("workflow-authoring")!.Validate(document.RootElement));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        new StudioPreferencesFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }
}
