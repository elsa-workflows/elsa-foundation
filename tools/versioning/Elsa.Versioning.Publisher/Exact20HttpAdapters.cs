using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Elsa.Versioning.Publisher;

/// <summary>Authenticated, fixed-repository GitHub reads for the incident-specific recovery command.</summary>
internal sealed class Exact20GitHub(HttpClient http, string token) : IExact20GitHub
{
    private const int MaximumJsonBytes = 2 * 1024 * 1024;
    private static readonly Uri ApiRoot = new(Exact20RecoveryPins.RepositoryApi, UriKind.Absolute);

    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public async Task<string> GetMainShaAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("commits/main", cancellationToken);
        return String(document.RootElement, "sha");
    }

    public async Task<Exact20WorkflowRun> GetRunAsync(long runId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"actions/runs/{Positive(runId)}", cancellationToken);
        var root = document.RootElement;
        return new Exact20WorkflowRun(
            Number(root, "id"),
            Number(root, "workflow_id"),
            String(root, "path"),
            String(root, "event"),
            String(root, "status"),
            NullableString(root, "conclusion"),
            Integer(root, "run_attempt"),
            String(root, "head_branch"),
            String(root, "head_sha"),
            Number(root.GetProperty("repository"), "id"),
            Number(root.GetProperty("head_repository"), "id"));
    }

    public async Task<Exact20Artifact> GetArtifactAsync(long artifactId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"actions/artifacts/{Positive(artifactId)}", cancellationToken);
        var root = document.RootElement;
        var workflowRun = root.GetProperty("workflow_run");
        return new Exact20Artifact(
            Number(root, "id"),
            String(root, "name"),
            Boolean(root, "expired"),
            Number(root, "size_in_bytes"),
            Date(root, "expires_at"),
            String(root, "digest"),
            String(root, "archive_download_url"),
            Number(workflowRun, "id"),
            Number(workflowRun, "repository_id"),
            Number(workflowRun, "head_repository_id"),
            String(workflowRun, "head_branch"),
            String(workflowRun, "head_sha"));
    }

    public async Task DownloadArtifactAsync(long artifactId, string destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var endpoint = ApiUri($"actions/artifacts/{Positive(artifactId)}/zip");
        using var apiResponse = await SendAsync(endpoint, authenticated: true, cancellationToken);
        HttpResponseMessage response;
        if (apiResponse.StatusCode == HttpStatusCode.OK)
        {
            response = apiResponse;
        }
        else if ((int)apiResponse.StatusCode is 301 or 302 or 303 or 307 or 308 && apiResponse.Headers.Location is { } location)
        {
            var downloadUri = location.IsAbsoluteUri ? location : new Uri(endpoint, location);
            if (!IsGitHubArtifactStorage(downloadUri))
                throw new InvalidOperationException("GitHub's artifact download redirect did not name an approved HTTPS storage host.");

            // The signed storage URL is requested separately; the GitHub token is never forwarded off api.github.com.
            response = await SendAsync(downloadUri, authenticated: false, cancellationToken);
        }
        else
        {
            throw HttpFailure("the pinned artifact download", apiResponse.StatusCode);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
                throw HttpFailure("the pinned artifact download", response.StatusCode);
            if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
                throw new InvalidDataException("The pinned artifact download exceeds its fixed size bound.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                    break;
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException("The pinned artifact download exceeds its fixed size bound.");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
    }

    public async Task<string> GetWorkflowStateAsync(long workflowId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"actions/workflows/{Positive(workflowId)}", cancellationToken);
        return String(document.RootElement, "state");
    }

    public async Task<Exact20RunCount> GetNonterminalRunCountAsync(long workflowId, string status, CancellationToken cancellationToken)
    {
        if (!Exact20RecoveryCommand.NonterminalStatusNames.Contains(status, StringComparer.Ordinal))
            throw new InvalidOperationException("The recovery command requested an unexpected workflow run status.");
        using var document = await GetJsonAsync($"actions/workflows/{Positive(workflowId)}/runs?status={Uri.EscapeDataString(status)}&per_page=1", cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("total_count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var value))
            return new Exact20RunCount(false, -1);
        return new Exact20RunCount(true, value);
    }

    public async Task<Exact20ApprovalComment> GetApprovalCommentAsync(long commentId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"issues/comments/{Positive(commentId)}", cancellationToken);
        var root = document.RootElement;
        return new Exact20ApprovalComment(
            Number(root, "id"),
            String(root, "issue_url"),
            String(root.GetProperty("user"), "login"),
            String(root, "body"),
            Date(root, "created_at"),
            Date(root, "updated_at"));
    }

    private async Task<JsonDocument> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(ApiUri(relativePath), authenticated: true, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw HttpFailure("a fixed GitHub API read", response.StatusCode);
        if (response.Content.Headers.ContentLength is > MaximumJsonBytes)
            throw new InvalidDataException("A fixed GitHub API response exceeds the recovery JSON bound.");

        try
        {
            var bytes = await ReadBoundedAsync(response.Content, MaximumJsonBytes, cancellationToken);
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A fixed GitHub API response was not valid JSON.", exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, bool authenticated, CancellationToken cancellationToken)
    {
        if (authenticated && !IsApiHost(uri))
            throw new InvalidOperationException("GitHub credentials may be sent only to api.github.com.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("elsa-exact20-recovery/1.0");
        if (authenticated)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new HttpRequestException("A fixed GitHub endpoint did not answer.", exception);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0)
                return buffer.ToArray();
            if (buffer.Length + read > maximumBytes)
                throw new InvalidDataException("A fixed GitHub API response exceeds the recovery JSON bound.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static Uri ApiUri(string path)
    {
        var uri = new Uri(ApiRoot, path);
        if (!IsApiHost(uri))
            throw new InvalidOperationException("The recovery command constructed a GitHub API URL outside api.github.com.");
        return uri;
    }

    private static bool IsApiHost(Uri uri) =>
        uri.IsAbsoluteUri && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
        string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 && uri.UserInfo.Length == 0;

    private static bool IsGitHubArtifactStorage(Uri uri) =>
        uri.IsAbsoluteUri && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
        uri.Port == 443 && uri.UserInfo.Length == 0 &&
        (string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase));

    private static long Positive(long value) => value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw new InvalidDataException($"A fixed GitHub API response has no integer {name} field.");

    private static int Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"A fixed GitHub API response has no integer {name} field.");

    private static string String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw new InvalidDataException($"A fixed GitHub API response has no string {name} field.");

    private static string? NullableString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Null ? null
        : element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw new InvalidDataException($"A fixed GitHub API response has no nullable string {name} field.");

    private static bool Boolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidDataException($"A fixed GitHub API response has no boolean {name} field.");

    private static DateTimeOffset Date(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : throw new InvalidDataException($"A fixed GitHub API response has no valid timestamp {name} field.");

    private static HttpRequestException HttpFailure(string action, HttpStatusCode status) =>
        new($"GitHub returned HTTP {(int)status} for {action}; response body omitted.");
}

internal sealed class Exact20Feed(NuGetFeed readFeed, Func<NuGetFeed> writeFeedFactory) : IExact20Feed
{
    private NuGetFeed? writeFeed;

    public void PrepareForExecution() =>
        writeFeed = writeFeedFactory();

    public Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
        readFeed.ListVersionsAsync(packageId, cancellationToken);

    public Task<Exact20PackageResponse> GetPackageAsync(string packageId, string version, CancellationToken cancellationToken) =>
        readFeed.GetPackageAsync(packageId, version, 16 * 1024 * 1024, cancellationToken);

    public Task<PushResult> PushAsync(string packagePath, CancellationToken cancellationToken) =>
        (writeFeed ?? throw new InvalidOperationException("The exact20 Feedz write adapter was not prepared after successful preflight.")).PushAsync(packagePath, cancellationToken);
}
