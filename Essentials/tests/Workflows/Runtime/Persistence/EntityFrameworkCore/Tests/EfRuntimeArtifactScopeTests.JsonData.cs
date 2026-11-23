using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Fact]
    public async Task Template_and_source_envelopes_keep_raw_data_through_a_new_store_scope()
    {
        const string raw = """{ "Key" : "\u0041", "n":1.0, "nil":null, "large":9007199254740993 }""";
        using var data = JsonDocument.Parse(raw);
        await using var database = await Database.CreateAsync();
        await using (var owner = database.Open("tenant-a"))
        {
            var node = new ExecutableNode("node", "node", "test", "1", "consumer", data.RootElement,
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
            var template = new ExecutableActivityTemplate("raw-template", "raw-hash", node,
                new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UtcNow);
            await owner.Template.SaveAsync(template);
            await owner.Store.SaveAsync(Reference("raw-source", template.TemplateId) with
            {
                Layout = [new("node", 0, 0, AdditionalProperties: data.RootElement)],
                AuthoredInputs = [new("node", "OrderID", "Literal", data.RootElement)]
            });
            using var retained = JsonDocument.Parse((await owner.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).ContentJson);
            Assert.Equal(raw, retained.RootElement.GetProperty("template").GetProperty("root").GetProperty("descriptorPayload").GetRawText());
        }
        await using var reopened = database.Open("tenant-a");
        Assert.Equal(raw, (await reopened.Template.FindAsync("raw-template"))!.Root.DescriptorPayload.GetRawText());
        var reference = (await reopened.Store.FindAsync("raw-source"))!;
        Assert.Equal(raw, Assert.Single(reference.Layout).AdditionalProperties!.Value.GetRawText());
        Assert.Equal(raw, Assert.Single(reference.AuthoredInputs).Value.GetRawText());
    }
}
