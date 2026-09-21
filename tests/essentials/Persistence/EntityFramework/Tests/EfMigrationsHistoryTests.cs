using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfMigrationsHistoryTests
{
    [Fact]
    public void TableName_prefixes_the_module_identifier()
    {
        Assert.Equal("__EFMigrationsHistory_ElsaSecrets", EfMigrationsHistory.TableName("ElsaSecrets"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TableName_rejects_blank_module(string module)
    {
        Assert.Throws<ArgumentException>(() => EfMigrationsHistory.TableName(module!));
    }

    [Theory]
    [InlineData("Elsa.Secrets")]
    [InlineData("Elsa Secrets")]
    [InlineData("Elsa/Secrets")]
    public void TableName_rejects_path_like_module_names(string module)
    {
        Assert.Throws<ArgumentException>(() => EfMigrationsHistory.TableName(module));
    }
}
