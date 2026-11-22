using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Alterations;

// Built-in alteration owners contribute their actual decisions, lazily for admitted capture only.
internal interface IAlterationHistorySource
{
    IEnumerable<RuntimeHistorySourceFact> HistoryFacts(DateTimeOffset occurredAt);
}
