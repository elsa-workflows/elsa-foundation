using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class IngressWaitResumeBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_wait_resume_reads_actual_source_before_any_activity_services_or_effects(bool temporaryFailure)
    {
        var scope = new PersistenceScope("scope");
        var pin = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        var now = DateTimeOffset.UtcNow;
        var bookmark = new BookmarkState("bookmark", "existing", "activity", "node", "resume", "http", "route", null,
            new Dictionary<string, string>(), now, null);
        var owner = new WorkflowExecutionState("existing", pin, WorkflowExecutionStatus.Running, null, now, now, now, null,
            null, null, null, new Dictionary<string, string>()) { Partition = new("scope"), RunKind = WorkflowRunKind.PublishedRun };
        var expected = StimulusResumeRecipient.From(new BookmarkStateObservation(bookmark, 1), owner, scope);
        var payload = new RuntimeResumeBookmarkCommandPayload(pin, "bookmark", "activity", "node", "resume", "http", "route", null, "reason")
        { ExpectedBookmarkResume = expected, IngressWaitOrigin = new("source", new string('a', 64), new string('b', 64), scope, "wait") };
        var work = new RuntimeSchedulerWorkItem("work", "existing", "command", WorkflowExecutionCommandKind.ResumeBookmark, "envelope", "key",
            now, now, payload: JsonSerializer.SerializeToElement(payload));
        var store = new UnavailableSource(temporaryFailure);
        // There are deliberately no activity, executable or checkpoint services: an unproved origin cannot reach them.
        await using var services = new ServiceCollection().AddSingleton<IRuntimeIngressDeliveryStore>(store).BuildServiceProvider();
        var handler = new WorkflowResumeBookmarkSchedulerWorkHandler(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        if (temporaryFailure)
        {
            var exception = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => handler.HandleAsync(work).AsTask());
            Assert.IsType<IOException>(exception.InnerException);
        }
        else await Assert.ThrowsAsync<InvalidDataException>(() => handler.HandleAsync(work).AsTask());
        Assert.Equal(1, store.Reads);
    }

    private sealed class UnavailableSource(bool temporary) : IRuntimeIngressDeliveryStore
    {
        internal int Reads;
        public ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsync(RuntimeIngressWaitOrigin origin, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromException<RuntimeCommittedIngressWait>(temporary ? new IOException("offline") :
                new InvalidDataException("There is no matching immutable physical source seal."));
        }
    }
}
