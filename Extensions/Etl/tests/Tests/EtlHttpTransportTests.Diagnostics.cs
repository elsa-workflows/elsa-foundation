using System.Text;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Tests.Support;

namespace Elsa.Etl.Tests;

public sealed partial class EtlHttpTransportTests
{
    [Theory]
    [InlineData("Bearer")]
    [InlineData("Basic")]
    public async Task Http_diagnostics_omit_request_credentials_and_response_values_but_retain_platform_origin_and_path(string scheme)
    {
        const string queryKey = "synthetic-query-credential";
        const string cursor = "opaque-private-cursor";
        const string responseValue = "private-response-value";
        const string bodyValue = "private-request-value";
        var credential = scheme == "Basic" ? Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-user:synthetic-password"))
            : "synthetic-bearer-value";
        var authorization = scheme + " " + credential;
        using var diagnostics = new EtlDiagnosticCapture();
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(400, responseValue,
                new Dictionary<string, string> { ["X-Private-Response"] = responseValue })));
        var uri = server.CreateUri("/page?cursor=" + cursor);
        var authenticator = new CustomAuthenticator((request, _) =>
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.RequestUri = new UriBuilder(request.RequestUri!)
                { Query = request.RequestUri!.Query.TrimStart('?') + "&api_key=" + queryKey }.Uri;
            return ValueTask.CompletedTask;
        });
        await using var source = await ResolveAuthenticatedAsync(uri, "Source", authenticator);
        await using var destination = await ResolveAuthenticatedAsync(uri, "MainOutput", authenticator);
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var fetched = await transport.FetchAsync(source, uri, NewFetchAttempt(), 1024);
        var delivered = await transport.DeliverAsync(destination, NewDeliveryAttempt(), Encoding.UTF8.GetBytes(bodyValue), 1024);

        Assert.Equal(400, fetched.Outcome.StatusCode);
        Assert.Null(fetched.Body);
        Assert.Equal(400, delivered.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, request =>
        {
            Assert.Equal(authorization, request.Headers["Authorization"]);
            Assert.Contains(cursor, request.RawTarget, StringComparison.Ordinal);
            Assert.Contains(queryKey, request.RawTarget, StringComparison.Ordinal);
        });
        Assert.Equal(bodyValue, Encoding.UTF8.GetString(server.Requests[1].Body));
        string[] markers = [queryKey, cursor, credential, responseValue, bodyValue, "synthetic-user", "synthetic-password"];
        diagnostics.AssertPlatformRequests(2, server.CreateUri("/page").AbsoluteUri, markers);
        EtlDiagnosticCapture.AssertAbsent(fetched + "\n" + delivered, markers);
        // ETL currently emits no custom spans. Adding instrumentation requires a positive value-free span proof.
        Assert.DoesNotContain(diagnostics.Activities, item => item.Source.StartsWith("Elsa.Etl", StringComparison.Ordinal));

        // A provider exception is refused before sending, without retaining its message or inner exception.
        var failure = new CustomAuthenticator((_, _) => throw new InvalidOperationException(string.Join(" ", markers),
            new Exception(uri.AbsoluteUri)));
        await using var refused = await ResolveAuthenticatedAsync(uri, "Source", failure);
        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await transport.FetchAsync(refused, uri, NewFetchAttempt(), 1024));
        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.Null(error.InnerException);
        EtlDiagnosticCapture.AssertAbsent(error.ToString(), markers);
        Assert.Equal(2, server.Requests.Count);
        diagnostics.AssertPlatformRequests(2, server.CreateUri("/page").AbsoluteUri, markers);
    }
}
