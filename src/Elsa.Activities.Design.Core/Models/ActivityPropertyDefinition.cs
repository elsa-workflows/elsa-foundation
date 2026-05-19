using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Core.Models
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TValueType"></typeparam>
    /// <param name="id"></param>
    /// <param name="name"></param>
    /// <param name="displayName"></param>
    /// <param name="description"></param>
    /// <param name="category"></param>
    /// <param name="order"></param>
    /// <param name="isBrowsable"></param>
    /// <param name="isSerializable"></param>
    /// <param name="uiHint"></param>
    /// <param name="propertyInfo"></param>
    /// <param name="uISpecifications"></param>
    public class ActivityPropertyDefinition<TValueType>(
        string id,
        string name,
        string displayName,
        string? description = null,
        string? category = null,
        float order = 0,
        bool isBrowsable = true,
        bool isSerializable = true,
        string? uiHint = null,
        IDictionary<string, object>? propertyInfo = null,
        IDictionary<string, object>? uISpecifications = null
        ) : ActivityPropertyDefinition(id, name, typeof(TValueType).FullName!, displayName, category, description, order, isBrowsable, isSerializable, uiHint, propertyInfo, uISpecifications)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    public class ActivityPropertyDefinition : IActivityPropertyDefinition
    {
        public ActivityPropertyDefinition(
            string id,
            string name,
            string fullyQualifiedType,
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
            FullyQualifiedType = fullyQualifiedType;
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

        /// <summary>
        /// The name.
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// The fully qualified type name.
        /// </summary>
        public string FullyQualifiedType { get; set; }  = null!;

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