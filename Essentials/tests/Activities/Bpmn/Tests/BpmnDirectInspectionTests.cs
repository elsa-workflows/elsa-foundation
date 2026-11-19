using System.IO.Compression;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task Inspection_reads_the_exact_authored_graph_wait_and_source_without_inventing_human_history()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        Assert.True(runtime.AdmitNewExecutions);
        var caller = new BpmnModelCaller("author", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var published = await runtime.PublishFromSourceAsync(definition, PublicationSource(), "inspect-publish", caller);
        var started = await runtime.StartAsync(published.SourceReferenceId, "inspect-start", null, caller);
        var before = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        var inspection = await runtime.InspectAsync(started.ExecutionId, caller);
        Assert.Equal(before.Revision, inspection.State.Revision);
        Assert.Equal(before.Executable, inspection.State.Executable);
        Assert.Equal(before.MaterialIncarnationId, inspection.State.MaterialIncarnationId);
        Assert.Equal(definition.Process.GetRawText(), inspection.Process.GetRawText());
        Assert.Equal(BpmnModelProtocol.Profile, inspection.Profile);
        Assert.Equal("version-1", inspection.Source!.SourceId);
        Assert.Equal("1.0.0", inspection.Source.SourceVersion);
        Assert.Equal("Review order", Assert.Single(inspection.Source.ActivityPresentation).DisplayName);
        Assert.Equal(PublicationSource().Layout[0].AdditionalProperties!.Value.GetRawText(), inspection.Source.Layout[0].AdditionalProperties!.Value.GetRawText());
        var human = Assert.Single(inspection.Work);
        Assert.Equal("Human", human.Kind); Assert.Null(human.WorkerExecutionId); Assert.Null(human.DueAt);
        Assert.Empty(inspection.Attempts); Assert.Empty(inspection.Incidents); Assert.Empty(inspection.Settlements);
        var retiredAt = inspection.Source.CreatedAt.AddSeconds(1);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .RetireAsync(published.SourceReferenceId, retiredAt, "Retained inspection control"));
        var retired = await runtime.InspectAsync(started.ExecutionId, caller);
        Assert.Equal(retiredAt, retired.Source!.RetiredAt);
        Assert.Equal(inspection.Process.GetRawText(), retired.Process.GetRawText());
        Assert.Equal(human.InvocationId, Assert.Single(retired.Work).InvocationId);
        await runtime.CompleteHumanAsync(started.ExecutionId, "inspect-complete", human.InvocationId, human.AttemptId, null, caller);
        var completed = await runtime.InspectAsync(started.ExecutionId, caller);
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, completed.State.Lifecycle);
        Assert.Empty(completed.Work); Assert.Empty(completed.Attempts);
        Assert.Equal("Completed", completed.State.TerminalResult!.Value.GetProperty("status").GetString());
        await AssertNativeEmptyAsync(scope.ServiceProvider);
    }

    [Fact]
    public async Task Inspection_of_accepted_work_survives_provider_restart_and_admission_off_without_design()
    {
        var caller = new BpmnModelCaller("author", null); string execution;
        await using (var host = await OpenAsync())
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var publication = await runtime.PublishFromSourceAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, PublicationSource(), "inspect-publish", caller);
            execution = (await runtime.StartAsync(publication.SourceReferenceId, "inspect-start", null, caller)).ExecutionId;
        }
        await using (var host = await OpenAsync(admitNewExecutions: false))
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            Assert.False(runtime.AdmitNewExecutions);
            var inspection = await runtime.InspectAsync(execution, caller);
            Assert.Equal(execution, inspection.State.ExecutionId); Assert.Equal("version-1", inspection.Source!.SourceId);
            Assert.Single(inspection.Work); Assert.Empty(inspection.Attempts);
        }
    }

    [Fact]
    public async Task Inspection_preserves_physical_attempts_and_cancellation_from_the_actual_legacy_writer()
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualNativeStartRetirement.sqlite.gz")))
        await using (var zipped = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await zipped.CopyToAsync(output);
        await using var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System)));
        await using var scope = await ScopeAsync(host);
        var state = Assert.Single((await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
        var document = BpmnDirectExecutionDriver.Read(state);
        var owed = Assert.Single(document.Cancellations!);
        var inspection = await scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>().InspectAsync(state.ExecutionId, new("operator", state.TenantId));
        var attempt = Assert.Single(inspection.Attempts, a => a.AttemptId == owed.Origin.AttemptId);
        Assert.Equal(owed.Origin.WorkerExecutionId, attempt.WorkerExecutionId);
        Assert.Equal(owed.Origin.WorkerExecutable, attempt.WorkerExecutable);
        Assert.NotEqual("Running", attempt.Status);
        var settlement = Assert.Single(inspection.Settlements, s => s.AttemptId == owed.Origin.AttemptId);
        Assert.Equal("NativeCancellation", settlement.Kind);
        Assert.Equal(owed.Settled, settlement.Settled); Assert.Equal(owed.NotAdmitted, settlement.NotAdmitted);
        Assert.Equal(state.Revision, inspection.State.Revision);
    }

    [Fact]
    public async Task Inspection_preserves_the_original_fault_and_another_requested_attempt_from_the_actual_legacy_writer()
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualConcurrentAdmittedFault.sqlite.gz")))
        await using (var zipped = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await zipped.CopyToAsync(output);
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var state = Assert.Single((await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
        var document = BpmnDirectExecutionDriver.Read(state);
        var result = Assert.Single(document.NativeResults!);
        var inspection = await scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>().InspectAsync(state.ExecutionId, new("operator", state.TenantId));
        var original = Assert.Single(inspection.Attempts, a => a.AttemptId == result.Origin.AttemptId);
        Assert.Equal(result.Status.ToString(), original.Status); Assert.Equal(result.FaultCode, original.FaultCode);
        Assert.Equal(result.Origin.WorkerExecutionId, original.WorkerExecutionId);
        Assert.Contains(inspection.Attempts, a => a.Status == "Requested");
        Assert.Equal(state.Revision, inspection.State.Revision);
    }

    [Fact]
    public async Task Inspection_of_missing_and_foreign_resources_remains_unavailable()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => runtime.InspectAsync("missing", new("operator", null)).AsTask());
        var caller = new BpmnModelCaller("author", null);
        var publication = await runtime.PublishAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, "publish", caller);
        var start = await runtime.StartAsync(publication.SourceReferenceId, "start", null, caller);
        await using var foreign = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new("other-tenant"));
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => foreign.ServiceProvider.GetRequiredService<IBpmnModelRuntime>()
            .InspectAsync(start.ExecutionId, new("other", "other-tenant")).AsTask());
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("worker-pin")]
    [InlineData("worker-address")]
    public async Task Inspection_refuses_inconsistent_native_origin_metadata_instead_of_returning_an_unrelated_run(string change)
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualConcurrentAdmittedFault.sqlite.gz")))
        await using (var zipped = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await zipped.CopyToAsync(output);
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var state = Assert.Single((await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
        var model = (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(state.Executable.ArtifactId))!.Model!;
        var document = BpmnDirectExecutionDriver.Read(state); var result = Assert.Single(document.NativeResults!); var origin = result.Origin;
        var changed = new RuntimeModelNativeWorkOrigin(origin.FormatVersion, change == "scope" ? new("other") : origin.Scope,
            origin.ExecutionId, origin.Protocol, origin.Executable, origin.MaterialIncarnationId, origin.BindingId, origin.InvocationId,
            origin.AttemptId, change == "worker-pin" ? new("other", "other", "other", "1.0.0", "other") : origin.WorkerExecutable,
            change == "worker-address" ? "unrelated-worker" : origin.WorkerExecutionId, origin.SourceOutboxItemId,
            origin.SourceIntentId, origin.ResultIntentKind, origin.ResultInputId);
        // Isolated projection negative control; the archived source checkpoint is never changed.
        var invalid = document with { NativeResults = [result with { Origin = changed }] };
        Assert.Throws<BpmnModelValidationException>(() => BpmnModelRuntime.InspectAttempts(state, model, invalid));
    }
}
