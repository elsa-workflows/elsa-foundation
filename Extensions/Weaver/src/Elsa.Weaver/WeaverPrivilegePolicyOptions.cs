namespace Elsa.Weaver;

/// <summary>Live host limits on generic privileged effects, independent of session autonomy and owning-resource permission.</summary>
public sealed class WeaverPrivilegePolicyOptions
{
    public const string SectionName = "Elsa:Weaver:Privileges";

    public bool Administrative { get; set; }
    public bool Irrecoverable { get; set; }
    public bool Activation { get; set; }
}
