using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DistributedCommandSendLeaseAckWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_frozen_command_transport_contract()
    {
        var adapter = new TransportAdapter();
        var result = await new DistributedCommandSendLeaseAckWorkload().ExecuteAsync(adapter);
        Assert.Equal(DistributedCommandSendLeaseAckWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(DistributedCommandSendLeaseAckWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(3, adapter.Opened.Count);
        Assert.Equal(8176, adapter.Shared.Items.Count);
        Assert.Equal([8, 8, 8, 8], adapter.Shared.LeaseTakes);
        Assert.Equal(32, adapter.Shared.StaleAcknowledgementsWithCurrentSuccessor);
        Assert.Equal(16, adapter.Shared.TokenOnlyStaleAcknowledgements);
        Assert.Equal(16, adapter.Shared.CurrentAcknowledgements);
    }

    [Theory]
    [InlineData(TransportFault.Alias)]
    [InlineData(TransportFault.Split)]
    [InlineData(TransportFault.FreshReopen)]
    [InlineData(TransportFault.ResponseOnlySend)]
    [InlineData(TransportFault.CrossCallerGrant)]
    [InlineData(TransportFault.DuplicateLease)]
    [InlineData(TransportFault.PartialLease)]
    [InlineData(TransportFault.CorruptLeaseItem)]
    [InlineData(TransportFault.CorruptLeaseToken)]
    [InlineData(TransportFault.ReverseLeaseOrder)]
    [InlineData(TransportFault.IgnoreLeaseToken)]
    [InlineData(TransportFault.AcceptStale)]
    [InlineData(TransportFault.RejectCurrent)]
    [InlineData(TransportFault.FabricateReopenList)]
    [InlineData(TransportFault.FabricateReopenCount)]
    public async Task Fails_closed_when_public_transport_drifts(TransportFault fault) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DistributedCommandSendLeaseAckWorkload().ExecuteAsync(new TransportAdapter(fault)).AsTask());

    [Fact]
    public void Public_adapter_surface_is_provider_neutral()
    {
        var roots = new[] { typeof(ICommandTransportWorkloadAdapter), typeof(CommandTransportClients), typeof(IExecutionCommandTransport) };
        var allowed = new HashSet<Type>
        {
            typeof(void), typeof(bool), typeof(int), typeof(long), typeof(string), typeof(object),
            typeof(DateTimeOffset), typeof(TimeSpan), typeof(CancellationToken), typeof(ValueTask<>),
            typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IEquatable<>),
            typeof(ICommandTransportWorkloadAdapter), typeof(CommandTransportClients),
            typeof(IExecutionCommandTransport), typeof(ExecutionCommandTransportItem),
            typeof(WorkflowExecutionCommandEnvelope)
        };
        var surface = roots.SelectMany(ExposedSignatureTypes).SelectMany(ExpandType).Distinct().ToArray();

        Assert.DoesNotContain(surface, type => !allowed.Contains(type));
    }

    private static IEnumerable<Type> ExposedSignatureTypes(Type type) =>
        type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
            .Concat(type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(property => property.PropertyType))
            .Concat(type.GetInterfaces())
            .Concat(type.BaseType is null ? [] : [type.BaseType]);

    private static IEnumerable<Type> ExpandType(Type type)
    {
        if (type.HasElementType)
            return ExpandType(type.GetElementType()!);
        if (!type.IsGenericType)
            return [type];
        return new[] { type.GetGenericTypeDefinition() }.Concat(type.GetGenericArguments().SelectMany(ExpandType));
    }

    private sealed class TransportAdapter(TransportFault fault = TransportFault.None) : ICommandTransportWorkloadAdapter
    {
        private readonly Backing _other = fault == TransportFault.Split ? new() : null!;
        public Backing Shared { get; } = new(); public List<Transport> Opened { get; } = [];
        public ValueTask<CommandTransportClients> OpenIndependentClientsAsync(CancellationToken ct = default)
        { var first = Open(Shared); var second = fault == TransportFault.Alias ? first : Open(fault == TransportFault.Split ? _other : Shared); return new(new CommandTransportClients(first, second)); }
        public ValueTask<IExecutionCommandTransport> ReopenClientAsync(CancellationToken ct = default) => new(Open(fault == TransportFault.FreshReopen ? new() : Shared));
        private Transport Open(Backing backing) { var client = new Transport(backing, fault); Opened.Add(client); return client; }
    }

    private sealed class Backing
    {
        public object Gate { get; } = new();
        public List<ExecutionCommandTransportItem> Items { get; } = [];
        public Dictionary<string, long> Sequences { get; } = [];
        public List<int> LeaseTakes { get; } = [];
        public int StaleAcknowledgementsWithCurrentSuccessor { get; set; }
        public int TokenOnlyStaleAcknowledgements { get; set; }
        public int CurrentAcknowledgements { get; set; }
    }

    private sealed class Transport(Backing backing, TransportFault fault) : IExecutionCommandTransport
    {
        public ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken ct = default)
        {
            lock (backing.Gate)
            {
                var seq = backing.Sequences.GetValueOrDefault(workflowId) + 1;
                backing.Sequences[workflowId] = seq;
                var item = new ExecutionCommandTransportItem($"transport:{workflowId}:{seq}", workflowId, envelope, seq, now);
                if (fault != TransportFault.ResponseOnlySend || workflowId != "command-workflow-0127" || envelope.EnvelopeId != "command-0063")
                    backing.Items.Add(item);
                return new(item);
            }
        }

        public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowId, string owner, DateTimeOffset now, TimeSpan duration, int take, CancellationToken ct = default)
        {
            lock (backing.Gate)
            {
                backing.LeaseTakes.Add(take);
                var selected = backing.Items
                    .Where(x => x.WorkflowExecutionId == workflowId && x.IsVisible(now))
                    .OrderBy(x => x.Sequence)
                    .Take(fault == TransportFault.PartialLease ? Math.Max(0, take - 1) : take)
                    .ToArray();
                var leased = selected
                    .Select(x => x.Lease(fault == TransportFault.CrossCallerGrant ? "wrong-owner" : owner, now + duration))
                    .ToArray();
                foreach (var item in leased)
                {
                    var index = backing.Items.FindIndex(x => x.TransportItemId == item.TransportItemId);
                    backing.Items[index] = item;
                }
                if (fault == TransportFault.DuplicateLease && leased.Length > 0)
                    return new((IReadOnlyList<ExecutionCommandTransportItem>)leased.Append(leased[0]).ToArray());
                if (fault == TransportFault.CorruptLeaseItem && leased.Length > 0)
                {
                    var item = leased[0];
                    var envelope = item.Envelope;
                    var corruptedCommand = envelope.Command with { CommandId = $"corrupted-{envelope.Command.CommandId}" };
                    var corruptedEnvelope = new WorkflowExecutionCommandEnvelope(
                        envelope.EnvelopeId, envelope.WorkflowExecutionId, corruptedCommand, envelope.IdempotencyKey,
                        envelope.DeliveryMode, envelope.EnqueuedAt, envelope.Sequence, envelope.Metadata, envelope.Partition);
                    leased[0] = new ExecutionCommandTransportItem(
                        item.TransportItemId, item.WorkflowExecutionId, corruptedEnvelope, item.Sequence, item.EnqueuedAt,
                        item.DeliveryAttemptCount, item.LeasedByOwnerId, item.LeaseExpiresAt);
                }
                if (fault == TransportFault.CorruptLeaseToken && leased.Length > 0)
                {
                    var item = leased[0];
                    leased[0] = new ExecutionCommandTransportItem(
                        item.TransportItemId, item.WorkflowExecutionId, item.Envelope, item.Sequence, item.EnqueuedAt,
                        item.DeliveryAttemptCount + 7, item.LeasedByOwnerId, item.LeaseExpiresAt);
                }
                if (fault == TransportFault.ReverseLeaseOrder)
                    Array.Reverse(leased);
                return new(leased);
            }
        }

        public ValueTask<bool> AckAsync(string workflowId, string id, string owner, long token, DateTimeOffset now, CancellationToken ct = default)
        {
            lock (backing.Gate)
            {
                var index = backing.Items.FindIndex(x => x.WorkflowExecutionId == workflowId && x.TransportItemId == id);
                if (index < 0)
                    return new(false);
                var item = backing.Items[index];
                var current = item.LeasedByOwnerId == owner &&
                              (item.LeaseToken == token || fault == TransportFault.IgnoreLeaseToken) &&
                              !item.IsVisible(now);
                if (!current && item.LeaseToken == 2 && !item.IsVisible(now))
                    backing.StaleAcknowledgementsWithCurrentSuccessor++;
                if (!current && item.LeasedByOwnerId == owner && item.LeaseToken != token && !item.IsVisible(now))
                    backing.TokenOnlyStaleAcknowledgements++;
                if (!current && fault == TransportFault.AcceptStale)
                    return new(true);
                if (current && fault == TransportFault.RejectCurrent)
                    return new(false);
                if (!current)
                    return new(false);
                backing.CurrentAcknowledgements++;
                backing.Items.RemoveAt(index);
                return new(true);
            }
        }

        public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int max, CancellationToken ct = default)
        {
            lock (backing.Gate)
            {
                var ids = backing.Items.Where(x => x.IsVisible(now)).Select(x => x.WorkflowExecutionId).Distinct().Order(StringComparer.Ordinal).Take(max).ToArray();
                if (fault == TransportFault.FabricateReopenList)
                    ids = ids.Take(ids.Length - 1).ToArray();
                return new((IReadOnlyCollection<string>)ids);
            }
        }

        public ValueTask<int> CountPendingAsync(string workflowId, CancellationToken ct = default)
        {
            lock (backing.Gate)
                return new(fault == TransportFault.FabricateReopenCount ? 0 : backing.Items.Count(x => x.WorkflowExecutionId == workflowId));
        }
    }
}
public enum TransportFault
{
    None,
    Alias,
    Split,
    FreshReopen,
    ResponseOnlySend,
    CrossCallerGrant,
    DuplicateLease,
    PartialLease,
    CorruptLeaseItem,
    CorruptLeaseToken,
    ReverseLeaseOrder,
    IgnoreLeaseToken,
    AcceptStale,
    RejectCurrent,
    FabricateReopenList,
    FabricateReopenCount
}
