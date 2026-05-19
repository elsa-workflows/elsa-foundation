namespace Elsa.Workflows.Design.Core.Models
{
    public abstract class WorkflowArgumentDefinition
    {
        protected WorkflowArgumentDefinition(string id, string name, string fullyQualifiedType)
        {
            Id = id;
            Name = name;
            FullyQualifiedType = fullyQualifiedType;
        }

        protected WorkflowArgumentDefinition(string id, string name, Type type)
        {
            Id = id;
            Name = name;
            FullyQualifiedType = type.FullName!;
        }

        /// <summary>
        /// The type of the input value.
        /// </summary>
        public string FullyQualifiedType { get; }

        /// <summary>
        /// The identifier of this argument
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The technical name of the workflow argument.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A user friendly name of the workflow argument.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// A description of the workflow argument.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// The category to which this workflow argument belongs.
        /// </summary>
        public string? Category { get; init; }
    }
}
