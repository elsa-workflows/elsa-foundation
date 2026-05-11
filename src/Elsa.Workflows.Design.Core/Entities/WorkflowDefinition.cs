using Elsa.Expressions.Core;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Options;

namespace Elsa.Workflows.Design.Core.Entities
{
    /// <summary>
    /// Represents a versioned workflow definition.
    /// </summary>
    public sealed class WorkflowDefinition : VersionedEntity, IWorkflowDefinition
    {
        /// <summary>
        /// The logical ID of the workflow. This ID is the same across versions. 
        /// </summary>
        public string DefinitionId { get; set; } = null!;

        /// <summary>
        /// The name of the workflow.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// A short description of what the workflow is about.  
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The version of the tool that created this workflow.
        /// </summary>
        public Version? ToolVersion { get; set; }

        /// <summary>
        /// A set of options for the workflow.
        /// </summary>
        public IWorkflowOptions Options { get; set; } = new WorkflowOptions();

        /// <summary>
        /// A set of workflow variables that are accessible throughout the workflow.
        /// </summary>
        public ICollection<IVariable> Variables { get; set; } = [];

        /// <summary>
        /// A set of input definitions.
        /// </summary>
        public ICollection<IInputDefinition> Inputs { get; set; } = [];

        /// <summary>
        /// A set of output definitions.
        /// </summary>
        public ICollection<IOutputDefinition> Outputs { get; set; } = [];

        /// <summary>
        /// A set of possible outcomes for this workflow.
        /// </summary>
        public ICollection<string> Outcomes { get; set; } = [];

        /// <summary>
        /// Stores custom information about the workflow. Can be used to store application-specific properties to associate with the workflow.
        /// </summary>
        public IDictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// The name of the workflow provider that created this workflow, if any.
        /// </summary>
        public string? ProviderName { get; set; }

        /// <summary>
        /// The name of the workflow materializer to interpret the <see cref="StringData"/> or <see cref="BinaryData"/>.
        /// </summary>
        public string MaterializerName { get; set; } = null!;

        /// <summary>
        /// Provider-specific data.
        /// </summary>
        public string? MaterializerContext { get; set; }

        /// <summary>
        /// A textual representation of the workflow. The data is to be interpreted by the configured materializer.
        /// </summary>
        public string? StringData { get; set; }

        /// <summary>
        /// The original source representation of the workflow (JSON, ElsaScript, YAML, etc.).
        /// When present, materializers should prefer this over StringData for full round-trip fidelity.
        /// This field enables symmetric materialization without requiring serialization round-trips.
        /// </summary>
        public string? OriginalSource { get; set; }

        /// <summary>
        /// A binary representation of the workflow. The data is to be interpreted by the configured materializer.
        /// </summary>
        public byte[]? BinaryData { get; set; }

        /// <summary>
        /// An option to use the workflow as a readonly workflow
        /// </summary>
        public bool IsReadonly { get; set; }

        /// <summary>
        /// Specifies whether the workflow is a system workflow.
        /// System workflows are provided by modules and are not meant to be modified by users.
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// Creates and returns a shallow copy of the workflow definition.
        /// </summary>
        public IWorkflowDefinition ShallowClone() => (WorkflowDefinition)MemberwiseClone();
    }
}
