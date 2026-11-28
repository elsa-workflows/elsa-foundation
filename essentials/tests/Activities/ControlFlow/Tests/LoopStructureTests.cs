using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// The four loop activities share one structure handler and one navigator, parameterized by loop. These tests
/// pin each loop's own structure kind and the compiled payload bytes that feed executable content hashes.
/// </summary>
public sealed class LoopStructureTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public LoopStructureTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
    }

    public static TheoryData<string> Loops => ["Do", "For", "ForEach", "While"];

    public void Dispose() => _provider.Dispose();

    [Theory]
    [MemberData(nameof(Loops))]
    public void Compiled_and_remapped_structure_payloads_keep_their_bytes(string loop)
    {
        var (structureKind, bodySlotName) = Identity(loop);
        var handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), handler => handler.Kind == structureKind);
        var node = new ActivityNode("node-loop", "activity-version-loop", [], []);
        var withBody = handler.ReplaceChildren(node, [new ActivityChildProjection(bodySlotName, [new ActivityNode("branch-body", "activity-version-branch-body", [], [])])]);
        var withoutBody = handler.ReplaceChildren(node, []);

        var compiled = handler.CompileExecutableStructure(withBody);
        var remapped = handler.RemapExecutableStructure(compiled, new Dictionary<string, string> { ["branch-body"] = "exec-body" });

        Assert.Equal(structureKind, compiled.Kind);
        Assert.Equal("""{"body":"branch-body"}""", compiled.Payload.GetRawText());
        Assert.Equal("""{"body":null}""", handler.CompileExecutableStructure(withoutBody).Payload.GetRawText());
        Assert.Equal("""{"body":null}""", withoutBody.Structure!.Payload.GetRawText());
        Assert.Equal(structureKind, remapped.Kind);
        Assert.Equal("""{"body":"exec-body"}""", remapped.Payload.GetRawText());
    }

    private static (string StructureKind, string BodySlotName) Identity(string loop) => loop switch
    {
        "Do" => (DoActivity.StructureKind, DoActivity.BodySlotName),
        "For" => (ForActivity.StructureKind, ForActivity.BodySlotName),
        "ForEach" => (ForEachActivity.StructureKind, ForEachActivity.BodySlotName),
        "While" => (WhileActivity.StructureKind, WhileActivity.BodySlotName),
        _ => throw new ArgumentOutOfRangeException(nameof(loop), loop, null)
    };
}
