using System.Security.Cryptography;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime;

namespace Elsa.Etl.Tests;

/// <summary>The same host-owned files and pinned revisions for native ETL journey fixtures.</summary>
internal static class EtlNativeTestFileBindings
{
    public static IReadOnlyList<EtlHostFileBindingOptions> Create(EtlScope scope, string directory)
    {
        return
        [
            Binding("source", "Source", "source.csv", immutableInput: true),
            Binding("lookup", "Lookup", "lookup.csv", immutableInput: true),
            Binding("output", "MainOutput", "output", immutableInput: false),
            Binding("errors", "ErrorOutput", "errors", immutableInput: false)
        ];

        EtlHostFileBindingOptions Binding(string alias, string role, string file, bool immutableInput) => new()
        {
            TenantId = scope.TenantId,
            PersistencePartition = scope.PersistencePartition,
            Alias = alias,
            Role = role,
            Revision = alias + "-v1",
            Path = Path.Combine(directory, file),
            IsCurrent = true,
            ExpectedSha256 = immutableInput
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, file)))).ToLowerInvariant()
                : null,
            ImmutableInput = immutableInput,
            AtomicPublication = !immutableInput
        };
    }
}
