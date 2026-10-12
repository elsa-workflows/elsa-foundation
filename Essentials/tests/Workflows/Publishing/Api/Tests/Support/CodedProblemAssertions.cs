using System.Net;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// Asserts the module's established problem shape end to end, shared by every test that checks a
/// coded Publishing failure (issue #1699): content type, status, <c>title</c>, <c>type</c> (the
/// exact RFC 7231 URI for the status), the additive <c>errorCode</c>, and that <c>errors[0]</c>
/// still carries the general message under its established key.
/// </summary>
internal static class CodedProblemAssertions
{
    public static void AssertCodedProblem(HttpResponseMessage response, string raw, HttpStatusCode expectedStatus, string expectedErrorCode)
    {
        Assert.True(response.StatusCode == expectedStatus, $"{(int)response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(raw);
        var root = problem.RootElement;
        Assert.Equal(expectedErrorCode, root.GetProperty("errorCode").GetString());
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedStatus == HttpStatusCode.Conflict ? "Conflict" : "Bad Request", root.GetProperty("title").GetString());
        Assert.Equal(
            expectedStatus == HttpStatusCode.Conflict
                ? "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.8"
                : "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
            root.GetProperty("type").GetString());
        var error = Assert.Single(root.GetProperty("errors").EnumerateArray());
        Assert.Equal("generalErrors", error.GetProperty("name").GetString());
        Assert.Equal(root.GetProperty("detail").GetString(), error.GetProperty("reason").GetString());
    }
}
