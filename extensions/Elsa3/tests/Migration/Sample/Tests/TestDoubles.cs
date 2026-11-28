using System.Text.Json;
using Acme;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa3.Migration.Sample.Tests;

internal static class TestContext
{
    public static ActivityExecutionContext Create(CancellationToken cancellationToken = default) =>
        new("workflow-1", "invocation-1", "attempt-1", "node-1", cancellationToken);
}

internal sealed class FakeInvoiceClient : IInvoiceClient
{
    public List<(string CustomerId, decimal Amount, string Currency)> Sent { get; } = [];
    public List<(string InvoiceNumber, string IdempotencyKey)> Archived { get; } = [];

    public Task<string> SendAsync(string customerId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        Sent.Add((customerId, amount, currency));
        return Task.FromResult($"INV-{Sent.Count:D4}");
    }

    public Task<string> GetStatusAsync(string invoiceNumber, CancellationToken cancellationToken) =>
        Task.FromResult($"Paid:{invoiceNumber}");

    public Task ArchiveAsync(string invoiceNumber, string idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Archived.Add((invoiceNumber, idempotencyKey));
        return Task.CompletedTask;
    }
}

/// <summary>Step variables over a dictionary keyed by variable name.</summary>
internal sealed class FakeStepVariables : IStepVariables
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

    public T Get<T>(VariableReference<T> reference) => Get<T>(reference.Name);
    public T Get<T>(ReadOnlyVariableReference<T> reference) => Get<T>(reference.Name);
    public void Set<T>(VariableReference<T> reference, T value) => Set(reference.Name, value);

    [Obsolete("A read-only reference cannot be written.")]
    public void Set<T>(ReadOnlyVariableReference<T> reference, T value) => throw new InvalidOperationException();

    public T Get<T>(string name) => (T)Values[name]!;
    public void Set<T>(string name, T value) => Values[name] = value;
}

internal sealed class FakeWorkflowState : IStepWorkflowState
{
    public Dictionary<string, JsonElement> Outputs { get; } = new(StringComparer.Ordinal);
    public bool HasReadProtectedValue => false;
    public bool TryGetOutput(string name, out JsonElement value) => Outputs.TryGetValue(name, out value);
    public void SetOutput(string name, JsonElement value) => Outputs[name] = value;
    public string? CorrelationId { get; set; }
    public string? InstanceName { get; set; }
}
