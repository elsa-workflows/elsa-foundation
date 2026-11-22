using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>
/// A host's Data Protection is composed from configuration (#2191): always under one application name, with the EF key store
/// only when configuration enables it, and a configuration the host cannot act on as written is refused, naming the key,
/// never quietly defaulted.
/// </summary>
public sealed class DataProtectionConfigurationTests : IAsyncLifetime
{
    private const string Ef = KeyRingStore.KeyStoreSection;
    private const string Certificate = $"{DataProtectionConfigurationExtensions.SectionName}:{DataProtectionConfigurationExtensions.CertificateSectionKey}";
    private readonly SqliteKeyRingDatabase _database = new();
    private readonly List<IAsyncDisposable> _hosts = [];
    private readonly List<IDisposable> _certificates = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
        foreach (var certificate in _certificates)
            certificate.Dispose();
        _database.Dispose();
    }

    /// <summary>
    /// Unconfigured, the keys stay where ASP.NET Core keeps them by default, but the application name is the default rather
    /// than derived from the content root, which differs between two hosts deployed to two directories.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Without_the_key_store_the_application_name_is_still_fixed(string? enabled)
    {
        var settings = new Dictionary<string, string?>();
        if (enabled is not null)
            settings[$"{Ef}:{DataProtectionConfigurationExtensions.EnabledKey}"] = enabled;

        var host = Built(settings);

        Assert.Equal(DataProtectionConfigurationExtensions.DefaultApplicationName, host.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.Null(host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        Assert.Null(host.Services.GetService<EfDataProtectionKeyRepository>());
    }

    /// <summary>A deployment that must not share keys with another names its own application; a blank name keeps the default.</summary>
    [Theory]
    [InlineData("Elsa.Staging", "Elsa.Staging")]
    [InlineData(" ", DataProtectionConfigurationExtensions.DefaultApplicationName)]
    public void The_application_name_is_read_from_configuration(string configured, string expected)
    {
        var host = Built(new() { [$"{DataProtectionConfigurationExtensions.SectionName}:{DataProtectionConfigurationExtensions.ApplicationNameKey}"] = configured });

        Assert.Equal(expected, host.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    /// <summary>The control: what ASP.NET Core names the application without the composition is the host's content root.</summary>
    [Fact]
    public void Without_the_composition_the_application_name_is_the_content_root()
    {
        var environment = Built(new()).Services.GetRequiredService<IHostEnvironment>();
        using var plain = new ServiceCollection().AddSingleton(environment).AddDataProtection().Services.BuildServiceProvider();

        var discriminator = plain.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;

        Assert.NotEqual(DataProtectionConfigurationExtensions.DefaultApplicationName, discriminator);
        Assert.Contains(Path.GetFileName(environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar)), discriminator, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabling_the_key_store_persists_the_key_ring_to_the_configured_database()
    {
        var store = await _database.CreateStoreAsync();

        var host = Built(store.Settings());

        Assert.IsType<EfDataProtectionKeyRepository>(host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        Assert.Same(host.Services.GetRequiredService<EfDataProtectionKeyRepository>(), host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
    }

    /// <summary>A host that meant to share its keys and silently kept them to itself is the failure that looks like success.</summary>
    [Fact]
    public void Key_store_settings_without_an_Enabled_switch_are_refused()
    {
        var failure = Assert.Throws<EfHostConfigurationException>(() => Built(new() { [$"{Ef}:Provider"] = "PostgreSql" }));

        Assert.Contains($"{Ef}:{DataProtectionConfigurationExtensions.EnabledKey}", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Enabled", "yes")]
    [InlineData("Pooling", "sometimes")]
    public void A_key_store_value_that_does_not_parse_is_refused_naming_its_key(string key, string value)
    {
        var failure = Assert.Throws<EfHostConfigurationException>(() => Built(new() { [$"{Ef}:Enabled"] = "true", [$"{Ef}:{key}"] = value }));

        Assert.Contains($"{Ef}:{key}", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_provider_is_refused()
    {
        var failure = Assert.Throws<ArgumentException>(() => Built(new() { [$"{Ef}:Enabled"] = "true", [$"{Ef}:Provider"] = "Oracle" }));

        Assert.Contains("'Oracle'", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Without a key store the certificate would protect nothing, and ASP.NET Core would refuse it at the first request.</summary>
    [Fact]
    public void A_certificate_without_a_key_store_is_refused()
    {
        var certificate = TestCertificate.Create();
        _certificates.Add(certificate);

        var failure = Assert.Throws<EfHostConfigurationException>(() => Built(certificate.Settings()));

        Assert.Contains($"{Ef}:{DataProtectionConfigurationExtensions.EnabledKey}", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_certificate_password_without_a_path_is_refused()
    {
        var settings = (await _database.CreateStoreAsync()).Settings();
        settings[$"{Certificate}:Password"] = TestCertificate.Password;

        var failure = Assert.Throws<EfHostConfigurationException>(() => Built(settings));

        Assert.Contains($"{Certificate}:Path", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "elsa-test-password")]
    [InlineData(true, "not-the-password")]
    public async Task A_certificate_that_cannot_be_loaded_is_refused_naming_its_key(bool exists, string password)
    {
        var certificate = TestCertificate.Create();
        _certificates.Add(certificate);
        var settings = (await _database.CreateStoreAsync()).Settings();
        foreach (var setting in certificate.Settings(exists ? certificate.Path : certificate.Path + ".missing", password))
            settings[setting.Key] = setting.Value;

        var failure = Assert.Throws<EfHostConfigurationException>(() => Built(settings));

        Assert.Contains($"{Certificate}:Path", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_certificate_without_its_private_key_is_refused()
    {
        var certificate = TestCertificate.CreateWithoutPrivateKey();
        _certificates.Add(certificate);
        var settings = With((await _database.CreateStoreAsync()).Settings(), certificate.Settings());

        var failure = Assert.Throws<EfHostConfigurationException>(() => Built(settings));

        Assert.Contains("private key", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A relative path is read from the host's content root, where its own settings are, not from the directory it was started
    /// in; from there this path names no file at all.
    /// </summary>
    [Fact]
    public async Task A_relative_certificate_path_is_read_from_the_content_root()
    {
        var certificate = TestCertificate.Create();
        _certificates.Add(certificate);
        Assert.False(File.Exists(certificate.PathFromSibling));

        var host = Built(With((await _database.CreateStoreAsync()).Settings(), certificate.Settings(certificate.PathFromSibling)));

        Assert.NotNull(host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor);
    }

    [Fact]
    public async Task The_key_store_is_composed_once_per_host()
    {
        var store = await _database.CreateStoreAsync();

        Assert.Throws<InvalidOperationException>(() => Built(store.Settings(), (services, configuration) => services.AddConfiguredDataProtection(configuration)));
    }

    private KeyRingHost Built(Dictionary<string, string?> settings, Action<IServiceCollection, IConfiguration>? compose = null)
    {
        var host = KeyRingHost.Build(settings, compose);
        _hosts.Add(host);
        return host;
    }

    private static Dictionary<string, string?> With(Dictionary<string, string?> settings, Dictionary<string, string?> more) =>
        settings.Concat(more).ToDictionary();
}
