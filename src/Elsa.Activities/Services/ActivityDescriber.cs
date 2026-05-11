using Elsa.Activities.Core.Contracts;
using Elsa.Activities.Core.Models;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Elsa.Activities.Services
{
    public sealed class ActivityDescriber : IActivityDescriber
    {
        public Task<ActivityDescriptor> DescribeActivityAsync([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<InputDescriptor>> DescribeInputPropertiesAsync([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<InputDescriptor> DescribeInputPropertyAsync(PropertyInfo propertyInfo, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<OutputDescriptor>> DescribeOutputPropertiesAsync([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<OutputDescriptor> DescribeOutputPropertyAsync(PropertyInfo propertyInfo, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<PropertyInfo> GetInputProperties([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<PropertyInfo> GetOutputProperties([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType)
        {
            throw new NotImplementedException();
        }
    }
}
