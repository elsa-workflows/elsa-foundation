using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Elsa.Etl.RabbitMq.ProviderTests;

/// <summary>
/// A real RabbitMQ broker for the ETL RabbitMQ delivery suite (spec 1200). A supplied AMQP URI wins; otherwise a container
/// starts. Without Docker the suite skips, unless <see cref="RequireVariable"/> makes it fail, as CI does.
/// </summary>
public sealed class EtlRabbitMqBrokerFixture : IAsyncLifetime
{
    internal const string RequireVariable = "ELSA_ETL_RABBITMQ_REQUIRE_NATIVE_PROVIDERS";
    private const string UriVariable = "ELSA_ETL_RABBITMQ_URI";
    private RabbitMqContainer? _container;

    internal string ConnectionString { get; private set; } = string.Empty;
    internal string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(UriVariable) is { Length: > 0 } supplied)
        {
            ConnectionString = supplied;
            return;
        }
        try
        {
            // A per-run password for a throwaway container, so no credential is committed.
            _container = new RabbitMqBuilder().WithImage("rabbitmq:4.1-alpine").WithUsername("etl")
                .WithPassword(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))).Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception exception) when (exception is DockerUnavailableException or HttpRequestException or TimeoutException ||
                                          exception.GetType().Name.Contains("Docker", StringComparison.Ordinal))
        {
            if (Environment.GetEnvironmentVariable(RequireVariable) is "1" or "true")
                throw;
            SkipReason = "Docker is unavailable for the ETL RabbitMQ provider suite.";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    /// <summary>Opens an administration connection outside the publisher.</summary>
    internal async Task<IConnection> ConnectAsync() =>
        await new ConnectionFactory { Uri = new Uri(ConnectionString) }.CreateConnectionAsync();

    /// <summary>The broker's URI with another password, which the broker refuses.</summary>
    internal string WithWrongPassword()
    {
        var uri = new UriBuilder(ConnectionString) { Password = "wrong" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)) };
        return uri.Uri.ToString();
    }
}

[CollectionDefinition(Name)]
public sealed class EtlRabbitMqBrokerCollection : ICollectionFixture<EtlRabbitMqBrokerFixture>
{
    public const string Name = "etl-rabbitmq-broker";
}
