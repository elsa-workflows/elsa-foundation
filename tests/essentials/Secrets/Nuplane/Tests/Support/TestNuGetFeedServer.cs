using System.IO.Compression;
using System.Net;
using System.Text;

namespace Elsa.Secrets.Nuplane.Tests.Support;

/// <summary>
/// An in-process NuGet V3 feed that serves nothing without basic authentication, so a test can prove the
/// credential the Secrets module holds actually reached the wire.
/// </summary>
/// <remarks>
/// <para>
/// Built here rather than reused: Nuplane's own <c>TestNuGetFeedServer</c> lives in its test project and
/// ships in no package, and this repository has no basic-auth feed server of its own — the Cli fixtures'
/// feeds are all directories. The shape is deliberately the same as Nuplane's, because what is being tested
/// is the same handshake.
/// </para>
/// <para>
/// It counts requests by outcome and never exposes the header it received, so "authentication happened" can
/// be asserted without the test holding the value a second time.
/// </para>
/// </remarks>
internal sealed class TestNuGetFeedServer : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task serveLoop;
    private readonly byte[] packageBytes;
    private readonly string packageId;
    private readonly string version;
    private readonly string baseAddress;
    private readonly string expectedAuthorization;

    public TestNuGetFeedServer(string packageId, string version, string userName, string password)
    {
        this.packageId = packageId;
        this.version = version;
        packageBytes = Nupkg(packageId, version);
        expectedAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}"));

        baseAddress = $"http://127.0.0.1:{FreePort()}/";
        listener.Prefixes.Add(baseAddress);
        listener.Start();
        serveLoop = Task.Run(ServeAsync);
    }

    /// <summary>Every request that arrived, authorized or not.</summary>
    public int Requests { get; private set; }

    /// <summary>Requests that arrived carrying the expected basic-auth header.</summary>
    public int AuthorizedRequests { get; private set; }

    /// <summary>Requests answered with 401 because the header was missing or wrong.</summary>
    public int UnauthorizedRequests { get; private set; }

    public int PackageDownloads { get; private set; }

    public Uri ServiceIndexUri => new(new(baseAddress), "v3/index.json");

    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync();
        listener.Stop();
        try
        {
            await serveLoop;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            listener.Close();
            shutdown.Dispose();
        }
    }

    /// <summary>A minimal but real <c>.nupkg</c>: a zip carrying the nuspec Nuplane reads identity from.</summary>
    private static byte[] Nupkg(string packageId, string version)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var nuspec = new StreamWriter(archive.CreateEntry($"{packageId}.nuspec").Open(), Encoding.UTF8))
        {
            nuspec.Write(
                $"""
                 <?xml version="1.0" encoding="utf-8"?>
                 <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                   <metadata>
                     <id>{packageId}</id>
                     <version>{version}</version>
                     <authors>Elsa</authors>
                     <description>The fixture module this feed serves, to whoever authenticates.</description>
                     <dependencies />
                   </metadata>
                 </package>
                 """);
        }

        return buffer.ToArray();
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private async Task ServeAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception failure) when (failure is HttpListenerException or ObjectDisposedException && shutdown.IsCancellationRequested)
            {
                break;
            }

            await HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        Requests++;
        if (!string.Equals(context.Request.Headers["Authorization"], expectedAuthorization, StringComparison.Ordinal))
        {
            UnauthorizedRequests++;
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            // The challenge NuGet's own client waits for; the plain HttpClient download path sends its
            // header unasked.
            context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"elsa-secrets-test\"");
            context.Response.Close();
            return;
        }

        AuthorizedRequests++;

        var path = context.Request.Url?.AbsolutePath ?? "";
        var id = packageId.ToLowerInvariant();
        var pinned = version.ToLowerInvariant();

        if (path.Equals("/v3/index.json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context.Response,
                $$"""
                  {
                    "version": "3.0.0",
                    "resources": [ { "@id": "{{baseAddress}}flatcontainer/", "@type": "PackageBaseAddress/3.0.0" } ]
                  }
                  """);
            return;
        }

        if (path.Equals($"/flatcontainer/{id}/index.json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context.Response, $$"""{ "versions": ["{{pinned}}"] }""");
            return;
        }

        if (path.Equals($"/flatcontainer/{id}/{pinned}/{id}.{pinned}.nupkg", StringComparison.OrdinalIgnoreCase))
        {
            PackageDownloads++;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength64 = packageBytes.Length;
            await context.Response.OutputStream.WriteAsync(packageBytes);
            context.Response.Close();
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        context.Response.Close();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload);
        response.Close();
    }
}
