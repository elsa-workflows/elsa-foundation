using System.ComponentModel;
using System.Net.Sockets;
using System.Security.Cryptography;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Elsa.Secrets.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Tests;

/// <summary>
/// When reading the secret's payload fails, the resolver reports a result rather than a throw, with none of the
/// exception's text, classified as a failed metadata read is (spec 188, FR-003, User Story 2): an outage of the payload
/// store is <see cref="SecretResolutionFailureCode.StoreUnavailable"/>, transient, and a store the host does not
/// register or a payload that does not decrypt or parse is <see cref="SecretResolutionFailureCode.CorruptState"/>,
/// permanent. A canceled resolution stays a cancellation.
/// </summary>
public sealed class SecretValueResolverPayloadFailureTests : IAsyncDisposable
{
    private const string TenantId = "tenant-1";
    private const string SecretName = "payments.api";
    private const string StoreDetail = "payload-detail-sentinel read from secrets-store.internal failed";
    private const string UnknownKeyId = "retired-key-sentinel";
    private readonly FailingSecretStore _store = new();
    private readonly ServiceProvider _provider;
    private readonly ISecretValueResolver _resolver;

    public SecretValueResolverPayloadFailureTests()
    {
        var services = new ServiceCollection().AddSecrets();
        services.Configure<SecretsOptions>(options => options.EncryptionKey = "payload-failure-test-encryption-key");
        services.AddSingleton<ISecretStore>(_store);
        _provider = services.BuildServiceProvider();
        _resolver = _provider.GetRequiredService<ISecretValueResolver>();
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>
    /// What a store can throw: the built-in stores throw nothing of their own beyond the value protector's failures,
    /// which the encrypted rows below raise for real; a replacement store can throw anything.
    /// </summary>
    public static TheoryData<Exception, SecretResolutionFailureCode> StoreFailures => new()
    {
        // Outages: the store could not be reached.
        { new IOException(StoreDetail), SecretResolutionFailureCode.StoreUnavailable },
        { new TimeoutException(StoreDetail), SecretResolutionFailureCode.StoreUnavailable },
        { new SocketException((int)SocketError.ConnectionRefused), SecretResolutionFailureCode.StoreUnavailable },
        { new HttpRequestException(StoreDetail, new SocketException((int)SocketError.HostUnreachable)), SecretResolutionFailureCode.StoreUnavailable },
        // The store's own timeout, not the caller's cancellation.
        { new OperationCanceledException(StoreDetail), SecretResolutionFailureCode.StoreUnavailable },
        { new ProviderException(StoreDetail, isTransient: true), SecretResolutionFailureCode.StoreUnavailable },
        { new ProviderException(StoreDetail, innerException: new Win32Exception(258)), SecretResolutionFailureCode.StoreUnavailable },

        // The store was read, and what it holds cannot be served.
        { new ProviderException(StoreDetail, sqlState: "42P01"), SecretResolutionFailureCode.CorruptState },
        { new InvalidOperationException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new FormatException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new CryptographicException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new ArgumentException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new NotSupportedException(StoreDetail), SecretResolutionFailureCode.CorruptState }
    };

    /// <summary>
    /// Protected values the encrypted store's value protector refuses, each with the exception it raises.
    /// </summary>
    public static TheoryData<string> DamagedProtectedValues => new()
    {
        "unsupported format",   // InvalidOperationException
        "unknown key",          // InvalidOperationException naming the key
        "not base64",           // FormatException
        "tampered ciphertext",  // AuthenticationTagMismatchException
        "short nonce"           // ArgumentException
    };

    [Theory]
    [MemberData(nameof(StoreFailures))]
    public async Task A_payload_store_failure_is_classified_without_its_text(Exception failure, SecretResolutionFailureCode code)
    {
        _store.Failure = failure;

        var resolved = await ResolveAsync(FailingSecretStore.Name);

        AssertFailed(resolved, code);
        Assert.Equal(1, _store.Reads);
    }

    [Theory]
    [MemberData(nameof(DamagedProtectedValues))]
    public async Task A_payload_that_does_not_decrypt_is_corrupt_without_its_text(string damage)
    {
        var protectedValue = _provider.GetRequiredService<ISecretValueProtector>().Protect("value");
        var parts = protectedValue.Split(':');
        var damaged = damage switch
        {
            "unsupported format" => StoreDetail,
            "unknown key" => string.Join(':', parts[0], UnknownKeyId, parts[2], parts[3], parts[4]),
            "not base64" => string.Join(':', parts[0], parts[1], StoreDetail, parts[3], parts[4]),
            "tampered ciphertext" => string.Join(':', parts[0], parts[1], parts[2], parts[3], Convert.ToBase64String(new byte[5])),
            "short nonce" => string.Join(':', parts[0], parts[1], Convert.ToBase64String(new byte[4]), parts[3], parts[4]),
            _ => throw new ArgumentOutOfRangeException(nameof(damage), damage, null)
        };

        var resolved = await ResolveAsync(SecretStoreNames.Encrypted, damaged);

        AssertFailed(resolved, SecretResolutionFailureCode.CorruptState);
        Assert.DoesNotContain(UnknownKeyId, resolved.Error ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_store_the_host_does_not_register_is_corrupt()
    {
        var resolved = await ResolveAsync("unregistered-store");

        AssertFailed(resolved, SecretResolutionFailureCode.CorruptState);
    }

    /// <summary>
    /// The built-in stores return no payload for a version that lacks the entry they read; no retry supplies it, so it is
    /// permanent, not an outage.
    /// </summary>
    [Theory]
    [InlineData(SecretStoreNames.Encrypted)]
    [InlineData(SecretStoreNames.Configuration)]
    public async Task A_store_that_returns_no_payload_is_corrupt_not_an_outage(string storeName)
    {
        var resolved = await ResolveAsync(storeName);

        AssertFailed(resolved, SecretResolutionFailureCode.CorruptState);
    }

    [Fact]
    public async Task A_payload_without_a_value_is_corrupt_not_an_outage()
    {
        _store.Payload = new SecretPayload { Value = null };

        var resolved = await ResolveAsync(FailingSecretStore.Name);

        Assert.False(resolved.Succeeded);
        Assert.Null(resolved.Value);
        Assert.Equal(SecretResolutionFailureCode.CorruptState, resolved.FailureCode);
        Assert.Equal("Secret store returned an empty value.", resolved.Error);
        Assert.Equal(1, _store.Reads);
    }

    [Fact]
    public async Task A_canceled_resolution_propagates_as_a_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _store.OnRead = cancellation.Cancel;
        _store.Failure = new IOException(StoreDetail);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ResolveAsync(FailingSecretStore.Name, cancellationToken: cancellation.Token));
        Assert.Equal(1, _store.Reads);
    }

    /// <summary>Seeds an active secret in <paramref name="storeName"/>, then resolves it.</summary>
    private async Task<ResolvedSecret> ResolveAsync(string storeName, string? protectedValue = null, CancellationToken cancellationToken = default)
    {
        var payload = new SecretPayload();
        if (protectedValue is not null)
            payload.Metadata["protectedValue"] = protectedValue;

        await _provider.GetRequiredService<ISecretRepository>().TryAddAsync(new Secret
        {
            Id = $"{TenantId}:{SecretName}",
            TenantId = TenantId,
            Name = SecretName,
            DisplayName = SecretName,
            StoreName = storeName,
            Versions = [new SecretVersion { Version = 1, Payload = payload }]
        }, CancellationToken.None);

        return await _resolver.ResolveAsync(TenantId, new SecretReference(SecretName), cancellationToken);
    }

    private static void AssertFailed(ResolvedSecret resolved, SecretResolutionFailureCode code)
    {
        Assert.False(resolved.Succeeded);
        Assert.Null(resolved.Value);
        Assert.Equal(code, resolved.FailureCode);
        Assert.Equal(code == SecretResolutionFailureCode.StoreUnavailable ? "Secret payload could not be read." : "Secret payload is unusable.", resolved.Error);
    }

    /// <summary>A secret store whose reads throw <see cref="Failure"/> (or return <see cref="Payload"/>), after running <see cref="OnRead"/>.</summary>
    private sealed class FailingSecretStore : ISecretStore
    {
        public const string Name = "failing";

        public Exception Failure { get; set; } = new InvalidOperationException(StoreDetail);

        public Action? OnRead { get; set; }

        /// <summary>When set, returned by reads instead of throwing <see cref="Failure"/>.</summary>
        public SecretPayload? Payload { get; set; }

        public int Reads { get; private set; }

        public SecretStoreDescriptor Descriptor { get; } = new(Name, "Failing", "A store whose reads fail.", SecretStoreCapabilities.Read, IsReadOnly: true);

        public ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default)
        {
            Reads++;
            OnRead?.Invoke();
            return Payload is null ? throw Failure : ValueTask.FromResult<SecretPayload?>(Payload);
        }

        public ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
