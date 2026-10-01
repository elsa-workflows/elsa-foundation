using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
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
    /// Hosts started together on an empty store, as the replicas of a first deployment are, both migrate it and both create the
    /// first key, each before it can read the other's: every host's first read of the store is held until every other host has
    /// read it too, so the race the replicas may run is the race this runs every time. Each host then protects with a key the
    /// other may not have read yet, and each still reads the other's payloads.
    /// </summary>
    [SkippableFact]
    public async Task Hosts_that_start_together_on_an_empty_store_read_each_others_payloads()
    {
        var store = await StoreAsync();
        using var firstReads = new Barrier(2);

        // Each host starts on a thread of its own: its first read waits for the other's, so one thread cannot start both.
        var hosts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => HostAsync(store.Settings(), (services, _) =>
            services.PostConfigure<KeyManagementOptions>(keys => keys.XmlRepository = new FirstReadTogether(keys.XmlRepository!, firstReads))))));

        Assert.Equal(2, (await hosts[0].StoredKeysAsync()).Count);
        Assert.Equal("issued by the first", hosts[1].Unprotect(hosts[0].Protect("issued by the first")));
        Assert.Equal("issued by the second", hosts[0].Unprotect(hosts[1].Protect("issued by the second")));
    }

    /// <summary>
    /// A key another host created after this one loaded its key ring, as a host that rotated its key or a replica that started
    /// a moment later did, is read from the store when a payload protected with it first arrives.
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

    /// <summary>
    /// The application name is the boundary between deployments that share a key store: the store records no application, so
    /// two deployments on one database read each other's keys, and only distinct names keep their payloads apart.
    /// </summary>
    [SkippableFact]
    public async Task Deployments_with_distinct_application_names_on_one_key_store_cannot_unprotect_each_others_payloads()
    {
        var store = await StoreAsync();
        var production = await HostAsync(With(store.Settings(), ApplicationName("Elsa.Production")));
        var staging = await HostAsync(With(store.Settings(), ApplicationName("Elsa.Staging")));

        Assert.ThrowsAny<CryptographicException>(() => staging.Unprotect(production.Protect("issued in production")));
        Assert.ThrowsAny<CryptographicException>(() => production.Unprotect(staging.Protect("issued in staging")));
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

    private async Task<KeyRingHost> HostAsync(IReadOnlyDictionary<string, string?> settings, Action<IServiceCollection, IConfiguration>? compose = null)
    {
        var host = await KeyRingHost.StartAsync(settings, compose);
        lock (_hosts)
            _hosts.Add(host);
        return host;
    }

    private static Dictionary<string, string?> ApplicationName(string name) =>
        new() { [$"{DataProtectionConfigurationExtensions.SectionName}:{DataProtectionConfigurationExtensions.ApplicationNameKey}"] = name };

    private TestCertificate Certificate()
    {
        var certificate = TestCertificate.Create();
        _certificates.Add(certificate);
        return certificate;
    }

    private static Dictionary<string, string?> With(Dictionary<string, string?> settings, Dictionary<string, string?> more) =>
        settings.Concat(more).ToDictionary();

    /// <summary>
    /// The host's key store, with its first read held until every host sharing <paramref name="barrier"/> has read the store
    /// once, so each finds it as it was before any of them wrote a key.
    /// </summary>
    private sealed class FirstReadTogether(IXmlRepository store, Barrier barrier) : IXmlRepository
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
        private int _reads;

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            var elements = store.GetAllElements();
            if (Interlocked.Increment(ref _reads) == 1 && !barrier.SignalAndWait(Patience))
                throw new TimeoutException($"The other hosts did not read the key store within {Patience}.");
            return elements;
        }

        public void StoreElement(XElement element, string friendlyName) => store.StoreElement(element, friendlyName);
    }
}
