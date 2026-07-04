using System.Security.Cryptography;
using System.Text;
using Elsa.Secrets.Options;
using Elsa.Secrets.Services;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretKeyRingTests
{
    [Fact]
    public void Value_Encrypted_Under_Old_Key_Is_Still_Readable_After_Rotation()
    {
        // Encrypt under k1 as the active key.
        var before = new DefaultSecretValueProtector(BuildRing(activeKeyId: "k1",
            ("k1", "material-one")));
        var protectedByK1 = before.Protect("top-secret");
        Assert.StartsWith("v2:k1:", protectedByK1);

        // Rotate: k2 becomes the active encryption key, k1 stays available for decryption.
        var after = new DefaultSecretValueProtector(BuildRing(activeKeyId: "k2",
            ("k1", "material-one"),
            ("k2", "material-two")));

        Assert.Equal("top-secret", after.Unprotect(protectedByK1));
        Assert.StartsWith("v2:k2:", after.Protect("new-secret"));
    }

    [Fact]
    public void Legacy_V1_Payload_Is_Readable_When_Encryption_Key_Remains_In_The_Ring()
    {
        var v1Payload = EncryptV1("hello", "legacy-secret");

        var options = new SecretsOptions
        {
            EncryptionKey = "legacy-secret",
            Keys = { new SecretEncryptionKeyOptions { KeyId = "k2", Key = "material-two" } },
            ActiveKeyId = "k2"
        };
        var protector = new DefaultSecretValueProtector(new DefaultSecretKeyRing(Microsoft.Extensions.Options.Options.Create(options)));

        Assert.Equal("hello", protector.Unprotect(v1Payload));
    }

    [Fact]
    public void Legacy_Encryption_Key_Is_The_Active_Key_When_No_Active_Key_Id_Is_Set()
    {
        var protector = new DefaultSecretValueProtector(new DefaultSecretKeyRing(
            Microsoft.Extensions.Options.Options.Create(new SecretsOptions { EncryptionKey = "only-key" })));

        Assert.StartsWith("v2:legacy:", protector.Protect("x"));
    }

    [Fact]
    public void Unknown_Key_Id_In_Payload_Fails_Loudly()
    {
        var protector = new DefaultSecretValueProtector(BuildRing(activeKeyId: "k1", ("k1", "material-one")));

        Assert.Throws<InvalidOperationException>(() => protector.Unprotect("v2:ghost:AAAA:BBBB:CCCC"));
    }

    [Theory]
    [InlineData("bad:id")]
    [InlineData("with space")]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_Key_Id_Is_Rejected_At_Build_Time(string keyId)
    {
        var options = new SecretsOptions { Keys = { new SecretEncryptionKeyOptions { KeyId = keyId, Key = "m" } } };

        Assert.Throws<InvalidOperationException>(() => new DefaultSecretKeyRing(Microsoft.Extensions.Options.Options.Create(options)));
    }

    [Fact]
    public void Duplicate_Key_Ids_Are_Rejected_At_Build_Time()
    {
        var options = new SecretsOptions
        {
            Keys =
            {
                new SecretEncryptionKeyOptions { KeyId = "dup", Key = "a" },
                new SecretEncryptionKeyOptions { KeyId = "dup", Key = "b" }
            }
        };

        Assert.Throws<InvalidOperationException>(() => new DefaultSecretKeyRing(Microsoft.Extensions.Options.Options.Create(options)));
    }

    [Fact]
    public void Active_Key_Id_Not_Present_In_Ring_Is_Rejected_At_Build_Time()
    {
        var options = new SecretsOptions
        {
            Keys = { new SecretEncryptionKeyOptions { KeyId = "k1", Key = "a" } },
            ActiveKeyId = "missing"
        };

        Assert.Throws<InvalidOperationException>(() => new DefaultSecretKeyRing(Microsoft.Extensions.Options.Options.Create(options)));
    }

    [Fact]
    public void Reserved_Legacy_Key_Id_Cannot_Be_Declared_Explicitly()
    {
        var options = new SecretsOptions
        {
            EncryptionKey = "legacy-secret",
            Keys = { new SecretEncryptionKeyOptions { KeyId = "legacy", Key = "a" } }
        };

        Assert.Throws<InvalidOperationException>(() => new DefaultSecretKeyRing(Microsoft.Extensions.Options.Options.Create(options)));
    }

    private static DefaultSecretKeyRing BuildRing(string activeKeyId, params (string KeyId, string Key)[] keys)
    {
        var options = new SecretsOptions { ActiveKeyId = activeKeyId };
        foreach (var (keyId, key) in keys)
            options.Keys.Add(new SecretEncryptionKeyOptions { KeyId = keyId, Key = key });

        return new DefaultSecretKeyRing(Microsoft.Extensions.Options.Options.Create(options));
    }

    private static string EncryptV1(string value, string encryptionKey)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(nonce);

        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return string.Join(':', "v1", Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
    }
}
