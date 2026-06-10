using Groundwork.Documents.Store;
using Xunit;

namespace Groundwork.Tests;

public sealed class DocumentStoreQueryTests
{
    [Theory]
    [InlineData(-1, null, "skip")]
    [InlineData(null, -1, "take")]
    public void NegativePagingValuesFailClearly(int? skip, int? take, string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DocumentStoreQuery("configurationDocument", "by-key", "alpha", skip, take));

        Assert.Equal(parameterName, exception.ParamName);
    }
}
