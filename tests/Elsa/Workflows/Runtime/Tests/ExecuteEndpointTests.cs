using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Covers issue #387: the <c>Execute</c> endpoint carried a dead exception filter
/// (<c>catch (ArgumentException e) when (e.ParamName == "artifactId")</c>) that no exception on its call
/// path could match, so real <see cref="ArgumentException"/>s fell through to the generic handler and were
/// misreported as HTTP 500. Sibling endpoints (<c>GetInstance</c>, <c>GetActivityExecution</c>) map any
/// <see cref="ArgumentException"/> to 400; <c>Execute</c> must behave the same. The endpoint is internal,
/// so it is resolved by name and built through FastEndpoints' <see cref="Factory"/> via reflection — the
/// same approach as <c>DefinitionEndpointSecurityTests</c> in Design.Api.
/// </summary>
public sealed class ExecuteEndpointTests
{
    [Theory]
    [InlineData("someOtherParam")]
    [InlineData("artifactId")]
    [InlineData(null)]
    public async Task ArgumentException_maps_to_400_regardless_of_param_name(string? paramName)
    {
        var endpoint = CreateEndpoint(new ThrowingRequestSender(new ArgumentException("Invalid execute request.", paramName)));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(
            () => HandleAsync(endpoint, new ExecuteWorkflow("artifact-1")));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task BlankSourceReferenceSelector_maps_to_400()
    {
        var endpoint = CreateEndpoint(new ThrowingRequestSender(
            new ArgumentException("Source reference ID cannot be blank when provided.", "sourceReferenceId")));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(
            () => HandleAsync(endpoint, new ExecuteWorkflow("artifact-1", SourceReferenceId: " ")));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task UnknownSourceReferenceSelector_maps_to_409()
    {
        var endpoint = CreateEndpoint(new ThrowingRequestSender(
            new WorkflowExecutableReferenceRejectedException(
                "artifact-1",
                WorkflowExecutableReferenceScope.Published,
                WorkflowExecutableReferenceRejectionReason.SelectionNotFound)));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(
            () => HandleAsync(endpoint, new ExecuteWorkflow("artifact-1", SourceReferenceId: "missing-ref")));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task Unexpected_exception_still_maps_to_500()
    {
        var endpoint = CreateEndpoint(new ThrowingRequestSender(new InvalidOperationException("Storage failure.")));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(
            () => HandleAsync(endpoint, new ExecuteWorkflow("artifact-1")));

        Assert.Equal(StatusCodes.Status500InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task Cancellation_is_rethrown()
    {
        var endpoint = CreateEndpoint(new ThrowingRequestSender(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => HandleAsync(endpoint, new ExecuteWorkflow("artifact-1")));
    }

    private const string EndpointTypeName = "Elsa.Workflows.Runtime.Api.Endpoints.Execute";

    private static BaseEndpoint CreateEndpoint(IRequestSender sender)
    {
        var endpointType = typeof(ExecuteWorkflow).Assembly.GetType(EndpointTypeName, throwOnError: true)!;
        var nullLoggerType = typeof(NullLogger<>).MakeGenericType(endpointType);
        var logger = (nullLoggerType.GetProperty("Instance")?.GetValue(null)
                      ?? nullLoggerType.GetField("Instance")?.GetValue(null))!;

        // Factory.Create<TEndpoint>(Action<DefaultHttpContext>, params object[] deps), invoked generically
        // because the endpoint type is internal.
        var create = typeof(Factory).GetMethods()
            .Single(m => m.Name == nameof(Factory.Create)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters() is [var first, var rest]
                         && first.ParameterType == typeof(Action<DefaultHttpContext>)
                         && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        Action<DefaultHttpContext> configureContext = context => context.Response.Body = new MemoryStream();
        return (BaseEndpoint)create.Invoke(null, [configureContext, new object[] { sender, logger }])!;
    }

    private static Task HandleAsync(BaseEndpoint endpoint, ExecuteWorkflow request) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [typeof(ExecuteWorkflow), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private sealed class ThrowingRequestSender(Exception exception) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
            where T : notnull =>
            throw exception;
    }
}
