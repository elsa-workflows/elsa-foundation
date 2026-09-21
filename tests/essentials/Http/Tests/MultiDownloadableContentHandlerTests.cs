using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Http.Tests;

public class MultiDownloadableContentHandlerTests
{
    [Fact]
    public async Task SelectsLowestPriorityMatchingHandler_RegardlessOfRegistrationOrder()
    {
        // Both handlers support the item; the higher-Priority one is registered FIRST so that a
        // naive FirstOrDefault (registration order) would pick it. Ordering by Priority ascending
        // must instead pick the lower-Priority handler.
        var highPriority = new FakeDownloadableContentHandler(priority: 10, marker: "high");
        var lowPriority = new FakeDownloadableContentHandler(priority: 1, marker: "low");

        var handler = CreateHandler(highPriority, lowPriority);

        var result = await SingleDownloadableMarkerAsync(handler);

        Assert.Equal("low", result);
    }

    [Fact]
    public async Task SelectsLowestPriorityMatchingHandler_WhenLowPriorityRegisteredFirst()
    {
        var lowPriority = new FakeDownloadableContentHandler(priority: 1, marker: "low");
        var highPriority = new FakeDownloadableContentHandler(priority: 10, marker: "high");

        var handler = CreateHandler(lowPriority, highPriority);

        var result = await SingleDownloadableMarkerAsync(handler);

        Assert.Equal("low", result);
    }

    private static MultiDownloadableContentHandler CreateHandler(params IDownloadableContentHandler[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var h in handlers)
            services.AddSingleton(h);
        return new MultiDownloadableContentHandler(services.BuildServiceProvider());
    }

    private static async Task<string> SingleDownloadableMarkerAsync(MultiDownloadableContentHandler handler)
    {
        var items = new object[] { "an-item" };
        var downloadableFunc = handler.GetDownloadablesAsync(items, CancellationToken.None).Single();
        var downloadable = await downloadableFunc();
        return downloadable.Filename!;
    }

    private sealed class FakeDownloadableContentHandler(float priority, string marker) : IDownloadableContentHandler
    {
        public float Priority => priority;
        public bool SupportsContent(object content) => content is string;

        public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
        {
            yield return () => new(new Downloadable(new MemoryStream(), marker));
        }
    }
}
