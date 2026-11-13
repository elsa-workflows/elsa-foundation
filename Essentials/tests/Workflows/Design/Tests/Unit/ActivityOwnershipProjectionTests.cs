using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class ActivityOwnershipProjectionTests
{
    private static readonly ActivityNode Child = new("child", "child@1", [], []);
    private static readonly ActivityNode Root = new("root", "root@1", [], [], new("test", "1", JsonSerializer.SerializeToElement(new { })));

    [Fact]
    public void Existing_custom_handlers_and_services_keep_their_lexical_children_as_owned_activities()
    {
        IActivityStructureHandler handler = new LegacyHandler();
        IActivityStructureService custom = new LegacyService();
        foreach (var ownership in new[] { handler.ProjectOwnership(Root), custom.ProjectOwnership(Root),
                     new DefaultActivityStructureService([handler]).ProjectOwnership(Root) })
        {
            Assert.Same(Child, Assert.Single(ownership.Activities));
            Assert.Empty(ownership.AdditionalNodeIds);
        }
    }

    [Fact]
    public void Owning_projection_does_not_widen_lexical_children_or_dispatch_to_a_different_schema()
    {
        var service = new DefaultActivityStructureService([new DocumentHandler()]);
        Assert.Empty(service.ProjectChildren(Root));
        var ownership = service.ProjectOwnership(Root);
        Assert.Same(Child, Assert.Single(ownership.Activities));
        Assert.Equal(["header"], ownership.AdditionalNodeIds);
        var unknown = Root with { Structure = new("test", "2", Root.Structure!.Payload) };
        Assert.Empty(service.ProjectOwnership(unknown).Activities);
        Assert.Empty(service.ProjectOwnership(unknown).AdditionalNodeIds);
    }

    private sealed class LegacyHandler : IActivityStructureHandler
    {
        public string Kind => "test";
        public string SchemaVersion => "1";
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [new("Body", [Child])];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => activity.Structure!;
    }

    private sealed class DocumentHandler : IActivityStructureHandler
    {
        public string Kind => "test";
        public string SchemaVersion => "1";
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityOwnershipProjection ProjectOwnership(ActivityNode activity) => new([Child], ["header"]);
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => activity.Structure!;
    }

    private sealed class LegacyService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [new("Body", [Child])];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
}
