using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Secrets.Services;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>Both readable stamps authenticate every session-owned content column before typed defaults can be read.</summary>
public sealed class WeaverSchemaProtectedContentTests
{
    public static TheoryData<string, bool, string> RejectedContent
    {
        get
        {
            var cases = new TheoryData<string, bool, string>();
            foreach (var stamp in new[] { "1.0.0", "1.1.0" })
            foreach (var paused in new[] { false, true })
            foreach (var change in new[] { "damaged", "plain", "foreign-key", "foreign-session", "foreign-column", "foreign-id", "invalid-json" })
                cases.Add(stamp, paused, change);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(RejectedContent))]
    public async Task Unreadable_paused_and_pending_columns_never_default_to_empty_or_change_recorded_state(string stamp, bool paused, string change)
    {
        const string id = "protected-session";
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore();
        var canary = "synthetic-" + Guid.NewGuid().ToString("N");
        var record = new WeaverSessionRecord(Records.Session(id))
        {
            PausedTurn = new(new(id, "turn", "proposal", "call", canary, [], 1)),
            PendingAudits = [new("pending", WeaverAuditEventKind.MessageAccepted, id, "actor-1", canary,
                DateTimeOffset.UtcNow, new Dictionary<string, string>())]
        };
        Assert.True((await store.CreateAsync(record, "protected-owner")).Succeeded);
        var baseline = (await store.LoadAsync(id))!;
        Assert.Equal(canary, baseline.PausedTurn!.Turn.ToolName);
        Assert.Equal(canary, Assert.Single(baseline.PendingAudits).Summary);
        var stored = await database.ReadAllTextAsync();
        var payloads = stored.Where(x => x.Column is "Payload" or "PausedTurnPayload" or "PendingAuditsPayload").ToArray();
        Assert.Equal(3, payloads.Length);
        Assert.All(payloads, x => Assert.StartsWith("v2:", x.Value, StringComparison.Ordinal));
        Assert.DoesNotContain(stored, x => x.Value.Contains(canary, StringComparison.Ordinal));

        var purpose = paused ? WeaverRecordCodec.PausedTurnPurpose : WeaverRecordCodec.PendingAuditsPurpose;
        var rowId = paused ? "paused-turn" : "pending-audits";
        var column = paused ? "PausedTurnPayload" : "PendingAuditsPayload";
        var ciphertext = Assert.Single(stored, x => x.Column == column).Value;
        var content = database.Codec.UnprotectText(purpose, id, rowId, ciphertext);
        var replacement = change switch
        {
            "damaged" => ciphertext[..^6] + "AAAAAA",
            "plain" => content,
            "foreign-key" => new WeaverRecordCodec(new DefaultSecretValueProtector(new TestKeyRing())).ProtectText(purpose, id, rowId, content),
            "foreign-session" => database.Codec.ProtectText(purpose, "another-session", rowId, content),
            "foreign-column" => database.Codec.ProtectText(paused ? WeaverRecordCodec.PendingAuditsPurpose : WeaverRecordCodec.PausedTurnPurpose, id, rowId, content),
            "foreign-id" => database.Codec.ProtectText(purpose, id, "another-row", content),
            "invalid-json" => database.Codec.ProtectText(purpose, id, rowId, "{"),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await using (var db = database.Open())
        {
            var rows = db.Set<WeaverSessionRow>().Where(x => x.SessionId == id);
            var changed = paused
                ? await rows.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SchemaVersion, stamp).SetProperty(x => x.PausedTurnPayload, replacement))
                : await rows.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SchemaVersion, stamp).SetProperty(x => x.PendingAuditsPayload, replacement));
            Assert.Equal(1, changed);
        }
        var before = await database.ReadAllTextAsync();
        var refusal = await Assert.ThrowsAsync<WeaverSessionUnavailableException>(() => store.LoadAsync(id));
        Assert.Equal("unreadable", refusal.Reason);
        var cause = Assert.IsType<InvalidDataException>(refusal.InnerException);
        if (change == "invalid-json") Assert.IsType<JsonException>(cause.InnerException);
        else if (change is "foreign-session" or "foreign-column" or "foreign-id") Assert.Null(cause.InnerException);
        else Assert.NotNull(cause.InnerException);
        Assert.Equal(before, await database.ReadAllTextAsync());
        Assert.Equal(new WeaverSessionOwner("actor-1", "tenant-1"), await store.FindOwnerAsync(id));
    }
}
