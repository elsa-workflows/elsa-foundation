using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Samples.Nuplane.Activities.Activities;
using Elsa.Samples.Nuplane.Activities.Descriptors;

namespace Elsa.Samples.Nuplane.Activities.Constructors;

public sealed class SampleNuplaneActivityConstructor(string messageTemplate, bool includeTimestamp) : IActivityConstructor<SampleNuplaneActivityDescriptor>
{
    public const string ConsumerKeyValue = "elsa.sample.nuplane-activity";
    public string ConsumerKey => ConsumerKeyValue;

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
