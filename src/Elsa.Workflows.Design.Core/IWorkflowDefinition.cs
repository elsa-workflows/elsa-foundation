using Elsa.Expressions.Core;

namespace Elsa.Workflows.Design.Core
{
    public interface IWorkflowDefinition
    {
        /// <summary>
        /// The logical ID of the workflow. This ID is the same across versions. 
        /// </summary>
        string DefinitionId { get; set; }

        /// <summary>
        /// The name of the workflow.
        /// </summary>
        string? Name { get; set; }

        /// <summary>
        /// A short description of what the workflow is about.  
        /// </summary>
        string? Description { get; set; }

        /// <summary>
        /// The version of the tool that created this workflow.
        /// </summary>
        Version? ToolVersion { get; set; }

        /// <summary>
        /// A set of options for the workflow.
        /// </summary>
        IWorkflowOptions Options { get; set; }

        /// <summary>
        /// A set of workflow variables that are accessible throughout the workflow.
        /// </summary>
        ICollection<IVariable> Variables { get; set; }

        /// <summary>
        /// A set of input definitions.
        /// </summary>
        ICollection<IInputDefinition> Inputs { get; set; }
        /// <summary>
        /// A set of output definitions.
        /// </summary>
        ICollection<IOutputDefinition> Outputs { get; set; }

        /// <summary>
        /// A set of possible outcomes for this workflow.
        /// </summary>
        ICollection<string> Outcomes { get; set; }

        /// <summary>
        /// Stores custom information about the workflow. Can be used to store application-specific properties to associate with the workflow.
        /// </summary>
        IDictionary<string, object> CustomProperties { get; set; }

        /// <summary>
        /// The name of the workflow provider that created this workflow, if any.
        /// </summary>
        string? ProviderName { get; set; }

        /// <summary>
        /// The name of the workflow materializer to interpret the <see cref="StringData"/> or <see cref="BinaryData"/>.
        /// </summary>
        string MaterializerName { get; set; }

        /// <summary>
        /// Provider-specific data.
        /// </summary>
        string? MaterializerContext { get; set; }

        /// <summary>
        /// A textual representation of the workflow. The data is to be interpreted by the configured materializer.
        /// </summary>
        string? StringData { get; set; }

        /// <summary>
        /// The original source representation of the workflow (JSON, ElsaScript, YAML, etc.).
        /// When present, materializers should prefer this over StringData for full round-trip fidelity.
        /// This field enables symmetric materialization without requiring serialization round-trips.
        /// </summary>
        string? OriginalSource { get; set; }

        /// <summary>
        /// A binary representation of the workflow. The data is to be interpreted by the configured materializer.
        /// </summary>
        byte[]? BinaryData { get; set; }

        /// <summary>
        /// An option to use the workflow as a readonly workflow
        /// </summary>
        bool IsReadonly { get; set; }

        /// <summary>
        /// Specifies whether the workflow is a system workflow.
        /// System workflows are provided by modules and are not meant to be modified by users.
        /// </summary>
        bool IsSystem { get; set; }

        /// <summary>
        /// Creates and returns a shallow copy of the workflow definition.
        /// </summary>
        IWorkflowDefinition ShallowClone();
    }
}
