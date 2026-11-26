using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;
using Nuplane.Sources.Directory.Builder;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Tests.Administration;

public sealed class PackageProtectionTests
{
    private static string AssetUrl => $"/_content/{PackageAssetTests.PackageId}/probe.txt";

    [Fact]
    public async Task Slow_responses_keep_the_payload_limit_while_queue_cancellation_releases_admission()
    {
        using var logs = new ContentLog();
        var writes = new ResponseWrites();
        await using var host = await AdministrationHost.StartAsync(nativeStore: true,
            afterStudio: builder => Observe(builder, logs), configurePipeline: app => app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments($"/_content/{PackageAssetTests.PackageId}")) { await next(context); return; }
                var original = context.Response.Body;
                context.Response.Body = new PausedWrites(original, writes);
                try { await next(context); }
                finally { context.Response.Body = original; }
            }));
        PackageAssetTests.Pack(host, "1.0.0", "detached bytes");
        await PackageAssetTests.Reconcile(host);
        using var cancellation = new CancellationTokenSource();
        var first = Enumerable.Range(0, 4).Select(_ => host.Client.GetAsync(AssetUrl)).ToArray();
        Task<HttpResponseMessage>[] queued = [];
        Task<HttpResponseMessage>? cancelled = null;
        Task<HttpResponseMessage>? replacement = null;
        try
        {
            await writes.FourEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // Package ownership has ended after detachment even though all four response buffers remain retained.
            await PackageAssetTests.Reconcile(host);
            queued = Enumerable.Range(0, 27).Select(_ => host.Client.GetAsync(AssetUrl)).ToArray();
            cancelled = host.Client.GetAsync(AssetUrl, cancellation.Token);
            await logs.WaitQueued(32).WaitAsync(TimeSpan.FromSeconds(30));
            using var saturationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var saturated = await host.Client.GetAsync(AssetUrl, saturationTimeout.Token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, saturated.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), saturated.Headers.RetryAfter!.Delta);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
            await logs.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30));
            replacement = host.Client.GetAsync(AssetUrl);
            await logs.WaitQueued(33).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(4, writes.Entered);
        }
        finally
        {
            writes.Release.TrySetResult();
            cancellation.Cancel();
            if (cancelled is not null)
            {
                try { using var response = await cancelled; }
                catch (OperationCanceledException) { }
            }
            foreach (var task in first.Concat(queued).Concat(replacement is null ? [] : [replacement]))
            {
                using var response = await task;
                response.EnsureSuccessStatusCode();
                Assert.Equal("detached bytes", await response.Content.ReadAsStringAsync());
            }
        }
    }

    [Theory]
    [InlineData("file-link")]
    [InlineData("directory-link")]
    [InlineData("hard-link")]
    public async Task Archive_reads_ignore_links_in_the_extracted_install(string kind)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        PackageAssetTests.Pack(host, "1.0.0", "inside");
        await PackageAssetTests.Reconcile(host);
        var package = Assert.Single((await host.App.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(CancellationToken.None)).Packages);
        var outside = Directory.CreateDirectory(Path.Combine(host.Root, "outside")).FullName;
        var sentinel = Path.Combine(outside, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "outside-link-sentinel");
        var assets = Path.Combine(package.InstallPath, "staticwebassets");
        var url = $"/_content/{PackageAssetTests.PackageId}/linked.txt";
        if (kind == "directory-link")
        {
            Directory.CreateSymbolicLink(Path.Combine(assets, "linked"), outside);
            url = $"/_content/{PackageAssetTests.PackageId}/linked/sentinel.txt";
        }
        else if (kind == "file-link") File.CreateSymbolicLink(Path.Combine(assets, "linked.txt"), sentinel);
        else
        {
            var target = Path.Combine(assets, "linked.txt");
            if (OperatingSystem.IsWindows()) Assert.True(CreateWindowsLink(target, sentinel, IntPtr.Zero));
            else Assert.Equal(0, CreateUnixLink(sentinel, target));
        }
        using var response = await host.Client.GetAsync(url);
        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain("outside-link-sentinel", await response.Content.ReadAsStringAsync());
        Assert.Equal("outside-link-sentinel", await File.ReadAllTextAsync(sentinel));
    }

    [Fact]
    public async Task A_static_provider_cannot_bypass_archive_verification()
    {
        await using var host = await AdministrationHost.StartAsync(nuplane => nuplane.AddDirectoryFeed("ordinary", "feed", feed => { feed.Watch = false; feed.IncludeAll(); }));
        PackageAssetTests.Pack(host, "1.0.0", "unprotected-package-sentinel");
        await PackageAssetTests.Reconcile(host);
        var package = Assert.Single((await host.App.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(CancellationToken.None)).Packages);
        File.Delete(Path.Combine(package.InstallPath, ".nuplane-archive"));
        using var physical = new PhysicalFileProvider(Path.Combine(package.InstallPath, "staticwebassets"));
        host.App.Environment.WebRootFileProvider = new MappedFile(physical.GetFileInfo("probe.txt"));
        using var response = await host.Client.GetAsync(AssetUrl);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("unprotected-package-sentinel", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_deployed_file_alias_cannot_bypass_archive_verification()
    {
        await using var host = await AdministrationHost.StartAsync(nuplane => nuplane.AddDirectoryFeed("ordinary", "feed", feed => { feed.Watch = false; feed.IncludeAll(); }));
        PackageAssetTests.Pack(host, "1.0.0", "aliased-unprotected-package-sentinel");
        await PackageAssetTests.Reconcile(host);
        var package = Assert.Single((await host.App.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(CancellationToken.None)).Packages);
        var installRoot = host.App.Services.GetRequiredService<IOptions<Nuplane.Feeds.Configuration.FeedResolutionOptions>>().Value.PackageInstallRoot!;
        var alias = Path.Combine(host.Root, "install-alias");
        Directory.CreateSymbolicLink(alias, installRoot);
        var relative = Path.GetRelativePath(installRoot, package.InstallPath);
        File.Delete(Path.Combine(package.InstallPath, ".nuplane-archive"));
        using var physical = new PhysicalFileProvider(Path.Combine(alias, relative, "staticwebassets"));
        host.App.Environment.WebRootFileProvider = new MappedFile(physical.GetFileInfo("probe.txt"));
        using var response = await host.Client.GetAsync(AssetUrl);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("aliased-unprotected-package-sentinel", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_custom_archive_install_path_cannot_supply_unverified_bytes()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, afterStudio: builder =>
        {
            var original = builder.Services.Last(service => service.ServiceType == typeof(INuplaneAdminOperations));
            builder.Services.Replace(ServiceDescriptor.Singleton<INuplaneAdminOperations>(provider => new ArchiveAdmin(
                (INuplaneAdminOperations)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), Path.Combine(builder.Environment.ContentRootPath, "custom.nupkg"))));
        });
        PackageAssetTests.Pack(host, "1.0.0", "archive-payload-sentinel");
        await PackageAssetTests.Reconcile(host);
        File.Copy(Path.Combine(host.Root, "feed", PackageAssetTests.PackageId + ".1.0.0.nupkg"), Path.Combine(host.Root, "custom.nupkg"));
        using var response = await host.Client.GetAsync(AssetUrl);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("archive-payload-sentinel", await response.Content.ReadAsStringAsync());
    }

    private static void Observe(WebApplicationBuilder builder, ContentLog logs)
    {
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(logs);
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateUnixLink(string existing, string target);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateWindowsLink(string target, string existing, IntPtr security);

    private sealed class ArchiveAdmin(INuplaneAdminOperations inner, string path) : INuplaneAdminOperations
    {
        public async Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken token)
        {
            var snapshot = await inner.GetPackagesAsync(token);
            return snapshot with { Packages = snapshot.Packages.Select(package => package with { InstallPath = path }).ToArray() };
        }
        public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken token) => inner.GetStateAsync(token);
        public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken token) => inner.TriggerReconcileAsync(token);
    }

    private sealed class MappedFile(IFileInfo file) : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IFileInfo GetFileInfo(string subpath) => subpath.TrimStart('/') == $"_content/{PackageAssetTests.PackageId}/probe.txt" ? file : new NotFoundFileInfo(subpath);
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }
}

internal sealed class ContentLog : ILoggerProvider
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _queue = new();
    private int _queued;
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitQueued(int count)
    {
        var signal = _queue.GetOrAdd(count, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (Volatile.Read(ref _queued) >= count) signal.TrySetResult();
        return signal.Task;
    }
    public ILogger CreateLogger(string categoryName) => new Capture(this);
    public void Dispose() { }
    private sealed class Capture(ContentLog owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (id.Name == "PackageContentRequestQueued")
            {
                var count = Interlocked.Increment(ref owner._queued);
                if (owner._queue.TryGetValue(count, out var signal)) signal.TrySetResult();
            }
            if (id.Name == "PackageContentRequestCancelled") owner.Cancelled.TrySetResult();

        }
    }
}

internal sealed class ResponseWrites
{
    private int _entered;
    public int Entered => Volatile.Read(ref _entered);
    public TaskCompletionSource FourEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task Enter(CancellationToken token)
    {
        if (Interlocked.Increment(ref _entered) == 4) FourEntered.TrySetResult();
        await Release.Task.WaitAsync(token);
    }
}

internal sealed class PausedWrites(Stream inner, ResponseWrites writes) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        await writes.Enter(token);
        await inner.WriteAsync(buffer, token);
    }
}
