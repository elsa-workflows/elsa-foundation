using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;
using static Elsa.Activities.Http.Tests.HttpResponseInstructionFixture;

namespace Elsa.Activities.Http.Tests;

public sealed class WriteHttpResponseExecutionTests
{
    [Fact]
    public async Task UnassignedStatusCodeUsesTheActivityDefault()
    {
        Assert.Equal(200, (await ExecuteAsync(new() { Body = Text("ok") })).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonpositiveStatusCodeDefaultsToTwoHundred(int statusCode)
    {
        Assert.Equal(200, (await ExecuteAsync(new() { StatusCode = statusCode })).StatusCode);
    }

    [Fact]
    public async Task ReturnsTheStatusBodyContentTypeAndHeadersInOneInstruction()
    {
        var activity = new WriteHttpResponse
        {
            StatusCode = 202,
            Body = Text("accepted"),
            ContentType = "text/plain",
            Headers = new Dictionary<string, string[]> { ["X-Custom"] = ["first", "second"] }
        };

        var instruction = await ExecuteAsync(activity);

        Assert.Equal(202, instruction.StatusCode);
        Assert.Equal("accepted", instruction.Body!.Value.GetString());
        Assert.Equal("text/plain", instruction.ContentType);
        Assert.Equal(["first", "second"], instruction.Headers["X-Custom"]);
    }

    private static async Task<HttpResponseInstruction> ExecuteAsync(WriteHttpResponse activity)
    {
        var transition = await ((IActivity)activity).ExecuteAsync(
            new("wf-1", "invocation-1", "attempt-1", "node-write-response", CancellationToken.None));
        return Assert.IsAssignableFrom<IActivityCompletionTransition<HttpResponseInstruction>>(transition).Result;
    }
}
