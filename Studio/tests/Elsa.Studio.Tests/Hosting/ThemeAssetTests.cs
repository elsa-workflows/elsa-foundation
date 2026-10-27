using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Elsa.Studio.Tests.Hosting;

public sealed class ThemeAssetTests
{
    private const string Assets = "/_elsa/theme-store/assets";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_only_source_collections_cannot_be_exposed_or_deleted_as_material_assets(bool directorySource)
    {
        const string id = "00000000000000000000000000000000.png";
        const string collection = """{"themes":[]}""";
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) =>
        {
            options.AssetsDirectory = "materials";
            Directory.CreateDirectory(Path.Combine(root, "materials"));
            var target = Path.Combine(root, "materials", id);
            File.WriteAllText(target, collection);
            if (OperatingSystem.IsWindows()) options.ThemesDirectory = target; // No link-creation privilege required.
            else
            {
                Directory.CreateDirectory(Path.Combine(root, "sources"));
                var source = Path.Combine(root, "sources", "themes.json");
                if (new FileInfo(source).LinkTarget is null) File.CreateSymbolicLink(source, target);
                options.ThemesDirectory = directorySource ? "sources" : source;
            }
        });

        var store = await host.Client.GetFromJsonAsync<JsonObject>("/_elsa/theme-store");
        using var read = await host.Client.GetAsync(Assets + "/" + id + "/content");
        using var delete = await host.Client.DeleteAsync(Assets + "/" + id);
        var sourceFile = Path.Combine(host.Root, "materials", id);
        Assert.True(File.Exists(sourceFile));
        Assert.Equal(collection, await File.ReadAllTextAsync(sourceFile));
        Assert.Empty(store!["assets"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/jpeg", ".jpg")]
    public async Task Material_upload_read_list_and_delete_use_the_configured_owned_directory(string contentType, string extension)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, _) => options.AssetsDirectory = "state/materials");
        using var uploaded = await Upload(host, [1, 2, 3], contentType);
        uploaded.EnsureSuccessStatusCode();
        var asset = JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!;
        var id = (string)asset["id"]!;
        Assert.EndsWith(extension, id);
        Assert.True(File.Exists(Path.Combine(host.Root, "state", "materials", id)));
        using var content = await host.Client.GetAsync(Assets + "/" + id + "/content");
        content.EnsureSuccessStatusCode();
        Assert.Equal(contentType, content.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await content.Content.ReadAsByteArrayAsync());
        var store = await host.Client.GetFromJsonAsync<JsonObject>("/_elsa/theme-store");
        Assert.Equal(id, (string?)Assert.Single(store!["assets"]!.AsArray())!["id"]);
        using var deleted = await host.Client.DeleteAsync(Assets + "/" + id);
        deleted.EnsureSuccessStatusCode();
        Assert.False(File.Exists(Path.Combine(host.Root, "state", "materials", id)));
        using var missing = await host.Client.GetAsync(Assets + "/" + id + "/content");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(0, "image/png", HttpStatusCode.BadRequest)]
    [InlineData(2 * 1024 * 1024, "image/png", HttpStatusCode.OK)]
    [InlineData(2 * 1024 * 1024 + 1, "image/png", HttpStatusCode.BadRequest)]
    [InlineData(3, "image/svg+xml", HttpStatusCode.BadRequest)]
    [InlineData(3, "text/html", HttpStatusCode.BadRequest)]
    public async Task Invalid_asset_payloads_never_publish_a_file(int length, string type, HttpStatusCode expected)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement");
        using var response = await Upload(host, new byte[length], type);
        Assert.Equal(expected, response.StatusCode);
        var folder = Path.Combine(host.Root, "theme-assets");
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Count() : 0);
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("../settings.json")]
    [InlineData("..\\settings.json")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Asset_ids_cannot_expose_or_delete_unrelated_owned_files(string id)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement");
        var folder = Directory.CreateDirectory(Path.Combine(host.Root, "theme-assets")).FullName;
        var file = Path.Combine(folder, "settings.json");
        await File.WriteAllTextAsync(file, "unrelated-owned-file-sentinel");
        var encoded = Uri.EscapeDataString(id);
        using var read = await host.Client.GetAsync(Assets + "/" + encoded + "/content");
        using var delete = await host.Client.DeleteAsync(Assets + "/" + encoded);
        Assert.False(read.IsSuccessStatusCode);
        Assert.False(delete.IsSuccessStatusCode);
        Assert.DoesNotContain("unrelated-owned-file-sentinel", await read.Content.ReadAsStringAsync());
        Assert.Equal("unrelated-owned-file-sentinel", await File.ReadAllTextAsync(file));
        var store = await host.Client.GetFromJsonAsync<JsonObject>("/_elsa/theme-store");
        Assert.Empty(store!["assets"]!.AsArray());
    }

    [Fact]
    public async Task Cancelled_partial_upload_is_never_listed_and_leaves_no_incomplete_file()
    {
        var partial = new PartialUpload();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configurePipeline: app => app.Use(async (context, next) =>
        {
            if (context.Request.Method != "POST" || context.Request.Path != Assets) { await next(context); return; }
            context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), new FormFileCollection { partial });
            try { await next(context); }
            finally { completed.TrySetResult(); }
        }));
        using var cancellation = new CancellationTokenSource();
        using var form = new MultipartFormDataContent();
        var pending = host.Client.PostAsync(Assets, form, cancellation.Token);
        try
        {
            await partial.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var folder = Path.Combine(host.Root, "theme-assets");
            Assert.Single(Directory.EnumerateFiles(folder));
            var store = await host.Client.GetFromJsonAsync<JsonObject>("/_elsa/theme-store");
            Assert.Empty(store!["assets"]!.AsArray());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Empty(Directory.EnumerateFiles(folder));
        }
        finally { cancellation.Cancel(); }
    }

    [Fact]
    public async Task An_opened_asset_response_survives_a_concurrent_delete()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configurePipeline: app => app.Use(async (context, next) =>
        {
            if (!context.Request.Path.Value!.EndsWith("/content", StringComparison.Ordinal)) { await next(context); return; }
            var original = context.Response.Body;
            context.Response.Body = new PausedAssetWrites(original, entered, release);
            try { await next(context); }
            finally { context.Response.Body = original; }
        }));
        using var uploaded = await Upload(host, [1, 2, 3], "image/png");
        uploaded.EnsureSuccessStatusCode();
        var id = (string)JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!["id"]!;
        var pending = host.Client.GetAsync(Assets + "/" + id + "/content");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var deleted = await host.Client.DeleteAsync(Assets + "/" + id);
            deleted.EnsureSuccessStatusCode();
            Assert.False(File.Exists(Path.Combine(host.Root, "theme-assets", id)));
        }
        finally { release.TrySetResult(); }
        using var response = await pending;
        response.EnsureSuccessStatusCode();
        Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
    }

    private static async Task<HttpResponseMessage> Upload(ThemeHost host, byte[] bytes, string type)
    {
        using var form = new MultipartFormDataContent();
        using var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new(type);
        form.Add(body, "asset", "../original-material.png");
        return await host.Client.PostAsync(Assets, form);
    }

    private sealed class PartialUpload : IFormFile
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ContentType => "image/png";
        public string ContentDisposition => "form-data; name=asset; filename=partial.png";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => 3;
        public string Name => "asset";
        public string FileName => "partial.png";
        public Stream OpenReadStream() => throw new NotSupportedException();
        public void CopyTo(Stream target) => throw new NotSupportedException();
        public async Task CopyToAsync(Stream target, CancellationToken token = default)
        {
            await target.WriteAsync(new byte[] { 1, 2, 3 }, token);
            await target.FlushAsync(token);
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
    }

    private sealed class PausedAssetWrites(Stream inner, TaskCompletionSource entered, TaskCompletionSource release) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        public override Task WriteAsync(byte[] bytes, int offset, int count, CancellationToken token) => WriteAsync(bytes.AsMemory(offset, count), token).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            await inner.WriteAsync(bytes, token);
        }
    }
}
