using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// Covers the built-in engine-intrinsic authoring descriptors (#929): Set Variable and Set Output must be
/// offered by the authoring catalog so the designer palette has a way to assign variables and outputs.
/// </summary>
public sealed class IntrinsicAuthoringCatalogTests
{
    private readonly IntrinsicAuthoringDescriptorProvider _provider = new();

    [Fact]
    public void Provider_surfaces_set_variable_and_set_output_under_primitives()
    {
        var descriptors = _provider.GetDescriptors();

        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal("Primitives", descriptor.Category);
            Assert.True(descriptor.Available);
            Assert.NotNull(descriptor.Intrinsic);
            Assert.Equal("intrinsic", descriptor.AuthoringTemplate.NodeId);
            Assert.Contains(descriptor.Ports, port => port.Name == "Done");
            Assert.Empty(descriptor.Outputs);
        });
    }

    [Fact]
    public void Set_variable_descriptor_authors_a_set_intrinsic_with_a_variable_target()
    {
        var setVariable = Single("Elsa.SetVariable");

        Assert.Equal("Set Variable", setVariable.DisplayName);
        Assert.Equal("elsa.intrinsic.set@1", setVariable.ActivityVersionId);
        Assert.Equal("elsa.intrinsic.set@1", setVariable.AuthoringTemplate.ActivityVersionId);
        Assert.Equal("Set", setVariable.Intrinsic!.Kind);
        Assert.Equal("value", setVariable.Intrinsic.ValueInputKey);
        Assert.Equal("variable", setVariable.Intrinsic.VariableInputKey);
        Assert.Null(setVariable.Intrinsic.OutputNameInputKey);

        var variableInput = Assert.Single(setVariable.Inputs, input => input.ReferenceKey == "variable");
        Assert.True(variableInput.IsRequired);
        Assert.Equal("variable-picker", variableInput.UiHint);
        var valueInput = Assert.Single(setVariable.Inputs, input => input.ReferenceKey == "value");
        Assert.Equal("Elsa.Any", valueInput.Type);
        Assert.True(valueInput.IsRequired);
    }

    [Fact]
    public void Set_output_descriptor_authors_a_set_output_intrinsic_with_a_literal_name()
    {
        var setOutput = Single("Elsa.SetOutput");

        Assert.Equal("Set Output", setOutput.DisplayName);
        Assert.Equal("elsa.intrinsic.set-output@1", setOutput.ActivityVersionId);
        Assert.Equal("SetOutput", setOutput.Intrinsic!.Kind);
        Assert.Equal("value", setOutput.Intrinsic.ValueInputKey);
        Assert.Null(setOutput.Intrinsic.VariableInputKey);
        Assert.Equal("name", setOutput.Intrinsic.OutputNameInputKey);

        var nameInput = Assert.Single(setOutput.Inputs, input => input.ReferenceKey == "name");
        Assert.True(nameInput.IsRequired);
        var valueInput = Assert.Single(setOutput.Inputs, input => input.ReferenceKey == "value");
        Assert.Equal("Elsa.Any", valueInput.Type);
    }

    [Fact]
    public async Task Catalog_handler_appends_built_in_intrinsics_to_the_persisted_catalog()
    {
        var handler = new ListActivityAuthoringCatalogRequestHandler(
            new EmptyDefinitionStore(),
            new EmptyVersionStore(),
            new NoneAddableEvaluator(),
            new NullSettingsStore(),
            [_provider]);

        var view = await handler.Handle(new ListActivityAuthoringCatalog(), CancellationToken.None);

        Assert.Contains(view.Activities, activity => activity.ActivityTypeKey == "Elsa.SetVariable");
        Assert.Contains(view.Activities, activity => activity.ActivityTypeKey == "Elsa.SetOutput");
    }

    private ActivityAuthoringDescriptorView Single(string activityTypeKey) =>
        Assert.Single(_provider.GetDescriptors(), descriptor => descriptor.ActivityTypeKey == activityTypeKey);

    private sealed class EmptyDefinitionStore : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(null);
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinition>>([]);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(null);
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class EmptyVersionStore : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersion?>(null);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
    }

    private sealed class NoneAddableEvaluator : IActivityAvailabilityEvaluator
    {
        public IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities, ActivityAvailabilitySettings? managementSettings = null) => [];
    }

    private sealed class NullSettingsStore : IActivityAvailabilitySettingsStore
    {
        public Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) => Task.FromResult<ActivityAvailabilitySettings?>(null);
        public Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
