using Elsa.Primitives.Persistence;
using Xunit;

namespace Elsa.Primitives.Tests.Persistence;

/// <summary>
/// Covers issue #384: <see cref="PageArgs.From"/> silently fell back to the default page when the caller
/// supplied only half of a page/pageSize or offset/limit pair (contradicting its documented
/// <see cref="ArgumentException"/> contract), and <see cref="PageArgs.Page"/> threw
/// <see cref="DivideByZeroException"/> for a legitimate <c>Limit == 0</c> ("count only") request.
/// </summary>
public sealed class PageArgsTests
{
    [Fact]
    public void From_complete_page_pair_produces_page_arguments()
    {
        var args = PageArgs.From(page: 2, pageSize: 25, offset: null, limit: null);

        Assert.Equal(50, args.Offset);
        Assert.Equal(25, args.Limit);
        Assert.Equal(2, args.Page);
        Assert.Equal(25, args.PageSize);
    }

    [Fact]
    public void From_complete_range_pair_produces_range_arguments()
    {
        var args = PageArgs.From(page: null, pageSize: null, offset: 30, limit: 10);

        Assert.Equal(30, args.Offset);
        Assert.Equal(10, args.Limit);
    }

    [Fact]
    public void From_prefers_the_page_pair_when_both_pairs_are_complete()
    {
        var args = PageArgs.From(page: 1, pageSize: 10, offset: 99, limit: 99);

        Assert.Equal(10, args.Offset);
        Assert.Equal(10, args.Limit);
    }

    [Theory]
    [InlineData(3, null, null, null)]
    [InlineData(null, 50, null, null)]
    [InlineData(null, null, 10, null)]
    [InlineData(null, null, null, 20)]
    [InlineData(3, null, 10, null)]
    [InlineData(null, 50, null, 20)]
    public void From_throws_when_a_pair_is_only_partially_specified(int? page, int? pageSize, int? offset, int? limit)
    {
        Assert.Throws<ArgumentException>(() => PageArgs.From(page, pageSize, offset, limit));
    }

    [Fact]
    public void From_defaults_to_the_first_page_of_100_when_nothing_is_specified()
    {
        var args = PageArgs.From(page: null, pageSize: null, offset: null, limit: null);

        Assert.Equal(0, args.Offset);
        Assert.Equal(100, args.Limit);
    }

    [Fact]
    public void Page_is_null_when_limit_is_zero_instead_of_dividing_by_zero()
    {
        var args = new PageArgs { Offset = 10, Limit = 0 };

        Assert.Null(args.Page);
    }

    [Fact]
    public void Page_is_null_when_offset_or_limit_is_missing()
    {
        Assert.Null(new PageArgs { Offset = null, Limit = 10 }.Page);
        Assert.Null(new PageArgs { Offset = 10, Limit = null }.Page);
    }

    [Fact]
    public void Page_is_the_zero_based_page_number()
    {
        Assert.Equal(3, new PageArgs { Offset = 30, Limit = 10 }.Page);
    }
}
