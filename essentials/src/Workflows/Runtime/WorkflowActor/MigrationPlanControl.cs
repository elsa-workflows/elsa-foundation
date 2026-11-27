using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

internal sealed record MigrationPlanControl(long Number, ActorRef[]? Actors = null, PageToken? Before = null,
    PageToken? After = null, bool AllSelected = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool KeepCursor = false)
{
    internal const string Select = "migration-plan.select";
    internal const string Acknowledge = "migration-plan.acknowledge";
    internal const string Complete = "migration-plan.complete";
    internal const string Close = "migration-plan.close";
    internal const string Deadline = "migration-plan.deadline";
}

/// <summary>The operator host supplies the same control key to the plan owners and authorized runners.</summary>
internal sealed class MigrationPlanAuthority(ReadOnlySpan<byte> key)
{
    private readonly byte[] _key = key.Length == 32 ? key.ToArray() :
        throw new ArgumentException("A migration plan control key must be 32 bytes.", nameof(key));

    internal string OperationId(string tenant, ActorRef plan, string kind, string operation) =>
        "plan-" + Convert.ToHexString(HMACSHA256.HashData(_key,
            JsonSerializer.SerializeToUtf8Bytes(new { tenant, plan, kind, operation })));

    internal string Protect(string tenant, ActorRef plan, string kind, string id, MigrationPlanControl control) =>
        JsonSerializer.Serialize(new SignedControl(control, Convert.ToHexString(Tag(tenant, plan, kind, id, control))));

    internal MigrationPlanControl? Read(string tenant, ActorRef plan, Message message)
    {
        if (message is not { Sender: null, Payload: { } payload }) return null;
        try
        {
            if (JsonSerializer.Deserialize<SignedControl>(payload) is not { Control: { } control, Tag.Length: 64 } signed)
                return null;
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signed.Tag),
                Tag(tenant, plan, message.Kind, message.Id, control)) ? control : null;
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }

    private byte[] Tag(string tenant, ActorRef plan, string kind, string id, MigrationPlanControl control) =>
        HMACSHA256.HashData(_key, JsonSerializer.SerializeToUtf8Bytes(new { tenant, plan, kind, id, control }));

    private sealed record SignedControl(MigrationPlanControl Control, string Tag);
}
