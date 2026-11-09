using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Tests;
using Elsa.Secrets.Services;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>
/// #3441 T028, FR-003: what the default owner records through the EF store is ciphertext in every payload column, and
/// the protected records hold the owner's Core records only: no credential and no provider or process object.
/// </summary>
public sealed class EfAgentProtectionTests
{
    private const string Canary = "canary-7f3d-content";

    [Fact]
    public async Task Payload_columns_hold_only_ciphertext_of_the_owners_records()
    {
        using var database = new AgentSqliteDatabase();
        using var harness = new AgentRestartHarness(clock => database.RecordStore(clock));
        var session = await harness.Host.CreateAsync();
        await harness.Host.PostAsync(session, "mutate the resource " + Canary);
        await harness.Host.StreamAsync(session);
        var audit = database.AuditStore();
        await audit.AssociateAsync(session.Id, session.ActorId, session.TenantId);
        await audit.EmitAsync(new("audit-1", AgentAuditEventKind.MessageAccepted, session.Id, session.ActorId, "Accepted " + Canary,
            DateTimeOffset.UtcNow, new Dictionary<string, string> { ["content"] = Canary }));

        var values = await database.ReadAllTextAsync();
        var protectedColumns = values.Where(x => x.Column is "Payload" or "PausedTurnPayload" or "Canonical").ToArray();

        // The turn recorded a message, a proposal, keys, a paused turn and snapshots, all through the default owner.
        Assert.Contains(protectedColumns, x => x.Table == "elsa_weaver_proposals");
        Assert.Contains(protectedColumns, x => x.Column == "PausedTurnPayload");
        Assert.Contains(values, x => x.Table == "elsa_weaver_call_keys");
        // Nothing the user wrote appears in plain text anywhere; every payload is the Secrets protector's ciphertext.
        Assert.DoesNotContain(values, x => x.Value.Contains(Canary, StringComparison.Ordinal));
        Assert.All(protectedColumns, x => Assert.StartsWith("v2:", x.Value, StringComparison.Ordinal));

        // Decrypted, the content is there, and only as plain JSON data: no credential, type marker or runtime object.
        var protector = new DefaultSecretValueProtector(database.Keys);
        var plain = protectedColumns.Select(x => protector.Unprotect(x.Value)).ToArray();
        Assert.Contains(plain, x => x.Contains(Canary, StringComparison.Ordinal));
        var names = plain.SelectMany(x => PropertyNames(JsonDocument.Parse(x).RootElement))
            .Concat(plain.Select(x => JsonDocument.Parse(x).RootElement.GetProperty("Value").GetString()!)
                .Where(x => x.StartsWith('{')).SelectMany(x => PropertyNames(JsonDocument.Parse(x).RootElement)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "$type", "ApiKey", "AccessToken", "Token", "Password", "Credential", "Credentials", "ClientSecret",
                     "CancellationToken", "ServiceProvider" })
            Assert.DoesNotContain(forbidden, names);
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(x => PropertyNames(x.Value).Prepend(x.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => []
    };
}
