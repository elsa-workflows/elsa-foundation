using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// What this store's reader does today with a stored executable whose input binding names a source it does not know,
/// or carries a property it does not know. These stand in for an older binary reading an executable a #4078 server
/// wrote: the reader here is the current one, with the same options the store uses (Web defaults, enums by name, framed
/// strings), so they prove what those options do with such bytes, not how a real older build behaves.
/// </summary>
public sealed partial class EfRuntimeArtifactScopeTests
{
    [Fact]
    public async Task A_stored_binding_with_an_unknown_source_name_fails_the_executable_read()
    {
        var exception = await ReadTamperedAsync(binding => binding["source"] = EfRelationalIdentity.Encode("ActivityResultMember"));

        // The store reports a corrupt payload; the cause is the enum converter refusing the name.
        var corrupt = Assert.IsType<InvalidDataException>(exception);
        Assert.Equal("The persisted workflow executable payload is corrupt.", corrupt.Message);
        var cause = Assert.IsType<JsonException>(corrupt.InnerException);
        Assert.StartsWith("The JSON value could not be converted to", cause.Message, StringComparison.Ordinal);
        Assert.EndsWith(".source", cause.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stored_binding_with_an_unknown_source_number_fails_the_executable_read_in_canonical_validation()
    {
        var exception = await ReadTamperedAsync(binding => binding["source"] = 8);

        // The number parses as an undefined enum value; the binding's constructor refuses it, and the store reports a
        // corrupt payload.
        var corrupt = Assert.IsType<InvalidDataException>(exception);
        Assert.Equal("The persisted workflow executable payload is corrupt.", corrupt.Message);
        var cause = Assert.IsAssignableFrom<Exception>(corrupt.InnerException);
        Assert.Contains("does not match its canonical payload", Flatten(cause), StringComparison.Ordinal);
    }

    /// <summary>
    /// The silent case the design note closes with new source kinds: a member selection written onto an existing
    /// whole-value kind is dropped by the reader without a trace, and the binding reads the whole value.
    /// </summary>
    [Fact]
    public async Task A_stored_selection_on_a_whole_value_binding_is_silently_dropped_by_the_executable_read()
    {
        var read = await ReadTamperedExecutableAsync(binding =>
            binding["selection"] = new JsonObject { ["schemaVersion"] = 1, ["segments"] = new JsonArray("customerId") });

        var binding = read.RootActivity.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.ActivityResult, binding.Source);
        Assert.Equal("$result", binding.ActivityResult!.ProjectionKey);
        Assert.DoesNotContain("customerId", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + " | " + Flatten(exception.InnerException);

    private static async Task<Exception?> ReadTamperedAsync(Action<JsonObject> tamper)
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await SaveAndTamperAsync(fixture, tamper);
        return await Record.ExceptionAsync(() => fixture.Executable.FindAsync("artifact-member").AsTask());
    }

    private static async Task<WorkflowExecutable> ReadTamperedExecutableAsync(Action<JsonObject> tamper)
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await SaveAndTamperAsync(fixture, tamper);
        return (await fixture.Executable.FindAsync("artifact-member"))!;
    }

    private static async Task SaveAndTamperAsync(Fixture fixture, Action<JsonObject> tamper)
    {
        var binding = new RuntimeInputBinding("Text", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult, activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null));
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["Text"] = binding }, new Dictionary<string, string>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        await fixture.Executable.SaveAsync(new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-member", "definition", "version", "1", "hash-member"),
            node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference));

        var row = await fixture.Context.WorkflowExecutables.SingleAsync();
        var content = JsonNode.Parse(row.ContentJson)!.AsObject();
        var bindings = content["rootActivity"]!["inputBindings"]!.AsObject();
        tamper(Assert.Single(bindings).Value!.AsObject());
        row.ContentJson = content.ToJsonString();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }
}
