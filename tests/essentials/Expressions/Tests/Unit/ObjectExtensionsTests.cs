using System.Collections;
using Elsa.Primitives.Extensions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

public class ObjectExtensionsTests
{
    [Fact]
    public void ConvertIEnumerableToArray_converts_generic_enumerable_to_array()
    {
        var result = new List<int> { 1, 2, 3 }.Select(x => x * 2).ConvertIEnumerableToArray();

        var array = Assert.IsType<int[]>(result);
        Assert.Equal(new[] { 2, 4, 6 }, array);
    }

    [Fact]
    public void ConvertIEnumerableToArray_returns_strings_and_dictionaries_unchanged()
    {
        var dictionary = new Dictionary<string, int> { ["a"] = 1 };

        Assert.Equal("hello", "hello".ConvertIEnumerableToArray());
        Assert.Same(dictionary, dictionary.ConvertIEnumerableToArray());
    }

    [Fact]
    public void ConvertIEnumerableToArray_returns_async_enumerables_unchanged()
    {
        var asyncEnumerable = new DualEnumerable();

        Assert.Same(asyncEnumerable, asyncEnumerable.ConvertIEnumerableToArray());
    }

    [Fact]
    public void ConvertIEnumerableToArray_returns_null_input_as_null()
    {
        Assert.Null(((object?)null).ConvertIEnumerableToArray());
    }

    /// <summary>
    /// Mimics data-access adapters (e.g. EF Core's AsyncIListEnumerableAdapter&lt;T&gt;) that
    /// implement both <see cref="IEnumerable{T}"/> and <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    private sealed class DualEnumerable : IEnumerable<int>, IAsyncEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => Enumerable.Range(1, 3).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
