using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Actual built-in owner decisions, evaluated lazily only for an admitted capture profile.</summary>
internal interface IAlterationHistorySource
{
    IEnumerable<RuntimeHistorySourceFact> HistoryFacts(DateTimeOffset occurredAt);
}
