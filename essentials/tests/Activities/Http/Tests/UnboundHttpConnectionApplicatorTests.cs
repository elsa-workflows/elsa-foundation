using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class UnboundHttpConnectionApplicatorTests
{
    [Fact]
    public async Task A_null_reference_is_sent_as_authored()
    {
        var applicator = new UnboundHttpConnectionApplicator();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        await applicator.ApplyAsync(
            request,
            connection: null,
            new ActivityExecutionContext("wf", "actexec-1", "attempt-1", "node-1", CancellationToken.None));
        Assert.Equal("https://example.test/", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task A_blank_bound_name_is_refused()
    {
        var applicator = new UnboundHttpConnectionApplicator();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        await Assert.ThrowsAsync<InvalidOperationException>(() => applicator.ApplyAsync(
            request,
            new ConnectionReference { Name = "  ", Type = "http" },
            new ActivityExecutionContext("wf", "actexec-1", "attempt-1", "node-1", CancellationToken.None)).AsTask());
    }
}
