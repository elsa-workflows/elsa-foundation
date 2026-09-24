namespace Elsa.Persistence.EntityFramework;

public static class EfRelationalProviderBinding
{
    public static string ProviderPackageId(string provider) => throw new NotSupportedException();

    public static string? DescribeBindingFailure(string provider) => throw new NotSupportedException();

    public static T Select<T>(string provider, string owner, T sqlite, T sqlServer, T postgreSql, T mySql) =>
        throw new NotSupportedException();
}
