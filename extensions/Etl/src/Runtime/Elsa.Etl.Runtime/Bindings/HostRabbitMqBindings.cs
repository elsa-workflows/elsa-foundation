using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Runtime.Bindings;

/// <summary>
/// Trusted host RabbitMQ bindings for RabbitMQ destinations (spec 1200) and sources (spec 1201). A pinned handle hashes the
/// target the publisher parses out of the AMQP URI and the allowed exchanges and queues, never the URI or its password.
/// </summary>
internal sealed class HostRabbitMqBindings(IEnumerable<EtlHostRabbitMqBindingOptions> options, IEnumerable<IEtlRabbitMqPublisher> publishers,
    IEnumerable<IEtlRabbitMqConsumer>? consumers = null)
{
    private readonly EtlHostRabbitMqBindingOptions[] _bindings = options.ToArray();
    private readonly IEtlRabbitMqPublisher? _publisher = publishers.ToArray() switch
    {
        [] => null,
        [var single] => single,
        _ => throw new InvalidOperationException("More than one ETL RabbitMQ publisher is registered.")
    };
    private readonly IEtlRabbitMqConsumer? _consumer = (consumers ?? []).ToArray() switch
    {
        [] => null,
        [var single] => single,
        _ => throw new InvalidOperationException("More than one ETL RabbitMQ consumer is registered.")
    };

    public bool ContainsBinding(EtlScope scope, string alias, string role) =>
        _bindings.Any(item => item.IsCurrent && Matches(item, scope, alias, role));

    public IReadOnlyList<EtlBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take) =>
        _bindings.Where(item => item.IsCurrent && Scope(item) == scope)
            .Select(item => new EtlBindingSummary(item.Alias, item.Role))
            .Where(item => afterAlias is null || StringComparer.Ordinal.Compare(item.Alias, afterAlias) > 0 ||
                StringComparer.Ordinal.Equals(item.Alias, afterAlias) && StringComparer.Ordinal.Compare(item.Role, afterRole) > 0)
            .OrderBy(item => item.Alias, StringComparer.Ordinal).ThenBy(item => item.Role, StringComparer.Ordinal)
            .Take(take).ToArray();

    /// <summary>
    /// Resolves the current revision for a new run and checks that it allows the entity: the exchange or, for role <c>Source</c>, the queue.
    /// </summary>
    public HostRabbitMqResolvedBinding ResolveCurrent(EtlScope scope, string alias, string role, string entity)
    {
        var current = _bindings.Where(item => item.IsCurrent && Matches(item, scope, alias, role)).ToArray();
        if (current.Length == 0) throw Unavailable();
        if (current.Length > 1) throw new EtlBindingException("etl.binding-ambiguous", "An ETL host RabbitMQ binding has ambiguous revisions.");
        return Resolve(current[0], entity);
    }

    /// <summary>Resolves the exact pinned revision for a running run and checks its handle and entity.</summary>
    public HostRabbitMqResolvedBinding ResolveExact(EtlScope scope, string alias, string role, string revision, string handle, string entity)
    {
        var pinned = _bindings.Where(item => Matches(item, scope, alias, role) && item.Revision == revision).ToArray();
        if (pinned.Length != 1) throw RevisionUnavailable();
        var resolved = Resolve(pinned[0], entity);
        return StringComparer.Ordinal.Equals(resolved.Handle, handle) ? resolved : throw RevisionUnavailable();
    }

    private HostRabbitMqResolvedBinding Resolve(EtlHostRabbitMqBindingOptions binding, string entity)
    {
        // The publisher parses every binding's target, and a source also needs the consumer; Elsa.Etl.RabbitMq registers both.
        var source = binding.Role == "Source";
        if (_publisher is null || source && _consumer is null)
            throw new EtlBindingException("etl.rabbitmq-unavailable", "No ETL RabbitMQ publisher or consumer is installed.");
        if (source && !binding.AllowedQueues.Contains(entity, StringComparer.Ordinal))
            throw new EtlBindingException("etl.rabbitmq-queue-not-allowed", "The RabbitMQ binding does not allow the queue.");
        if (!source && !binding.AllowedExchanges.Contains(entity, StringComparer.Ordinal))
            throw new EtlBindingException("etl.rabbitmq-exchange-not-allowed", "The RabbitMQ binding does not allow the exchange.");
        if (string.IsNullOrWhiteSpace(binding.ConnectionString))
            throw Unavailable();
        EtlRabbitMqTarget target;
        try
        {
            target = _publisher.DescribeTarget(binding.ConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or UriFormatException)
        {
            // The cause is not attached: a URI parser's message can echo the AMQP URI, password included.
            throw new EtlBindingException("etl.binding-unavailable", "The RabbitMQ binding's AMQP URI is invalid.");
        }
        var exchanges = binding.AllowedExchanges.Order(StringComparer.Ordinal).ToArray();
        // A source's handle also covers its allowed queues; a destination's keeps spec 1200's shape, so its pinned runs resolve.
        object identity = source
            ? new
            {
                FormatVersion = 1, ScopeIdentity = EtlRuntimeIdentity.ScopeKey(Scope(binding)), binding.Alias, binding.Role,
                binding.Revision, target.Host, target.Port, target.VirtualHost, target.User, AllowedExchanges = exchanges,
                AllowedQueues = binding.AllowedQueues.Order(StringComparer.Ordinal).ToArray()
            }
            : new
            {
                FormatVersion = 1, ScopeIdentity = EtlRuntimeIdentity.ScopeKey(Scope(binding)), binding.Alias, binding.Role,
                binding.Revision, target.Host, target.Port, target.VirtualHost, target.User, AllowedExchanges = exchanges
            };
        var handle = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
        return new(binding.Alias, binding.Revision, handle, _publisher, binding.ConnectionString) { Consumer = _consumer };
    }

    private static bool Matches(EtlHostRabbitMqBindingOptions item, EtlScope scope, string alias, string role) =>
        Scope(item) == scope && item.Alias == alias && item.Role == role;

    private static EtlScope Scope(EtlHostRabbitMqBindingOptions item) => new(item.TenantId, item.PersistencePartition);

    private static EtlBindingException Unavailable() =>
        new("etl.binding-unavailable", "The scoped ETL binding is unavailable for this connector role.");

    private static EtlBindingException RevisionUnavailable() =>
        new("etl.binding-revision-unavailable", "The pinned ETL RabbitMQ binding revision is unavailable.");
}

/// <summary>A resolved RabbitMQ binding; its AMQP URI never leaves the runtime.</summary>
internal sealed record HostRabbitMqResolvedBinding(string Alias, string Revision, string Handle, IEtlRabbitMqPublisher Publisher,
    string ConnectionString)
{
    /// <summary>The consumer a source binding reads with; null when none is installed.</summary>
    public IEtlRabbitMqConsumer? Consumer { get; init; }

    public override string ToString() => nameof(HostRabbitMqResolvedBinding);
}
