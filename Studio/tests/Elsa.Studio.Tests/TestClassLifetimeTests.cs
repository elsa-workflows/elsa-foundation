namespace Elsa.Studio.Tests;

public sealed class TestClassLifetimeTests
{
    [Fact]
    public void Test_classes_with_async_cleanup_use_the_xunit_lifetime_contract()
    {
        // xUnit 2 does not invoke IAsyncDisposable. A teardown-shaped method alone can
        // leave hundreds of TestServers and their configuration watchers running.
        var missingLifetime = typeof(TestClassLifetimeTests).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetMethods().Any(method => method.IsDefined(typeof(FactAttribute), inherit: true)))
            .Where(type => typeof(IAsyncDisposable).IsAssignableFrom(type)
                           || type.GetMethod("DisposeAsync", Type.EmptyTypes) is not null)
            .Where(type => !typeof(IAsyncLifetime).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .Order()
            .ToArray();

        Assert.True(missingLifetime.Length == 0,
            "xUnit 2 only awaits async teardown through IAsyncLifetime: " + string.Join(", ", missingLifetime));
    }
}
