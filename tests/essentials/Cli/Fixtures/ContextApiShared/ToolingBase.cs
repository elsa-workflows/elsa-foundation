namespace Elsa.Persistence.EntityFramework
{
    public static class EfRelationalProviderBinding
    {
        public static string ProviderPackageId(string provider) => provider;

        public static string? DescribeBindingFailure(string provider) => null;

        public static T Select<T>(string provider, string owner, T sqlite, T sqlServer, T postgreSql, T mySql) => sqlite;
    }
}

namespace Elsa.Persistence.EntityFramework.Tooling
{
    public sealed class EfToolingConfigurationContext : IDisposable
    {
        public const int Version = 1;

        public void Dispose() { }
    }

    public static partial class EfToolingHost
    {
        public static Task<int> RunAsync(Stream request, Stream response)
        {
            WriteMarker("ELSA_CONTEXT_LEGACY_MARKER");
            return Task.FromResult(0);
        }

        public static EfToolingConfigurationContext CreateConfigurationContext(Stream request, CancellationToken cancellationToken)
        {
            WriteMarker("ELSA_CONTEXT_FACTORY_MARKER");
            return new();
        }

        private static void WriteMarker(string variable)
        {
            var path = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(path))
                File.WriteAllText(path, "invoked");
        }
    }
}
