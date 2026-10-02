using System.Data.Common;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    /// Hosts started together on an empty store, as the replicas of a first deployment are, race twice, and both races are run
    /// every time here rather than left to timing. Each host's migrator sends its first command to the store, its first look
    /// at or claim on the migration history, only once the other's is ready to go too: neither holds EF Core's migration lock
    /// yet, so both start migrating the empty store at once, and the lock is what orders their changes from there. Each host's
    /// first read of the key ring is held until the other has read it too, so both find it empty and both create the first
    /// key. Each host then protects with a key the other may not have read yet, and each still reads the other's payloads.
    /// </summary>
    [SkippableFact]
    public async Task Hosts_that_start_together_on_an_empty_store_read_each_others_payloads()
    {
        var store = await StoreAsync();
        using var migrations = new Rendezvous(2);
        using var firstReads = new Rendezvous(2);

        // Each host starts on a thread of its own: each waits for the other twice, so one thread cannot start both.
        var hosts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => HostAsync(store.Settings(), (services, _) =>
        {
            FirstCommandTogether.AddTo(services, migrations);
            services.PostConfigure<KeyManagementOptions>(keys => keys.XmlRepository = new FirstReadTogether(keys.XmlRepository!, firstReads));
        }))));

        Assert.Equal(2, migrations.Arrivals);
        Assert.Equal(2, firstReads.Arrivals);
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

    /// <summary>A point every host of a test waits at until all of them have arrived, counting who did.</summary>
    private sealed class Rendezvous(int hosts) : IDisposable
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
        private readonly Barrier _barrier = new(hosts);
        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public void Arrive(string what)
        {
            Interlocked.Increment(ref _arrivals);
            if (!_barrier.SignalAndWait(Patience))
                throw new TimeoutException($"The other hosts did not reach {what} within {Patience}.");
        }

        public void Dispose() => _barrier.Dispose();
    }

    /// <summary>
    /// The host's key store, with its first read held until every host has read the store once, so each finds it as it was
    /// before any of them wrote a key.
    /// </summary>
    private sealed class FirstReadTogether(IXmlRepository store, Rendezvous rendezvous) : IXmlRepository
    {
        private int _reads;

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            var elements = store.GetAllElements();
            if (Interlocked.Increment(ref _reads) == 1)
                rendezvous.Arrive("their first read of the key store");
            return elements;
        }

        public void StoreElement(XElement element, string friendlyName) => store.StoreElement(element, friendlyName);
    }

    /// <summary>
    /// Holds the first command a host's key store context sends until every host's is ready to go too. The migrator is the
    /// first to use the context, so this is each host's migration first reaching the store: on SQLite its claim on EF Core's
    /// migration lock, on the other engines its look at, or creation of, the history table. Held before it runs, it leaves no
    /// host holding the lock or having created anything, so from here the hosts migrate the store at once.
    /// </summary>
    private sealed class FirstCommandTogether(Rendezvous rendezvous) : DbCommandInterceptor
    {
        private int _commands;

        /// <summary>Adds a fresh interceptor of this host to its key store context, whichever engine the context binds.</summary>
        public static void AddTo(IServiceCollection services, Rendezvous rendezvous)
        {
            var interceptor = new FirstCommandTogether(rendezvous);
            services.ConfigureDbContext<DataProtectionKeysSqliteDbContext>(options => options.AddInterceptors(interceptor));
            services.ConfigureDbContext<DataProtectionKeysSqlServerDbContext>(options => options.AddInterceptors(interceptor));
            services.ConfigureDbContext<DataProtectionKeysPostgreSqlDbContext>(options => options.AddInterceptors(interceptor));
            services.ConfigureDbContext<DataProtectionKeysMySqlDbContext>(options => options.AddInterceptors(interceptor));
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) =>
            Hold(result);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Hold(result));

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) =>
            Hold(result);

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Hold(result));

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
            Hold(result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Hold(result));

        private T Hold<T>(T result)
        {
            if (Interlocked.Increment(ref _commands) == 1)
                rendezvous.Arrive("their migration's first command to the key store");
            return result;
        }
    }
}
