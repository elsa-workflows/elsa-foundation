using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Contracts;

namespace Elsa.Activities.Design.Core.Models
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class ActivityPropertyDefinition : IActivityPropertyDefinition
    {
        public ActivityPropertyDefinition(
            string id,
            string name,
            ITypeInformation typeInfo,
            string displayName,
            string? category,
            string? description = null,
            float order = 0,
            bool isBrowsable = true,
            bool isSerializable = true,
            string? uiHint = null,
            IDictionary<string, object>? propertyInfo = null,
            IDictionary<string, object>? uISpecifications = null
      )
        {
            Id = id;
            Name = name;
            TypeInfo = typeInfo;
            DisplayName = displayName;
            Description = description;
            Order = order;
            IsBrowsable = isBrowsable;
            IsSerializable = isSerializable;
            UiHint = uiHint;
            Category = category;
            PropertyInfo = propertyInfo ?? new Dictionary<string, object>();
            UISpecifications = uISpecifications ?? new Dictionary<string, object>(); ;
        }

        public string Id { get; }

        public ITypeInformation TypeInfo { get; }

        /// <summary>
        /// The name.
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// The user friendly name of the input. Used by UI tools.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// The user friendly description of the input. Used by UI tools.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The order in which this input should be displayed by UI tools.
        /// </summary>
        public float Order { get; set; }

        /// <summary>
        /// True if this property should be displayed by UI tools, false otherwise.
        /// </summary>
        public bool? IsBrowsable { get; set; } = true;

        /// <summary>
        /// True if this property can be serialized.
        /// </summary>
        public bool? IsSerializable { get; set; }

        /// <summary>
        /// The category to which this input belongs within the activity
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Additional text displayed by UI tools to provide hints about how to use this property.
        /// </summary>
        public string? UiHint { get; set; }

        public IDictionary<string, object>? PropertyInfo { get; }

        public IDictionary<string, object>? UISpecifications { get; }
    }
}