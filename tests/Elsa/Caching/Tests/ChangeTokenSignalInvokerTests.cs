using System.Reflection;
using Elsa.Caching.Memory.Services;
using Xunit;

namespace Elsa.Caching.Tests;

public sealed class ChangeTokenSignalInvokerTests
{
    [Fact]
    public void GetToken_Returns_Same_Token_Instance_For_Same_Key()
    {
        var invoker = new ChangeTokenSignalInvoker();

        var first = invoker.GetToken("my-key");
        var second = invoker.GetToken("my-key");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetToken_Returns_Different_Tokens_For_Different_Keys()
    {
        var invoker = new ChangeTokenSignalInvoker();

        var first = invoker.GetToken("key-a");
        var second = invoker.GetToken("key-b");

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task TriggerTokenAsync_Cancels_Token_And_Sets_HasChanged()
    {
        var invoker = new ChangeTokenSignalInvoker();
        var token = invoker.GetToken("my-key");

        Assert.False(token.HasChanged);

        await invoker.TriggerTokenAsync("my-key");

        Assert.True(token.HasChanged);
    }

    [Fact]
    public async Task TriggerTokenAsync_Removes_Entry_So_Subsequent_GetToken_Returns_A_Fresh_Token()
    {
        var invoker = new ChangeTokenSignalInvoker();
        var first = invoker.GetToken("my-key");

        await invoker.TriggerTokenAsync("my-key");

        var second = invoker.GetToken("my-key");

        Assert.NotSame(first, second);
        Assert.False(second.HasChanged);
    }

    [Fact]
    public async Task TriggerTokenAsync_Unknown_Key_Does_Not_Throw()
    {
        var invoker = new ChangeTokenSignalInvoker();

        // Should be a no-op; must not throw even though the key was never requested via GetToken.
        await invoker.TriggerTokenAsync("does-not-exist");
    }

    /// <summary>
    /// Regression test for https://github.com/elsa-workflows/elsa-foundation/issues/270.
    /// <para>
    /// Before the fix, <c>TriggerTokenAsync</c> called <see cref="CancellationTokenSource.Cancel"/> on the
    /// per-key <see cref="CancellationTokenSource"/> but never disposed it, leaking the native wait handle.
    /// There is no way to observe disposal through the public <see cref="Microsoft.Extensions.Primitives.IChangeToken"/>
    /// surface once cancellation has already happened (registering a callback on an already-cancelled token
    /// invokes the callback synchronously without ever checking the disposed flag), so this test reaches into
    /// the private <c>_changeTokens</c> dictionary via reflection to obtain the underlying
    /// <see cref="CancellationTokenSource"/> directly and asserts it was disposed.
    /// </para>
    /// <para>
    /// A disposed <see cref="CancellationTokenSource"/> throws <see cref="ObjectDisposedException"/> when its
    /// <see cref="CancellationTokenSource.Token"/> property is accessed, regardless of whether cancellation was
    /// already requested. This test fails before the #270 fix (no exception is thrown) and passes after it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TriggerTokenAsync_Disposes_The_Underlying_CancellationTokenSource()
    {
        var invoker = new ChangeTokenSignalInvoker();
        invoker.GetToken("my-key");

        var tokenSource = GetTokenSource(invoker, "my-key");

        // Sanity check: not cancelled or disposed yet.
        Assert.False(tokenSource.IsCancellationRequested);
        _ = tokenSource.Token;

        await invoker.TriggerTokenAsync("my-key");

        Assert.True(tokenSource.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => tokenSource.Token);
    }

    /// <summary>
    /// Retrieves the private <see cref="CancellationTokenSource"/> backing the change token for <paramref name="key"/>
    /// via reflection, since <see cref="ChangeTokenSignalInvoker"/> intentionally does not expose it publicly.
    /// </summary>
    private static CancellationTokenSource GetTokenSource(ChangeTokenSignalInvoker invoker, string key)
    {
        var dictionaryField = typeof(ChangeTokenSignalInvoker).GetField("_changeTokens", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not locate the '_changeTokens' field via reflection. The production implementation may have changed.");

        var dictionary = dictionaryField.GetValue(invoker)
            ?? throw new InvalidOperationException("The '_changeTokens' field was null.");

        var tryGetValue = dictionary.GetType().GetMethod("TryGetValue")
            ?? throw new InvalidOperationException("Could not locate 'TryGetValue' on the change token dictionary.");

        var args = new object?[] { key, null };
        var found = (bool)tryGetValue.Invoke(dictionary, args)!;
        Assert.True(found, $"Expected an entry for key '{key}' in the change token dictionary.");

        var changeTokenInfo = args[1]!;
        var tokenSourceProperty = changeTokenInfo.GetType().GetProperty("TokenSource", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not locate the 'TokenSource' property via reflection. The production implementation may have changed.");

        return (CancellationTokenSource)tokenSourceProperty.GetValue(changeTokenInfo)!;
    }
}
