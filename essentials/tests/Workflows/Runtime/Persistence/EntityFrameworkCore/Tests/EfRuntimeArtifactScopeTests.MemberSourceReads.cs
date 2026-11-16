using System.Text.Json;
using System.Text.Json.Nodes;
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
        // Before #4078 slice A this row used "ActivityResultMember", which is now a known kind (next row).
        var exception = await ReadTamperedAsync(binding => binding["source"] = "ActivityResultSlice");

        // The store reports a corrupt payload; the cause is the enum converter refusing the name.
        var corrupt = Assert.IsType<InvalidDataException>(exception);
        Assert.Equal("The persisted workflow executable payload is corrupt.", corrupt.Message);
        var cause = Assert.IsType<JsonException>(corrupt.InnerException);
        Assert.StartsWith("The JSON value could not be converted to", cause.Message, StringComparison.Ordinal);
        Assert.EndsWith(".source", cause.Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Since #4078 slice A a member kind stored without its read selection, by name or by number, is refused by the
    /// binding's own invariant; it is never read as the whole value its payload names.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_stored_member_read_without_its_selection_fails_the_executable_read(bool byName)
    {
        var exception = await ReadTamperedAsync(binding => binding["source"] = byName
            ? JsonValue.Create("ActivityResultMember")
            : JsonValue.Create(8));

        var corrupt = Assert.IsType<InvalidDataException>(exception);
        Assert.Equal("The persisted workflow executable payload is corrupt.", corrupt.Message);
        Assert.Contains("reads a member and requires a read selection", Flatten(corrupt.InnerException!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stored_binding_with_an_unknown_source_number_fails_the_executable_read_in_canonical_validation()
    {
        // Before #4078 slice A this row used 8, which is now ActivityResultMember (previous row).
        var exception = await ReadTamperedAsync(binding => binding["source"] = 99);

        // The number parses as an undefined enum value; the binding's constructor refuses it, and the store reports a
        // corrupt payload.
        var corrupt = Assert.IsType<InvalidDataException>(exception);
        Assert.Equal("The persisted workflow executable payload is corrupt.", corrupt.Message);
        var cause = Assert.IsAssignableFrom<Exception>(corrupt.InnerException);
        Assert.Contains("does not match its canonical payload", Flatten(cause), StringComparison.Ordinal);
    }

    /// <summary>
    /// The silent case the design note closes with new source kinds. Before #4078 slice A this reader dropped a member
    /// selection written onto an existing whole-value kind and read the whole value. Since slice A this reader knows the
    /// property and the binding's invariant refuses a selection on a whole-value kind; a reader built before slice A
    /// still drops it, which is why a member read is only ever written under a member kind.
    /// </summary>
    [Fact]
    public async Task A_stored_selection_on_a_whole_value_binding_fails_the_executable_read()
    {
        var exception = await ReadTamperedAsync(binding => binding["selection"] = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["segments"] = new JsonArray(Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("customerId")),
            ["mode"] = "DeferredFit"
        });

        var corrupt = Assert.IsType<InvalidDataException>(exception);
        Assert.Contains("reads a whole value and cannot carry a read selection", Flatten(corrupt.InnerException!), StringComparison.Ordinal);
    }

    /// <summary>A member executable written by #4078 slice A round-trips through this store with its selection and requirement.</summary>
    [Fact]
    public async Task A_stored_member_executable_reads_back_with_its_selection_and_requirement()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(MemberExecutable());

        var read = (await fixture.Executable.FindAsync("artifact-member"))!;

        var binding = read.RootActivity.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.ActivityResultMember, binding.Source);
        Assert.Equal(["customer", "a.b", ""], binding.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, binding.Selection.Mode);
        Assert.Contains(read.RuntimeRequirements, requirement => requirement.ConsumerKey == RuntimeReadSelection.RequirementKey);
    }

    /// <summary>
    /// Stand-in for an older reader of that same stored executable: the bytes this slice wrote, read with the store's
    /// options into a binding whose source enum stops at <c>SecretRead</c>, as every build before slice A did. The
    /// member name is refused, so the selection is never dropped into a whole-value read. A test-local enum proves what
    /// the converter does with these bytes, not how a real older build behaves.
    /// </summary>
    [Fact]
    public async Task A_reader_whose_source_kinds_stop_at_SecretRead_refuses_a_stored_member_read()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(MemberExecutable());
        var row = await fixture.Context.WorkflowExecutables.SingleAsync();
        var stored = Assert.Single(JsonNode.Parse(row.ContentJson)!["rootActivity"]!["inputBindings"]!.AsObject()).Value!.ToJsonString();

        Assert.Contains("selection", stored, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => Stores.RuntimeArtifactJson.Deserialize<OlderBinding>(stored));
    }

    private sealed record OlderBinding(OlderSource Source);

    private enum OlderSource { Literal, Expression, WorkflowRequest, VariableRead, ActivityResult, SecretRead }

    private static WorkflowExecutable MemberExecutable()
    {
        var binding = new RuntimeInputBinding("Text", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResultMember, new RuntimeReadSelection(1, ["customer", "a.b", ""], RuntimeReadSelectionMode.DeferredFit),
            activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null));
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["Text"] = binding }, new Dictionary<string, string>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-member", "definition", "version", "1", "hash-member"),
            node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
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
