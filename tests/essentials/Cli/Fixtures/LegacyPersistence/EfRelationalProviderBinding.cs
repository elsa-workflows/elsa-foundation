namespace Elsa.Persistence.EntityFramework;

public static class EfRelationalProviderBinding
{
    public static string ProviderPackageId(string provider) => throw new NotSupportedException();

    public static string? DescribeBindingFailure(string provider) => throw new NotSupportedException();

    /// <summary>Names the one provider the legacy `status` request uses; every other selection is unsupported, as before.</summary>
    public static T Select<T>(string provider, string owner, T sqlite, T sqlServer, T postgreSql, T mySql) =>
        provider == "Sqlite" ? sqlite : throw new NotSupportedException();
}
