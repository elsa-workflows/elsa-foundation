namespace Elsa.Cluster.Core.Models;

/// <summary>
/// One named section of a member report. The set of sections is part of the contract: a new section, such as
/// the one placement adds for its requirement kinds, is a contract change. A section is data only and never
/// carries a connection string, a secret or configuration text.
/// </summary>
public abstract record MemberReportSection
{
    private protected MemberReportSection()
    {
    }
}
