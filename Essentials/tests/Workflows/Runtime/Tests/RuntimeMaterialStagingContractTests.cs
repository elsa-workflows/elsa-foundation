using System.Collections;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeMaterialStagingContractTests
{
    [Fact]
    public void Request_snapshots_raw_bytes_and_member_list_while_preserving_missing_incarnation()
    {
        byte[] bytes = [1, 2, 3]; var member = Member(bytes); var members = new List<RuntimeMaterialStageMember> { member };
        var command = new RuntimeMaterialStageCommand("command", "attempt", "actor", new("target", "partition", null), members);
        bytes[0] = 9; members.Clear(); var returned = member.GetOriginalPayloadBytes(); returned[1] = 9;
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(command.Members).GetOriginalPayloadBytes());
        Assert.Null(command.Members[0].ExpectedIncarnationId);
        // Typed legacy bodies are inputs; the selected owner must freeze them before its first await.
    }

    [Fact]
    public void Caller_collections_cannot_hide_extra_work_or_duplicate_owner_identity()
    {
        var member = Member([1]); var hidden = new MisreportedMembers(member);
        Assert.Throws<RuntimeMaterialStagingException>(() => new RuntimeMaterialStageCommand("command", "attempt", "actor", new("target", "partition", null), hidden));
        Assert.True(hidden.Enumerated <= RuntimeMaterialStageCommand.MaximumMembers + 1);
        Assert.Throws<RuntimeMaterialStagingException>(() => new RuntimeMaterialStageCommand("command", "attempt", "actor", new("target", "partition", null), [member, member]));
    }

    [Fact]
    public async Task Unqualified_owners_refuse_every_operation_and_unknown_retention()
    {
        IRuntimeMaterialStagingStore owner = new LegacyOwner(); var command = new RuntimeMaterialStageCommand("command", "attempt", "actor", new("target", "partition", null), [Member([1])]);
        foreach (var operation in new Func<Task>[] { () => owner.RegisterAsync(command).AsTask(), () => owner.StageAsync(command, 1).AsTask(),
            () => owner.FindReceiptAsync("command").AsTask(), () => owner.AdvanceFenceAsync(command, 1).AsTask(), () => owner.RevokeAsync(command, 1).AsTask() })
            Assert.True((await Assert.ThrowsAsync<RuntimeMaterialStagingException>(operation)).IsUnsupported);
        IRuntimeMaterialStageRetentionReader retention = new LegacyRetention();
        Assert.True((await Assert.ThrowsAsync<RuntimeMaterialStagingException>(() => retention.ReadStageRetentionRootsAsync(10).AsTask())).IsUnsupported);
    }

    private static RuntimeMaterialStageMember Member(byte[] bytes)
    {
        var root = new ExecutableNode("root", "root", "sample", "1", "sample", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var template = new ExecutableActivityTemplate("template", "hash", root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fixture/1", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        return new(new("activity-template", "template", new("source", "partition", null)), bytes, null, null, null, template: template);
    }
    private sealed class LegacyOwner : IRuntimeMaterialStagingStore { }
    private sealed class LegacyRetention : IRuntimeMaterialStageRetentionReader { }
    private sealed class MisreportedMembers(RuntimeMaterialStageMember member) : IReadOnlyList<RuntimeMaterialStageMember>
    {
        public int Count => 1;
        public int Enumerated { get; private set; }
        public RuntimeMaterialStageMember this[int index] => member;
        public IEnumerator<RuntimeMaterialStageMember> GetEnumerator()
        {
            while (true)
            {
                if (++Enumerated > RuntimeMaterialStageCommand.MaximumMembers + 1) throw new InvalidOperationException("The command failed to bound enumeration.");
                yield return new(new("activity-template", "template", new("source-" + Enumerated, "partition", null)), new byte[] { 1 }, null, null, null, template: member.Template);
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
