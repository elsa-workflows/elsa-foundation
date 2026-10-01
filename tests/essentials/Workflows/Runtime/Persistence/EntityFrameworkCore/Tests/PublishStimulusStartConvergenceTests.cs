using Elsa.Persistence.EntityFramework.Tests;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary><see cref="PublishStimulusStartContract"/> on SQLite. PostgreSQL runs it in the provider-test lane.</summary>
public sealed class PublishStimulusStartConvergenceTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("publish-stimulus");

    private string ConnectionString => $"{_database.ConnectionString};Pooling=False";

    [Fact]
    public Task A_PublishStimulus_delivered_again_on_another_node_starts_its_workflow_once() =>
        PublishStimulusStartContract.ARedeliveryOnAnotherNodeStartsTheWorkflowOnceAsync("Sqlite", ConnectionString);

    [Fact]
    public Task A_PublishStimulus_delivery_racing_on_another_node_converges_on_the_first_start() =>
        PublishStimulusStartContract.ARacingDeliveryOnAnotherNodeConvergesOnTheFirstStartAsync("Sqlite", ConnectionString);

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
