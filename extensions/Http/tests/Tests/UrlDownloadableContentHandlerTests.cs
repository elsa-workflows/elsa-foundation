using System.Net;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Options;
using Elsa.Http.Services;
using Microsoft.AspNetCore.StaticFiles;
using Xunit;

namespace Elsa.Http.Tests;

public class UrlDownloadableContentHandlerTests
{
    private readonly UrlDownloadableContentHandler handler;
    private readonly TrackingHttpContent content = new([1, 2, 3]);

    public UrlDownloadableContentHandlerTests()
    {
        handler = new(new StubFileDownloader(content), new FileExtensionContentTypeProvider());
    }

    [Fact]
    public async Task DisposingDownloadableStream_DisposesUnderlyingResponse()
    {
        var downloadableFunc = handler.GetDownloadablesAsync(new Uri("https://example.com/file.bin"), CancellationToken.None).Single();
        var downloadable = await downloadableFunc();

        Assert.False(content.Disposed, "The response content should still be alive before the downloadable stream is disposed.");

        await downloadable.Stream.DisposeAsync();

        Assert.True(content.Disposed, "Disposing the downloadable's stream must dispose the underlying HttpResponseMessage (and its content).");
    }

    [Fact]
    public async Task DownloadableStream_ReadsResponseContent()
    {
        var downloadableFunc = handler.GetDownloadablesAsync(new Uri("https://example.com/file.bin"), CancellationToken.None).Single();
        var downloadable = await downloadableFunc();

        using var buffer = new MemoryStream();
        await downloadable.Stream.CopyToAsync(buffer);

        Assert.Equal([1, 2, 3], buffer.ToArray());
    }

    private sealed class StubFileDownloader(TrackingHttpContent content) : IFileDownloader
    {
        public Task<HttpResponseMessage> DownloadAsync(Uri url, FileDownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            return Task.FromResult(response);
        }
    }

    private sealed class TrackingHttpContent : HttpContent
    {
        private readonly byte[] bytes;

        public TrackingHttpContent(byte[] bytes) => this.bytes = bytes;

        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Disposed = true;

            base.Dispose(disposing);
        }
    }
}
