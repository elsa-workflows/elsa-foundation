using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelPlaybackCursor(IDataProtectionProvider protection)
{
    private const string Prefix = "kernel-playback-v1:";
    private readonly IDataProtector _protector = protection.CreateProtector("Elsa.Workflows.Runtime.KernelPlayback.v1");

    internal sealed record Window(string Binding, long Upper, long RetainedAfter, long Count);

    internal Window? Open(string? token, string binding)
    {
        if (token is null) return null;
        if (token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            try
            {
                if (JsonSerializer.Deserialize<Window>(_protector.Unprotect(token[Prefix.Length..])) is
                    { RetainedAfter: >= 0, Count: >= 0 } window && window.Binding == binding && window.Upper >= window.RetainedAfter)
                    return window;
            }
            catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { }
        }
        throw new ArgumentException("The sequence token does not match this playback window.", nameof(token));
    }

    internal string Seal(Window window) => Prefix + _protector.Protect(JsonSerializer.Serialize(window));

    internal static string Bind(string tenant, string artifact, RuntimeHistoryOccurrenceQuery query, string access) =>
        RuntimeHistoryIdentity.CreateBinding([tenant, artifact, query.WorkflowExecutionId, KernelHistoryJournal.Policy,
            query.Limit.ToString(CultureInfo.InvariantCulture), query.ActivityExecutionId, query.IncludeDescendants ? "1" : "0", access]);
}
