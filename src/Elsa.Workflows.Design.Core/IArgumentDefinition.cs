namespace Elsa.Workflows.Design.Core
{
    public interface IArgumentDefinition
    {
        /// <summary>
        /// The type of the input value.
        /// </summary>
        Type Type { get; set; }

        /// <summary>
        /// The technical name of the input.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// A user friendly name of the input.
        /// </summary>
        string DisplayName { get; set; }

        /// <summary>
        /// A description of the input.
        /// </summary>
        string Description { get; set; }

        /// <summary>
        /// The category to which this input belongs.
        /// </summary>
        string Category { get; set; }
    }
}
