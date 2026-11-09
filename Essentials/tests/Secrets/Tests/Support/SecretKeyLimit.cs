using Elsa.Secrets.Core.Models;
using Xunit;

namespace Elsa.Secrets.Tests.Support;

/// <summary>
/// A tenant whose secrets reach <see cref="SecretNameConstraints.MaximumKeyLength"/>: a secret named
/// <see cref="NameLength"/> characters there, with a generated id, fills the key the row its delete writes has.
/// The Secrets and Secrets EF test projects compile this file in.
/// </summary>
internal static class SecretKeyLimit
{
    public const int SecretIdLength = 11; // ShortIdentityGenerator ids are 11 Base62 characters

    public static readonly string TenantId = new('t', 200);

    /// <summary>The longest name a secret in <see cref="TenantId"/> can have.</summary>
    public static readonly int NameLength = SecretNameConstraints.MaximumKeyLength - TenantId.Length - SecretIdLength;

    /// <summary>Asserts that a secret in <see cref="TenantId"/> with this name and id fills the key exactly.</summary>
    public static void AssertFillsKey(string name, string id) =>
        Assert.Equal(SecretNameConstraints.MaximumKeyLength, TenantId.Length + name.Length + id.Length);
}
