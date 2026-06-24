namespace Elsa.Secrets.Options;

public sealed class SecretsOptions
{
    public const string SectionName = "Elsa:Secrets";

    public string? EncryptionKey { get; set; }
}
