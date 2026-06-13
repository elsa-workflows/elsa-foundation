using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Samples.Nuplane.Activities.Activities;
using Elsa.Samples.Nuplane.Activities.Descriptors;

namespace Elsa.Samples.Nuplane.Activities.Constructors;

public sealed class SampleNuplaneActivityConstructor(string messageTemplate, bool includeTimestamp) : IActivityConstructor<SampleNuplaneActivityDescriptor>
{
    public string DescriptorType => typeof(SampleNuplaneActivityDescriptor).FullName!;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        var descriptor = payload.Deserialize<SampleNuplaneActivityDescriptor>()
                         ?? SampleNuplaneActivityDescriptor.Default;

        return Construct(descriptor, inputs, outputs, cancellationToken);
    }

    public ValueTask<IActivity> Construct(
        SampleNuplaneActivityDescriptor descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        var activity = new SayHelloFromNuplane
        {
            MessageTemplate = messageTemplate,
            IncludeTimestamp = includeTimestamp
        };
        if (inputs?.TryGetValue(nameof(SayHelloFromNuplane.Recipient), out var recipient) == true)
            activity.Recipient = (InputArgument<string>)recipient;

        return ValueTask.FromResult<IActivity>(activity);
    }
}
