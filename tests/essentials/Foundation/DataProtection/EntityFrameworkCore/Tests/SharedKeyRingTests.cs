using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>
/// What #2191 is for: a payload one host protects, such as a sign-in cookie or an antiforgery token, is unprotected by another
/// host and by the same host recreated, because both read one key ring from the platform database under one application name.
/// Each check has its control beside it, since sharing keys looks the same as sharing anything else: the hosts of a test run on
/// one machine as one user, where ASP.NET Core's default key directory is shared too, so a host on another database must fail.
/// </summary>
public abstract class SharedKeyRingTests(IKeyRingDatabase database) : IAsyncLifetime
{
    private readonly List<IAsyncDisposable> _hosts = [];
    private readonly List<IDisposable> _certificates = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
        foreach (var certificate in _certificates)
            certificate.Dispose();
    }

    [SkippableFact]
    public async Task A_payload_one_host_protects_is_unprotected_by_another_host_on_the_same_key_store()
    {
        var store = await StoreAsync();
        var a = await HostAsync(store.Settings());
        var b = await HostAsync(store.Settings());

        Assert.Equal("issued by a", b.Unprotect(a.Protect("issued by a")));
        Assert.Equal("issued by b", a.Unprotect(b.Protect("issued by b")));
        // One key, written by the first host as it started and read by the second, stamped with the family's version.
        var key = Assert.Single(await a.StoredKeysAsync());
        Assert.Equal(DataProtectionKeysEfModule.SchemaVersion, key.SchemaVersion);
        Assert.StartsWith("<key ", key.Xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hosts started together on an empty store, as the replicas of a first deployment are, may each create a key before
    /// either reads the other's, and each then protects with its own. A payload protected with a key a host has not read yet
    /// is still read by it, because it reads the store again for a key it does not know. Starting hosts together only makes
    /// that race likely, so it is made certain here: the second host creates a key just after the first has loaded its key
    /// ring, as a replica that started a moment later does.
    /// </summary>
    [SkippableFact]
    public async Task A_payload_protected_with_a_key_another_host_has_not_read_yet_is_read_by_it()
    {
        var store = await StoreAsync();
        var a = await HostAsync(store.Settings());
        var b = await HostAsync(store.Settings());
        var now = DateTimeOffset.UtcNow;
        b.Services.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddDays(90));

        var payload = b.Protect("issued with a new key");

        Assert.Equal(2, (await a.StoredKeysAsync()).Count);
        Assert.Equal("issued with a new key", a.Unprotect(payload));
    }

    [SkippableFact]
    public async Task A_host_on_another_key_store_cannot_unprotect_the_payload()
    {
        var a = await HostAsync((await StoreAsync()).Settings());
        var elsewhere = await HostAsync((await StoreAsync()).Settings());

        var payload = a.Protect("issued by a");

        Assert.ThrowsAny<CryptographicException>(() => elsewhere.Unprotect(payload));
    }

    /// <summary>A container recreated on the same database keeps every session it issued.</summary>
    [SkippableFact]
    public async Task A_payload_outlives_the_host_that_protected_it()
    {
        var store = await StoreAsync();
        string payload;
        await using (var first = await KeyRingHost.StartAsync(store.Settings()))
            payload = first.Protect("before the recreate");

        var recreated = await HostAsync(store.Settings());

        Assert.Equal("before the recreate", recreated.Unprotect(payload));
    }

    /// <summary>
    /// The key ring loads as the host starts, after the host's migrator has created the table, so a host's first start neither
    /// logs the key ring as failed to load nor leaves the store empty until the first request. The key ring's errors are Data
    /// Protection's and the store's queries'; EF Core's command log also carries the migrator's first look for a history table
    /// that does not exist yet on some engines, which is not one.
    /// </summary>
    [SkippableFact]
    public async Task The_key_ring_loads_from_the_store_as_a_host_first_starts()
    {
        var host = await HostAsync((await StoreAsync()).Settings());

        var errors = host.Log.Entries
            .Where(entry => entry.Level == LogLevel.Error && (entry.Category.StartsWith("Microsoft.AspNetCore.DataProtection", StringComparison.Ordinal) ||
                                                              entry.Category == "Microsoft.EntityFrameworkCore.Query"))
            .Select(entry => $"{entry.Category}: {entry.Message}")
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        Assert.Single(await host.StoredKeysAsync());
    }

    [SkippableFact]
    public async Task Keys_are_encrypted_at_rest_with_the_certificate_every_host_is_configured_with()
    {
        var store = await StoreAsync();
        var certificate = Certificate();
        var a = await HostAsync(With(store.Settings(), certificate.Settings()));
        var b = await HostAsync(With(store.Settings(), certificate.Settings()));

        Assert.Equal("issued by a", b.Unprotect(a.Protect("issued by a")));
        var key = Assert.Single(await a.StoredKeysAsync()).Xml;
        Assert.Contains("<encryptedSecret", key, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", key, StringComparison.Ordinal);
    }

    /// <summary>The control: without the certificate, the store's keys are unreadable, so the encryption above is real.</summary>
    [SkippableFact]
    public async Task A_host_without_the_certificate_cannot_unprotect_what_the_encrypted_keys_protected()
    {
        var store = await StoreAsync();
        var certificate = Certificate();
        var a = await HostAsync(With(store.Settings(), certificate.Settings()));
        var payload = a.Protect("issued by a");

        var withoutCertificate = await HostAsync(store.Settings());

        Assert.ThrowsAny<CryptographicException>(() => withoutCertificate.Unprotect(payload));
    }

    private async Task<KeyRingStore> StoreAsync()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        return await database.CreateStoreAsync();
    }

    private async Task<KeyRingHost> HostAsync(IReadOnlyDictionary<string, string?> settings)
    {
        var host = await KeyRingHost.StartAsync(settings);
        _hosts.Add(host);
        return host;
    }

    private TestCertificate Certificate()
    {
        var certificate = TestCertificate.Create();
        _certificates.Add(certificate);
        return certificate;
    }

    private static Dictionary<string, string?> With(Dictionary<string, string?> settings, Dictionary<string, string?> more) =>
        settings.Concat(more).ToDictionary();
}
