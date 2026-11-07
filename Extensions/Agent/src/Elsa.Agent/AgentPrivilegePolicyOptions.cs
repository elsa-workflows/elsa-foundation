namespace Elsa.Agent;

/// <summary>Live host limits on generic privileged effects, independent of session autonomy and owning-resource permission.</summary>
public sealed class AgentPrivilegePolicyOptions
{
    public const string SectionName = "Elsa:Agent:Privileges";

    public bool Administrative { get; set; }
    public bool Irrecoverable { get; set; }
    public bool Activation { get; set; }
}
