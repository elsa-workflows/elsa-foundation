namespace Acme;

/// <summary>The synthetic Acme invoicing service the sample's activities take through their constructors.</summary>
public interface IInvoiceClient
{
    /// <summary>Sends an invoice and returns its number.</summary>
    Task<string> SendAsync(string customerId, decimal amount, string currency, CancellationToken cancellationToken);

    /// <summary>Returns the status of an invoice.</summary>
    Task<string> GetStatusAsync(string invoiceNumber, CancellationToken cancellationToken);

    /// <summary>Archives an invoice once per idempotency key.</summary>
    Task ArchiveAsync(string invoiceNumber, string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>A stand-in implementation so the feature composes; a real port registers the user's own client.</summary>
internal sealed class InMemoryInvoiceClient : IInvoiceClient
{
    private int _next;

    public Task<string> SendAsync(string customerId, decimal amount, string currency, CancellationToken cancellationToken) =>
        Task.FromResult($"INV-{Interlocked.Increment(ref _next):D4}");

    public Task<string> GetStatusAsync(string invoiceNumber, CancellationToken cancellationToken) =>
        Task.FromResult("Open");

    public Task ArchiveAsync(string invoiceNumber, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
