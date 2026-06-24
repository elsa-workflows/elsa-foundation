using Elsa.Secrets.Core.Permissions;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsPermissionsTests
{
    [Fact]
    public void Permission_Names_Are_Stable()
    {
        Assert.Equal("secrets:read", SecretsPermissions.Read);
        Assert.Equal("secrets:write", SecretsPermissions.Write);
        Assert.Equal("secrets:update-value", SecretsPermissions.UpdateValue);
        Assert.Equal("secrets:delete", SecretsPermissions.Delete);
        Assert.Equal("secrets:test", SecretsPermissions.Test);
        Assert.Equal("secrets:use", SecretsPermissions.Use);
    }
}
