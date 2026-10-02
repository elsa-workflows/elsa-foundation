using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "Secrets",
    DisplayName = "Secrets",
    Description = "Provides secret metadata, encrypted and configuration-backed secret stores, and runtime secret expression resolution."
)]
public class SecretsFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Encryption key",
        Description = "Single encryption key, held in the key ring under the reserved id 'legacy'. It is the active key unless Active key id names another. Supply it through a secret outside development.",
        Category = "Security",
        Secret = true)]
    public string? EncryptionKey { get; set; }

    [ManifestSetting(
        DisplayName = "Active key id",
        Description = "Id of the key new values are encrypted with: a key in Keys, or 'legacy' for Encryption key. Required when Keys holds more than one key and Encryption key is not set.",
        Category = "Security")]
    public string? ActiveKeyId { get; set; }

    [ManifestSetting(
        DisplayName = "Keys",
        Description = "Rotation key ring, key id to key material. Every key can decrypt; only the active key encrypts. Keep a key for as long as any value it encrypted remains stored.",
        Category = "Security",
        Secret = true)]
    public IDictionary<string, string>? Keys { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSecrets();

        // Only settings that are set are applied, so a host that configures SecretsOptions itself keeps its keys when
        // the shell configures none. A key id declared by both is refused by the key ring as a duplicate.
        services.Configure<SecretsOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(EncryptionKey))
                options.EncryptionKey = EncryptionKey;

            if (!string.IsNullOrWhiteSpace(ActiveKeyId))
                options.ActiveKeyId = ActiveKeyId;

            if (Keys is null) return;

            foreach (var (keyId, key) in Keys)
                options.Keys.Add(new SecretEncryptionKeyOptions { KeyId = keyId, Key = key });
        });
    }
}
