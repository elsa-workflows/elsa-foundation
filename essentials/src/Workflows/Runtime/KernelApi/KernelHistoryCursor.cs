using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelHistoryCursor(IDataProtectionProvider protection)
{
    private const string Prefix = "kernel-history-v1:";
    private readonly IDataProtector _protector = protection.CreateProtector("Elsa.Workflows.Runtime.KernelHistory.v1");

    internal sealed record Position(long AfterSequence, long? UpperSequence, long? LastRevision = null);
    private sealed record Ticket(string Binding, Position Position);

    internal static bool IsKernelCursor(string? cursor) => cursor?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    internal Position Open(RuntimeHistoryQuery query, string tenant, string artifact)
    {
        if (query.ContinuationToken is not { } token) return new(0, null);
        if (IsKernelCursor(token))
        {
            try
            {
                var ticket = JsonSerializer.Deserialize<Ticket>(_protector.Unprotect(token[Prefix.Length..]));
                if (ticket?.Binding == Binding(query, tenant, artifact) &&
                    ticket.Position is { AfterSequence: >= 0 } position &&
                    (position.UpperSequence is null || position.UpperSequence >= position.AfterSequence))
                    return position;
            }
            catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { }
        }
        throw new ArgumentException("The continuation does not match this run history.", nameof(query));
    }

    internal string Seal(RuntimeHistoryQuery query, string tenant, string artifact, Position position) =>
        Prefix + _protector.Protect(JsonSerializer.Serialize(new Ticket(Binding(query, tenant, artifact), position)));

    private static string Binding(RuntimeHistoryQuery query, string tenant, string artifact) => RuntimeHistoryIdentity.CreateBinding(
        [tenant, artifact, query.WorkflowExecutionId, query.AccessBinding, query.ActivityExecutionId, query.IncludeDescendants ? "1" : "0",
            query.BookmarkId, query.Category?.ToString(), query.Name, query.Search]);
}
