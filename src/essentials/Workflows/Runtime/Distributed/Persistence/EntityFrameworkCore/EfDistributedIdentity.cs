using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

/// <summary>Provider-independent scoped identity, digest, and ordinal UTF-16 projections shared by EF leaves.</summary>
internal static class EfDistributedIdentity
{
    public static string CreateId(string scope, string logicalIdentity) =>
        Hash($"{scope.Length}:{scope}{logicalIdentity.Length}:{logicalIdentity}");

    public static string Hash(string value) => EfRelationalIdentity.Hash(value);

    public static string EncodeScope(string value) => EfRelationalIdentity.Encode(value);

    public static string DecodeScope(string encoded) => EfRelationalIdentity.Decode(encoded);

    public static byte[] CreateOrderKey(string value, int width) =>
        EfRelationalIdentity.CreateOrderKey(value, width / sizeof(char) - 1);
}
