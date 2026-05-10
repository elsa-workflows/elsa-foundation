using Elsa.Workflows.Design.Core;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Core.Options;
using Elsa.Expressions.Models;

namespace Elsa.Server
{
    public static class TestWorkflowDefinition
    {
        public static readonly WorkflowDefinition Entity = new()
        {
            Id = Guid.NewGuid().ToString(),
            DefinitionId = Guid.NewGuid().ToString(),
            Version = 1,
            IsLatest = true,
            IsPublished = true,
            Name = "Test Workflow",
            Description = "A test workflow definition.",
            CreatedAt = DateTimeOffset.UtcNow,
            ProviderName = "Code",
            MaterializerName = "Json",
            Options = new WorkflowOptions { ActivationStrategyType = typeof(int), ActivityCategory = "TestCategory", AutoUpdateConsumingWorkflows = true, CommitStrategyName = "TestCommitStrategy", IncidentStrategyType = typeof(object), UsableAsActivity = true },
            Inputs =
            [
                new InputOrOutputDefinition(typeof(string), "InputName", "Input Display Name", "Input Description", "Input Category")
            ],
            Outputs =
            [
                new InputOrOutputDefinition(typeof(string), "OutputName", "Output Display Name", "Output Description", "Output Category")
            ],
            Variables =
            [
                new Variable
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "VariableName",
                    StorageDriverType = typeof(string),
                    Value = "Variable Value"
                }
            ]
        };

        private class InputOrOutputDefinition(Type type, string name, string displayName, string description, string category)
            : IInputDefinition, IOutputDefinition
        {
            public Type Type { get; set; } = type;
            public string Name { get; set; } = name;
            public string DisplayName { get; set; } = displayName;
            public string Description { get; set; } = description;
            public string Category { get; set; } = category;
        }
    }
}
