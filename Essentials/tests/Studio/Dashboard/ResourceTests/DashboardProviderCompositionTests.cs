using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Elsa.Studio.Dashboard.ResourceTests;
public sealed class DashboardProviderCompositionTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public void Unshipped_builtin_provider_migrations_cannot_be_selected(string provider)
    {
        var services = new ServiceCollection();
        Assert.Throws<NotSupportedException>(() => services.AddDashboardResourcesEntityFrameworkCore(new DashboardResourcePersistenceOptions { Provider = provider }));
        Assert.Empty(services);
    }
}
