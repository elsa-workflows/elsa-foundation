namespace Elsa.Samples.Nuplane.Activities.Descriptors;

public sealed record SampleNuplaneActivityDescriptor(string ActivityType)
{
    public static SampleNuplaneActivityDescriptor Default { get; } = new("SayHelloFromNuplane");
}
