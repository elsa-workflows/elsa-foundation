using System.Text.Json;
using System.Net;
using System.Text;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Models;
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
        await using var host = await PublishingMinimalApiHost.StartAsync(_ => new ExceptionSender(wrapped));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publishing/workflows/version/publish")
        {
            Content = new StringContent("{\"versionId\":\"version\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("actual-draft", diagnostic.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal("consumer/inputs/CustomerId", diagnostic.GetProperty("authoredPath").GetString());
        var location = diagnostic.GetProperty("location");
        Assert.Equal("elsa.graph", location.GetProperty("providerKey").GetString());
        Assert.Equal("CustomerId", location.GetProperty("referenceKey").GetString());
        Assert.Equal("consumer", Assert.Single(location.GetProperty("nodeOrigin").EnumerateArray()).GetProperty("id").GetString());
    }

    private sealed class ExceptionSender(Exception exception) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromException<T>(exception);
    }
}
