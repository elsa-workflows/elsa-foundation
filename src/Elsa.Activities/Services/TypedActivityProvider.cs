using Elsa.Activities.Core.Contracts;
using Elsa.Activities.Core.Models;
using Elsa.Activities.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace Elsa.Activities.Services
{
    /// <summary>
    /// Provides activity descriptors based on a list of activity types registered in the <see cref="TypedActivityProviderOptions"/>.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="TypedActivityProvider"/> class.
    /// </remarks>
    [UsedImplicitly]
    public sealed class TypedActivityProvider(IOptions<TypedActivityProviderOptions> options, IActivityDescriber activityDescriber)
        : IActivityProvider
    {
        private readonly TypedActivityProviderOptions _options = options.Value;

        /// <inheritdoc />
        public async ValueTask<IEnumerable<ActivityDescriptor>> GetDescriptorsAsync(CancellationToken cancellationToken = default)
        {
            var activityTypes = _options.ActivityTypes;
            var descriptors = await DescribeActivityTypesAsync(activityTypes, cancellationToken).ToListAsync(cancellationToken);
            return descriptors;
        }

        private async IAsyncEnumerable<ActivityDescriptor> DescribeActivityTypesAsync(IEnumerable<Type> activityTypes, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var activityType in activityTypes)
            {
                var descriptor = await activityDescriber.DescribeActivityAsync(activityType, cancellationToken);
                yield return descriptor;
            }
        }
    }
}
