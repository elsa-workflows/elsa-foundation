using System.Text.Json;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Api.Endpoints;
using Elsa.Workflows.Publishing.Core.Models;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityDataSelectionPublicationProblemsTests
{
    [Fact]
    public async Task Wrapped_refusals_keep_the_real_resource_and_editor_repair_location_on_the_wire()
    {
        var refusal = new ActivityDataSelectionRejectedException([new("consumer/inputs/CustomerId", "ValueFlow/UnavailableProducer", "The producer is unavailable.")])
        { ResourceKind = "ActivityDefinitionDraft", ResourceId = "actual-draft", ProviderKey = "elsa.graph" };
        var wrapped = new WorkflowExecutableCompilationException("definition", "version", refusal.Message, refusal);
        Assert.True(ActivityDataSelectionPublicationProblems.TryFind(wrapped, out var found));
        var context = new DefaultHttpContext();
        context.Request.Path = "/publish";
        context.Response.Body = new MemoryStream();
        var problem = ActivityDataSelectionPublicationProblems.Create(found, context);
        await ActivityPublishingProblems.WriteAsync(context.Response, problem, default);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("actual-draft", diagnostic.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal("consumer/inputs/CustomerId", diagnostic.GetProperty("authoredPath").GetString());
        var location = diagnostic.GetProperty("location");
        Assert.Equal("elsa.graph", location.GetProperty("providerKey").GetString());
        Assert.Equal("CustomerId", location.GetProperty("referenceKey").GetString());
        Assert.Equal("consumer", Assert.Single(location.GetProperty("nodeOrigin").EnumerateArray()).GetProperty("id").GetString());
    }
}
