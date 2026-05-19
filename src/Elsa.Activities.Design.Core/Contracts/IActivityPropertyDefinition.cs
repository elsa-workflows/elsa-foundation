namespace Elsa.Activities.Design.Core.Contracts
{
    public interface IActivityPropertyDefinition
    {
        string Id { get; }

        /// <summary>
        /// The name.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// 
        /// </summary>
        string FullyQualifiedType { get; set; }

        /// <summary>
        /// The user friendly name of the input. Used by UI tools.
        /// </summary>
        string? DisplayName { get; set; }

        /// <summary>
        /// The user friendly description of the input. Used by UI tools.
        /// </summary>
        string? Description { get; set; }

        /// <summary>
        /// The order in which this input should be displayed by UI tools.
        /// </summary>
        float Order { get; set; }

        /// <summary>
        /// True if this property should be displayed by UI tools, false otherwise.
        /// </summary>
        bool? IsBrowsable { get; set; }

        /// <summary>
        /// True if this property can be serialized.
        /// </summary>
        bool? IsSerializable { get; set; }
    }
}
